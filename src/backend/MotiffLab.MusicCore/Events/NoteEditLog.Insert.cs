using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Events;

public sealed partial record NoteEditLog
{
    /// <summary>
    /// The log carried over to the notes the same piece gives once <paramref name="length"/> ticks of music are put in at
    /// <paramref name="at"/> (v1.3 i-1b: a page no engine read, read again into the same Notes), so each correction still
    /// applies to the same music; null when one cannot be carried exactly. Corrections move here into the same Notes (to other Notes read again: ReadingCarry): notes are
    /// numbered in time order, so a correction of an imported note names that note's number among <paramref name="after"/>,
    /// and every value it records from <paramref name="at"/> on moves on by <paramref name="length"/>; an added note keeps its
    /// id; a correction of a note across <paramref name="at"/> is not carried (<see cref="Crosses"/>). Exact means: laid on <paramref name="after"/>, the carried log applies, moves and leaves unfit the same corrections
    /// as on <paramref name="before"/>, and every note it changes or adds there it changes or adds here, moved so. The
    /// other notes are the music as read again: a note tied into the page put in sounds as the page now goes on.
    /// </summary>
    public NoteEditLog? CarriedOver(IReadOnlyList<Note> before, IReadOnlyList<Note> after, Tick at, long length)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (length < 0 || Crosses(at) || IdsMovedOn(before, after, at, length) is not { } ids)
        {
            return null;
        }

        string Id(string id) => ids.GetValueOrDefault(id) ?? id;
        NoteEditLog carried = new(Edits.Select(edit => edit with
        {
            NoteId = Id(edit.NoteId),
            Before = edit.Before is { } replaced ? MovedOn(replaced, at, length) : null,
            After = edit.After is { } values ? MovedOn(values, at, length) : null,
        }));
        EditsApplied was = ApplyEach(before), now = carried.ApplyEach(after);
        HashSet<string> touched = [.. was.EditedIds(this).Select(Id)];
        bool same = was.Applied.SequenceEqual(now.Applied)
            && was.Moved.Select(static moved => (moved.Index, moved.Shift)).SequenceEqual(now.Moved.Select(static moved => (moved.Index, moved.Shift)))
            && was.Unfit.Select(static unfit => (unfit.Index, unfit.Reason)).SequenceEqual(now.Unfit.Select(static unfit => (unfit.Index, unfit.Reason)))
            && touched.SetEquals(now.EditedIds(carried))
            && Faces(was.Notes.Where(note => touched.Contains(Id(note.Id)))
                .Select(note => note with { Id = Id(note.Id), Span = MovedOn(NoteValues.Of(note), at, length).Span }))
                .SequenceEqual(Faces(now.Notes.Where(note => touched.Contains(note.Id))));
        return same ? carried : null;
    }

    /// <summary>
    /// The id each note of <paramref name="before"/> has among <paramref name="after"/>, the same piece's notes once
    /// <paramref name="length"/> ticks of music are put in at <paramref name="at"/>: notes are numbered in time order, so a note
    /// is the one at its place among them, moved on from <paramref name="at"/>; null for a note not found there the same (on
    /// its track, with its values moved so), such as one tied into the music put in. Null when <paramref name="after"/> has
    /// fewer notes. What else names a note by its id follows it by this map too (<c>FollowedWork.Inserted</c>).
    /// </summary>
    internal static Dictionary<string, string?>? IdsMovedOn(IReadOnlyList<Note> before, IReadOnlyList<Note> after, Tick at, long length)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        int put = after.Count - before.Count;
        if (put < 0)
        {
            return null;
        }

        Dictionary<string, string?> ids = new(StringComparer.Ordinal);
        int head = before.Count(note => note.Span.Onset < at);
        for (int index = 0; index < before.Count; index++)
        {
            Note note = before[index];
            Note found = after[index < head ? index : index + put];
            ids[note.Id] = found.Track == note.Track && NoteValues.Of(found) == MovedOn(NoteValues.Of(note), at, length) ? found.Id : null;
        }

        return ids;
    }

    private static IEnumerable<NoteFace> Faces(IEnumerable<Note> notes) => notes.Select(NoteFace.Of).Order();

    /// <summary>
    /// Whether a correction records a note that sounds across <paramref name="at"/> (from before it to after it): music put in
    /// there would cut it, so it cannot be carried over (<see cref="CarriedOver"/>).
    /// </summary>
    public bool Crosses(Tick at) => Edits.Any(edit => Across(edit.Before, at) || Across(edit.After, at));

    private static bool Across(NoteValues? values, Tick at) => values is { } found && found.Span.Onset < at && found.Span.End > at;

    /// <summary>Values from <paramref name="at"/> on moved on by <paramref name="length"/>; earlier ones as they are.</summary>
    private static NoteValues MovedOn(NoteValues values, Tick at, long length) => values.Span.Onset < at
        ? values
        : values with { Span = new TickSpan(new Tick(values.Span.Onset.Value + length), values.Span.Duration) };

    /// <summary>What of a note a correction gives or keeps, to compare the notes before and after a carry.</summary>
    private readonly record struct NoteFace(string Id, int Pitch, long Onset, long Duration, int Velocity, int Track, Hand? Hand, Attribution Attribution) : IComparable<NoteFace>
    {
        public static NoteFace Of(Note note) =>
            new(note.Id, note.Pitch.MidiNumber, note.Span.Onset.Value, note.Span.Duration, note.Velocity, note.Track, note.Hand?.Value, note.Attribution);

        public int CompareTo(NoteFace other) => string.CompareOrdinal(Id, other.Id);
    }
}