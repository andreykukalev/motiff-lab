using MotiffLab.MusicCore.Import.Transcription;

namespace MotiffLab.MusicCore.Tests.Import.Transcription;

public class RecordingRangeTests
{
    private const long Minute = 60_000;

    [Fact]
    public void A_recording_of_any_length_starts_whole()
    {
        RecordingRange shortOne = RecordingRange.Start(3 * Minute);
        Assert.Equal((0L, 3 * Minute), (shortOne.FromMilliseconds, shortOne.ToMilliseconds));
        Assert.True(shortOne.IsWhole);

        RecordingRange concert = RecordingRange.Start(90 * Minute);
        Assert.Equal((0L, 90 * Minute), (concert.FromMilliseconds, concert.ToMilliseconds));
        Assert.True(concert.IsWhole);
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordingRange.Start(0));
    }

    [Fact]
    public void Moving_the_start_keeps_the_end_however_long_the_stretch()
    {
        RecordingRange range = RecordingRange.Start(30 * Minute).WithStart(2 * Minute);
        Assert.Equal((2 * Minute, 30 * Minute), (range.FromMilliseconds, range.ToMilliseconds));
        Assert.False(range.IsWhole);
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
    public void Moving_the_end_keeps_the_start_however_far_away()
    {
        RecordingRange range = RecordingRange.Start(30 * Minute).WithStart(10 * Minute).WithEnd(14 * Minute);
        Assert.Equal((10 * Minute, 14 * Minute), (range.FromMilliseconds, range.ToMilliseconds));

        RecordingRange longer = range.WithEnd(29 * Minute);
        Assert.Equal((10 * Minute, 29 * Minute), (longer.FromMilliseconds, longer.ToMilliseconds));
    }

    [Fact]
    public void Moving_the_end_before_the_start_carries_the_start_back_with_the_same_length()
    {
        RecordingRange range = RecordingRange.Start(30 * Minute).WithStart(10 * Minute).WithEnd(14 * Minute).WithEnd(9 * Minute);
        Assert.Equal((5 * Minute, 9 * Minute), (range.FromMilliseconds, range.ToMilliseconds));

        RecordingRange before = range.WithEnd(-5);
        Assert.Equal((0L, 1_000L), (before.FromMilliseconds, before.ToMilliseconds));
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

        // Forty minutes: twenty parts of two minutes.
        Assert.Equal(20, RecordingRange.Start(40 * Minute).Parts.Count);
    }

    [Fact]
    public void Dragging_the_whole_stretch_keeps_its_length_and_stops_at_the_ends()
    {
        RecordingRange range = RecordingRange.Start(30 * Minute).WithEnd(3 * Minute);
        RecordingRange moved = range.MovedTo(12 * Minute);
        Assert.Equal((12 * Minute, 15 * Minute), (moved.FromMilliseconds, moved.ToMilliseconds));
        Assert.Equal((27 * Minute, 30 * Minute), (range.MovedTo(29 * Minute).FromMilliseconds, range.MovedTo(29 * Minute).ToMilliseconds));
        Assert.Equal((0L, 3 * Minute), (range.MovedTo(-5).FromMilliseconds, range.MovedTo(-5).ToMilliseconds));
    }
}