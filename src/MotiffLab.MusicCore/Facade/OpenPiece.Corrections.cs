using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
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

    private string CallOnCorrections(string name, Request request) => name switch
    {
        "corrections" => CorrectionsAnswer(CorrectionBar(request)),
        "corrections.begin" => BeginDraft(request),
        "corrections.save" => EndDraft(request, keep: true),
        "corrections.discard" => EndDraft(request, keep: false),
        "corrections.reset" => Correct(request, _ => NoteEditLog.Empty),
        "corrections.add" => AddNote(request),
        "corrections.set" => Correct(request, edits => edits.Change(Piece.Notes, CorrectableNote(request).Id, Set(CorrectableNote(request), request))),
        "corrections.change" => Correct(request, edits => edits.Change(Piece.Notes, CorrectableNote(request).Id, Changed(CorrectableNote(request), request))),
        "corrections.remove" => Correct(request, edits => edits.Remove(Piece.Notes, CorrectableNote(request).Id)),
        "corrections.revert" => Correct(request, edits => edits.Revert(request.Required("id"))),
        "corrections.undo" => _draftFrom is not null
            ? Correct(request, _ => _draftSteps.Count > 0 ? _draftSteps.Peek() : throw FacadeException.Unavailable("There is no correction to undo."), undo: true)
            : Correct(request, edits => edits.IsEmpty ? throw FacadeException.Unavailable("There is no correction to undo.") : edits.Undo()),
        _ => throw new FacadeException("unknownCall", $"There is no call \"{name}\"."),
    };

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
            edits.Apply(Piece.Notes);
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
            if (Selection is { } selection)
            {
                MotifSelection again = MotifSelector.Select(Notes, Piece.Meter, selection.Span, selection.Filter, MotifSelection.MaxStructureBars);
                Selection = again.NoteIds.Count > 0 ? again : null;
            }

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

        HashSet<string> edited = [.. Project.Edits.EditedIds];
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