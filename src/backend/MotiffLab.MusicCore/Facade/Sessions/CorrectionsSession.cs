using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade.Sessions;

/// <summary>
/// The player's corrections of the notes and the layer they are laid over the notes with (audit W1.7, the second typed
/// session; S1-I7, contract 4, the layer of version 0.5 within contract 12): the note editor's draft and its steps, the
/// assistant's draft or the scan's variant accepted last, and how the corrections are laid. The edit log itself is the
/// project's: each correction here answers the log it makes, and the open piece keeps it in its project. The session owns
/// the rest: every change goes through a method here that says what it changes. It reads the piece through
/// <see cref="SessionPiece"/>, takes typed values, never a request, and returns typed results; it writes no JSON.
/// OpenPiece.Corrections.cs is its JSON adapter.
/// </summary>
/// <remarks>
/// The note editor on the roll (testing item 15) works on a draft: <see cref="Begin"/> starts it, every correction after it
/// sounds and shows at once but the project file keeps the corrections as they were (<see cref="Kept"/>) until
/// <see cref="End"/> keeps them; <see cref="End"/> without keeping puts them back, <see cref="Reset"/> takes every
/// correction out, and <see cref="Undo"/> steps back through the draft, the reset too. While the layer is off no correction
/// is written against the notes as imported (<see cref="RefuseWhileOff"/>).
/// </remarks>
internal sealed class CorrectionsSession
{
    /// <summary>A step in time: a sixteenth, as in the web app.</summary>
    public const long TimeStep = CanonicalTime.TicksPerQuarter / 4;

    /// <summary>A step in loudness.</summary>
    public const int VelocityStep = 10;

    /// <summary>The corrections when the draft began; null when there is no draft.</summary>
    private NoteEditLog? _draftFrom;

    /// <summary>The draft's logs before each of its steps, the last first undone.</summary>
    private readonly Stack<NoteEditLog> _draftSteps = new();

    /// <summary>The corrections before and after the assistant's draft or the variant accepted last, so one Undo takes it back whole while nothing came after it.</summary>
    private (NoteEditLog Before, NoteEditLog After)? _acceptedDraft;

    /// <summary>The corrections laid one by one over the notes, for the log they were laid from.</summary>
    private (NoteEditLog Log, EditsApplied Applied)? _layered;

    /// <summary>How the corrections are laid over the notes the piece is open on.</summary>
    public EditsLayer Layer { get; private set; } = EditsLayer.Strict;

    /// <summary>The ids of the motifs, variations and ideas left out when the piece opened, because they no longer fit the notes.</summary>
    public IReadOnlyList<string> Hidden { get; private set; } = [];

    /// <summary>The note editor's draft is open.</summary>
    public bool Editing => _draftFrom is not null;

    /// <summary>A piece opened with <paramref name="layer"/>, which left out <paramref name="hidden"/>.</summary>
    public void Start(EditsLayer layer, IReadOnlyList<string> hidden)
    {
        Layer = layer;
        Hidden = hidden;
    }

    /// <summary>Refuses a correction while the layer is off.</summary>
    public void RefuseWhileOff(Words words)
    {
        if (Layer == EditsLayer.Off)
        {
            throw FacadeException.Unavailable(words.T("Corrections are off.", "Правки выключены."));
        }
    }

    /// <summary>Refuses to switch the layer while the note editor's draft is open.</summary>
    public void RefuseWhileEditing(Words words)
    {
        if (Editing)
        {
            throw FacadeException.Unavailable(words.T("Save or discard the note editing first.", "Сначала сохраните или отмените правку нот."));
        }
    }

    /// <summary>The corrections the draft began from, refused when no draft is open.</summary>
    public NoteEditLog DraftBegun() => _draftFrom ?? throw FacadeException.Unavailable("No note editing has begun.");

    /// <summary>The corrections before the draft or the variant accepted last, refused when other corrections came after it.</summary>
    public NoteEditLog AcceptedBefore(SessionPiece piece) =>
        _acceptedDraft is { } accepted && accepted.After.Equals(piece.Project.Edits)
            ? accepted.Before
            : throw FacadeException.Unavailable(piece.Words.T("Other corrections came after the draft: undo them one by one.", "После черновика были другие правки: отменяйте их по одной."));

