using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis.Phrasing;

/// <summary>
/// One unit of any level, as the brackets and the facade show it: a short unit, a phrase or a sentence with
/// its number at its level, its place and how its start was found. The maps keep the richer records
/// (<see cref="ShortUnit"/>, <see cref="Phrase"/>, <see cref="Sentence"/>); this is the view every level shares.
/// </summary>
/// <param name="Number">1 for the first unit of the piece at this level, then in order.</param>
/// <param name="Start">How the unit's start was found: imported for the start of the piece, otherwise the boundary's attribution.</param>
/// <param name="Evidence">The evidence names behind the start, in lower camel case; empty for the start of the piece or a sentence.</param>
/// <param name="Description">How the start was found, in the language asked for, as <see cref="PhraseMap.Describe(PhraseBoundary?, Words, CloseKind)"/> words it; a sentence's basis.</param>
public sealed record PhraseUnit(PhraseLevel Level, int Number, TickSpan Span, int FirstBar, int LastBar, Attribution Start, IReadOnlyList<string> Evidence, string Description)
{
    /// <summary>The number of bars the unit touches, counting partly covered bars.</summary>
    public int BarCount => LastBar - FirstBar + 1;

    /// <summary>Whether the unit runs longer than a phrase usually does (<see cref="PhraseMap.LongPhraseBars"/>).</summary>
    public bool IsLong => BarCount > PhraseMap.LongPhraseBars;

    /// <summary>Whether <paramref name="span"/> lies within the unit.</summary>
    public bool Contains(TickSpan span) => Span.Onset <= span.Onset && span.End <= Span.End;

    public bool Equals(PhraseUnit? other) =>
        other is not null && Level == other.Level && Number == other.Number && Span == other.Span && FirstBar == other.FirstBar && LastBar == other.LastBar
        && Start == other.Start && Evidence.SequenceEqual(other.Evidence, StringComparer.Ordinal) && Description == other.Description;

    public override int GetHashCode() => HashCode.Combine(Level, Number, Span);
}