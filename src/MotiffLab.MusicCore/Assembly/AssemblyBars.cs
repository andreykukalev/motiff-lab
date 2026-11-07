using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assembly;

/// <summary>
/// One bar of a source or of the player's notes in an assembly (decision record 0024): where it is and the time
/// signature it is written in. A bar shorter than its signature is a short bar: a pickup, the last bar, or a bar at the
/// seam of a carry.
/// </summary>
public readonly record struct AssemblyBar(TickSpan Span, TimeSignature Signature)
{
    public bool IsShort => Span.Duration != Signature.BarTicks;
}

/// <summary>
/// Bars as a list, so a carry can cut them at its seams and splice the source's bars in (decision record 0024,
/// section 3): the bars before the carry stay, the source's bars come in as they are, and the bars after it move
/// with the music, so their downbeats stay downbeats. Two short bars meeting at a seam become one bar.
/// </summary>
public static class AssemblyBars
{
    /// <summary>The grid seams are put on: a 32nd note, the grid notation is written on.</summary>
    public const long Grid = Notation.NotationGenerator.Grid;

    /// <summary>The bars of a meter up to <paramref name="end"/>, the last one whole.</summary>
    public static IReadOnlyList<AssemblyBar> FromMeter(MeterMap meter, Tick end)
    {
        ArgumentNullException.ThrowIfNull(meter);
        Tick last = end.Value > 0 ? end : new Tick(1);
        return [.. meter.BarsBefore(last).Select(bar => new AssemblyBar(bar.Span, bar.Signature))];
    }

    /// <summary>A position put on the grid: the nearest 32nd.</summary>
    public static Tick Snap(Tick position) => new(Math.Max(0, (position.Value + (Grid / 2)) / Grid * Grid));

    /// <summary>
    /// The bars with <c>[start, end)</c> replaced by <paramref name="fragment"/> (already moved to start at
    /// <paramref name="start"/>); <paramref name="end"/> equal to <paramref name="start"/> inserts. The bars after
    /// <paramref name="end"/> move by the difference in length; short bars meeting at a seam are joined.
    /// </summary>
    public static IReadOnlyList<AssemblyBar> Splice(IReadOnlyList<AssemblyBar> bars, Tick start, Tick end, IReadOnlyList<AssemblyBar> fragment)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(fragment);
        if (end < start)
        {
            throw new ArgumentException("A carry ends after it starts.", nameof(end));
        }

        long fragmentLength = fragment.Sum(bar => bar.Span.Duration);
        long delta = fragmentLength - (end.Value - start.Value);
        List<AssemblyBar> before = [];
        List<AssemblyBar> after = [];
        foreach (AssemblyBar bar in bars)
        {
            if (bar.Span.End <= start)
            {
                before.Add(bar);
                continue;
            }

            if (bar.Span.Onset < start)
            {
                before.Add(bar with { Span = TickSpan.FromBounds(bar.Span.Onset, start) });
            }

            if (bar.Span.End > end)
            {
                Tick from = bar.Span.Onset > end ? bar.Span.Onset : end;
                after.Add(bar with { Span = new TickSpan(from.Offset(delta), bar.Span.End.Value - from.Value) });
            }
        }

        if (before.Count == 0 && after.Count == 0 && fragment.Count == 0)
        {
            return bars;
        }

        List<AssemblyBar> joined = [.. before];
        int seamA = joined.Count;
        joined.AddRange(fragment);
        int seamB = joined.Count;
        joined.AddRange(after);

