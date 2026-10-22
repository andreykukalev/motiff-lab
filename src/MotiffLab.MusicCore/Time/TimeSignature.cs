namespace MotiffLab.MusicCore.Time;

/// <summary>
/// A time signature: <see cref="Numerator"/> beats of the note value named by <see cref="Denominator"/>.
/// </summary>
/// <remarks>
/// The beat is the denominator's note value, as written and as MIDI and MusicXML state it: 6/8 has six
/// eighth-note beats, not two dotted-quarter beats. Grouping beats into a felt pulse is analysis
/// (decision record 0004). Denominators are powers of two up to 128, so bar and beat lengths are
/// always whole canonical ticks. Compound numerators such as 3+2/8 are given as their sum.
/// The default value is 1/1, so an invalid signature cannot exist.
/// </remarks>
public readonly record struct TimeSignature
{
    // Stored so that default(TimeSignature) is 1/1 rather than an invalid 0/0.
    private readonly byte _numeratorMinusOne;
    private readonly byte _denominatorExponent;

    public const int MaxNumerator = 255;

    public const int MaxDenominator = 128;

    public TimeSignature(int numerator, int denominator)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(numerator, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(numerator, MaxNumerator);
        if (denominator < 1 || denominator > MaxDenominator || !int.IsPow2(denominator))
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "The denominator must be a power of two from 1 to 128.");
        }

        _numeratorMinusOne = (byte)(numerator - 1);
        _denominatorExponent = (byte)int.Log2(denominator);
    }

    public static TimeSignature CommonTime { get; } = new(4, 4);

    public int Numerator => _numeratorMinusOne + 1;

    public int Denominator => 1 << _denominatorExponent;

    /// <summary>Length of one beat in canonical ticks.</summary>
    public long BeatTicks => CanonicalTime.TicksPerQuarter * 4L / Denominator;

    /// <summary>Length of a complete bar in canonical ticks.</summary>
    public long BarTicks => BeatTicks * Numerator;

    public override string ToString() => $"{Numerator}/{Denominator}";
}

/// <summary>A time signature that takes effect at a canonical position.</summary>
public readonly record struct MeterChange(Tick Position, TimeSignature Signature);