using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis.Phrasing;

/// <summary>What in the source supports a phrase boundary (S1-I5).</summary>
public enum PhraseEvidence
{
    /// <summary>A phrase slur starts or ends here. Imported.</summary>
    Slur,

    /// <summary>A beat or more of silence comes before this note. Derived from the notes.</summary>
    Rest,

    /// <summary>A repeat sign stands on the bar line here. Imported.</summary>
    RepeatSign,

    /// <summary>
    /// The passage that opens the piece, or that starts at another boundary, comes back here note for
    /// note. Derived from the notes, so inferred.
    /// </summary>
    Repetition,

    /// <summary>
    /// Inside a stretch that runs longer than a phrase with no boundary (<see cref="PhraseMap.LongPhraseBars"/>), a
    /// passage starts again at once, note for note: the start of a phrase answered by one that starts the same, or
    /// either edge of a short figure played twice in a row. Derived from the notes, so inferred.
    /// </summary>
    Restatement,
}

/// <summary>A place where a new phrase starts: the onset of its first note.</summary>
/// <param name="At">The onset of the first note after the boundary.</param>
/// <param name="Evidence">Everything in the source that supports the boundary, in enum order; empty for a boundary only the player set.</param>
/// <param name="Attribution">
/// Imported when a slur or repeat sign in the source marks it; inferred, with
/// <see cref="PhraseBoundaryFinder.RestConfidence"/>, when only a rest does; user-confirmed or
/// user-edited when the player set it (<see cref="PhraseBoundaryFinder.WithPlayerBoundaries"/>).
/// </param>
public sealed record PhraseBoundary(Tick At, IReadOnlyList<PhraseEvidence> Evidence, Attribution Attribution);