    /// <summary>The assistant's draft made again over the notes, refused when it no longer fits them or touches a note taken from a version.</summary>
    public static AssistantDraft Acceptable(SessionPiece piece, AssistantDraft? draft)
    {
        AssistantDraft made = draft ?? throw FacadeException.Unavailable(piece.Words.T("This draft no longer fits the notes. Ask again.", "Этот черновик больше не подходит к нотам. Спросите ещё раз."));
        return made.Edits.Any(static edit => PerformanceTake.IsTaken(edit.NoteId))
            ? throw FacadeException.Unavailable(piece.Words.T("The draft changes a note taken from a version.", "Черновик меняет ноту, взятую из версии."))
            : made;
    }

    /// <summary>The note <paramref name="id"/> as it sounds now, refused when it came from a performance.</summary>
    public Note Correctable(SessionPiece piece, string id)
    {
        if (PerformanceTake.IsTaken(id))
        {
            throw FacadeException.Unavailable("This note comes from a performance: restore the bar to correct the print.");
        }

        return Corrected(piece).FirstOrDefault(note => note.Id == id) ?? throw new FacadeException("notFound", $"There is no note {id}.");
    }

    /// <summary>The imported notes with the corrections as the layer lays them: the printed notes every performance is compared with.</summary>
    public IReadOnlyList<Note> Corrected(SessionPiece piece) => Layer switch
    {
        EditsLayer.Off => piece.Piece.Notes,
        EditsLayer.Lenient => Layered(piece).Notes,
        _ => piece.Project.Edits.Apply(piece.Piece.Notes),
    };

    /// <summary>The corrections the project file keeps: those the note editor's draft began from while it is open (testing item 15).</summary>
    public NoteEditLog Kept(SessionPiece piece) => _draftFrom ?? piece.Project.Edits;

    /// <summary>The note editor's draft begins (again from its first step when it is open).</summary>
    public void Begin(SessionPiece piece)
    {
        _draftFrom ??= piece.Project.Edits;
        _draftSteps.Clear();
    }

    /// <summary>The draft ends: its corrections kept (<paramref name="keep"/>), else those it began from put back.</summary>
    public NoteEditLog? End(SessionPiece piece, bool keep)
    {
        NoteEditLog from = DraftBegun();
        _draftSteps.Clear();
        _draftFrom = null;
        return keep ? null : Correct(piece, _ => from);
    }

    /// <summary>The note moved by steps: <paramref name="pitch"/> semitones, <paramref name="onset"/> and <paramref name="length"/> in sixteenths, <paramref name="velocity"/> in tens.</summary>
    public NoteEditLog? Move(SessionPiece piece, Note note, int pitch, int onset, int length, int velocity) =>
        Correct(piece, edits => edits.Change(piece.Piece.Notes, note.Id, new(
            new Pitch(Math.Clamp(note.Pitch.MidiNumber + pitch, 0, Pitch.MaxMidiNumber)),
            new TickSpan(new Tick(Math.Max(0, note.Span.Onset.Value + (onset * TimeStep))), Math.Max(1, note.Span.Duration + (length * TimeStep))),
            Math.Clamp(note.Velocity + (velocity * VelocityStep), Note.MinVelocity, Note.MaxVelocity))));

    /// <summary>The note's pitch (MIDI), onset and length (ticks) set at once, each unchanged when null.</summary>
    public NoteEditLog? Set(SessionPiece piece, Note note, int? pitch, long? onset, long? length) =>
        Correct(piece, edits => edits.Change(piece.Piece.Notes, note.Id, new(
            new Pitch(Math.Clamp(pitch ?? note.Pitch.MidiNumber, 0, Pitch.MaxMidiNumber)),
            new TickSpan(new Tick(Math.Max(0, onset ?? note.Span.Onset.Value)), Math.Max(1, length ?? note.Span.Duration)),
            note.Velocity)));

    /// <summary>The note taken out.</summary>
    public NoteEditLog? Remove(SessionPiece piece, Note note) => Correct(piece, edits => edits.Remove(piece.Piece.Notes, note.Id));

