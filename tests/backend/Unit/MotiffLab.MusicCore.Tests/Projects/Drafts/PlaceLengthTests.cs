using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Projects.Drafts;

/// <summary>A place made longer or shorter (version 1.4, Andrei's answer of 2026-11-26): its base cut again from its Notes, the journal following its start.</summary>
public class PlaceLengthTests
{
    private static readonly DateTimeOffset Later = At.AddMinutes(5);

    /// <summary>The base of bars 1–8 of the score cut again over bars <paramref name="first"/> to <paramref name="last"/>.</summary>
    private static DraftVariant Cut(int first, int last) => Variant("v_1", Score(), first, last);

    [Fact]
    public void A_place_made_longer_at_its_end_has_the_bars_of_its_source_and_keeps_its_steps()
    {
        Draft draft = DraftTakes.Take(Draft.Start("d_1", "Место", At, "pl_1", Variant("v_1", Score(), 1, 4)).With(Variant("v_2", Octaves(), 1, 4, notes: "perf_1"), DraftDrop.Variant, "pl_1"), "pl_1", 1, null, Later);

        Draft longer = PlaceLength.Resized(draft, "pl_1", Cut(1, 6));

        DraftVariant @base = longer.FindPlace("pl_1")!.BaseVariant;
        Assert.Equal(("v_1", (1, 6)), (@base.Id, @base.From.Bars!.Value));
        Assert.Equal(6, @base.Music.Bars.Count);
        Assert.Equal(draft.Steps, longer.Steps);
        Assert.Equal(new Tick(6 * BarLength), longer.ResultOf("pl_1").Music.End);
        Assert.Contains(longer.ResultOf("pl_1").Music.Notes, static note => note.Id == "v_2.h1r0");
    }

    /// <summary>A tune whose bars all differ, so a variant longer than the base lines up with it by its music: each bar a step higher.</summary>
    private static int[] Tune(int bar) => [60 + bar, 64 + bar, 67 + bar, 72 + bar];

    [Fact]
    public void A_place_made_shorter_at_its_start_drops_the_steps_outside_and_numbers_the_others_from_its_new_start()
    {
        SourceMusic notes = Score(melody: Tune), heard = Score(melody: bar => bar is 1 or 5 ? [.. Tune(bar).Select(static pitch => pitch + 12)] : Tune(bar), prefix: "h");
        Draft drafted = Draft.Start("d_1", "Предложение", At, "pl_1", Variant("v_1", notes, 1, 8)).With(Variant("v_2", heard, 1, 8, notes: "perf_1"), DraftDrop.Variant, "pl_1");
        Draft taken = DraftTakes.Take(DraftTakes.Take(drafted, "pl_1", 1, 2, Later), "pl_1", 1, 6, Later);

        Draft shorter = PlaceLength.Resized(taken, "pl_1", Variant("v_1", notes, 3, 8));

        DraftTake kept = Assert.IsType<DraftTake>(Assert.Single(shorter.Steps));
        Assert.Equal((2, new DraftTechnique(TechniqueKind.Octave, [4])), (kept.Number, kept.Technique));
        SourceMusic result = shorter.ResultOf("pl_1").Music;
        Assert.Contains(result.Notes, static note => note.Id == "v_2.h5r0" && note.Span.Onset == new Tick(3 * BarLength));
        Assert.DoesNotContain(result.Notes, static note => note.Id.StartsWith("v_1.n0", StringComparison.Ordinal) || note.Id.StartsWith("v_1.n1", StringComparison.Ordinal));
        // The приём of the variant is read against the new base: bar 6 of before is bar 4, taken; bar 2 of before is gone.
        Assert.Equal([(4, TechniqueState.Taken)], shorter.TechniquesOf("pl_1").Single(static technique => technique.Kind == TechniqueKind.Octave).Occurrences.Select(static occurrence => (occurrence.Bar, occurrence.State)));
    }

    [Fact]
    public void A_left_приём_whose_bar_is_cut_off_goes_and_one_inside_is_numbered_again()
    {
        Draft left = DraftTakes.Leave(Drafted(Octaves()), "pl_1", 1, null, Later);

        Assert.Empty(PlaceLength.Resized(left, "pl_1", Cut(3, 8)).Steps);
        Assert.Equal([2, 6], ((DraftLeave)Assert.Single(PlaceLength.Resized(left, "pl_1", Cut(1, 7)).Steps)).Technique!.Bars);
        Assert.Equal([1, 5], ((DraftLeave)Assert.Single(PlaceLength.Resized(left, "pl_1", Cut(2, 8)).Steps)).Technique!.Bars);
    }

    [Fact]
    public void The_other_variants_keep_their_anchors_inside_the_new_base_moved_with_its_start()
    {
        Draft anchored = DraftAnchors.Confirm(DraftAnchors.Confirm(Drafted(Octaves()), "pl_1", "v_2", new DraftAnchor(new Tick(BarLength), new Tick(BarLength))), "pl_1", "v_2", new DraftAnchor(new Tick(5 * BarLength), new Tick(5 * BarLength)));

        Draft shorter = PlaceLength.Resized(anchored, "pl_1", Cut(3, 8));

        Assert.Equal([new DraftAnchor(new Tick(3 * BarLength), new Tick(5 * BarLength))], shorter.FindPlace("pl_1")!.Find("v_2")!.Anchors);
    }

