namespace MotiffLab.MusicCore.Time;

/// <summary>
/// An exact rational amount of musical time, measured in quarter notes.
/// </summary>
/// <remarks>
/// Source formats express time in their own units (MIDI ticks at a file-specific PPQ,
/// MusicXML divisions that can change mid-part). Converting those units to quarter notes is
/// exact, so importers accumulate positions as <see cref="QuarterFraction"/> and round to
/// canonical ticks only once per boundary (see <see cref="CanonicalTime"/>).
/// The value is always stored reduced, with a positive denominator. The default value is zero.
/// Arithmetic that cannot be represented with 64-bit numerator and denominator throws
/// <see cref="OverflowException"/>; it never wraps or loses precision silently.
/// </remarks>
public readonly struct QuarterFraction : IEquatable<QuarterFraction>, IComparable<QuarterFraction>
{
    // Stored as denominator - 1 so that default(QuarterFraction) is 0/1.
    private readonly long _denominatorMinusOne;

    public QuarterFraction(long numerator, long denominator)
    {
        if (denominator == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), "Denominator must not be zero.");
        }

        this = Create(numerator, denominator);
    }

    private QuarterFraction(long reducedNumerator, long positiveDenominator, bool _)
    {
        Numerator = reducedNumerator;
        _denominatorMinusOne = positiveDenominator - 1;
    }

    public static QuarterFraction Zero => default;

    public long Numerator { get; }

    public long Denominator => _denominatorMinusOne + 1;

    public bool IsNegative => Numerator < 0;

    public static QuarterFraction FromQuarters(long quarters) => new(quarters, 1, true);

    public static QuarterFraction operator +(QuarterFraction left, QuarterFraction right) =>
        Create(
            ((Int128)left.Numerator * right.Denominator) + ((Int128)right.Numerator * left.Denominator),
            (Int128)left.Denominator * right.Denominator);

    public static QuarterFraction operator -(QuarterFraction left, QuarterFraction right) =>
        Create(
            ((Int128)left.Numerator * right.Denominator) - ((Int128)right.Numerator * left.Denominator),
            (Int128)left.Denominator * right.Denominator);

    public static bool operator ==(QuarterFraction left, QuarterFraction right) => left.Equals(right);

    public static bool operator !=(QuarterFraction left, QuarterFraction right) => !left.Equals(right);

    public static bool operator <(QuarterFraction left, QuarterFraction right) => left.CompareTo(right) < 0;

    public static bool operator >(QuarterFraction left, QuarterFraction right) => left.CompareTo(right) > 0;

    public static bool operator <=(QuarterFraction left, QuarterFraction right) => left.CompareTo(right) <= 0;

    public static bool operator >=(QuarterFraction left, QuarterFraction right) => left.CompareTo(right) >= 0;

    public int CompareTo(QuarterFraction other) =>
        ((Int128)Numerator * other.Denominator).CompareTo((Int128)other.Numerator * Denominator);

    // Values are always reduced, so equal fractions have equal components.
    public bool Equals(QuarterFraction other) =>
        Numerator == other.Numerator && Denominator == other.Denominator;

    public override bool Equals(object? obj) => obj is QuarterFraction other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    public override string ToString() => Denominator == 1 ? $"{Numerator}q" : $"{Numerator}/{Denominator}q";

    internal static QuarterFraction Create(Int128 numerator, Int128 denominator)
    {
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        Int128 divisor = GreatestCommonDivisor(Int128.Abs(numerator), denominator);
        if (divisor > 1)
        {
            numerator /= divisor;
            denominator /= divisor;
        }

        if (numerator < long.MinValue || numerator > long.MaxValue || denominator > long.MaxValue)
        {
            throw new OverflowException("The quarter-note fraction is too large to represent exactly.");
        }

        return new QuarterFraction((long)numerator, (long)denominator, true);
    }

    private static Int128 GreatestCommonDivisor(Int128 a, Int128 b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a == 0 ? 1 : a;
    }
}