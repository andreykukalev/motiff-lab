using MotiffLab.MusicCore.Projects.Drafts;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Projects.Drafts;

/// <summary>
/// «Та же музыка» (decision record 0034, section 3): a variant of place N is offered first when the alignment finds at least
/// 50 % of the fragment's notes in that place's base, with the percentage.
/// </summary>
public class SameMusicTests
{
    /// <summary>A draft of two places: bars 1–4 of the notes, then four bars of other music.</summary>
    private static Draft TwoPlaces() =>
        Draft.Start("d_1", "Черновик 1", At, "pl_1", Variant("v_1", Score(), 1, 4))
            .With(Variant("v_2", Score(melody: _ => [60, 62, 64, 65], prefix: "o"), 1, 4), DraftDrop.Place, "pl_2");

    /// <summary>Four bars like the base where the right hand of the first <paramref name="changed"/> beats of all 16 plays other notes.</summary>
    private static PlaceMatch Against(int changed, string place = "pl_1")
    {
        int[] notes = [72, 74, 76, 77];
        DraftVariant fragment = Variant("v_9", Score(4, bar => [.. Enumerable.Range(0, 4).Select(i => (bar * 4) + i < changed ? 90 + i : notes[i])], prefix: "f"), 1, 4);
        return SameMusic.Of(TwoPlaces(), fragment.Music).Single(match => match.Place == place);
    }

    [Fact]
    public void The_same_music_is_found_in_its_place_with_the_percentage_and_offered_first()
    {
        IReadOnlyList<PlaceMatch> matches = SameMusic.Of(TwoPlaces(), Variant("v_9", Score(prefix: "f"), 1, 4).Music);

        Assert.Equal(100, matches[0].Percent);
        Assert.True(matches[0].Percent > matches[1].Percent);
        Assert.Equal("pl_1", SameMusic.Suggested(matches)!.Place);
    }

    [Fact]
    public void Half_of_the_fragment_found_is_the_same_music_and_less_is_not()
    {
        // 20 notes: 16 of the right hand, 4 of the left; changing 10 leaves 10 of 20, changing 11 leaves 9.
        PlaceMatch half = Against(10);
        PlaceMatch less = Against(11);

        Assert.Equal((10, 20, 50, true), (half.Matched, half.Total, half.Percent, half.IsSameMusic));
        Assert.Equal((9, 20, 45, false), (less.Matched, less.Total, less.Percent, less.IsSameMusic));
        Assert.Null(SameMusic.Suggested([less]));
        Assert.Equal(half, SameMusic.Suggested([less, half]));
    }

    [Fact]
    public void Other_music_is_not_offered_as_a_variant()
    {
        IReadOnlyList<PlaceMatch> matches = SameMusic.Of(TwoPlaces(), Variant("v_9", Score(melody: _ => [95, 93, 91, 89], prefix: "f"), 1, 4).Music);

        Assert.All(matches, match => Assert.True(match.Percent < 50, $"{match.Place}: {match.Percent} %"));
        Assert.Null(SameMusic.Suggested(matches));
    }
}