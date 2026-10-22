namespace MotiffLab.MusicCore.Time;

/// <summary>
/// The bars of a piece: where each bar starts, how long it is and which time signature it is in.
/// Converts canonical ticks to bar and beat positions and back.
/// </summary>
/// <remarks>
/// <para>
/// Bars are a view of the canonical timeline (decision record 0002), built from time signature
/// changes and an optional pickup. The last time signature continues without end.
/// </para>
/// <para>
/// Pickup: when <c>pickupDuration</c> is positive, the piece starts with a short bar numbered 0,
/// and bar 1 starts after it. A pickup is counted from the end of a full bar, so a one-beat pickup
/// in 4/4 is on beat 4. Without a pickup, the first bar is bar 1.
/// </para>
/// <para>
/// Changes: every change after the first starts a new bar. A change that falls part-way through a
/// bar ends that bar early; the short bar keeps its number and is reported as incomplete, counted
/// from its start. This is how a MIDI file that changes meter mid-bar is represented without
/// moving any note. Bars are numbered consecutively; MusicXML bars excluded from numbering are not
/// modelled yet (decision record 0004).
/// </para>
/// </remarks>
public sealed class MeterMap
{
    private readonly MeterChange[] _changes;
    private readonly Segment[] _segments;

    public MeterMap(IEnumerable<MeterChange> changes, long pickupDuration = 0)
    {
        ArgumentNullException.ThrowIfNull(changes);
        MeterChange[] list = [.. changes];

        if (list.Length == 0 || list[0].Position != Tick.Zero)
        {
            throw new ArgumentException("The first time signature must start at the beginning of the piece.", nameof(changes));
        }

        for (int i = 1; i < list.Length; i++)
        {
            if (list[i].Position <= list[i - 1].Position)
            {
                throw new ArgumentException("Time signature changes must be in strictly increasing order of position.", nameof(changes));
            }
        }

        ArgumentOutOfRangeException.ThrowIfNegative(pickupDuration);
        if (pickupDuration >= list[0].Signature.BarTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(pickupDuration), pickupDuration, "A pickup must be shorter than a full bar of the first time signature.");
        }

        if (list.Length > 1 && list[1].Position.Value < pickupDuration)
        {
            throw new ArgumentException("A time signature cannot change inside the pickup bar.", nameof(changes));
        }

