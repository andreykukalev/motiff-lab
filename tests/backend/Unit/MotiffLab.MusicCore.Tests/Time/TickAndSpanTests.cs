using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Time;

public class TickAndSpanTests
{
    [Fact]
    public void Tick_rejects_negative_positions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tick(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tick(10).Offset(-11));
    }

    [Fact]
    public void Tick_offset_overflow_throws()
    {
        Assert.Throws<OverflowException>(() => new Tick(long.MaxValue).Offset(1));
    }

    [Fact]
    public void Tick_converts_back_to_exact_quarters()
    {
        Assert.Equal(new QuarterFraction(3, 2), new Tick(720).ToQuarters());
    }

    [Fact]
    public void Span_end_is_exclusive()
    {
        TickSpan quarter = new(new Tick(480), 480);

        Assert.Equal(new Tick(960), quarter.End);
        Assert.True(quarter.Contains(new Tick(480)));
        Assert.False(quarter.Contains(new Tick(960)));
        Assert.False(quarter.Overlaps(new TickSpan(new Tick(960), 240)));
        Assert.True(quarter.Overlaps(new TickSpan(new Tick(959), 1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Span_requires_positive_duration(long duration)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickSpan(Tick.Zero, duration));
    }

    [Fact]
    public void Span_from_bounds_requires_end_after_onset()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TickSpan.FromBounds(new Tick(10), new Tick(10)));
        Assert.Equal(new TickSpan(new Tick(10), 5), TickSpan.FromBounds(new Tick(10), new Tick(15)));
    }

    [Fact]
    public void Span_whose_end_overflows_is_rejected()
    {
        Assert.Throws<OverflowException>(() => new TickSpan(new Tick(long.MaxValue), 1));
    }

    [Fact]
    public void Default_span_is_valid()
    {
        Assert.Equal(1, default(TickSpan).Duration);
        Assert.Equal(new Tick(1), default(TickSpan).End);
    }
}