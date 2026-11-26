using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Performance;

/// <summary>
/// Two readings of the same music matched («Как напечатано», step 2; concept 3.3, point 1): the notes that start together
/// aligned with gaps as a performance is (<see cref="PerformanceAlignment.AlignOnsets"/>, ticks on both sides), which gives
/// each onset and each bar of the earlier reading its place in the later. The one match of two readings: the corrections
/// carried over (<see cref="ReadingCarry"/>) and the player's work in ticks following other working notes (the project's
/// FollowedWork) both read it. What is carried, and how it is told, belongs to them, not here. Pure: the same readings give
/// the same match.
/// </summary>
internal sealed class ReadingMatch
{
    private readonly ImportedPiece _before;
    private readonly ImportedPiece _after;
    private readonly IReadOnlyList<(long From, long To)> _onsets;
    private Dictionary<int, Bar>? _bars;

    public ReadingMatch(ImportedPiece before, ImportedPiece after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        _before = before;
        _after = after;
        _onsets = PerformanceAlignment.AlignOnsets(before.Notes, after.Notes);
    }

    /// <summary>How far an onset of the earlier reading lies in the later: as far as the onsets matched at or before it, else the first matched.</summary>
    public long Shift(long onset)
    {
        (long From, long To)? at = _onsets.Count > 0 ? _onsets[0] : null;
        foreach ((long From, long To) pair in _onsets.TakeWhile(pair => pair.From <= onset))
        {
            at = pair;
        }

        return at is { } matched ? matched.To - matched.From : 0;
    }

    /// <summary>The bar of the later reading an earlier bar's music starts in, when it starts a bar there; else null.</summary>
    public Bar? Moved(Bar earlier)
    {
        (long From, long To)[] first = [.. _onsets.Where(pair => earlier.Span.Contains(new Tick(pair.From))).Take(1)];
        long start = earlier.Span.Onset.Value;
        long at = Math.Max(0, start + (first.Length == 1 ? first[0].To - first[0].From : Shift(start)));
        Bar later = _after.Meter.BarAt(new Tick(at));
        return later.Span.Onset.Value == at ? later : null;
    }

    /// <summary>
    /// Where a place of the earlier reading lies in the later, by bars (Andrei's answer 5 of 2026-11-25: «по сопоставлению
    /// тактов»): in a bar with a match (<see cref="MatchedBars"/>), as far from the start of the bar its music moved to as it
    /// was from its own, at most to that bar's end; with <paramref name="end"/>, the end of a span, read in the bar it ends in.
    /// A place in a bar with no match is kept where the music before it went (<see cref="Shift"/>, as a correction is placed),
    /// and is not <c>Matched</c>.
    /// </summary>
    public (Tick At, bool Matched) Place(Tick at, bool end)
    {
        long from = end && at.Value > 0 ? at.Value - 1 : at.Value;
        Bar earlier = _before.Meter.BarAt(new Tick(from));
        _bars ??= MatchedBars();
        return _bars.TryGetValue(earlier.Number, out Bar later)
            ? (new Tick(later.Span.Onset.Value + Math.Min(at.Value - earlier.Span.Onset.Value, later.Span.Duration)), true)
            : (new Tick(Math.Max(0, at.Value + Shift(from))), false);
    }

    /// <summary>
    /// A span of the earlier reading in the later, its onset and its end each placed (<see cref="Place(Tick, bool)"/>); one
    /// that comes out empty or turned round keeps its length from its new onset. <c>Matched</c> when both ends are.
    /// </summary>
    public (TickSpan Span, bool Matched) Place(TickSpan span)
    {
        (Tick onset, bool first) = Place(span.Onset, end: false);
        (Tick end, bool last) = Place(span.End, end: true);
        return (end > onset ? TickSpan.FromBounds(onset, end) : new TickSpan(onset, span.Duration), first && last);
    }

    /// <summary>
    /// The earlier reading's bars with a match, by number, each with the bar of the later reading its music moved to
    /// (<see cref="Moved"/>) before the later reading's end. A bar none of whose onsets matched (no notes, or none the
    /// alignment paired) moves as the music before it, and only to a bar no bar with matched onsets of its own moved to:
    /// so the bars of a page the later reading has not are without a match rather than on the music after them.
    /// </summary>
    private Dictionary<int, Bar> MatchedBars()
    {
        List<(int Number, Bar Later, bool Own)> moved = [];
        for (int number = _before.Meter.FirstBarNumber; ; number++)
        {
            Bar earlier = _before.Meter.GetBar(number);
            if (earlier.Span.Onset >= _before.End)
            {
                break;
            }

            if (Moved(earlier) is { } later && later.Span.Onset < _after.End)
            {
                moved.Add((number, later, _onsets.Any(pair => earlier.Span.Contains(new Tick(pair.From)))));
            }
        }

        HashSet<int> owned = [.. moved.Where(static bar => bar.Own).Select(static bar => bar.Later.Number)];
        return moved.Where(bar => bar.Own || !owned.Contains(bar.Later.Number)).ToDictionary(static bar => bar.Number, static bar => bar.Later);
    }
}