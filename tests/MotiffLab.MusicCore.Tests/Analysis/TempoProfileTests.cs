using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Tests.Import.Transcription;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>A piece's tempo bar by bar, computed and never stored (version 0.5, «Темп живого исполнения»).</summary>
public class TempoProfileTests
{
    private const long Bar = 1920;

    private static readonly Tick s_end = new(12 * Bar);

    /// <summary>Twelve bars of 4/4: a quarter at 120 a minute, then at 90 in bars 5 to 8, then at 120 again.</summary>
    private static PlaybackTempo[] Slower() =>
        [new(Tick.Zero, 500_000), new(new Tick(4 * Bar), 666_667), new(new Tick(8 * Bar), 500_000)];

    [Fact]
    public void A_steady_tempo_is_one_value_throughout()
    {
        TempoProfile profile = TempoProfile.Of(MeterMap.Default, [], s_end, inferred: false)!;

        Assert.Equal(12, profile.Bars.Count);
        Assert.Equal((1200, 1200, 1200), (profile.Usual, profile.Low, profile.High));
        Assert.False(profile.Varies);
        Assert.Empty(profile.Changes);
        MotifFact fact = Assert.Single(profile.Facts(Words.English));
        Assert.Equal("piece.tempo.profile", fact.Id);
        Assert.Equal(FactCertainty.Detected, fact.Certainty);
        Assert.Equal("Tempo: a quarter at 120 BPM throughout.", fact.Text);
    }

    [Fact]
    public void Bars_markedly_slower_than_usual_are_a_change()
    {
        TempoProfile profile = TempoProfile.Of(MeterMap.Default, Slower(), s_end, inferred: false)!;

        Assert.Equal(1200, profile.Usual);
        Assert.Equal(900, profile.Low);
        Assert.Equal(1200, profile.High);
        TempoChange change = Assert.Single(profile.Changes);
        Assert.Equal((5, 8, true, 900), (change.FirstBar, change.LastBar, change.Slower, change.Tenths));
        Assert.Equal(TickSpan.FromBounds(new Tick(4 * Bar), new Tick(8 * Bar)), change.Span);

        IReadOnlyList<MotifFact> facts = profile.Facts(Words.Russian);
        Assert.Equal(["piece.tempo.profile", "piece.tempo.change.1"], facts.Select(fact => fact.Id));
        Assert.Equal("Темп: обычно четверть 120 BPM, от 90 до 120 BPM; медленнее в 1 отрезке.", facts[0].Text);
        Assert.Equal("Медленнее в тактах 5–8: четверть около 90 BPM.", facts[1].Text);
        Assert.Equal(change.Span, Assert.Single(facts[1].Places).Span);
    }

    [Fact]
    public void A_piece_of_the_layer_has_its_own_profile_and_two_bars_are_no_change()
    {
        TempoProfile within = TempoProfile.Of(MeterMap.Default, Slower(), s_end, inferred: false, TickSpan.FromBounds(new Tick(4 * Bar), new Tick(8 * Bar)))!;
        PlaybackTempo[] brief = [new(Tick.Zero, 500_000), new(new Tick(4 * Bar), 666_667), new(new Tick(6 * Bar), 500_000)];

        Assert.Equal(4, within.Bars.Count);
        Assert.Equal(900, within.Usual);
        Assert.Empty(within.Changes);
        Assert.Empty(TempoProfile.Of(MeterMap.Default, brief, s_end, inferred: false)!.Changes);
        Assert.Null(TempoProfile.Of(MeterMap.Default, [], Tick.Zero, inferred: false));
    }

    [Fact]
    public void A_recordings_profile_is_inferred_and_follows_the_playing()
    {
        ImportedPiece heard = BarGridTests.Import(LivePlaying.Heard(LivePlaying.At(1_000_000, 150_000, BarGridTests.Waltz())));
        ImportedPiece barred = BarGrid.Find(heard.Midi!)!.Apply(heard);

        TempoProfile profile = TempoProfile.Of(barred.Meter, PlaybackScore.FromPiece(barred).Tempos, barred.End, inferred: true)!;

        Assert.True(profile.Inferred);
        Assert.InRange(profile.Usual, 970, 1030);
        Assert.Equal(FactCertainty.Inferred, profile.Facts(Words.English)[0].Certainty);
        Assert.Equal(profile.Bars, TempoProfile.Of(barred.Meter, PlaybackScore.FromPiece(barred).Tempos, barred.End, inferred: true)!.Bars);
    }
}