    [Fact]
    public void The_seam_kept_as_it_is_goes_only_where_the_place_s_boundary_moved()
    {
        Draft two = Draft.Start("d_1", "Коллаж", At, "pl_1", Variant("v_1", Score(), 1, 4))
            .With(Variant("v_2", Score(), 5, 8), DraftDrop.Place, "pl_2")
            .With(next => new DraftLeave(next, At, "pl_2") { Seam = true });

        Assert.Empty(PlaceLength.Resized(two, "pl_1", Variant("v_1", Score(), 1, 3)).Steps);
        Assert.Single(PlaceLength.Resized(two, "pl_1", Variant("v_1", Score(), 2, 4)).Steps);
        Assert.Empty(PlaceLength.Resized(two, "pl_2", Variant("v_2", Score(), 6, 8)).Steps);
        Assert.Single(PlaceLength.Resized(two, "pl_2", Variant("v_2", Score(), 5, 7)).Steps);
    }

    [Theory]
    [InlineData("pl_1", 1, 3, false)]
    [InlineData("pl_2", 6, 8, false)]
    [InlineData("pl_1", 2, 4, true)]
    [InlineData("pl_2", 5, 7, true)]
    public void A_bridge_goes_with_its_steps_where_the_boundary_of_its_seam_moved(string place, int first, int last, bool kept)
    {
        Draft two = Draft.Start("d_1", "Коллаж", At, "pl_1", Variant("v_1", Score(), 1, 4))
            .With(Variant("v_2", Score(fifths: 1, prefix: "b"), 5, 8, notes: "n_g"), DraftDrop.Place, "pl_2");
        Draft bridged = DraftBridges.Put(two, "pl_2", At, Ids).With(next => new DraftTempo(next, At, "pl_0003", 100));
        SourceMusic music = place == "pl_1" ? Score() : Score(fifths: 1, prefix: "b");

        Draft resized = PlaceLength.Resized(bridged, place, Variant(place == "pl_1" ? "v_1" : "v_2", music, first, last, notes: place == "pl_1" ? "n_1" : "n_g"));

        Assert.Equal(kept ? ["pl_1", "pl_0003", "pl_2"] : ["pl_1", "pl_2"], resized.Places.Select(static each => each.Id));
        Assert.Equal(kept ? 1 : 0, resized.Steps.Count);
    }

    [Fact]
    public void Bars_put_in_move_with_the_start_and_go_when_their_bar_line_is_cut_off()
    {
        Draft inserted = DraftTakes.Take(Drafted(WithABarAdded(4)), "pl_1", 1, null, Later);
        Assert.Equal(TechniqueKind.InsertBars, inserted.TechniquesOf("pl_1")[0].Kind);

        Draft later = PlaceLength.Resized(inserted, "pl_1", Cut(2, 8));

        DraftTake step = Assert.IsType<DraftTake>(Assert.Single(later.Steps));
        Assert.Equal(new Tick(3 * BarLength), step.Insert!.At);
        Assert.Equal([4], step.Technique!.Bars);
        Assert.Contains(later.ResultOf("pl_1").Music.Notes, static note => note.Id == "v_2.x0" && note.Span.Onset == new Tick(3 * BarLength));
        Assert.Empty(PlaceLength.Resized(inserted, "pl_1", Cut(1, 3)).Steps);
    }

    [Fact]
    public void A_bridge_a_base_made_by_the_app_the_same_span_or_another_variant_is_refused()
    {
        Draft drafted = Drafted(Octaves());
        var made = new NotesProvenance(NotesProvenance.Variation) { ParamsJson = """{"carry":"octave"}""" };
        Draft composed = Draft.Start("d_1", "Своё", At, "pl_1", new DraftVariant("v_1", At, Cut(1, 8).From, made, Score()));

        Assert.Throws<ArgumentException>(() => PlaceLength.Resized(drafted, "pl_1", Cut(1, 8)));
        Assert.Throws<ArgumentException>(() => PlaceLength.Resized(drafted, "pl_1", Variant("v_2", Score(), 1, 4)));
        Assert.Throws<ArgumentException>(() => PlaceLength.Resized(drafted, "pl_1", Variant("v_1", Score(), 1, 4, DraftPart.Right)));
        Assert.Throws<KeyNotFoundException>(() => PlaceLength.Resized(drafted, "pl_9", Cut(1, 4)));
        Assert.Throws<InvalidOperationException>(() => PlaceLength.Resized(composed, "pl_1", Cut(1, 4)));
        Draft bridged = DraftBridges.Put(Draft.Start("d_1", "Коллаж", At, "pl_1", Cut(1, 4)).With(Variant("v_2", Score(fifths: 1, prefix: "o"), 1, 4, notes: "n_other"), DraftDrop.Place, "pl_2"), "pl_2", At, Ids);
        DraftPlace bridge = bridged.Places[1];
        Assert.True(bridge.Bridge);
        Assert.Throws<InvalidOperationException>(() => PlaceLength.Resized(bridged, bridge.Id, bridge.BaseVariant));
        Assert.NotNull(PlaceLength.Refusal(bridge, Words.English));
        Assert.NotNull(PlaceLength.Refusal(composed.Places[0], Words.Russian));
        Assert.Null(PlaceLength.Refusal(drafted.Places[0], Words.Russian));
    }
}