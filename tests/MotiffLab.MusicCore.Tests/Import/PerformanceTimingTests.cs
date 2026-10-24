using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import;

public class PerformanceTimingTests
{
    private static MidiImport ImportFixture(string name) =>
        MidiImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Midi", "Fixtures", name)));

    private static PerformanceTiming MeasureFixture(string name)
    {
        MidiImport import = ImportFixture(name);
        return PerformanceTiming.Measure(import.Notes, import.Meter);
    }

    private static Note Note(int index, long onset, long duration = 120) => new()
    {
        Id = $"n{index}",
        Pitch = new Pitch(60 + (index % 12)),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    /// <summary>AC-S1-05: a performance played to a click is detected, with its figures and a default grid.</summary>
    [Fact]
    public void A_performance_to_a_click_offers_restore_rhythm_with_its_figures_and_a_default_grid()
    {
        PerformanceTiming timing = MeasureFixture("performance-click.mid");

        Assert.Equal(TimingKind.Performance, timing.Kind);
        Assert.Equal(128, timing.Onsets);
        Assert.Equal(42, timing.OffGridPercent);
        Assert.Equal(11, timing.Grids[0].MeanOffset(timing.Onsets));
        Assert.Equal("16th", timing.DefaultGrid?.Name);
        Assert.Equal(
            "This file was played rather than written: 42% of onsets are more than 10 ticks off the 16th and triplet-8th grids, and on the 16th grid they are 11 ticks off on average. "
            + "The playing follows the file's tempo, so Restore rhythm would snap it to 16ths, the grid it sits closest to.",
            timing.Describe());
    }

    /// <summary>AC-S1-06: a free-tempo performance is explained and stays usable as played.</summary>
    [Fact]
    public void A_free_tempo_performance_says_it_cannot_be_restored_in_this_version()
    {
        PerformanceTiming timing = MeasureFixture("free-tempo.mid");

        Assert.Equal(TimingKind.FreeTempo, timing.Kind);
        Assert.All(timing.Grids, grid => Assert.False(grid.Fits(timing.Onsets)));
        Assert.Null(timing.DefaultGrid);
        Assert.Contains("cannot restore free-tempo playing in this version, so the file plays as it was recorded", timing.Describe());
    }

    [Theory]
    [InlineData("scale-type0.mid")]
    [InlineData("two-hands-type1.mid")]
    [InlineData("piano-single-track.mid")]
    [InlineData("drums-channel10.mid")]
    [InlineData("no-time-signature.mid")]
    [InlineData("large-5000-notes.mid")]
    public void Quantised_files_offer_nothing(string name)
    {
        PerformanceTiming timing = MeasureFixture(name);

        Assert.Equal(TimingKind.Quantised, timing.Kind);
        Assert.Equal(0, timing.OffGrid);
        Assert.Null(timing.Describe());
    }

    [Fact]
    public void A_file_with_few_notes_is_not_judged()
    {
        Assert.Equal(TimingKind.TooFewNotes, MeasureFixture("performance-type1.mid").Kind);
    }

    [Fact]
    public void Written_32nds_stay_quantised()
    {
        // Onsets on every 32nd: half of them are 60 ticks off the 16th grid, but they are written values.
        Note[] notes = [.. Enumerable.Range(0, 16).Select(i => Note(i, i * 60L, 60))];

        Assert.Equal(TimingKind.Quantised, PerformanceTiming.Measure(notes, MeterMap.Default).Kind);
    }

    [Fact]
    public void Histograms_hold_every_onset_and_centre_on_the_grid()
    {
        PerformanceTiming timing = MeasureFixture("performance-click.mid");

        Assert.All(timing.Grids, grid => Assert.Equal(timing.Onsets, grid.Histogram.Sum()));
        GridFit sixteenths = timing.Grids[0];
        Assert.Equal(13, sixteenths.Histogram.Count);
        Assert.Equal(sixteenths.Histogram.Max(), Math.Max(sixteenths.Histogram[5], sixteenths.Histogram[6]));
    }

    [Fact]
    public void Swing_is_the_median_offbeat_position_through_the_beat()
    {
        // Eight beats of a long-short pair: the offbeat lands 320 ticks into each 480-tick beat (2:1).
        Note[] notes = [.. Enumerable.Range(0, 16).Select(i => Note(i, ((i / 2) * 480L) + (i % 2 == 0 ? 0 : 320)))];

        PerformanceTiming timing = PerformanceTiming.Measure(notes, MeterMap.Default);

        Assert.Equal(new SwingMeasure(8, 320, 480), timing.Swing);
        Assert.Equal(66, timing.Swing!.Percent);
        Assert.Equal(TimingKind.Quantised, timing.Kind);
    }

    [Fact]
    public void Measuring_is_deterministic()
    {
        PerformanceTiming first = MeasureFixture("performance-click.mid");
        PerformanceTiming second = MeasureFixture("performance-click.mid");

        Assert.Equal(first.Describe(), second.Describe());
        Assert.Equal(first.Grids.Select(grid => grid.Histogram), second.Grids.Select(grid => grid.Histogram));
        Assert.Equal(first.Swing, second.Swing);
    }
}