        _changes = list;
        PickupDuration = pickupDuration;
        _segments = BuildSegments(list, pickupDuration);
    }

    /// <summary>4/4 from the start, no pickup: the reading for a MIDI file without a time signature (S1-I3).</summary>
    public static MeterMap Default { get; } = new([new MeterChange(Tick.Zero, TimeSignature.CommonTime)]);

    public IReadOnlyList<MeterChange> Changes => _changes;

    /// <summary>Length of the pickup bar in ticks, or 0 when the piece starts on a full bar.</summary>
    public long PickupDuration { get; }

    public bool HasPickup => PickupDuration > 0;

    /// <summary>0 when the piece has a pickup, otherwise 1.</summary>
    public int FirstBarNumber => HasPickup ? 0 : 1;

    public TimeSignature SignatureAt(Tick position) => BarAt(position).Signature;

    /// <summary>The bar that contains a position.</summary>
    public Bar BarAt(Tick position)
    {
        if (position.Value < PickupDuration)
        {
            return PickupBar();
        }

        int segmentIndex = FindSegmentByTick(position);
        Segment segment = _segments[segmentIndex];
        return BarInSegment(segmentIndex, (position.Value - segment.Start.Value) / segment.Signature.BarTicks);
    }

    /// <summary>The bar with a given number. Throws if the piece has no such bar.</summary>
    public Bar GetBar(int number)
    {
        if (number < FirstBarNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, $"The first bar is bar {FirstBarNumber}.");
        }

        if (number == 0)
        {
            return PickupBar();
        }

        int segmentIndex = FindSegmentByBar(number);
        return BarInSegment(segmentIndex, number - _segments[segmentIndex].FirstBar);
    }

    /// <summary>The bars that start before <paramref name="end"/>, in order: for drawing the grid of a piece.</summary>
    public IEnumerable<Bar> BarsBefore(Tick end)
    {
        if (end == Tick.Zero)
        {
            yield break;
        }

        for (Bar bar = BarAt(Tick.Zero); ; bar = GetBar(bar.Number + 1))
        {
            yield return bar;
            if (bar.Span.End >= end)
            {
                yield break;
            }
        }
    }

    /// <summary>The bar, beat and offset within the beat of a position.</summary>
    public MetricPosition ToPosition(Tick position)
    {
        Bar bar = BarAt(position);
        long offset = position.Value - bar.Span.Onset.Value + bar.LeadIn;
        long beatTicks = bar.Signature.BeatTicks;
        return new MetricPosition(bar.Number, (int)(offset / beatTicks) + 1, offset % beatTicks);
    }

    /// <summary>
    /// The canonical position of a bar, beat and offset. Throws if the position is not inside that
    /// bar, for example beat 4 of a bar cut short after three beats, or beat 1 of a one-beat pickup.
    /// </summary>
    public Tick ToTick(MetricPosition position)
    {
        if (position.Beat < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Beats are numbered from 1.");
        }

        Bar bar = GetBar(position.Bar);
        long beatTicks = bar.Signature.BeatTicks;
        if (position.BeatOffset < 0 || position.BeatOffset >= beatTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, $"The offset must be less than one beat ({beatTicks} ticks).");
        }

        long offset = checked(((position.Beat - 1L) * beatTicks) + position.BeatOffset) - bar.LeadIn;
        if (offset < 0 || offset >= bar.Span.Duration)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, $"Bar {bar.Number} does not contain this beat.");
        }

        return bar.Span.Onset.Offset(offset);
    }

    private static Segment[] BuildSegments(MeterChange[] changes, long pickupDuration)
    {
        List<Segment> segments = new(changes.Length);
        int firstBar = 1;

        for (int i = 0; i < changes.Length; i++)
        {
            Tick start = i == 0 ? new Tick(pickupDuration) : changes[i].Position;
            Tick? end = i + 1 < changes.Length ? changes[i + 1].Position : null;
            if (end == start)
            {
                // The second change starts right after the pickup, so the first signature has no full bars.
                continue;
            }

            segments.Add(new Segment(start, changes[i].Signature, firstBar));

            if (end is Tick next)
            {
                long barTicks = changes[i].Signature.BarTicks;
                long bars = (next.Value - start.Value + barTicks - 1) / barTicks;
                firstBar = checked(firstBar + (int)Math.Min(bars, int.MaxValue));
            }
        }

        return [.. segments];
    }

    private Bar PickupBar()
    {
        TimeSignature signature = _changes[0].Signature;
        return new Bar(0, new TickSpan(Tick.Zero, PickupDuration), signature, IsPickup: true);
    }

    private Bar BarInSegment(int segmentIndex, long index)
    {
        Segment segment = _segments[segmentIndex];
        long barTicks = segment.Signature.BarTicks;
        Tick onset = segment.Start.Offset(checked(index * barTicks));
        long duration = barTicks;

        if (segmentIndex + 1 < _segments.Length)
        {
            duration = Math.Min(duration, _segments[segmentIndex + 1].Start.Value - onset.Value);
        }

        return new Bar(checked(segment.FirstBar + (int)index), new TickSpan(onset, duration), segment.Signature, IsPickup: false);
    }

    private int FindSegmentByTick(Tick position)
    {
        int low = 0;
        int high = _segments.Length - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (_segments[mid].Start <= position)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }

    private int FindSegmentByBar(int number)
    {
        int low = 0;
        int high = _segments.Length - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (_segments[mid].FirstBar <= number)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }

    private sealed record Segment(Tick Start, TimeSignature Signature, int FirstBar);
}

/// <summary>One bar of a <see cref="MeterMap"/>.</summary>
/// <param name="Number">0 for a pickup, otherwise counted from 1.</param>
/// <param name="Span">Where the bar sounds on the canonical timeline.</param>
/// <param name="Signature">The time signature in force for the bar.</param>
/// <param name="IsPickup">True for the short opening bar 0.</param>
public readonly record struct Bar(int Number, TickSpan Span, TimeSignature Signature, bool IsPickup)
{
    /// <summary>False for a pickup or a bar cut short by a time signature change.</summary>
    public bool IsComplete => Span.Duration == Signature.BarTicks;

    /// <summary>Ticks of the full bar that come before this one starts: non-zero only for a pickup, which ends on the bar line.</summary>
    internal long LeadIn => IsPickup ? Signature.BarTicks - Span.Duration : 0;
}

/// <summary>
/// A position as bar, beat and ticks after the beat, for display, snapping and explanations.
/// Derived from a canonical <see cref="Tick"/> through a <see cref="MeterMap"/>; never stored as truth.
/// </summary>
/// <param name="Bar">Bar number: 0 for a pickup, otherwise from 1.</param>
/// <param name="Beat">Beat within the bar, from 1. In a pickup, beats count from the end of a full bar.</param>
/// <param name="BeatOffset">Canonical ticks after the start of the beat.</param>
public readonly record struct MetricPosition(int Bar, int Beat, long BeatOffset)
{
    public override string ToString() => BeatOffset == 0 ? $"{Bar}:{Beat}" : $"{Bar}:{Beat}+{BeatOffset}t";
}