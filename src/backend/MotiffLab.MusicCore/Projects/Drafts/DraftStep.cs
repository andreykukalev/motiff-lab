using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>The kind of a приём, the label of a step that took or left one (decision record 0034, section 4).</summary>
public enum TechniqueKind
{
    Fill,
    Bass,
    Simplify,
    Ornament,
    Octave,
    Rhythm,
    InsertBars,
    Other,
}

/// <summary>The приём a step came from: its kind and the place's bars, from 1. A label only: the rebuild never reads it.</summary>
public sealed record DraftTechnique(TechniqueKind Kind, IReadOnlyList<int> Bars)
{
    public IReadOnlyList<int> Bars { get; } = Bars is { Count: > 0 } && Bars.All(static bar => bar >= 1) ? [.. Bars] : throw new ArgumentException("A приём names the place's bars, from 1.", nameof(Bars));

    public bool Equals(DraftTechnique? other) => other is not null && Kind == other.Kind && Bars.SequenceEqual(other.Bars);

    public override int GetHashCode() => HashCode.Combine(Kind, Bars.Count);
}

/// <summary>Bars of a variant put in whole at a place tick (<see cref="TechniqueKind.InsertBars"/>): the music after them moves on.</summary>
/// <param name="At">Where in the place, in its ticks.</param>
/// <param name="Span">The variant's bars, in its ticks.</param>
public sealed record DraftInsert(Tick At, TickSpan Span);

/// <summary>
/// One step of a draft's journal (decision record 0034, section 4; docs/project-file.md, "Drafts"): it names its place
/// and changes only it. Steps are applied in number order on the place's base and undone one at a time.
/// </summary>
/// <param name="Place">The place's id.</param>
public abstract record DraftStep(int Number, DateTimeOffset At, string Place)
{
    public int Number { get; } = Number > 0 ? Number : throw new ArgumentOutOfRangeException(nameof(Number), Number, "Steps are numbered from 1.");

    public string Place { get; } = !string.IsNullOrWhiteSpace(Place) ? Place : throw new ArgumentException("A step names its place.", nameof(Place));

    /// <summary>The steps accepted together with this one as one, by a shared id; null for a step alone.</summary>
    public string? Group { get; init; }

    /// <summary>Who proposed it: null for the player, else a name such as <c>assistant</c>.</summary>
    public string? Origin { get; init; }
}

/// <summary>
/// A приём, a hand or a selection of a variant taken into its place: the base notes it takes out and the variant notes it
/// puts in (by their ids in the place), or, with <see cref="Insert"/>, the variant's bars put in whole. The rebuild reads
/// only these, so the step means the same after the comparison's rules change.
/// </summary>
public sealed record DraftTake(int Number, DateTimeOffset At, string Place, string Variant) : DraftStep(Number, At, Place)
{
    public string Variant { get; } = !string.IsNullOrWhiteSpace(Variant) ? Variant : throw new ArgumentException("A take names its variant.", nameof(Variant));

    public DraftTechnique? Technique { get; init; }

    /// <summary>Only the notes of a hand were taken: <see cref="DraftPart.Right"/> or <see cref="DraftPart.Left"/>; null otherwise.</summary>
    public DraftPart? Part { get; init; }

    public IReadOnlyList<string> Removed { get; init; } = [];

    public IReadOnlyList<string> Added { get; init; } = [];

    public DraftInsert? Insert { get; init; }
}

/// <summary>A приём kept as in the base, or with <see cref="Seam"/> the seam before the place kept as it is.</summary>
public sealed record DraftLeave(int Number, DateTimeOffset At, string Place) : DraftStep(Number, At, Place)
{
    public string? Variant { get; init; }

    public DraftTechnique? Technique { get; init; }

    public bool Seam { get; init; }
}

/// <summary>The place's notes moved by <paramref name="Semitones"/>.</summary>
public sealed record DraftTranspose(int Number, DateTimeOffset At, string Place, int Semitones) : DraftStep(Number, At, Place)
{
    public int Semitones { get; } = Semitones is >= -24 and <= 24 and not 0 ? Semitones : throw new ArgumentOutOfRangeException(nameof(Semitones), Semitones, "A transposition moves by 1 to 24 semitones up or down.");
}

/// <summary>The place played at <paramref name="QuarterNotesPerMinute"/> from its start, its own changes in proportion.</summary>
public sealed record DraftTempo(int Number, DateTimeOffset At, string Place, int QuarterNotesPerMinute) : DraftStep(Number, At, Place)
{
    public int QuarterNotesPerMinute { get; } = Playback.PlayerTempo.IsValid(QuarterNotesPerMinute)
        ? QuarterNotesPerMinute
        : throw new ArgumentOutOfRangeException(nameof(QuarterNotesPerMinute), QuarterNotesPerMinute, "A tempo is 20 to 300 quarter notes a minute.");
}