    /// <summary>The note put back as imported: its corrections, and those of the notes whose corrections the layer moved onto it.</summary>
    public NoteEditLog? Revert(SessionPiece piece, string noteId)
    {
        IEnumerable<string> laidOn = Layer == EditsLayer.Strict ? [noteId] : Layered(piece).NamedFor(noteId);
        return Correct(piece, edits => laidOn.Aggregate(edits, static (log, id) => log.Revert(id)));
    }

    /// <summary>Every correction taken out: the notes as imported.</summary>
    public NoteEditLog? Reset(SessionPiece piece) => Correct(piece, static _ => NoteEditLog.Empty);

    /// <summary>The last correction undone: in the note editor's draft, its last step (the reset too), else the last in the log.</summary>
    public NoteEditLog? Undo(SessionPiece piece) => _draftFrom is not null
        ? Correct(piece, _ => _draftSteps.Count > 0 ? _draftSteps.Peek() : throw FacadeException.Unavailable("There is no correction to undo."), undo: true)
        : Correct(piece, static edits => edits.IsEmpty ? throw FacadeException.Unavailable("There is no correction to undo.") : edits.Undo());

    /// <summary>
    /// A note added at <paramref name="onset"/> (ticks), <paramref name="pitch"/> (MIDI), <paramref name="length"/> ticks
    /// long; as loud as, on the track of, and in the hand by its pitch of the nearest note unless given. <paramref name="added"/>
    /// is its id.
    /// </summary>
    public NoteEditLog? Add(SessionPiece piece, int pitch, long onset, long length, int? velocity, int? track, Hand? hand, out string? added)
    {
        Note? near = piece.Notes.OrderBy(note => Math.Abs(note.Span.Onset.Value - onset) + (Math.Abs(note.Pitch.MidiNumber - pitch) * CanonicalTime.TicksPerQuarter)).FirstOrDefault();
        int loud = velocity ?? near?.Velocity ?? 80;
        int on = track ?? near?.Track ?? 0;
        Hand? played = hand ?? (near?.Hand is null ? null : pitch < 60 ? Hand.Left : Hand.Right);
        string? id = null;
        NoteEditLog? edits = Correct(piece, log =>
        {
            NoteEditLog with = log.Add(piece.Piece.Notes, new NoteValues(new Pitch(pitch), new TickSpan(new Tick(onset), length), Math.Clamp(loud, Note.MinVelocity, Note.MaxVelocity)), on, played, out string newId);
            id = newId;
            return with;
        });
        added = id;
        return edits;
    }

    /// <summary>
    /// The assistant's draft accepted (version 0.6.1, #582 item 17.3): its edits join the corrections as the web app's Accept
    /// adds them, removed, changed, then added; <see cref="UndoDraft"/> takes it back whole.
    /// </summary>
    public NoteEditLog? Accept(SessionPiece piece, AssistantDraft draft)
    {
        NoteEditLog before = piece.Project.Edits;
        NoteEditLog? edits = Correct(piece, log => draft.Edits.Aggregate(log, (current, edit) => edit.Kind switch
        {
            DraftEditKind.Remove => current.Remove(piece.Piece.Notes, edit.NoteId),
            DraftEditKind.Change => current.Change(piece.Piece.Notes, edit.NoteId, edit.After!.Value),
            _ => current.Add(piece.Piece.Notes, edit.After!.Value, edit.Track, edit.Hand, out _),
        }));
        _acceptedDraft = (before, edits ?? before);
        return edits;
    }

