using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Playback;

public class PlaybackHandsTests
{
    private static Note Note(string id, Hand? hand, bool inferred = false) => new()
    {
        Id = id,
        Pitch = new Pitch(60),
        Span = new TickSpan(Tick.Zero, 480),
        Velocity = 80,
        Track = 0,
        Hand = hand is { } value ? new Attributed<Hand>(value, inferred ? Attribution.Inferred(0.7) : Attribution.Imported) : null,
        Attribution = Attribution.Imported,
    };

    [Fact]
    public void Both_hands_marked_in_the_file_can_be_heard_apart_and_are_not_inferred()
    {
        PlaybackHands hands = PlaybackHands.Of([Note("a", Hand.Right), Note("b", Hand.Left)]);

        Assert.Equal(new PlaybackHands(Right: true, Left: true, Inferred: false), hands);
        Assert.True(hands.CanSeparate);
    }

    [Fact]
    public void One_inferred_hand_makes_the_offer_say_inferred()
    {
        PlaybackHands hands = PlaybackHands.Of([Note("a", Hand.Right), Note("b", Hand.Left, inferred: true), Note("c", null)]);

        Assert.True(hands.Inferred);
        Assert.True(hands.CanSeparate);
    }

    [Fact]
    public void Notes_without_hands_or_with_one_hand_cannot_be_heard_apart()
    {
        Assert.False(PlaybackHands.Of([Note("a", null), Note("b", null)]).CanSeparate);
        Assert.False(PlaybackHands.Of([Note("a", Hand.Right)]).CanSeparate);
        Assert.Equal(default, PlaybackHands.Of([]));
    }
}