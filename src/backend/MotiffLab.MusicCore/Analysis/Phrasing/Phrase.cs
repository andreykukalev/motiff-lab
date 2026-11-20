using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis.Phrasing;

/// <summary>How much the source says about where its phrases are, before the player changes anything.</summary>
public enum PhraseEvidenceLevel
{
    /// <summary>No suggested phrase is long (<see cref="PhraseMap.LongPhraseBars"/>).</summary>
    Enough,

    /// <summary>Some boundaries were found, but part of the piece runs longer than <see cref="PhraseMap.LongPhraseBars"/> bars without one.</summary>
    Partial,

    /// <summary>Nothing in the source marks a boundary, so no phrase is suggested.</summary>
    None,
}

/// <summary>One phrase of the piece, from where it starts to where the next one starts.</summary>
/// <param name="Number">1 for the first phrase of the piece, then in order.</param>
/// <param name="Span">From the phrase's start to the next phrase's start, or the end of the piece.</param>
/// <param name="FirstBar">The bar the phrase starts in.</param>
/// <param name="LastBar">The bar the phrase ends in.</param>
/// <param name="Start">The boundary it starts at, with its evidence; null for the first phrase, which starts with the piece.</param>
public sealed record Phrase(int Number, TickSpan Span, int FirstBar, int LastBar, PhraseBoundary? Start)
{
    /// <summary>The number of bars the phrase touches, counting partly covered bars.</summary>
    public int BarCount => LastBar - FirstBar + 1;

    /// <summary>
    /// Whether the phrase runs longer than a phrase usually does (<see cref="PhraseMap.LongPhraseBars"/>), so the
    /// source probably lacks a boundary inside it. It is still selected whole (decision records 0013 and 0018).
    /// </summary>
    public bool IsLong => BarCount > PhraseMap.LongPhraseBars;
}

/// <summary>A selection run on into a neighbouring phrase (#126).</summary>
/// <param name="Span">The selection with the neighbouring phrase.</param>
/// <param name="Into">The phrase it runs into.</param>
/// <param name="Shortened">Whether it stops before the far edge of <paramref name="Into"/>, because a selection holds at most <see cref="MotifSelection.MaxBars"/> bars.</param>
public sealed record PhraseExtension(TickSpan Span, Phrase Into, bool Shortened);