    /// <summary>
    /// Another engine's reading of a scan's bar chosen (v1.3 i-2, ADR 0035 section 6): the bar's notes on those staves taken
    /// out (a note tied past the bar line kept from it on) and the variant's put in, accepted together (one group), so
    /// <see cref="UndoDraft"/> takes them back at once. Refused on a bar with a note taken from a version.
    /// </summary>
    public NoteEditLog? TakeVariant(SessionPiece piece, Bar bar, IReadOnlyList<VariantStaff> staves)
    {
        Note[] replaced = [.. piece.Notes.Where(note => bar.Span.Contains(note.Span.Onset) && staves.Any(staff => staff.Track == note.Track && staff.Staff == StaffOf(note)))];
        if (replaced.Any(static note => PerformanceTake.IsTaken(note.Id)))
        {
            throw FacadeException.Unavailable(piece.Words.T("The bar has a note taken from a version.", "В такте есть нота, взятая из версии."));
        }

        string group = Project.NextId("g_", piece.Project.Edits.Edits.Select(static edit => edit.Group).OfType<string>());
        NoteEditLog before = piece.Project.Edits;
        NoteEditLog? edits = Correct(piece, log =>
        {
            NoteEditLog taken = staves.Aggregate(replaced.Aggregate(log, (current, note) => Leave(piece, current, note, bar)), (current, staff) => AddTo(staff, current, piece.Piece.Notes, replaced));
            return new NoteEditLog([.. log.Edits, .. taken.Edits.Skip(log.Edits.Count).Select(edit => edit with { Group = group })]);
        });
        _acceptedDraft = (before, edits ?? before);
        return edits;
    }

    /// <summary>The assistant's draft or the variant accepted last taken back whole, refused when other corrections came after it.</summary>
    public NoteEditLog? UndoDraft(SessionPiece piece)
    {
        NoteEditLog before = AcceptedBefore(piece);
        _acceptedDraft = null;
        return Correct(piece, _ => before);
    }

    /// <summary>The layer switched: on lays the corrections one by one, off leaves them out; whether it changed.</summary>
    public bool Switch(bool on)
    {
        EditsLayer layer = on ? EditsLayer.Lenient : EditsLayer.Off;
        if (layer == Layer)
        {
            return false;
        }

        Layer = layer;
        return true;
    }

    /// <summary>The corrections of <paramref name="noteIds"/> that no longer fit taken out of the log; the others stay.</summary>
    public NoteEditLog? DropUnfit(SessionPiece piece, IReadOnlyList<string> noteIds)
    {
        NoteEditLog edits = piece.Project.Edits.DropUnfit(piece.Piece.Notes, noteIds);
        if (edits.Equals(piece.Project.Edits))
        {
            return null;
        }

        if (_draftFrom is not null)
        {
            _draftSteps.Push(piece.Project.Edits);
        }

        return edits;
    }

    /// <summary>
    /// The bar's notes as they sound, each with whether the player corrected it and how it can move; the imported notes
    /// taken out anywhere, to put back; how many corrections the log holds; the draft; and the layer.
    /// </summary>
    public BarCorrections Bar(SessionPiece piece, Bar bar)
    {
        HashSet<string> edited = EditedNow(piece);
        CorrectionNote[] notes = [.. piece.Notes.Where(note => note.Span.Onset >= bar.Span.Onset && note.Span.Onset < bar.Span.End).Select(note =>
        {
            bool taken = PerformanceTake.IsTaken(note.Id);
            return new CorrectionNote(note, edited.Contains(note.Id), taken, CanEarlier: !taken && note.Span.Onset.Value >= TimeStep, CanShorter: !taken && note.Span.Duration > TimeStep);
        })];
        HashSet<string> kept = [.. Corrected(piece).Select(note => note.Id)];
        Note[] removed = [.. piece.Piece.Notes.Where(note => edited.Contains(note.Id) && !kept.Contains(note.Id))];
        NoteEditLog log = piece.Project.Edits;
        return new BarCorrections(bar, notes, removed, log.Edits.Count, Editing, _draftFrom is not null ? _draftSteps.Count > 0 : !log.IsEmpty, _draftFrom is { } from && !from.Equals(log), Laid(piece));
    }

    /// <summary>The layer and what became of each correction; null when the corrections are laid strictly.</summary>
    public LaidCorrections? Laid(SessionPiece piece)
    {
        if (Layer == EditsLayer.Strict)
        {
            return null;
        }

        EditsApplied applied = Layered(piece);
        MeterMap meter = piece.Piece.Meter;
        return new LaidCorrections(
            Layer == EditsLayer.Off,
            piece.Project.EditsOff,
            applied.Applied.Count,
            [.. applied.Moved.Select(moved => new MisfitCorrection(moved.Edit.NoteId, BarOf(moved.Edit, meter), moved.Reason, moved.NoteId))],
            [.. applied.Unfit.Select(unfit => new MisfitCorrection(unfit.Edit.NoteId, BarOf(unfit.Edit, meter), unfit.Reason))],
            Hidden);
    }

