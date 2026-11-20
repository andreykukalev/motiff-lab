using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Projects.Drafts;

/// <summary>A draft's structure (decision record 0034, sections 1 to 3): places in order, variants with one base, the shelf, the journal.</summary>
public class DraftModelTests
{
    private static Draft Started() => Draft.Start("d_1", "Предложение 1", At, "pl_1", Variant("v_1", Score(), 1, 4));

    [Fact]
    public void A_draft_starts_with_one_place_whose_base_is_the_fragment_and_an_empty_shelf_and_journal()
    {
        Draft draft = Started();

        DraftPlace place = Assert.Single(draft.Places);
        Assert.Equal(("pl_1", "v_1"), (place.Id, place.Base));
        Assert.Empty(draft.Shelf);
        Assert.Empty(draft.Steps);
        Assert.Equal(1, draft.Next);
        Assert.Equal("Предложение 1", draft.Name);
    }

    [Fact]
    public void A_fragment_goes_where_the_player_says_never_merged_by_itself()
    {
        Draft draft = Started()
            .With(Variant("v_2", Score(prefix: "r"), 1, 4), DraftDrop.Variant, "pl_1")
            .With(Variant("v_3", Score(prefix: "o"), 5, 6), DraftDrop.Place, "pl_2")
            .With(Variant("v_4", Score(prefix: "s"), 7, 8), DraftDrop.Shelf);

        Assert.Equal(["v_1", "v_2"], draft.Places[0].Variants.Select(variant => variant.Id));
        Assert.Equal("v_1", draft.Places[0].Base);
        Assert.Equal(("pl_2", "v_3"), (draft.Places[1].Id, draft.Places[1].Base));
        Assert.Equal("v_4", Assert.Single(draft.Shelf).Id);
        Assert.Empty(draft.Steps);
        // The base's music is untouched by the variant beside it.
        Assert.Equal(Started().ResultOf("pl_1").Music.Notes.Select(note => note.Id), draft.ResultOf("pl_1").Music.Notes.Select(note => note.Id));
    }

    [Fact]
    public void A_variant_of_the_shelf_is_put_in_a_place_or_as_a_new_place_at_the_end()
    {
        Draft shelved = Started().With(Variant("v_2", Score(prefix: "s"), 1, 4), DraftDrop.Shelf);

        Draft inPlace = shelved.Placed("v_2", DraftDrop.Variant, "pl_1");
        Draft newPlace = shelved.Placed("v_2", DraftDrop.Place, "pl_2");

        Assert.Empty(inPlace.Shelf);
        Assert.Equal(["v_1", "v_2"], inPlace.Places[0].Variants.Select(variant => variant.Id));
        Assert.Equal(["pl_1", "pl_2"], newPlace.Places.Select(place => place.Id));
        Assert.Throws<KeyNotFoundException>(() => shelved.Placed("v_9", DraftDrop.Place, "pl_2"));
        Assert.Throws<ArgumentException>(() => shelved.Placed("v_2", DraftDrop.Shelf, "pl_2"));
    }

    [Fact]
    public void Choosing_another_base_gives_the_place_its_music_and_removes_the_steps_of_that_place_only()
    {
        Draft draft = Started()
            .With(Variant("v_2", Score(prefix: "r", fifths: 2), 1, 4), DraftDrop.Variant, "pl_1")
            .With(Variant("v_3", Score(prefix: "o"), 5, 6), DraftDrop.Place, "pl_2");
        draft = new Draft(draft.Id, draft.Name, draft.CreatedAt, draft.Places, draft.Shelf, [new DraftTranspose(1, At, "pl_1", 2), new DraftTranspose(2, At, "pl_2", -1)], 3);

        Draft rebased = draft.WithBase("pl_1", "v_2");

        Assert.Equal("v_2", rebased.Places[0].Base);
        Assert.Equal(2, Assert.Single(rebased.Steps).Number);
        Assert.Equal(2, rebased.ResultOf("pl_1").Music.Keys[0].Fifths);
        Assert.Same(draft, draft.WithBase("pl_1", "v_1"));
        Assert.Throws<KeyNotFoundException>(() => draft.WithBase("pl_1", "v_3"));
    }

    [Fact]
    public void Ids_of_places_and_variants_are_distinct_and_steps_name_what_the_draft_has()
    {
        Draft draft = Started();

        Assert.Throws<ArgumentException>(() => draft.With(Variant("v_1", Score(), 1, 2), DraftDrop.Shelf));
        Assert.Throws<ArgumentException>(() => draft.With(Variant("v_2", Score(), 1, 2), DraftDrop.Place, "pl_1"));
        Assert.Throws<KeyNotFoundException>(() => draft.With(Variant("v_2", Score(), 1, 2), DraftDrop.Variant, "pl_9"));
        Assert.Throws<ArgumentException>(() => new Draft("d_1", "x", At, draft.Places, [], [new DraftTranspose(1, At, "pl_9", 1)], 2));
        Assert.Throws<ArgumentException>(() => new Draft("d_1", "x", At, draft.Places, [], [new DraftTake(1, At, "pl_1", "v_9")], 2));
        Assert.Throws<ArgumentException>(() => new Draft("d_1", "x", At, draft.Places, [], [new DraftTranspose(1, At, "pl_1", 1)], 1));
        Assert.Throws<ArgumentException>(() => draft.Renamed("  "));
        Assert.Equal("Моя версия", draft.Renamed("  Моя \n версия ").Name);
    }

