using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.MusicXml;

/// <summary>The timeline rules on their own: values placed per tick, a pickup and bars aligned to the measures.</summary>
public class MusicXmlTimelineTests
{
    private static readonly TimeSignature s_threeFour = new(3, 4);

    /// <summary>Measures starting at these quarters; the last number is where the last measure ends.</summary>
    private static (Func<int, QuarterFraction, QuarterFraction> At, Tick[] Boundaries) Measures(params long[] starts) => (
        (measure, offset) => QuarterFraction.FromQuarters(starts[measure]) + offset,
        starts.Select(start => CanonicalTime.ToTick(QuarterFraction.FromQuarters(start)).Tick).ToArray());

    private static Timed<T> At<T>(int track, int measure, T value) => new(track, 0, measure, QuarterFraction.Zero, value);

    [Fact]
    public void A_value_repeated_in_every_part_is_kept_once_and_a_different_one_replaces_it()
    {
        var (at, _) = Measures(0, 4, 8);
        var issues = new ImportIssueCollector();

        var placed = TimedValues.Merge([At(1, 0, 90), At(0, 0, 90), At(0, 1, 100), At(1, 1, 120)], at, issues, MusicXmlIssueCodes.TempoSuperseded);

        Assert.Equal([(0L, 90), (1920L, 120)], placed.Select(value => (value.Tick.Value, value.Value)));
        Assert.Equal(1, issues.CountOf(MusicXmlIssueCodes.TempoSuperseded));
    }

    [Fact]
    public void A_short_first_measure_is_a_pickup_and_a_signature_inside_it_is_dropped()
    {
        var (at, boundaries) = Measures(0, 1, 4, 7);
        var issues = new ImportIssueCollector();
        List<Timed<TimeSignature>> times = [At(0, 0, s_threeFour), new(0, 1, 0, new QuarterFraction(1, 2), TimeSignature.CommonTime)];

        var (signatures, meter) = MusicXmlTimeline.Meter(times, at, issues, boundaries);

        Assert.Equal(480, meter.PickupDuration);
        Assert.Equal([s_threeFour], signatures.Select(time => new TimeSignature(time.Numerator, time.Denominator)));
        Assert.Equal(1, issues.CountOf(MusicXmlIssueCodes.MeterInPickup));
    }

    [Fact]
    public void After_a_short_measure_the_signature_is_restated_so_the_next_measure_starts_a_bar()
    {
        var (at, boundaries) = Measures(0, 3, 5, 8, 11);
        var issues = new ImportIssueCollector();

        var (_, meter) = MusicXmlTimeline.Meter([At(0, 0, s_threeFour)], at, issues, boundaries);

        Assert.Equal(0, meter.PickupDuration);
        Assert.Equal([0L, 2400L], meter.Changes.Select(change => change.Position.Value));
        Assert.Equal(1, issues.CountOf(MusicXmlIssueCodes.MeasureShort));
    }

    [Fact]
    public void A_score_without_a_signature_or_tempo_at_the_start_assumes_four_four_and_the_default_tempo()
    {
        var (at, boundaries) = Measures(0, 4, 8);
        var issues = new ImportIssueCollector();

        var (signatures, _) = MusicXmlTimeline.Meter([], at, issues, boundaries);
        var tempos = MusicXmlTimeline.Tempos([], at, issues);

        Assert.True(signatures.Single() is { Numerator: 4, Denominator: 4, IsAssumed: true });
        Assert.True(tempos.Single().IsAssumed);
        Assert.Equal(1, issues.CountOf(MusicXmlIssueCodes.MeterDefault));
        Assert.Equal(1, issues.CountOf(MusicXmlIssueCodes.TempoDefault));
    }
}