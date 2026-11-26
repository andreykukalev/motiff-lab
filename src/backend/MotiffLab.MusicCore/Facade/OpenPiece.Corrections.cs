using System.Text.Json;

using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// Correcting the imported notes (S1-I7; contract 4), for the iPad app's Fix notes in the Check drawer as the web app's
/// note corrections: the notes of a bar, a note a step higher, lower, earlier, later, shorter, longer, softer or louder,
/// taken out, put back as imported, and the last correction undone. Each correction is a step in the project's edit log;
/// the imported notes never change. Notes taken from a performance (0022) are not corrected here.
/// </summary>
/// <remarks>
/// The note editor on the roll (testing item 15) adds a note (`corrections.add`), sets a note's pitch, onset and length at
/// once (`corrections.set`), and works on a draft: `corrections.begin` starts it, every correction after it sounds and
/// shows at once but the project file keeps the corrections as they were until `corrections.save`; `corrections.discard`
/// puts them back, `corrections.reset` takes every correction out (the notes as imported), and `corrections.undo` steps
/// back through the draft, the reset too.
/// </remarks>
internal sealed partial class OpenPiece
{
    /// <summary>A step in time: a sixteenth, as in the web app.</summary>
    private const long CorrectionStep = CanonicalTime.TicksPerQuarter / 4;

    private const int VelocityStep = 10;

    /// <summary>The corrections when the draft began; null when there is no draft.</summary>
    private NoteEditLog? _draftFrom;

    /// <summary>The draft's logs before each of its steps, the last first undone.</summary>
    private readonly Stack<NoteEditLog> _draftSteps = new();

    /// <summary>The corrections before and after the assistant's draft accepted last, so one Undo takes it back whole while nothing came after it.</summary>
    private (NoteEditLog Before, NoteEditLog After)? _acceptedDraft;

    /// <summary>The last correction undone: in the note editor's draft, its last step (the reset too), else the last in the log.</summary>
    private string UndoCorrection(Request request) => _draftFrom is not null
        ? Correct(request, _ => _draftSteps.Count > 0 ? _draftSteps.Peek() : throw FacadeException.Unavailable("There is no correction to undo."), undo: true)
        : Correct(request, edits => edits.IsEmpty ? throw FacadeException.Unavailable("There is no correction to undo.") : edits.Undo());

    /// <summary>
    /// corrections.acceptDraft {"bar", "tool", "parameters", "index"} (version 0.6.1, #582 item 17.3, the iPad's Accept of the
    /// assistant's draft): the draft is made again from the call that made it (tools are pure) over the notes as they are, and
    /// its edits join the corrections as the web app's Accept adds them, removed, changed, then added. Refused when the draft
    /// no longer fits the notes or touches a note taken from a version. The model never calls this: only the player's Accept.
    /// corrections.undoDraft takes the whole draft back at once, as the web app's one Undo does.
    /// </summary>
    private string AcceptDraft(Request request)
    {
        JsonElement parameters = request.Element("parameters") ?? throw FacadeException.BadRequest("Send \"parameters\", the tool's.");
        var call = new SessionDraft("d1", request.Required("tool"), parameters, request.Int("index") ?? 0, DraftState.Proposed);
        AssistantDraft draft = call.Make(Assistant)
            ?? throw FacadeException.Unavailable(Words.T("This draft no longer fits the notes. Ask again.", "Этот черновик больше не подходит к нотам. Спросите ещё раз."));
        if (draft.Edits.Any(static edit => PerformanceTake.IsTaken(edit.NoteId)))
        {
            throw FacadeException.Unavailable(Words.T("The draft changes a note taken from a version.", "Черновик меняет ноту, взятую из версии."));
        }

        NoteEditLog before = Project.Edits;
        string answer = Correct(request, edits => draft.Edits.Aggregate(edits, (log, edit) => edit.Kind switch
        {
            DraftEditKind.Remove => log.Remove(Piece.Notes, edit.NoteId),
            DraftEditKind.Change => log.Change(Piece.Notes, edit.NoteId, edit.After!.Value),
            _ => log.Add(Piece.Notes, edit.After!.Value, edit.Track, edit.Hand, out _),
        }));
        _acceptedDraft = (before, Project.Edits);
        return answer;
    }

