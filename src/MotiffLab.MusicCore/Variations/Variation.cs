using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Variations;

/// <summary>The nine properties a player can ask a variation to keep (S1-E2), in the order of the Keep table.</summary>
public enum KeepProperty
{
    Pitches,
    PitchOrder,
    Contour,
    Intervals,
    Rhythm,
    Melody,
    Bass,
    Harmony,
    Register,
}

/// <summary>Whether a kept property survived a step (S1-E9).</summary>
public enum KeepStatus
{
    Kept,

    /// <summary>The step changed it; shown on its chip, never blocking.</summary>
    NoLongerKept,

    /// <summary>The engine cannot check it yet: melody, bass and harmony need voice and chord analysis (S1-A3).</summary>
    NotChecked,
}

/// <summary>The result of checking one kept property against the motif before the step.</summary>
public sealed record KeepCheck(KeepProperty Property, KeepStatus Status);

/// <summary>One note of the motif before and after a step. Both share the note's id.</summary>
public sealed record NoteChange(Note Before, Note After)
{
    public bool PitchChanged => Before.Pitch != After.Pitch;

    public bool OnsetChanged => Before.Span.Onset != After.Span.Onset;

    public bool DurationChanged => Before.Span.Duration != After.Span.Duration;
}

/// <summary>
/// A variation of a motif after one step: its notes, the note-by-note diff to the motif, the checks
/// of the kept properties, and an explanation built only from the diff (S1-E4).
/// </summary>
/// <param name="Operation">The catalogue id of the operation, for example "V1".</param>
/// <param name="Notes">The variation's notes, ordered by onset, then pitch, then id.</param>
/// <param name="Changes">Every motif note paired with its varied note, in the motif's order.</param>
public sealed record Variation(
    string Operation,
    IReadOnlyList<Note> Notes,
    IReadOnlyList<NoteChange> Changes,
    IReadOnlyList<KeepCheck> Checks,
    string Explanation);