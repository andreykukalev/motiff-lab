using MotiffLab.MusicCore.Analytics;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Analytics;

/// <summary>The Stage 1 analytics events: counts, flags and fixed choices only, never note content or free text (#64).</summary>
public class AnalyticsCatalogueTests
{
    [Fact]
    public void Every_event_the_app_sends_passes_the_catalogue()
    {
        AnalyticsEvent[] events =
        [
            AnalyticsEvent.AppOpened(),
            AnalyticsEvent.PieceImported("midi", opened: true, notes: 1_234, tracks: 2, performanceTiming: false, readMilliseconds: 85),
            AnalyticsEvent.StarterOpened("fur_elise"),
            AnalyticsEvent.MotifSelected(2),
            AnalyticsEvent.PlaybackStarted("variation", 100, loop: true),
            AnalyticsEvent.VariationProposed(new RhythmReshapeParameters(RhythmReshapeKind.HalfTime), kept: 2),
            AnalyticsEvent.VariationStep("accept", 1),
            AnalyticsEvent.NoteEdited(HandEditKind.Remove),
            AnalyticsEvent.MidiExported(variation: true, steps: 3),
        ];

        Assert.Equal(AnalyticsCatalogue.EventNames, events.Select(e => e.Name));
        Assert.All(events, e => Assert.All(e.Properties.Values, value => Assert.True(value is int or bool or string)));
    }

    [Fact]
    public void A_note_count_is_sent_as_its_band()
    {
        Assert.Equal("5000", AnalyticsEvent.PieceImported("midi", true, 1_234, 2, false, 85).Properties["notes"]);
        Assert.Equal("100", AnalyticsCatalogue.Band(0));
        Assert.Equal("500", AnalyticsCatalogue.Band(101));
        Assert.Equal("more", AnalyticsCatalogue.Band(1_000_000));
    }

    [Fact]
    public void Choices_name_the_parameters_not_what_was_played()
    {
        AnalyticsEvent proposed = AnalyticsEvent.VariationProposed(new RhythmReshapeParameters(RhythmReshapeKind.Displace, 2), kept: 1);

        Assert.Equal("V1", proposed.Properties["op"]);
        Assert.Equal("displace", proposed.Properties["kind"]);
        Assert.Equal(2, proposed.Properties["amount"]);
        Assert.Equal(0, AnalyticsEvent.VariationProposed(new RhythmReshapeParameters(RhythmReshapeKind.DoubleTime), 0).Properties["amount"]);
        Assert.Equal("double_time", AnalyticsCatalogue.Choice(RhythmReshapeKind.DoubleTime));
    }

    [Fact]
    public void Free_text_cannot_be_sent()
    {
        // A title or file name in place of a choice, or under a property the event does not have.
        Assert.Throws<ArgumentException>(() => AnalyticsEvent.StarterOpened("Für Elise (Beethoven, opening)"));
        Assert.Throws<ArgumentException>(() => AnalyticsEvent.PieceImported("my-song.mid", true, 10, 1, false, 5));
        Assert.Throws<ArgumentException>(() => AnalyticsCatalogue.Create("app_opened", new Dictionary<string, object> { ["file"] = "my-song.mid" }));
        Assert.Throws<ArgumentException>(() => AnalyticsCatalogue.Create("motif_selected", new Dictionary<string, object> { ["bars"] = "2" }));
    }

    [Fact]
    public void Values_outside_their_range_and_unknown_or_incomplete_events_are_refused()
    {
        Assert.Throws<ArgumentException>(() => AnalyticsEvent.MotifSelected(9));
        Assert.Throws<ArgumentException>(() => AnalyticsEvent.PlaybackStarted("both", 200, false));
        Assert.Throws<ArgumentException>(() => AnalyticsCatalogue.Create("note_played"));
        Assert.Throws<ArgumentException>(() => AnalyticsCatalogue.Create("motif_selected"));
    }

    [Fact]
    public void Counts_beyond_what_can_be_sent_are_clamped()
    {
        AnalyticsEvent imported = AnalyticsEvent.PieceImported("musicxml", true, 10, tracks: 500, performanceTiming: true, readMilliseconds: long.MaxValue);

        Assert.Equal(AnalyticsCatalogue.MaxTracks, imported.Properties["tracks"]);
        Assert.Equal(AnalyticsCatalogue.MaxMilliseconds, imported.Properties["read_ms"]);
    }
}