    /// <summary>
    /// corrections.variant {"bar", "staves": [{"part", "staff", "notes": [{"onset" (ticks in the bar), "pitch", "length"}]}]}
    /// (v1.3 i-2, ADR 0035 section 6): another engine's reading of a scan's bar to check, as scan.join answered it, chosen by
    /// the player: the bar's notes on those staves are taken out (a note tied past the bar line kept from it on) and the variant's put in, the player's corrections accepted
    /// together (one group), and corrections.undoDraft takes them back at once. Only that bar changes: a note of the variant
    /// starting outside it is refused, so no bar is ever fixed from another. Refused on a bar with a note taken from a version.
    /// </summary>
    private string TakeVariant(Request request)
    {
        Bar bar = CorrectionBar(request);
        VariantStaff[] staves = [.. request.Elements("staves").Select(staff => VariantStaff.Of(Request.Of(staff), bar))];
        if (staves.Length == 0)
        {
            throw FacadeException.BadRequest("Send \"staves\", the variant's.");
        }

        Note[] replaced = [.. Notes.Where(note => bar.Span.Contains(note.Span.Onset) && staves.Any(staff => staff.Track == note.Track && staff.Staff == StaffOf(note)))];
        if (replaced.Any(static note => PerformanceTake.IsTaken(note.Id)))
        {
            throw FacadeException.Unavailable(Words.T("The bar has a note taken from a version.", "В такте есть нота, взятая из версии."));
        }

        string group = Projects.Project.NextId("g_", Project.Edits.Edits.Select(static edit => edit.Group).OfType<string>());
        NoteEditLog before = Project.Edits;
        string answer = Correct(request, edits =>
        {
            NoteEditLog log = staves.Aggregate(replaced.Aggregate(edits, (current, note) => Leave(current, note, bar)), (current, staff) => staff.AddTo(current, Piece.Notes, replaced));
            return new NoteEditLog([.. edits.Edits, .. log.Edits.Skip(edits.Edits.Count).Select(edit => edit with { Group = group })]);
        });
        _acceptedDraft = (before, Project.Edits);
        return answer;
    }

    /// <summary>
    /// A note of the bar taken out for a variant: removed, or, when it sounds on past the bar line (tied into the next bar),
    /// kept from the bar line on, so the next bar sounds as before: no other bar changes.
    /// </summary>
    private NoteEditLog Leave(NoteEditLog log, Note note, Bar bar) => note.Span.End <= bar.Span.End
        ? log.Remove(Piece.Notes, note.Id)
        : log.Change(Piece.Notes, note.Id, NoteValues.Of(note) with { Span = new TickSpan(bar.Span.End, note.Span.End.Value - bar.Span.End.Value) });

    /// <summary>One staff of a variant as corrections.variant takes it: its track (the part from 0), its staff, and its notes in the bar's ticks.</summary>
    private sealed record VariantStaff(int Track, int Staff, IReadOnlyList<NoteValues> Notes)
    {
        public static VariantStaff Of(Request staff, Bar bar)
        {
            int part = staff.Int("part") ?? throw FacadeException.BadRequest("Each staff needs \"part\", from 1.");
            int number = staff.Int("staff") ?? throw FacadeException.BadRequest("Each staff needs \"staff\", from 1.");
            return new VariantStaff(part - 1, number, [.. staff.Elements("notes").Select(note => Values(Request.Of(note), bar))]);
        }

        /// <summary>The notes added to the log on this staff: as loud as the notes they replace there, the staff's hand when the track has hands.</summary>
        public NoteEditLog AddTo(NoteEditLog log, IReadOnlyList<Note> imported, IReadOnlyList<Note> replaced)
        {
            Hand? hand = imported.Any(note => note.Track == Track && note.Hand is not null) ? Staff switch { 1 => Hand.Right, 2 => Hand.Left, _ => null } : null;
            int velocity = replaced.FirstOrDefault(note => note.Track == Track)?.Velocity ?? 80;
            return Notes.Aggregate(log, (current, note) => current.Add(imported, note with { Velocity = velocity }, Track, hand, out _));
        }

        private static NoteValues Values(Request note, Bar bar)
        {
            long onset = bar.Span.Onset.Value + (note.Long("onset") ?? throw FacadeException.BadRequest("Each note needs \"onset\", in ticks in the bar."));
            int pitch = note.Int("pitch") ?? throw FacadeException.BadRequest("Each note needs \"pitch\".");
            long length = note.Long("length") ?? throw FacadeException.BadRequest("Each note needs \"length\", in ticks.");
            return bar.Span.Contains(new Tick(onset)) && pitch is >= 0 and <= Pitch.MaxMidiNumber && length > 0
                ? new NoteValues(new Pitch(pitch), new TickSpan(new Tick(onset), length), Note.MinVelocity)
                : throw FacadeException.BadRequest("A note of the variant starts outside its bar, or its pitch or length is out of range.");
        }
    }