    /// <summary>The bar a correction is in: where the note it found started, or where an added note starts.</summary>
    private static int BarOf(NoteEdit edit, MeterMap meter) => meter.BarAt((edit.Before ?? edit.After)?.Span.Onset ?? Tick.Zero).Number;

    /// <summary>The corrections laid one by one over the imported notes, read again only when the log changed.</summary>
    private EditsApplied Layered(SessionPiece piece)
    {
        NoteEditLog log = piece.Project.Edits;
        if (_layered is { } cached && ReferenceEquals(cached.Log, log))
        {
            return cached.Applied;
        }

        EditsApplied applied = log.ApplyEach(piece.Piece.Notes);
        _layered = (log, applied);
        return applied;
    }

    /// <summary>The notes the corrections now change, remove or add.</summary>
    private HashSet<string> EditedNow(SessionPiece piece) => Layer switch
    {
        EditsLayer.Strict => [.. piece.Project.Edits.EditedIds],
        EditsLayer.Off => [],
        _ => [.. Layered(piece).EditedIds(piece.Project.Edits)],
    };

    /// <summary>
    /// The log <paramref name="change"/> makes of the project's, refused when it does not fit the notes as laid strictly;
    /// null when nothing changed. In the note editor's draft the log before it is its step (an undo takes the last off).
    /// </summary>
    private NoteEditLog? Correct(SessionPiece piece, Func<NoteEditLog, NoteEditLog> change, bool undo = false)
    {
        NoteEditLog edits;
        try
        {
            edits = change(piece.Project.Edits);
            if (Layer == EditsLayer.Strict)
            {
                edits.Apply(piece.Piece.Notes);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
        catch (OverflowException)
        {
            throw FacadeException.BadRequest("The note would end past the longest time a piece can hold.");
        }

        if (edits.Equals(piece.Project.Edits))
        {
            return null;
        }

        if (_draftFrom is not null)
        {
            if (undo)
            {
                _draftSteps.Pop();
            }
            else
            {
                _draftSteps.Push(piece.Project.Edits);
            }
        }

        return edits;
    }

    /// <summary>
    /// A note of the bar taken out for a variant: removed, or, when it sounds on past the bar line (tied into the next bar),
    /// kept from the bar line on, so the next bar sounds as before: no other bar changes.
    /// </summary>
    private static NoteEditLog Leave(SessionPiece piece, NoteEditLog log, Note note, Bar bar) => note.Span.End <= bar.Span.End
        ? log.Remove(piece.Piece.Notes, note.Id)
        : log.Change(piece.Piece.Notes, note.Id, NoteValues.Of(note) with { Span = new TickSpan(bar.Span.End, note.Span.End.Value - bar.Span.End.Value) });

    /// <summary>A variant's notes added to the log on its staff: as loud as the notes they replace there, the staff's hand when the track has hands.</summary>
    private static NoteEditLog AddTo(VariantStaff staff, NoteEditLog log, IReadOnlyList<Note> imported, IReadOnlyList<Note> replaced)
    {
        Hand? hand = imported.Any(note => note.Track == staff.Track && note.Hand is not null) ? staff.Staff switch { 1 => Hand.Right, 2 => Hand.Left, _ => null } : null;
        int velocity = replaced.FirstOrDefault(note => note.Track == staff.Track)?.Velocity ?? 80;
        return staff.Notes.Aggregate(log, (current, note) => current.Add(imported, note with { Velocity = velocity }, staff.Track, hand, out _));
    }

    /// <summary>The staff of the part a note is on: the staff it was read on, else 2 for a left hand and 1 for any other.</summary>
    private static int StaffOf(Note note) => note.Source is MusicXmlSourceLocation read ? read.Staff : note.Hand?.Value == Hand.Left ? 2 : 1;
}