        // The seam after the fragment first, so the index of the first seam stays right.
        JoinAt(joined, seamB);
        JoinAt(joined, seamA);
        return joined;
    }

    /// <summary>The bars moved by <paramref name="offset"/> ticks.</summary>
    public static IReadOnlyList<AssemblyBar> Moved(IEnumerable<AssemblyBar> bars, long offset) =>
        [.. bars.Select(bar => bar with { Span = new TickSpan(bar.Span.Onset.Offset(offset), bar.Span.Duration) })];

    /// <summary>The bars of <c>[start, end)</c>, cut at both ends.</summary>
    public static IReadOnlyList<AssemblyBar> Cut(IReadOnlyList<AssemblyBar> bars, Tick start, Tick end)
    {
        ArgumentNullException.ThrowIfNull(bars);
        List<AssemblyBar> cut = [];
        foreach (AssemblyBar bar in bars)
        {
            if (bar.Span.End <= start || bar.Span.Onset >= end)
            {
                continue;
            }

            Tick from = bar.Span.Onset < start ? start : bar.Span.Onset;
            Tick to = bar.Span.End > end ? end : bar.Span.End;
            cut.Add(bar with { Span = TickSpan.FromBounds(from, to) });
        }

        if (cut.Count == 0 && end > start)
        {
            TimeSignature signature = bars.Count > 0 ? bars[^1].Signature : TimeSignature.CommonTime;
            cut.Add(new AssemblyBar(TickSpan.FromBounds(start, end), signature));
        }

        return cut;
    }

    /// <summary>
    /// The meter the bars make: a time signature change wherever a bar does not continue the one before, a short bar
    /// in the middle written with a signature of its own length ("5/16"), a short first bar as a pickup, and a short
    /// last bar left as the end of the piece.
    /// </summary>
    public static MeterMap ToMeter(IReadOnlyList<AssemblyBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (bars.Count == 0)
        {
            return MeterMap.Default;
        }

        long pickup = 0;
        int first = 0;
        if (bars.Count > 1 && bars[0].IsShort && bars[0].Span.Duration < bars[1].Signature.BarTicks && !bars[1].IsShort)
        {
            pickup = bars[0].Span.Duration;
            first = 1;
        }

        List<MeterChange> changes = [];
        TimeSignature segment = default;
        long segmentStart = 0;
        for (int i = first; i < bars.Count; i++)
        {
            AssemblyBar bar = bars[i];
            bool last = i == bars.Count - 1;
            TimeSignature written = bar.IsShort && !last ? OfLength(bar.Span.Duration, bar.Signature) : bar.Signature;
            bool continues = changes.Count > 0 && written == segment
                && (bar.Span.Onset.Value - segmentStart) % segment.BarTicks == 0
                && (bar.Span.Duration == segment.BarTicks || last);
            if (continues)
            {
                continue;
            }

            Tick at = changes.Count == 0 ? Tick.Zero : bar.Span.Onset;
            changes.Add(new MeterChange(at, written));
            segment = written;
            segmentStart = changes.Count == 1 ? pickup : bar.Span.Onset.Value;
        }

        return new MeterMap(changes, pickup);
    }

    /// <summary>A time signature that is <paramref name="length"/> ticks long, in the beat of <paramref name="like"/> where it fits: a short bar's own.</summary>
    public static TimeSignature OfLength(long length, TimeSignature like)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        if (length == like.BarTicks)
        {
            return like;
        }

        for (int denominator = like.Denominator; denominator <= TimeSignature.MaxDenominator; denominator *= 2)
        {
            long beat = CanonicalTime.TicksPerQuarter * 4L / denominator;
            if (length % beat == 0 && length / beat <= TimeSignature.MaxNumerator)
            {
                return new TimeSignature((int)(length / beat), denominator);
            }
        }

        for (int denominator = like.Denominator / 2; denominator >= 1; denominator /= 2)
        {
            long beat = CanonicalTime.TicksPerQuarter * 4L / denominator;
            if (length % beat == 0 && length / beat <= TimeSignature.MaxNumerator)
            {
                return new TimeSignature((int)(length / beat), denominator);
            }
        }

        // Off the grid: the nearest 128th.
        long grid = CanonicalTime.TicksPerQuarter * 4L / TimeSignature.MaxDenominator;
        return new TimeSignature((int)Math.Clamp((length + (grid / 2)) / grid, 1, TimeSignature.MaxNumerator), TimeSignature.MaxDenominator);
    }

    /// <summary>Joins the two bars on either side of <paramref name="seam"/> when both are short.</summary>
    private static void JoinAt(List<AssemblyBar> bars, int seam)
    {
        if (seam <= 0 || seam >= bars.Count || !bars[seam - 1].IsShort || !bars[seam].IsShort)
        {
            return;
        }

        AssemblyBar left = bars[seam - 1];
        AssemblyBar right = bars[seam];
        long length = left.Span.Duration + right.Span.Duration;
        TimeSignature signature = length == left.Signature.BarTicks ? left.Signature
            : length == right.Signature.BarTicks ? right.Signature
            : OfLength(length, left.Signature);
        bars[seam - 1] = new AssemblyBar(new TickSpan(left.Span.Onset, length), signature);
        bars.RemoveAt(seam);
    }
}