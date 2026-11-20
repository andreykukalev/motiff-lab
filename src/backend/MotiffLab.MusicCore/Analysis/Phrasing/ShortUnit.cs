using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis.Phrasing;

/// <summary>What in the notes starts a short unit (decision record 0018, section 3: motifs and half-phrases of 1–4 bars).</summary>
public enum ShortEvidence
{
    /// <summary>The unit starts with its phrase; the phrase's own evidence says why.</summary>
    PhraseStart,

    /// <summary>Half a beat or more of silence across every part ends before the bar line it starts at.</summary>
    Rest,

    /// <summary>The figure that opens the phrase, on the top line, comes back here.</summary>
    Return,

    /// <summary>A note held for longer than a beat ends exactly at the bar line, and nothing sounds across it.</summary>
    HeldNote,

    /// <summary>Nothing in the notes marks it: the phrase was divided for its length, at the bar line nearest its middle.</summary>
    Length,
}

/// <summary>Where a short unit starts: the onset of its first note, with the evidence for cutting there.</summary>
/// <param name="Attribution">
/// The phrase's own attribution for a phrase start; inferred with the evidence's confidence for a cut read from the
/// notes; user-edited when the player cut here, user-confirmed when they cut where the notes already suggested it.
/// </param>
public sealed record ShortBoundary(Tick At, ShortEvidence Evidence, Attribution Attribution);

/// <summary>One short unit: a motif or half-phrase of a phrase, from where it starts to where the next one starts.</summary>
/// <param name="Number">1 for the first unit of the piece, then in order.</param>
/// <param name="Phrase">The phrase the unit divides; a phrase of up to <see cref="ShortMap.MaxBars"/> bars is one unit.</param>
public sealed record ShortUnit(int Number, TickSpan Span, int FirstBar, int LastBar, Phrase Phrase, ShortBoundary Start)
{
    /// <summary>The number of bars the unit touches, counting partly covered bars.</summary>
    public int BarCount => LastBar - FirstBar + 1;
}