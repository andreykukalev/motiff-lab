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

    /// <summary>The engine cannot check it yet: harmony needs chord analysis (S1-A3).</summary>
    NotChecked,
}

/// <summary>
/// The result of checking one property against the motif before the step. Contour, intervals,
/// melody and bass are read on the lines of <see cref="Motifs.MotifTexture"/>, so a chord under the
/// melody does not count as melodic movement.
/// </summary>
public sealed record KeepCheck(KeepProperty Property, KeepStatus Status);

/// <summary>One note of the motif before and after a step. Both share the note's id.</summary>
public sealed record NoteChange(Note Before, Note After)
{
    public bool PitchChanged => Before.Pitch != After.Pitch;

    public bool OnsetChanged => Before.Span.Onset != After.Span.Onset;

    public bool DurationChanged => Before.Span.Duration != After.Span.Duration;
}

/// <summary>
/// A variation of a motif: its notes, the note-by-note diff to the notes it was made from, the
/// checks of the kept properties and of every other property, the operation rules it raised, and an
/// explanation built only from the diff (S1-E4).
/// </summary>
/// <remarks>
/// The diff pairs notes by id. A note whose id is in both is a <see cref="NoteChange"/>, even when
/// nothing about it changed; a note only in the result is added, and one only in the source removed.
/// </remarks>
/// <param name="Operation">The catalogue id of the operation, for example "V1"; for a chain of steps, the ids in order, joined by " › ".</param>
/// <param name="Notes">The variation's notes, ordered by onset, then pitch, then id.</param>
/// <param name="Changes">Every source note that is still there, paired with its varied note, in the source's order.</param>
/// <param name="Added">Notes the step added, in the order of <paramref name="Notes"/>.</param>
/// <param name="Removed">Source notes the step removed, in the source's order.</param>
/// <param name="Checks">The properties the player asked to keep, each checked, in the order of the Keep table.</param>
/// <param name="Properties">
/// All nine properties checked, whether or not they were asked to be kept: what the step changed
/// and what stayed invariant, in the order of the Keep table.
/// </param>
/// <param name="Flags">Operation rules the result raises without being blocked, such as a hand span over a 10th.</param>
public sealed record Variation(
    string Operation,
    IReadOnlyList<Note> Notes,
    IReadOnlyList<NoteChange> Changes,
    IReadOnlyList<Note> Added,
    IReadOnlyList<Note> Removed,
    IReadOnlyList<KeepCheck> Checks,
    IReadOnlyList<KeepCheck> Properties,
    IReadOnlyList<string> Flags,
    string Explanation);