using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Time;

public class MeterMapTests
{
    private static readonly TimeSignature s_threeFour = new(3, 4);
    private static readonly TimeSignature s_sixEight = new(6, 8);

    private static MeterChange Change(long tick, TimeSignature signature) => new(new Tick(tick), signature);

    private static MetricPosition Pos(int bar, int beat, long offset = 0) => new(bar, beat, offset);

    [Fact]
    public void Default_map_is_four_four_from_bar_one()
    {
        MeterMap map = MeterMap.Default;

        Assert.False(map.HasPickup);
        Assert.Equal(1, map.FirstBarNumber);
        Assert.Equal(Pos(1, 1), map.ToPosition(Tick.Zero));
        Assert.Equal(Pos(1, 2, 240), map.ToPosition(new Tick(720)));
        Assert.Equal(Pos(3, 1), map.ToPosition(new Tick(3840)));
        Assert.Equal(TimeSignature.CommonTime, map.SignatureAt(new Tick(1_000_000)));
    }

    [Fact]
    public void Single_meter_bars_repeat_without_end()
    {
        MeterMap map = new([Change(0, s_threeFour)]);

        Bar bar = map.BarAt(new Tick(1440 * 100 + 5));

        Assert.Equal(101, bar.Number);
        Assert.Equal(new TickSpan(new Tick(1440 * 100), 1440), bar.Span);
        Assert.True(bar.IsComplete);
        Assert.False(bar.IsPickup);
    }

    [Fact]
    public void Beat_is_the_denominator_note_value()
    {
        MeterMap map = new([Change(0, s_sixEight)]);

        Assert.Equal(Pos(1, 4), map.ToPosition(new Tick(720)));
        Assert.Equal(Pos(1, 6, 120), map.ToPosition(new Tick(1320)));
        Assert.Equal(Pos(2, 1), map.ToPosition(new Tick(1440)));
    }

    [Fact]
    public void Triplet_positions_are_exact_offsets()
    {
        MeterMap map = MeterMap.Default;

        // Second and third eighth-note triplets of beat 2.
        Assert.Equal(Pos(1, 2, 160), map.ToPosition(new Tick(640)));
        Assert.Equal(Pos(1, 2, 320), map.ToPosition(new Tick(800)));
    }

    [Fact]
    public void Pickup_is_bar_zero_counted_from_the_end_of_a_full_bar()
    {
        // One-beat pickup in 3/4 (AC-S1-02: bar 1 starts after the pickup).
        MeterMap map = new([Change(0, s_threeFour)], pickupDuration: 480);

        Assert.True(map.HasPickup);
        Assert.Equal(0, map.FirstBarNumber);
        Assert.Equal(Pos(0, 3), map.ToPosition(Tick.Zero));
        Assert.Equal(Pos(0, 3, 240), map.ToPosition(new Tick(240)));
        Assert.Equal(Pos(1, 1), map.ToPosition(new Tick(480)));
        Assert.Equal(Pos(2, 1), map.ToPosition(new Tick(480 + 1440)));

        Bar pickup = map.GetBar(0);
        Assert.True(pickup.IsPickup);
        Assert.False(pickup.IsComplete);
        Assert.Equal(new TickSpan(Tick.Zero, 480), pickup.Span);
        Assert.Equal(new TickSpan(new Tick(480), 1440), map.GetBar(1).Span);
    }