    [Fact]
    public void Deleting_a_draft_deletes_only_the_draft()
    {
        Project piece = Project.Create("p_1", "minuet.mid", [1, 2, 3], At);
        Project withDrafts = piece.WithNewDraft(Started()).WithNewDraft(Draft.Start("d_2", "Другой", At, "pl_1", Variant("v_1", Score(), 1, 1)));

        Project left = withDrafts.WithoutDraft("d_1");

        Assert.Equal("d_2", Assert.Single(left.Drafts).Id);
        Assert.Equal(ProjectFile.Write(piece), ProjectFile.Write(left.WithoutDraft("d_2")));
        Assert.Throws<KeyNotFoundException>(() => left.WithoutDraft("d_1"));
        Assert.Throws<ArgumentException>(() => withDrafts.WithNewDraft(Started()));
    }

    [Fact]
    public void A_draft_is_changed_in_place_and_keeps_its_id()
    {
        Project piece = Project.Create("p_1", "minuet.mid", [1, 2, 3], At).WithNewDraft(Started());

        Project renamed = piece.ChangeDraft("d_1", draft => draft.Renamed("Кода"));

        Assert.Equal("Кода", renamed.FindDraft("d_1")!.Name);
        Assert.Throws<KeyNotFoundException>(() => piece.ChangeDraft("d_9", draft => draft));
        Assert.Throws<ArgumentException>(() => piece.ChangeDraft("d_1", _ => Draft.Start("d_2", "x", At, "pl_1", Variant("v_1", Score(), 1, 1))));
    }

    [Fact]
    public void The_draft_is_one_score_of_its_places_each_in_its_own_meter_key_and_tempo_with_its_own_bar_numbers()
    {
        SourceMusic waltz = SourceMusic.Of(
            [Note("w0", 67, 0, 3 * Quarter), Note("w1", 69, 3 * Quarter, 3 * Quarter)],
            new MeterMap([new MeterChange(Tick.Zero, new TimeSignature(3, 4))]),
            new Tick(6 * Quarter),
            [new PlaybackTempo(Tick.Zero, 500_000)],
            []);
        Draft draft = Started()
            .With(Variant("v_2", Score(fifths: -3, perMinute: 60, prefix: "o"), 1, 2), DraftDrop.Place, "pl_2")
            .With(DraftFragment.Variant("v_3", At, waltz, new TickSpan(Tick.Zero, 6 * Quarter), DraftPart.All, "n_1", Read), DraftDrop.Place, "pl_3");

        DraftMusic score = DraftMusic.Of(draft);

        Assert.Equal([("pl_1", 1, 4), ("pl_2", 5, 6), ("pl_3", 7, 8)], score.Places.Select(place => (place.Place, place.FirstBar, place.LastBar)));
        Assert.Equal(new Tick(6 * BarLength), score.Places[2].Span.Onset);
        Assert.Equal([0, -3, 0], score.Places.Select(place => KeySignature.InForce(score.Music.Keys, place.Span.Onset)!.Fifths));
        Assert.Equal([(0, (KeyMode?)KeyMode.Major), (-3, KeyMode.Major)], score.Places.Take(2).Select(place => (place.Key!.Fifths, place.Key.Mode)));
        Assert.Null(score.Places[2].Key);
        Assert.Null(KeySignature.InForce(score.Music.Keys, score.Places[2].Span.Onset)!.Mode);
        Assert.Equal([90, 60, 120], score.Places.Select(place => score.Music.TempoAt(place.Span.Onset)));
        Assert.Equal(new TimeSignature(3, 4), score.Music.Meter.BarAt(score.Places[2].Span.Onset).Signature);
        Assert.Contains(score.Music.Notes, note => note.Id == "v_3.w1");
    }

    [Fact]
    public void A_first_place_whose_source_states_no_key_has_none_in_the_score()
    {
        SourceMusic keyless = Score() with { Keys = [] };
        Draft draft = Draft.Start("d_1", "Предложение 1", At, "pl_1", Variant("v_1", keyless, 1, 2))
            .With(Variant("v_2", Score(fifths: 2, prefix: "o"), 1, 2), DraftDrop.Place, "pl_2");

        DraftMusic score = DraftMusic.Of(draft);

        Assert.Null(score.Places[0].Key);
        Assert.Null(KeySignature.InForce(score.Music.Keys, Tick.Zero));
        Assert.Equal(2, Assert.Single(score.Music.Keys).Fifths);
    }

    [Fact]
    public void Anchors_are_only_on_a_variant_of_a_place_that_is_not_its_base_and_a_new_base_drops_its_own()
    {
        DraftVariant anchored = Variant("v_2", Score(prefix: "h"), 1, 4) with { Anchors = [new DraftAnchor(new Tick(BarLength), new Tick(BarLength))] };
        Draft draft = Started().With(anchored, DraftDrop.Variant, "pl_1");

        Draft rebased = draft.WithBase("pl_1", "v_2");

        Assert.Single(draft.FindPlace("pl_1")!.Find("v_2")!.Anchors);
        Assert.Empty(rebased.FindPlace("pl_1")!.BaseVariant.Anchors);
        Assert.Throws<ArgumentException>(() => new DraftPlace("pl_9", "v_2", [anchored]));
        Assert.Throws<ArgumentException>(() => Started().With(anchored, DraftDrop.Shelf));
        Assert.Throws<ArgumentException>(() => Started().With(anchored, DraftDrop.Place, "pl_2"));
    }
}