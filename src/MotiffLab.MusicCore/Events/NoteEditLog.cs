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
/// One correction of an imported note (S1-I7): its values when the player changed it, and the new
/// values, or none when the player removed it.
/// </summary>
/// <param name="After">The new values; null when the note was removed.</param>
public sealed record NoteEdit(string NoteId, NoteValues Before, NoteValues? After)
{
    public bool IsRemoval => After is null;
}

/// <summary>
/// The player's corrections of imported notes, oldest first (S1-I7). The imported notes are never
/// changed: the corrected notes are the imported ones with the log applied, so dropping an edit
/// restores the imported value exactly. Every corrected note is marked user-edited.
/// </summary>
/// <remarks>
/// Each edit records the values it replaced, and applying the log checks them, so a log that no
/// longer fits the notes is refused rather than half applied. Pure: the same notes and log give the
/// same result. Compares by value.
/// </remarks>
public sealed record NoteEditLog
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
        ArgumentNullException.ThrowIfNull(imported);
        if (IsEmpty)
        {
            return imported;
        }

        Dictionary<string, Note?> current = imported.ToDictionary(static note => note.Id, static note => (Note?)note, StringComparer.Ordinal);
        foreach (NoteEdit edit in Edits)
        {
            if (!current.TryGetValue(edit.NoteId, out Note? note) || note is null)
            {
                throw new InvalidOperationException($"Note {edit.NoteId} is not in the piece.");
            }

            if (NoteValues.Of(note) != edit.Before)
            {
                throw new InvalidOperationException($"Note {edit.NoteId} is not as the edit found it.");
            }

            current[edit.NoteId] = edit.After is { } after
                ? note with { Pitch = after.Pitch, Span = after.Span, Velocity = after.Velocity, Attribution = Attribution.UserEdited }
                : null;
        }

        return [.. imported.Select(note => current[note.Id]).OfType<Note>().OrderBy(static note => note.Span.Onset)];
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

    /// <summary>The log without its last edit.</summary>
    /// <exception cref="InvalidOperationException">The log is empty.</exception>
    public NoteEditLog Undo() => IsEmpty
        ? throw new InvalidOperationException("There is no edit to undo.")
        : new(Edits.Take(Edits.Count - 1));

    /// <summary>The log without any edit of note <paramref name="noteId"/>, so the note is back as imported. Edits of other notes stay.</summary>
    public NoteEditLog Revert(string noteId) => new(Edits.Where(edit => edit.NoteId != noteId));

    public bool Equals(NoteEditLog? other) => other is not null && Edits.SequenceEqual(other.Edits);

    public override int GetHashCode() => Edits.Aggregate(0, static (hash, edit) => HashCode.Combine(hash, edit));

    private Note Current(IReadOnlyList<Note> imported, string noteId)
    {
        ArgumentNullException.ThrowIfNull(noteId);
        return Apply(imported).FirstOrDefault(note => note.Id == noteId)
            ?? throw new ArgumentException($"Note {noteId} is not in the piece.", nameof(noteId));
    }
}