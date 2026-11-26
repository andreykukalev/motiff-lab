using MotiffLab.MusicCore.Projects.Drafts;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Projects.Drafts;

/// <summary>The order of a draft's places (version 1.4, F1; spec «Карта черновика: порядок мест»): its structure, not a step.</summary>
public class DraftOrderTests
{
    /// <summary>Three places, bars 1–2, 3–4 and 5–6, the seam before the second and the third kept as it is, a step transposing the third.</summary>
    private static Draft Three() =>
        Draft.Start("d_1", "Коллаж", At, "pl_1", Variant("v_1", Score(), 1, 2))
            .With(Variant("v_2", Score(prefix: "b"), 3, 4), DraftDrop.Place, "pl_2")
            .With(Variant("v_3", Score(prefix: "c"), 5, 6), DraftDrop.Place, "pl_3")
            .With(next => new DraftLeave(next, At, "pl_2") { Seam = true })
            .With(next => new DraftLeave(next, At, "pl_3") { Seam = true })
            .With(next => new DraftTranspose(next, At, "pl_3", 2));

    private static string[] Order(Draft draft) => [.. draft.Places.Select(static place => place.Id)];

    [Theory]
    [InlineData("pl_3", 0, new[] { "pl_3", "pl_1", "pl_2" })]
    [InlineData("pl_1", 2, new[] { "pl_2", "pl_3", "pl_1" })]
    [InlineData("pl_2", 2, new[] { "pl_1", "pl_3", "pl_2" })]
    [InlineData("pl_2", 0, new[] { "pl_2", "pl_1", "pl_3" })]
    public void A_place_moves_to_an_index_and_the_others_keep_their_order(string place, int index, string[] order)
    {
        Draft moved = Three().WithPlaceAt(place, index);

        Assert.Equal(order, Order(moved));
        Assert.Equal(Three().Next, moved.Next);
        // The place's music goes with it: its variants, base and its own steps.
        Assert.Equal(Three().ResultOf(place).Music.Notes, moved.ResultOf(place).Music.Notes);
        Assert.Contains(moved.Steps, static step => step is DraftTranspose { Place: "pl_3" });
    }

    [Fact]
    public void A_seam_kept_as_it_is_goes_where_the_place_before_it_is_another_now()
    {
        // pl_3 first: pl_3 has no place before it, pl_1 has pl_3, pl_2 still has pl_1.
        Draft moved = Three().WithPlaceAt("pl_3", 0);

        Assert.Equal([1, 3], moved.Steps.Select(static step => step.Number));
        Assert.Contains(moved.Steps, static step => step is DraftLeave { Place: "pl_2", Seam: true });
    }

    [Fact]
    public void Moving_the_last_two_places_round_drops_both_their_seams_kept()
    {
        Draft moved = Three().WithPlaceAt("pl_3", 1);

        Assert.Equal(["pl_1", "pl_3", "pl_2"], Order(moved));
        Assert.Equal([3], moved.Steps.Select(static step => step.Number));
    }

    [Fact]
    public void A_place_moved_where_it_is_or_outside_the_places_is_refused()
    {
        Draft draft = Three();

        Assert.Throws<ArgumentException>(() => draft.WithPlaceAt("pl_2", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.WithPlaceAt("pl_2", 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.WithPlaceAt("pl_2", -1));
        Assert.Throws<KeyNotFoundException>(() => draft.WithPlaceAt("pl_9", 0));
        // A draft of one place has nowhere to move it.
        Assert.Throws<ArgumentException>(() => Draft.Start("d_1", "Одно", At, "pl_1", Variant("v_1", Score(), 1, 2)).WithPlaceAt("pl_1", 0));
    }

    /// <summary>Bars 1–2 in C major, a bridge into bars 3–4 in G major with a step of its own, then bars 5–6 in C major.</summary>
    private static Draft Bridged()
    {
        Draft draft = Draft.Start("d_1", "Коллаж", At, "pl_1", Variant("v_1", Score(), 1, 2))
            .With(Variant("v_2", Score(fifths: 1, prefix: "b"), 3, 4, notes: "n_g"), DraftDrop.Place, "pl_2")
            .With(Variant("v_3", Score(prefix: "c"), 5, 6), DraftDrop.Place, "pl_3");
        return DraftBridges.Put(draft, "pl_2", At).With(next => new DraftTempo(next, At, "pl_0004", 100));
    }

    [Theory]
    [InlineData("pl_2", 0, new[] { "pl_2", "pl_1", "pl_3" })]
    [InlineData("pl_1", 2, new[] { "pl_2", "pl_1", "pl_3" })]
    [InlineData("pl_0004", 3, new[] { "pl_1", "pl_2", "pl_3" })]
    public void A_move_that_gives_a_bridge_other_neighbours_drops_the_bridge_and_its_steps(string place, int index, string[] order)
    {
        Draft moved = Bridged().WithPlaceAt(place, index);

        Assert.Equal(order, Order(moved));
        Assert.Empty(moved.Steps);
    }

    [Fact]
    public void A_move_that_keeps_a_bridge_s_neighbours_keeps_the_bridge()
    {
        Draft moved = Bridged().WithPlaceAt("pl_3", 0);

        Assert.Equal(["pl_3", "pl_1", "pl_0004", "pl_2"], Order(moved));
        Assert.Single(moved.Steps);
    }

    [Fact]
    public void A_bridge_put_in_or_left_drops_the_seam_kept_before_the_place_after_it()
    {
        Draft two = Draft.Start("d_1", "Коллаж", At, "pl_1", Variant("v_1", Score(), 1, 2))
            .With(Variant("v_2", Score(fifths: 1, prefix: "b"), 3, 4, notes: "n_g"), DraftDrop.Place, "pl_2");
        Draft kept = two.With(next => new DraftLeave(next, At, "pl_2") { Seam = true });

        Assert.Empty(DraftBridges.Put(kept, "pl_2", At).Steps);
        Draft bridged = DraftBridges.Put(two, "pl_2", At).With(next => new DraftLeave(next, At, "pl_2") { Seam = true });
        Assert.Empty(DraftBridges.Leave(bridged, "pl_0003").Steps);
    }
}