    /// <summary>The staff of the part a note is on: the staff it was read on, else 2 for a left hand and 1 for any other.</summary>
    private static int StaffOf(Note note) => note.Source is MusicXmlSourceLocation read ? read.Staff : note.Hand?.Value == Hand.Left ? 2 : 1;

    private string UndoDraft(Request request)
    {
        if (_acceptedDraft is not { } accepted || !accepted.After.Equals(Project.Edits))
        {
            throw FacadeException.Unavailable(Words.T("Other corrections came after the draft: undo them one by one.", "После черновика были другие правки: отменяйте их по одной."));
        }

        _acceptedDraft = null;
        return Correct(request, _ => accepted.Before);
    }

    private Bar CorrectionBar(Request request)
    {
        int number = request.Int("bar") ?? throw FacadeException.BadRequest("Send \"bar\".");
        Bar bar;
        try
        {
            bar = Piece.Meter.GetBar(number);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw FacadeException.BadRequest($"There is no bar {number}.");
        }

        return bar.Span.Onset < Piece.End ? bar : throw FacadeException.BadRequest($"There is no bar {number}.");
    }

    /// <summary>The note named by "id" as it sounds now, refused when it came from a performance.</summary>
    private Note CorrectableNote(Request request)
    {
        string id = request.Required("id");
        if (PerformanceTake.IsTaken(id))
        {
            throw FacadeException.Unavailable("This note comes from a performance: restore the bar to correct the print.");
        }

        return Printed.FirstOrDefault(note => note.Id == id) ?? throw new FacadeException("notFound", $"There is no note {id}.");
    }

    /// <summary>The note's values moved by the steps asked for: "pitch" semitones, "onset" and "length" in sixteenths, "velocity" in tens.</summary>
    private static NoteValues Changed(Note note, Request request)
    {
        int pitch = request.Int("pitch") ?? 0;
        long onset = (request.Int("onset") ?? 0) * CorrectionStep;
        long length = (request.Int("length") ?? 0) * CorrectionStep;
        int velocity = (request.Int("velocity") ?? 0) * VelocityStep;
        return new(
            new Pitch(Math.Clamp(note.Pitch.MidiNumber + pitch, 0, Pitch.MaxMidiNumber)),
            new TickSpan(new Tick(Math.Max(0, note.Span.Onset.Value + onset)), Math.Max(1, note.Span.Duration + length)),
            Math.Clamp(note.Velocity + velocity, Note.MinVelocity, Note.MaxVelocity));
    }

    private string BeginDraft(Request request)
    {
        _draftFrom ??= Project.Edits;
        _draftSteps.Clear();
        return CorrectionsAnswer(CorrectionBar(request));
    }

    private string EndDraft(Request request, bool keep)
    {
        if (_draftFrom is not { } from)
        {
            throw FacadeException.Unavailable("No note editing has begun.");
        }

        _draftSteps.Clear();
        _draftFrom = null;
        return keep ? CorrectionsAnswer(CorrectionBar(request)) : Correct(request, _ => from);
    }

    /// <summary>A note added where "onset" (ticks) says, "pitch" (MIDI) and "length" (ticks) long; the track and hand of the nearest note unless sent.</summary>
    private string AddNote(Request request)
    {
        int pitch = request.Int("pitch") ?? throw FacadeException.BadRequest("Send \"pitch\".");
        long onset = request.Long("onset") ?? throw FacadeException.BadRequest("Send \"onset\", in ticks.");
        long length = request.Long("length") ?? CanonicalTime.TicksPerQuarter / 2;
        if (pitch is < 0 or > Pitch.MaxMidiNumber || onset < 0 || length <= 0)
        {
            throw FacadeException.BadRequest("\"pitch\" runs from 0 to 127, \"onset\" from 0 and \"length\" from 1.");
        }

        Note? near = Notes.OrderBy(note => Math.Abs(note.Span.Onset.Value - onset) + (Math.Abs(note.Pitch.MidiNumber - pitch) * CanonicalTime.TicksPerQuarter)).FirstOrDefault();
        int velocity = request.Int("velocity") ?? near?.Velocity ?? 80;
        int track = request.Int("track") ?? near?.Track ?? 0;
        Hand? hand = request.Text("hand") switch
        {
            "left" => Hand.Left,
            "right" => Hand.Right,
            _ => near?.Hand is null ? null : pitch < 60 ? Hand.Left : Hand.Right,
        };
        string? id = null;
        return Correct(request, edits =>
        {
            NoteEditLog added = edits.Add(Piece.Notes, new NoteValues(new Pitch(pitch), new TickSpan(new Tick(onset), length), Math.Clamp(velocity, Note.MinVelocity, Note.MaxVelocity)), track, hand, out string newId);
            id = newId;
            return added;
        }, added: () => id);
    }