    [Fact]
    public void Pickup_shorter_than_a_beat_starts_part_way_through_the_last_beat()
    {
        // An eighth-note pickup in 4/4 starts on the "and" of beat 4.
        MeterMap map = new([Change(0, TimeSignature.CommonTime)], pickupDuration: 240);

        Assert.Equal(Pos(0, 4, 240), map.ToPosition(Tick.Zero));
        Assert.Equal(Tick.Zero, map.ToTick(Pos(0, 4, 240)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ToTick(Pos(0, 4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ToTick(Pos(0, 1)));
    }

    [Fact]
    public void Meter_change_on_a_bar_line_starts_the_next_bar_number()
    {
        // Two bars of 4/4, then 3/4.
        MeterMap map = new([Change(0, TimeSignature.CommonTime), Change(3840, s_threeFour)]);

        Bar third = map.GetBar(3);
        Assert.Equal(new TickSpan(new Tick(3840), 1440), third.Span);
        Assert.Equal(s_threeFour, third.Signature);
        Assert.Equal(Pos(2, 4), map.ToPosition(new Tick(3360)));
        Assert.Equal(Pos(3, 1), map.ToPosition(new Tick(3840)));
        Assert.Equal(Pos(4, 2), map.ToPosition(new Tick(3840 + 1440 + 480)));
        Assert.All([map.GetBar(1), map.GetBar(2), third], bar => Assert.True(bar.IsComplete));
    }

    [Fact]
    public void Meter_change_mid_bar_cuts_the_bar_short_without_moving_anything()
    {
        // 4/4, but 3/4 starts after three beats of bar 2: bar 2 is a three-beat 4/4 bar.
        MeterMap map = new([Change(0, TimeSignature.CommonTime), Change(1920 + 1440, s_threeFour)]);

        Bar cut = map.GetBar(2);
        Assert.Equal(new TickSpan(new Tick(1920), 1440), cut.Span);
        Assert.Equal(TimeSignature.CommonTime, cut.Signature);
        Assert.False(cut.IsComplete);
        Assert.False(cut.IsPickup);

        Assert.Equal(Pos(2, 3, 479), map.ToPosition(new Tick(3359)));
        Assert.Equal(Pos(3, 1), map.ToPosition(new Tick(3360)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ToTick(Pos(2, 4)));
    }

    [Fact]
    public void Cut_bar_may_end_part_way_through_a_beat()
    {
        MeterMap map = new([Change(0, TimeSignature.CommonTime), Change(600, s_threeFour)]);

        Assert.Equal(new TickSpan(Tick.Zero, 600), map.GetBar(1).Span);
        Assert.Equal(Pos(1, 2, 119), map.ToPosition(new Tick(599)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ToTick(Pos(1, 2, 120)));
        Assert.Equal(Pos(2, 1), map.ToPosition(new Tick(600)));
    }

    [Fact]
    public void Meter_change_right_after_the_pickup_takes_over_bar_one()
    {
        // Pickup in 4/4, then 3/4 from bar 1.
        MeterMap map = new([Change(0, TimeSignature.CommonTime), Change(480, s_threeFour)], pickupDuration: 480);

        Assert.Equal(Pos(0, 4), map.ToPosition(Tick.Zero));
        Assert.Equal(TimeSignature.CommonTime, map.GetBar(0).Signature);
        Assert.Equal(new TickSpan(new Tick(480), 1440), map.GetBar(1).Span);
        Assert.Equal(s_threeFour, map.GetBar(1).Signature);
        Assert.Equal(Pos(2, 1), map.ToPosition(new Tick(1920)));
    }

    [Fact]
    public void Pickup_then_later_change_numbers_bars_from_one_after_the_pickup()
    {
        MeterMap map = new([Change(0, s_threeFour), Change(480 + (2 * 1440), s_sixEight)], pickupDuration: 480);

        Assert.Equal(new TickSpan(new Tick(480 + (2 * 1440)), 1440), map.GetBar(3).Span);
        Assert.Equal(s_sixEight, map.SignatureAt(new Tick(3360)));
        Assert.Equal(s_threeFour, map.SignatureAt(new Tick(3359)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(239)]
    [InlineData(480)]
    [InlineData(1919)]
    [InlineData(2400)]
    [InlineData(3359)]
    [InlineData(3360)]
    [InlineData(5000)]
    [InlineData(123_456)]
    public void Positions_round_trip_through_bar_and_beat(long tick)
    {
        MeterMap map = new(
            [Change(0, TimeSignature.CommonTime), Change(1920 + 480, s_threeFour), Change(1920 + 480 + 1000, s_sixEight)],
            pickupDuration: 480);

        Tick position = new(tick);

        Assert.Equal(position, map.ToTick(map.ToPosition(position)));
    }

    [Fact]
    public void Every_tick_round_trips_across_pickup_and_changes()
    {
        MeterMap map = new(
            [Change(0, s_threeFour), Change(240 + 1440 + 700, new TimeSignature(5, 16)), Change(240 + 1440 + 700 + 1800, TimeSignature.CommonTime)],
            pickupDuration: 240);

        for (long tick = 0; tick < 8000; tick++)
        {
            Tick position = new(tick);
            MetricPosition metric = map.ToPosition(position);

            Assert.Equal(position, map.ToTick(metric));
            Assert.True(map.BarAt(position).Span.Contains(position));
            Assert.Equal(map.BarAt(position).Number, metric.Bar);
        }
    }

    [Fact]
    public void Bars_tile_the_timeline_with_consecutive_numbers()
    {
        MeterMap map = new(
            [Change(0, TimeSignature.CommonTime), Change(480 + 1920 + 960, s_sixEight), Change(10_000, s_threeFour)],
            pickupDuration: 480);

        Bar[] bars = [.. map.BarsBefore(new Tick(20_000))];

        Assert.Equal(0, bars[0].Number);
        Assert.Equal(Tick.Zero, bars[0].Span.Onset);
        for (int i = 1; i < bars.Length; i++)
        {
            Assert.Equal(bars[i - 1].Number + 1, bars[i].Number);
            Assert.Equal(bars[i - 1].Span.End, bars[i].Span.Onset);
            Assert.Equal(bars[i], map.GetBar(bars[i].Number));
        }

        Assert.True(bars[^1].Span.Contains(new Tick(19_999)));
        Assert.Equal([0, 2, 7], bars.Where(bar => !bar.IsComplete).Select(bar => bar.Number));
    }

    [Fact]
    public void Bars_before_stops_at_the_bar_containing_the_last_tick()
    {
        MeterMap map = MeterMap.Default;

        Assert.Empty(map.BarsBefore(Tick.Zero));
        Assert.Equal([1], map.BarsBefore(new Tick(1)).Select(bar => bar.Number));
        Assert.Equal([1], map.BarsBefore(new Tick(1920)).Select(bar => bar.Number));
        Assert.Equal([1, 2], map.BarsBefore(new Tick(1921)).Select(bar => bar.Number));
    }

    [Fact]
    public void Bar_numbers_outside_the_piece_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MeterMap.Default.GetBar(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeterMap.Default.ToTick(Pos(0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MeterMap([Change(0, s_threeFour)], 480).GetBar(-1));
    }

    [Fact]
    public void Invalid_metric_positions_are_rejected()
    {
        MeterMap map = MeterMap.Default;

        Assert.Throws<ArgumentOutOfRangeException>(() => map.ToTick(Pos(1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ToTick(Pos(1, 5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ToTick(Pos(1, 1, 480)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ToTick(Pos(1, 1, -1)));
    }

    [Fact]
    public void Map_must_start_with_a_signature_at_tick_zero()
    {
        Assert.Throws<ArgumentException>(() => new MeterMap([]));
        Assert.Throws<ArgumentException>(() => new MeterMap([Change(480, s_threeFour)]));
    }

    [Fact]
    public void Changes_must_strictly_increase()
    {
        Assert.Throws<ArgumentException>(() => new MeterMap([Change(0, s_threeFour), Change(0, s_sixEight)]));
        Assert.Throws<ArgumentException>(() => new MeterMap([Change(0, s_threeFour), Change(2880, s_sixEight), Change(1440, s_threeFour)]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1440)]
    [InlineData(2000)]
    public void Pickup_must_be_shorter_than_a_full_bar(long pickup)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MeterMap([Change(0, s_threeFour)], pickup));
    }

    [Fact]
    public void Meter_cannot_change_inside_the_pickup()
    {
        Assert.Throws<ArgumentException>(() => new MeterMap([Change(0, s_threeFour), Change(240, s_sixEight)], pickupDuration: 480));
    }

    [Fact]
    public void Changes_are_exposed_in_order()
    {
        MeterChange[] changes = [Change(0, TimeSignature.CommonTime), Change(1920, s_threeFour)];

        Assert.Equal(changes, new MeterMap(changes).Changes);
    }

    [Fact]
    public void Metric_position_text_is_compact()
    {
        Assert.Equal("5:2", Pos(5, 2).ToString());
        Assert.Equal("5:2+240t", Pos(5, 2, 240).ToString());
    }
}