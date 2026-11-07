using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assembly;

/// <summary>
/// A place in the player's notes held by a note, not by a time (decision record 0024, section 4): the onset of
/// <paramref name="NoteId"/> plus <paramref name="Offset"/> ticks, so material put in or taken out earlier never moves
/// what a later step meant. Without a note (nothing to hold on to) <paramref name="Offset"/> is a time from the start.
/// </summary>
public readonly record struct TimeAnchor(string? NoteId, long Offset)
{
    /// <summary>The anchor for a time: the note that starts there or nearest to it.</summary>
    public static TimeAnchor At(IReadOnlyList<Note> notes, Tick position)
    {
        ArgumentNullException.ThrowIfNull(notes);
        Note? nearest = null;
        long best = long.MaxValue;
        foreach (Note note in notes)
        {
            long distance = Math.Abs(note.Span.Onset.Value - position.Value);
            if (distance < best || (distance == best && nearest is not null && string.CompareOrdinal(note.Id, nearest.Id) < 0))
            {
                (nearest, best) = (note, distance);
            }
        }

        return nearest is null ? new TimeAnchor(null, position.Value) : new TimeAnchor(nearest.Id, position.Value - nearest.Span.Onset.Value);
    }

    /// <summary>The start of a note.</summary>
    public static TimeAnchor StartOf(Note note) => new((note ?? throw new ArgumentNullException(nameof(note))).Id, 0);

    /// <summary>The end of a note.</summary>
    public static TimeAnchor EndOf(Note note) => new((note ?? throw new ArgumentNullException(nameof(note))).Id, note.Span.Duration);
}

/// <summary>Where a carry goes in the player's notes: replacing <c>[Start, End)</c>, or inserted at <see cref="Start"/> when <see cref="End"/> is null.</summary>
public sealed record AssemblyPlace(TimeAnchor Start, TimeAnchor? End)
{
    public bool Inserts => End is null;

    /// <summary>The notes from <paramref name="first"/> to <paramref name="last"/>, as the player selected them: from the start of the first to the end of the last.</summary>
    public static AssemblyPlace Between(Note first, Note last) => new(TimeAnchor.StartOf(first), TimeAnchor.EndOf(last));

    /// <summary>A time span held by the nearest notes, as a bar matched by the comparison.</summary>
    public static AssemblyPlace Over(IReadOnlyList<Note> notes, TickSpan span) => new(TimeAnchor.At(notes, span.Onset), TimeAnchor.At(notes, span.End));

    /// <summary>Inserting at a time: before the music that starts there.</summary>
    public static AssemblyPlace InsertAt(IReadOnlyList<Note> notes, Tick position) => new(TimeAnchor.At(notes, position), null);
}

/// <summary>
/// One step of the assembly's journal (decision record 0024, section 4): a transaction with its number («перенос №3»)
/// and when it was made. The player's notes are rebuilt from the base by the steps in order.
/// </summary>
public abstract record AssemblyStep(int Number, DateTimeOffset At)
{
    public int Number { get; } = Number > 0 ? Number : throw new ArgumentOutOfRangeException(nameof(Number), Number, "Steps are numbered from 1.");
}

/// <summary>
/// A fragment of a source carried into the player's notes («В ноты…»): the source's notes that start in
/// <paramref name="From"/>, put at <paramref name="To"/>, raised or lowered by <paramref name="Transpose"/> semitones.
/// </summary>
public sealed record CarryStep(int Number, DateTimeOffset At, string SourceId, TickSpan From, AssemblyPlace To, int Transpose = 0) : AssemblyStep(Number, At)
{
    public string SourceId { get; } = !string.IsNullOrWhiteSpace(SourceId) ? SourceId : throw new ArgumentException("A carry names its source.", nameof(SourceId));

    public int Transpose { get; } = Transpose is >= -24 and <= 24 ? Transpose : throw new ArgumentOutOfRangeException(nameof(Transpose), Transpose, "A carry moves by at most two octaves.");

    /// <summary>The id a carried note takes: its source note's id behind the step's number, so it stays the same on every rebuild.</summary>
    public string IdOf(string sourceNoteId) => $"c{Number}.{sourceNoteId}";
}

/// <summary>One note changed in the player's notes («✎ Ноты»): removed, or moved by <paramref name="Semitones"/>.</summary>
public sealed record NoteStep(int Number, DateTimeOffset At, string NoteId, int? Semitones) : AssemblyStep(Number, At)
{
    public string NoteId { get; } = !string.IsNullOrWhiteSpace(NoteId) ? NoteId : throw new ArgumentException("A note change names its note.", nameof(NoteId));

    public bool Removes => Semitones is null;
}

/// <summary>The notes of a place in the player's notes moved by <paramref name="Semitones"/> («Транспонировать…»).</summary>
public sealed record TransposeStep(int Number, DateTimeOffset At, AssemblyPlace Place, int Semitones) : AssemblyStep(Number, At)
{
    public int Semitones { get; } = Semitones is >= -24 and <= 24 and not 0 ? Semitones : throw new ArgumentOutOfRangeException(nameof(Semitones), Semitones, "A transposition moves by 1 to 24 semitones up or down.");
}