    /// <summary>The note's values as sent: "pitch" (MIDI), "onset" and "length" in ticks, each unchanged when not sent.</summary>
    private static NoteValues Set(Note note, Request request)
    {
        int pitch = request.Int("pitch") ?? note.Pitch.MidiNumber;
        long onset = request.Long("onset") ?? note.Span.Onset.Value;
        long length = request.Long("length") ?? note.Span.Duration;
        return new(
            new Pitch(Math.Clamp(pitch, 0, Pitch.MaxMidiNumber)),
            new TickSpan(new Tick(Math.Max(0, onset)), Math.Max(1, length)),
            note.Velocity);
    }

    private string Correct(Request request, Func<NoteEditLog, NoteEditLog> change, bool undo = false, Func<string?>? added = null)
    {
        Bar bar = CorrectionBar(request);
        NoteEditLog edits;
        try
        {
            edits = change(Project.Edits);
            if (_layer == EditsLayer.Strict)
            {
                edits.Apply(Piece.Notes);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        if (!edits.Equals(Project.Edits))
        {
            if (_draftFrom is not null)
            {
                if (undo)
                {
                    _draftSteps.Pop();
                }
                else
                {
                    _draftSteps.Push(Project.Edits);
                }
            }

            Project = Project with { Edits = edits };
            RefreshNotes();
            // The selection keeps its passage over the corrected notes; a variation in progress was made from the old ones.
            Reselect();
            Chain = null;
            _chip = null;
        }

        return CorrectionsAnswer(bar, added?.Invoke());
    }

    /// <summary>
    /// The bar's notes as they sound, each with where it is, how long, how loud and whether the player corrected it; the
    /// imported notes taken out anywhere, to put back; and how many corrections the log holds.
    /// </summary>
    /// <param name="added">The id of the note just added, answered as "added".</param>
    private string CorrectionsAnswer(Bar bar, string? added = null) => Answer.Write(json =>
    {
        if (added is not null)
        {
            json.WriteString("added", added);
        }

        HashSet<string> edited = EditedNow();
        json.WriteNumber("bar", bar.Number);
        Answer.Span(json, "span", bar.Span);
        json.WriteStartArray("notes");
        foreach (Note note in Notes.Where(note => note.Span.Onset >= bar.Span.Onset && note.Span.Onset < bar.Span.End))
        {
            bool taken = PerformanceTake.IsTaken(note.Id);
            json.WriteStartObject();
            json.WriteString("id", note.Id);
            json.WriteNumber("pitch", note.Pitch.MidiNumber);
            json.WriteString("name", note.Pitch.ToString());
            CorrectionPlace(json, note.Span.Onset);
            json.WriteString("length", VariationFacts.Length(note.Span.Duration, Words));
            json.WriteNumber("velocity", note.Velocity);
            json.WriteBoolean("edited", edited.Contains(note.Id));
            json.WriteBoolean("taken", taken);
            json.WriteBoolean("canEarlier", !taken && note.Span.Onset.Value >= CorrectionStep);
            json.WriteBoolean("canShorter", !taken && note.Span.Duration > CorrectionStep);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        HashSet<string> kept = [.. Printed.Select(note => note.Id)];
        json.WriteStartArray("removed");
        foreach (Note note in Piece.Notes.Where(note => edited.Contains(note.Id) && !kept.Contains(note.Id)))
        {
            json.WriteStartObject();
            json.WriteString("id", note.Id);
            json.WriteString("name", note.Pitch.ToString());
            CorrectionPlace(json, note.Span.Onset);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteNumber("count", Project.Edits.Edits.Count);
        json.WriteBoolean("editing", _draftFrom is not null);
        json.WriteBoolean("canUndo", _draftFrom is not null ? _draftSteps.Count > 0 : !Project.Edits.IsEmpty);
        json.WriteBoolean("changed", _draftFrom is { } from && !from.Equals(Project.Edits));
        LayerFields(json);
    });

    /// <summary>Where a note starts: its bar and beat, and the rest of the beat in words when it starts off the beat.</summary>
    private void CorrectionPlace(Utf8JsonWriter json, Tick onset)
    {
        MetricPosition position = Piece.Meter.ToPosition(onset);
        json.WriteNumber("onset", onset.Value);
        json.WriteNumber("atBar", position.Bar);
        json.WriteNumber("beat", position.Beat);
        if (position.BeatOffset == 0)
        {
            json.WriteNull("offset");
        }
        else
        {
            json.WriteString("offset", VariationFacts.Length(position.BeatOffset, Words));
        }
    }
}