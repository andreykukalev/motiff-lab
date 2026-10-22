namespace MotiffLab.MusicCore.Time;

/// <summary>
/// The canonical timeline and the one place where exact source time is rounded to ticks.
/// </summary>
/// <remarks>
/// <para>
/// Resolution: 480 ticks per quarter note (docs/05-domain-architecture.md, "Time").
/// 480 = 2⁵·3·5, so every duration down to a 128th note, and triplet and quintuplet
/// subdivisions of those, is exact. Other subdivisions (for example septuplets) are rounded.
/// </para>
/// <para>
/// Rounding policy: a non-negative exact position is rounded to the nearest tick, with halves
/// rounded up. Negative positions are invalid and throw <see cref="ArgumentOutOfRangeException"/>.
/// Positions whose tick count does not fit in <see cref="long"/> throw <see cref="OverflowException"/>.
/// </para>
/// <para>
/// No drift: spans are converted by rounding their onset and end independently from exact
/// positions, then taking the difference. Each boundary is off by at most half a tick, and the
/// error never accumulates across consecutive events. A span that would round to zero length is
/// widened to one tick and reported as inexact.
/// </para>
/// </remarks>
public static class CanonicalTime
{
    public const int TicksPerQuarter = 480;

    /// <summary>Rounds an exact position to the nearest canonical tick.</summary>
    public static ConvertedTick ToTick(QuarterFraction position)
    {
        if (position.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "A position cannot be before the start of the piece.");
        }

        Int128 scaled = (Int128)position.Numerator * TicksPerQuarter;
        Int128 denominator = position.Denominator;
        Int128 rounded = ((2 * scaled) + denominator) / (2 * denominator);

        if (rounded > long.MaxValue)
        {
            throw new OverflowException("The position is too far from the start of the piece to represent in ticks.");
        }

        return new ConvertedTick(new Tick((long)rounded), IsExact: scaled % denominator == 0);
    }

    /// <summary>Converts an exact onset and end to a canonical span without accumulating rounding error.</summary>
    public static ConvertedSpan ToSpan(QuarterFraction onset, QuarterFraction end)
    {
        if (end <= onset)
        {
            throw new ArgumentOutOfRangeException(nameof(end), end, "The end must be after the onset.");
        }

        ConvertedTick start = ToTick(onset);
        ConvertedTick stop = ToTick(end);

        if (stop.Tick <= start.Tick)
        {
            return new ConvertedSpan(new TickSpan(start.Tick, 1), IsExact: false);
        }

        return new ConvertedSpan(TickSpan.FromBounds(start.Tick, stop.Tick), start.IsExact && stop.IsExact);
    }
}

/// <summary>A canonical tick and whether the source position was represented without rounding.</summary>
public readonly record struct ConvertedTick(Tick Tick, bool IsExact);

/// <summary>A canonical span and whether both boundaries and the length were represented without rounding.</summary>
public readonly record struct ConvertedSpan(TickSpan Span, bool IsExact);