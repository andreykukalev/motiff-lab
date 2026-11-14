using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Events;

/// <summary>The values of a note a correction can change: pitch, timing and velocity.</summary>
public readonly record struct NoteValues(Pitch Pitch, TickSpan Span, int Velocity)
{
    public static NoteValues Of(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return new(note.Pitch, note.Span, note.Velocity);
    }
}

/// <summary>
/// One correction of the notes (S1-I7; testing item 15): a note's values when the player changed it and the new values,
/// or none when the player removed it; or a note the player added, with no values before.
/// </summary>
/// <param name="Before">The values the edit replaced; null when the edit adds the note.</param>
/// <param name="After">The new values; null when the note was removed.</param>
public sealed record NoteEdit(string NoteId, NoteValues? Before, NoteValues? After)
{
    public bool IsRemoval => After is null;

    /// <summary>True when the edit adds a note the piece did not have, such as one a scan missed.</summary>
    public bool IsAddition => Before is null;

    /// <summary>The track an added note goes in; 0 for every other edit.</summary>
    public int Track { get; init; }

    /// <summary>The hand an added note is played with, when the piece has hands; null otherwise and for every other edit.</summary>
    public Hand? Hand { get; init; }
}

/// <summary>
/// The player's corrections of imported notes, oldest first (S1-I7): changed, removed and added notes (testing item 15).
/// The imported notes are never changed: the corrected notes are the imported ones with the log applied, so dropping an
/// edit restores the imported value exactly. Every corrected or added note is marked user-edited.
/// </summary>
/// <remarks>
/// Each edit records the values it replaced, and applying the log checks them, so a log that no
/// longer fits the notes is refused rather than half applied (<see cref="Apply"/>); <see cref="ApplyEach"/> lays
/// the edits one by one instead and says which no longer fit (version 0.5). Pure: the same notes and log give the
/// same result. Compares by value.
/// </remarks>
public sealed partial record NoteEditLog
{
    public NoteEditLog(IEnumerable<NoteEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        Edits = [.. edits];
    }

    public static NoteEditLog Empty { get; } = new([]);

    public IReadOnlyList<NoteEdit> Edits { get; }

    public bool IsEmpty => Edits.Count == 0;

    /// <summary>The ids of the notes the log changes or removes, in the order they were first edited.</summary>
    public IEnumerable<string> EditedIds => Edits.Select(static edit => edit.NoteId).Distinct(StringComparer.Ordinal);

    /// <summary>
    /// The imported notes with every edit applied, in the imported order and then by onset; removed
    /// notes are left out, and changed notes are marked user-edited.
    /// </summary>
    /// <exception cref="InvalidOperationException">An edit names a note that is not there, or one whose values are not what the edit replaced.</exception>
    public IReadOnlyList<Note> Apply(IReadOnlyList<Note> imported)
    {
        EditsApplied each = ApplyEach(imported);
        return each.AllFit ? each.Notes : throw Refusal(each);
    }

    /// <summary>The log with note <paramref name="noteId"/> changed to <paramref name="after"/>; the same log when nothing changes.</summary>
    /// <exception cref="ArgumentException">The values are out of range, or the note is not in the piece.</exception>
    public NoteEditLog Change(IReadOnlyList<Note> imported, string noteId, NoteValues after)
    {
        if (after.Velocity is < Note.MinVelocity or > Note.MaxVelocity)
        {
            throw new ArgumentException($"Velocity runs from {Note.MinVelocity} to {Note.MaxVelocity}.", nameof(after));
        }

        Note note = Current(imported, noteId);
        NoteValues before = NoteValues.Of(note);
        return before == after ? this : new([.. Edits, new NoteEdit(noteId, before, after)]);
    }

    /// <summary>The log with note <paramref name="noteId"/> removed.</summary>
    /// <exception cref="ArgumentException">The note is not in the piece, or was already removed.</exception>
    public NoteEditLog Remove(IReadOnlyList<Note> imported, string noteId) =>
        new([.. Edits, new NoteEdit(noteId, NoteValues.Of(Current(imported, noteId)), null)]);

    /// <summary>
    /// The log with a note added (testing item 15), such as one a scan missed: its values, its track and its hand. The new
    /// note's id is the first of "a1", "a2", … that no note of the piece or the log has; <paramref name="id"/> gives it.
    /// </summary>
    /// <exception cref="ArgumentException">The values are out of range.</exception>
    public NoteEditLog Add(IReadOnlyList<Note> imported, NoteValues values, int track, Hand? hand, out string id)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentOutOfRangeException.ThrowIfNegative(track);
        if (values.Velocity is < Note.MinVelocity or > Note.MaxVelocity)
        {
            throw new ArgumentException($"Velocity runs from {Note.MinVelocity} to {Note.MaxVelocity}.", nameof(values));
        }

        var taken = imported.Select(static note => note.Id).Concat(EditedIds).ToHashSet(StringComparer.Ordinal);
        int number = 1;
        while (taken.Contains($"a{number}"))
        {
            number++;
        }

        id = $"a{number}";
        return new([.. Edits, new NoteEdit(id, null, values) { Track = track, Hand = hand }]);
    }

    /// <summary>The log without its last edit.</summary>
    /// <exception cref="InvalidOperationException">The log is empty.</exception>
    public NoteEditLog Undo() => IsEmpty
        ? throw new InvalidOperationException("There is no edit to undo.")
        : new(Edits.Take(Edits.Count - 1));

    /// <summary>The log without any edit of note <paramref name="noteId"/>, so the note is back as imported, or gone when the player added it. Edits of other notes stay.</summary>
    public NoteEditLog Revert(string noteId) => new(Edits.Where(edit => edit.NoteId != noteId));

    public bool Equals(NoteEditLog? other) => other is not null && Edits.SequenceEqual(other.Edits);

    public override int GetHashCode() => Edits.Aggregate(0, static (hash, edit) => HashCode.Combine(hash, edit));

    private Note Current(IReadOnlyList<Note> imported, string noteId)
    {
        ArgumentNullException.ThrowIfNull(noteId);
        // The notes as they are with the corrections that fit, so a correction can be made while another no longer fits.
        return ApplyEach(imported).Notes.FirstOrDefault(note => note.Id == noteId)
            ?? throw new ArgumentException($"Note {noteId} is not in the piece.", nameof(noteId));
    }
}