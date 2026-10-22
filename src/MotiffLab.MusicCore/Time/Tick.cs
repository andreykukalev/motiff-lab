namespace MotiffLab.MusicCore.Time;

/// <summary>
/// A position in canonical musical time: a non-negative count of ticks from the start of the
/// piece at <see cref="CanonicalTime.TicksPerQuarter"/> ticks per quarter note.
/// </summary>
/// <remarks>
/// Canonical ticks are the single timeline shared by every input format. Source-format units
/// (MIDI ticks, MusicXML divisions) are separate types and never convert to <see cref="Tick"/>
/// implicitly.
/// </remarks>
public readonly struct Tick : IEquatable<Tick>, IComparable<Tick>
{
    public Tick(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    public static Tick Zero => default;

    public long Value { get; }

    /// <summary>Moves the position by a number of ticks. Throws if the result is negative or overflows.</summary>
    public Tick Offset(long ticks)
    {
        long result = checked(Value + ticks);
        if (result < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks), "The offset moves the position before the start of the piece.");
        }

        return new Tick(result);
    }

    /// <summary>Exact position of this tick in quarter notes.</summary>
    public QuarterFraction ToQuarters() => new(Value, CanonicalTime.TicksPerQuarter);

    public static bool operator ==(Tick left, Tick right) => left.Equals(right);

    public static bool operator !=(Tick left, Tick right) => !left.Equals(right);

    public static bool operator <(Tick left, Tick right) => left.Value < right.Value;

    public static bool operator >(Tick left, Tick right) => left.Value > right.Value;

    public static bool operator <=(Tick left, Tick right) => left.Value <= right.Value;

    public static bool operator >=(Tick left, Tick right) => left.Value >= right.Value;

    public int CompareTo(Tick other) => Value.CompareTo(other.Value);

    public bool Equals(Tick other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is Tick other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => $"{Value}t";
}