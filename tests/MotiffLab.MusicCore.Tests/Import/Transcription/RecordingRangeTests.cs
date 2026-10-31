using MotiffLab.MusicCore.Import.Transcription;

namespace MotiffLab.MusicCore.Tests.Import.Transcription;

public class RecordingRangeTests
{
    private const long Minute = 60_000;

    [Fact]
    public void A_short_recording_starts_whole_and_a_long_one_at_its_first_seven_minutes()
    {
        RecordingRange shortOne = RecordingRange.Start(3 * Minute);
        Assert.Equal((0L, 3 * Minute), (shortOne.FromMilliseconds, shortOne.ToMilliseconds));
        Assert.True(shortOne.IsWhole);

        RecordingRange longOne = RecordingRange.Start(30 * Minute);
        Assert.Equal((0L, 7 * Minute), (longOne.FromMilliseconds, longOne.ToMilliseconds));
        Assert.False(longOne.IsWhole);
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordingRange.Start(0));
    }

    [Fact]
    public void Moving_the_start_keeps_the_end_while_the_stretch_fits()
    {
        RecordingRange range = RecordingRange.Start(30 * Minute).WithStart(2 * Minute);
        Assert.Equal((2 * Minute, 7 * Minute), (range.FromMilliseconds, range.ToMilliseconds));
    }

    [Fact]
    public void Moving_the_start_past_the_end_carries_the_end_along_with_the_same_length()
    {
        RecordingRange range = RecordingRange.Start(30 * Minute).WithEnd(4 * Minute).WithStart(12 * Minute + 30_000);
        Assert.Equal((12 * Minute + 30_000, 16 * Minute + 30_000), (range.FromMilliseconds, range.ToMilliseconds));

        RecordingRange atTheEnd = range.WithStart(29 * Minute);
        Assert.Equal((29 * Minute, 30 * Minute), (atTheEnd.FromMilliseconds, atTheEnd.ToMilliseconds));

        RecordingRange pastTheEnd = range.WithStart(31 * Minute);
        Assert.Equal((30 * Minute - 1_000, 30 * Minute), (pastTheEnd.FromMilliseconds, pastTheEnd.ToMilliseconds));
    }

    [Fact]
    public void Moving_the_end_more_than_seven_minutes_away_pulls_the_start_after_it()
    {
        RecordingRange range = RecordingRange.Start(30 * Minute).WithEnd(20 * Minute);
        Assert.Equal((13 * Minute, 20 * Minute), (range.FromMilliseconds, range.ToMilliseconds));
        Assert.Equal(7 * Minute, range.Milliseconds);

        RecordingRange before = range.WithEnd(-5);
        Assert.Equal((0L, 1_000L), (before.FromMilliseconds, before.ToMilliseconds));
    }

    [Fact]
    public void Moving_the_end_within_seven_minutes_of_the_start_keeps_the_start()
    {
        RecordingRange range = RecordingRange.Start(30 * Minute).WithStart(10 * Minute).WithEnd(14 * Minute);
        Assert.Equal((10 * Minute, 14 * Minute), (range.FromMilliseconds, range.ToMilliseconds));
    }

    [Fact]
    public void A_recording_shorter_than_a_second_is_heard_whole()
    {
        RecordingRange range = RecordingRange.Start(400).WithStart(300).WithEnd(100);
        Assert.Equal((0L, 400L), (range.FromMilliseconds, range.ToMilliseconds));
    }

    [Fact]
    public void The_stretch_tells_how_many_requests_it_takes_and_how_much_sound_they_carry()
    {
        RecordingRange two = RecordingRange.Start(30 * Minute).WithStart(10 * Minute).WithEnd(13 * Minute);
        Assert.Equal(2, two.Parts.Count);

        // Two parts of 90 s, each with 5 s of the other: 95 s each.
        Assert.Equal(190_000, two.SentMilliseconds);

        RecordingRange one = two.WithEnd(11 * Minute + 30_000);
        Assert.Single(one.Parts);
        Assert.Equal(90_000, one.SentMilliseconds);

        Assert.Equal(4, RecordingRange.Start(30 * Minute).Parts.Count);
    }
}