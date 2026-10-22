using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Time;

public class CanonicalTimeTests
{
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 1, 480)]
    [InlineData(1, 3, 160)] // triplet eighth
    [InlineData(1, 5, 96)] // quintuplet sixteenth
    [InlineData(1, 32, 15)] // 128th note
    [InlineData(1, 96, 5)] // triplet 128th
    public void Common_subdivisions_are_exact(long numerator, long denominator, long expectedTick)
    {
        ConvertedTick result = CanonicalTime.ToTick(new QuarterFraction(numerator, denominator));

        Assert.Equal(new Tick(expectedTick), result.Tick);
        Assert.True(result.IsExact);
    }

    [Theory]
    [InlineData(1, 960, 1)] // exactly half a tick rounds up
    [InlineData(1, 961, 0)] // just under half a tick rounds down
    [InlineData(1, 7, 69)] // 68.57 ticks
    [InlineData(3, 7, 206)] // 205.71 ticks
    public void Inexact_positions_round_to_nearest_with_halves_up(long numerator, long denominator, long expectedTick)
    {
        ConvertedTick result = CanonicalTime.ToTick(new QuarterFraction(numerator, denominator));

        Assert.Equal(new Tick(expectedTick), result.Tick);
        Assert.False(result.IsExact);
    }

    [Fact]
    public void Negative_positions_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalTime.ToTick(new QuarterFraction(-1, 2)));
    }

    [Fact]
    public void Positions_beyond_the_tick_range_throw()
    {
        Assert.Throws<OverflowException>(() => CanonicalTime.ToTick(QuarterFraction.FromQuarters(long.MaxValue)));
    }

    [Fact]
    public void Span_requires_end_after_onset()
    {
        QuarterFraction one = QuarterFraction.FromQuarters(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalTime.ToSpan(one, one));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalTime.ToSpan(one, QuarterFraction.Zero));
    }

    [Fact]
    public void Span_shorter_than_a_tick_is_widened_and_reported()
    {
        // 1/2000 of a quarter is 0.24 ticks: both boundaries round to tick 0.
        ConvertedSpan result = CanonicalTime.ToSpan(QuarterFraction.Zero, new QuarterFraction(1, 2000));

        Assert.Equal(new TickSpan(Tick.Zero, 1), result.Span);
        Assert.False(result.IsExact);
    }

    [Fact]
    public void Septuplets_fill_exactly_one_quarter_without_drift()
    {
        // Seven septuplet sixteenths across one quarter. Rounding each duration (69 ticks) would
        // end at 483; rounding absolute boundaries ends exactly on the next beat.
        List<TickSpan> spans = [];
        QuarterFraction step = new(1, 7);
        QuarterFraction position = QuarterFraction.Zero;
        for (int i = 0; i < 7; i++)
        {
            QuarterFraction end = position + step;
            spans.Add(CanonicalTime.ToSpan(position, end).Span);
            position = end;
        }

        Assert.Equal(new Tick(480), spans[^1].End);
        Assert.Equal(480, spans.Sum(s => s.Duration));
        for (int i = 1; i < spans.Count; i++)
        {
            Assert.Equal(spans[i - 1].End, spans[i].Onset);
        }
    }

    [Fact]
    public void Error_stays_within_half_a_tick_over_long_sequences()
    {
        MidiTimeBase midi = new(1000);
        for (long midiTick = 0; midiTick <= 100_000; midiTick += 7)
        {
            Tick tick = midi.ToCanonical(midiTick).Tick;
            QuarterFraction error = tick.ToQuarters() - midi.ToQuarters(midiTick);
            QuarterFraction halfTick = new(1, 2 * CanonicalTime.TicksPerQuarter);

            Assert.True(error <= halfTick && error >= QuarterFraction.Zero - halfTick, $"tick {midiTick}: error {error}");
        }
    }
}