namespace MotiffLab.MusicCore.Time;

/// <summary>
/// A non-empty stretch of canonical time: an onset and a duration of at least one tick.
/// Used for the sounding time of notes and rests.
/// </summary>
/// <remarks>
/// <see cref="End"/> is exclusive: a span starting at 0 with duration 480 ends where the next
/// quarter note begins. The default value is a one-tick span at the start of the piece, so an
/// empty or negative span cannot exist.
/// </remarks>
public readonly struct TickSpan : IEquatable<TickSpan>
{
    // Stored as duration - 1 so that default(TickSpan) still has a positive duration.
    private readonly long _durationMinusOne;

    public TickSpan(Tick onset, long duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, 1);
        _ = checked(onset.Value + duration);

        Onset = onset;
        _durationMinusOne = duration - 1;
    }

    public Tick Onset { get; }

    /// <summary>Length in canonical ticks; always at least 1.</summary>
    public long Duration => _durationMinusOne + 1;

    /// <summary>The first tick after the span.</summary>
    public Tick End => new(Onset.Value + Duration);

    /// <summary>Creates a span from an onset and an exclusive end. Throws unless end is after onset.</summary>
    public static TickSpan FromBounds(Tick onset, Tick end)
    {
        if (end <= onset)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "The end must be after the onset.");
        }

        return new TickSpan(onset, end.Value - onset.Value);
    }

    public bool Contains(Tick position) => position >= Onset && position < End;

    public bool Overlaps(TickSpan other) => Onset < other.End && other.Onset < End;

    public static bool operator ==(TickSpan left, TickSpan right) => left.Equals(right);

    public static bool operator !=(TickSpan left, TickSpan right) => !left.Equals(right);

    public bool Equals(TickSpan other) => Onset == other.Onset && Duration == other.Duration;

    public override bool Equals(object? obj) => obj is TickSpan other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Onset, Duration);

    public override string ToString() => $"[{Onset.Value}t, {End.Value}t)";
}