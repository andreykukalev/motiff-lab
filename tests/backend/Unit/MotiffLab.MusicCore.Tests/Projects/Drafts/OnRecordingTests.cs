using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Projects.Drafts;

/// <summary>«Найти на записи» (version 1.4, F2; spec «Ноты + запись»): the stretch of a recording that is a place's music, found by the engine.</summary>
public class OnRecordingTests
{
    private static readonly int[][] s_theme = [[76, 75, 76, 75], [76, 71, 74, 72], [69, 60, 64, 69], [71, 64, 68, 71]];

    private static readonly int[] s_other = [60, 62, 64, 65];

    /// <summary>A place whose base is the four bars of the theme.</summary>
    private static DraftPlace Place() => new("pl_1", "v_1", [Variant("v_1", Score(bars: 4, melody: bar => s_theme[bar]), 1, 4)]);

    /// <summary>A recording of <paramref name="bars"/> bars with the theme from bar <paramref name="from"/> (from 1), the rest other music.</summary>
    private static SourceMusic Recording(int bars, params int[] from) =>
        Score(bars: bars, melody: bar => from.Select(start => bar - (start - 1)).FirstOrDefault(index => index is >= 0 and < 4, -1) is var index and >= 0 ? s_theme[index] : s_other, prefix: "h");

    [Fact]
    public void The_bars_of_the_recording_that_play_the_place_are_found()
    {
        (TickSpan span, PlaceMatch match) = OnRecording.Find(Place(), Recording(20, 7))!.Value;

        Assert.Equal(Bars(7, 10), span);
        Assert.Equal((20, 20), (match.Matched, match.Total));
        Assert.True(match.IsSameMusic);
    }

    [Fact]
    public void Where_the_place_is_played_twice_the_earlier_is_found()
    {
        Assert.Equal(Bars(3, 6), OnRecording.Find(Place(), Recording(16, 3, 11))!.Value.Span);
    }

    [Fact]
    public void A_bar_the_recording_adds_inside_the_place_s_music_is_in_the_stretch()
    {
        int[][] played = [s_other, s_other, s_other, s_other, s_theme[0], s_theme[1], [61, 66, 69, 63], s_theme[2], s_theme[3], s_other, s_other, s_other];
        SourceMusic recording = Score(bars: played.Length, melody: bar => played[bar], prefix: "h");

        (TickSpan span, PlaceMatch match) = OnRecording.Find(Place(), recording)!.Value;

        Assert.Equal(Bars(5, 9), span);
        Assert.True(match.IsSameMusic);
    }

    [Fact]
    public void A_place_near_the_end_of_a_long_recording_is_found()
    {
        Assert.Equal(Bars(317, 320), OnRecording.Find(Place(), Recording(320, 317))!.Value.Span);
    }

    [Fact]
    public void On_a_recording_whose_bars_are_half_as_long_the_whole_place_is_found()
    {
        SourceMusic recording = Recording(20, 7);
        SourceMusic twoFour = SourceMusic.Of(recording.Notes, new MeterMap([new MeterChange(Tick.Zero, new TimeSignature(2, 4))]), recording.End, recording.Tempos, recording.Keys);

        (TickSpan span, PlaceMatch match) = OnRecording.Find(Place(), twoFour)!.Value;

        Assert.Equal(Bars(7, 10), span);
        Assert.Equal((20, 20), (match.Matched, match.Total));
    }

    [Fact]
    public void A_bar_that_matches_apart_from_the_place_s_music_stays_out_of_the_stretch()
    {
        // Sixteen bars of their own; the recording plays the first as the place's first a few bars early, other music, then
        // the place with its first bar played otherwise.
        static int[] Own(int bar) => [60 + (bar * 5 % 12), 62 + (bar * 7 % 12), 64 + (bar * 3 % 12), 67 + (bar % 12)];
        DraftPlace place = new("pl_1", "v_1", [Variant("v_1", Score(bars: 16, melody: Own), 1, 16)]);
        int[][] played = [.. Enumerable.Range(0, 8).Select(_ => s_other), Own(0), s_other, s_other, s_other, [61, 63, 66, 70], .. Enumerable.Range(1, 15).Select(Own), .. Enumerable.Range(0, 8).Select(_ => s_other)];
        SourceMusic recording = Score(bars: played.Length, melody: bar => played[bar], prefix: "h");

        Assert.Equal(Bars(14, 28), OnRecording.Find(place, recording)!.Value.Span);
    }

    [Fact]
    public void A_recording_shorter_than_the_place_is_found_as_far_as_it_plays_it()
    {
        SourceMusic recording = Score(bars: 3, melody: bar => s_theme[bar], prefix: "h");

        Assert.Equal(Bars(1, 3), OnRecording.Find(Place(), recording)!.Value.Span);
    }

    [Fact]
    public void A_recording_without_the_place_s_music_finds_nothing()
    {
        Assert.Null(OnRecording.Find(Place(), Score(bars: 12, melody: _ => s_other, prefix: "h")));
        Assert.Null(OnRecording.Find(Place(), Score(bars: 12, melody: _ => [80, 82, 83, 85], prefix: "h") with { Notes = [] }));
    }
}