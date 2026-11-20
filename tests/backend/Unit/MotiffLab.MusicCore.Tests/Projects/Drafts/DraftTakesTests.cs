using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Projects.Drafts;

/// <summary>Taking and leaving приёмы, «Взять всё уверенное», a hand or a selection from a variant, undo and anchors, as journal steps (decision record 0034, section 4).</summary>
public class DraftTakesTests
{
    private static readonly DateTimeOffset Later = At.AddMinutes(5);

    [Fact]
    public void Taken_everywhere_a_technique_is_one_step_of_its_notes_and_the_result_plays_the_variant_there()
    {
        Draft draft = DraftTakes.Take(Drafted(Octaves()), "pl_1", 1, null, Later);

        DraftTake step = Assert.IsType<DraftTake>(Assert.Single(draft.Steps));
        Assert.Equal((1, 2, "v_2"), (step.Number, draft.Next, step.Variant));
        Assert.Equal(new DraftTechnique(TechniqueKind.Octave, [2, 6]), step.Technique);
        Assert.Equal(8, step.Removed.Count);
        Assert.Equal(8, step.Added.Count);
        SourceMusic result = draft.ResultOf("pl_1").Music;
        Assert.Contains(result.Notes, note => note.Id == "v_2.h5r0" && note.Span.Onset == new Tick(5 * BarLength));
        Assert.DoesNotContain(result.Notes, note => note.Id == "v_1.n1r0");
        Assert.Equal(TechniqueState.Taken, Assert.Single(draft.TechniquesOf("pl_1")).State);
    }

    [Fact]
    public void Taken_only_here_a_technique_stays_open_elsewhere_and_keeps_its_number()
    {
        Draft draft = DraftTakes.Take(Drafted(OctavesAndBass()), "pl_1", 1, 6, Later);

        PlaceTechnique octave = draft.TechniquesOf("pl_1")[0];
        Assert.Equal(1, octave.Number);
        Assert.Equal([(2, TechniqueState.Open), (6, TechniqueState.Taken)], octave.Occurrences.Select(static occurrence => (occurrence.Bar, occurrence.State)));
        Assert.Equal([6], Assert.IsType<DraftTake>(Assert.Single(draft.Steps)).Technique!.Bars);
        Assert.Throws<InvalidOperationException>(() => DraftTakes.Take(draft, "pl_1", 1, 6, Later));
        Assert.Throws<KeyNotFoundException>(() => DraftTakes.Take(draft, "pl_1", 9, null, Later));
    }

    [Fact]
    public void Left_a_technique_keeps_the_base_and_its_number_goes()
    {
        Draft drafted = Drafted(Octaves());

        Draft draft = DraftTakes.Leave(drafted, "pl_1", 1, null, Later);

        DraftLeave step = Assert.IsType<DraftLeave>(Assert.Single(draft.Steps));
        Assert.Equal(("v_2", new DraftTechnique(TechniqueKind.Octave, [2, 6])), (step.Variant, step.Technique));
        Assert.Equal(drafted.ResultOf("pl_1").Music.Notes, draft.ResultOf("pl_1").Music.Notes);
        Assert.Equal(TechniqueState.Left, Assert.Single(draft.TechniquesOf("pl_1")).State);
        Assert.Throws<InvalidOperationException>(() => DraftTakes.Take(draft, "pl_1", 1, null, Later));
    }

    [Fact]
    public void Take_all_sure_takes_every_sure_open_technique_as_one_group_one_undo_takes_back()
    {
        Draft drafted = DraftTakes.Leave(Drafted(OctavesAndBass()), "pl_1", 2, null, Later);
        Draft other = Drafted(OctavesAndBass());

        (Draft draft, IReadOnlyList<int> taken) = DraftTakes.TakeSure(other, "pl_1", Later);

        Assert.Equal([1, 2], taken);
        Assert.Equal(2, draft.Steps.Count);
        Assert.All(draft.Steps, step => Assert.Equal("g_0001", step.Group));
        Draft undone = draft.Undone();
        Assert.Empty(undone.Steps);
        Assert.Equal(3, undone.Next);
        Assert.Equal([1], DraftTakes.TakeSure(drafted, "pl_1", Later).Taken);
    }

    [Fact]
    public void Take_all_sure_never_merges_two_variants_in_one_bar()
    {
        Draft draft = Drafted(Octaves()).With(Variant("v_3", Octaves(), 1, 8, notes: "perf_2"), DraftDrop.Variant, "pl_1");
        Assert.Equal([("v_2", true), ("v_3", true)], draft.TechniquesOf("pl_1").Select(static technique => (technique.Variant, technique.IsSure)));

        (Draft taken, IReadOnlyList<int> numbers) = DraftTakes.TakeSure(draft, "pl_1", Later);

        Assert.Equal([1], numbers);
        SourceMusic result = taken.ResultOf("pl_1").Music;
        Assert.Equal(5, result.NotesIn(new Tick(BarLength), new Tick(2 * BarLength)).Count);
        Assert.DoesNotContain(result.Notes, static note => note.Id.StartsWith("v_3.", StringComparison.Ordinal));
    }

    [Fact]
    public void Take_all_sure_never_takes_inserted_bars_and_says_when_nothing_is_sure()
    {
        Draft draft = Drafted(WithABarAdded(after: 4));

        Assert.Throws<InvalidOperationException>(() => DraftTakes.TakeSure(draft, "pl_1", Later));
    }

    [Fact]
    public void Inserted_bars_taken_go_in_whole_and_the_bars_after_them_move_on()
    {
        Draft draft = DraftTakes.Take(Drafted(WithABarAdded(after: 4)), "pl_1", 1, null, Later);

        DraftTake step = Assert.IsType<DraftTake>(Assert.Single(draft.Steps));
        Assert.Equal(new DraftInsert(new Tick(4 * BarLength), new TickSpan(new Tick(4 * BarLength), BarLength)), step.Insert);
        SourceMusic result = draft.ResultOf("pl_1").Music;
        Assert.Equal(9, result.Bars.Count);
        Assert.Equal(new Tick(5 * BarLength), result.Notes.Single(note => note.Id == "v_1.n4r0").Span.Onset);
        Assert.Equal(TechniqueState.Taken, Assert.Single(draft.TechniquesOf("pl_1")).State);
    }

    [Fact]
    public void A_hand_taken_from_a_variant_replaces_the_hand_of_the_place_and_decides_its_techniques()
    {
        Draft draft = DraftTakes.TakeFrom(Drafted(OctavesAndBass()), "pl_1", "v_2", DraftPart.Right, null, Later);

        DraftTake step = Assert.IsType<DraftTake>(Assert.Single(draft.Steps));
        Assert.Equal((DraftPart.Right, (DraftTechnique?)null), (step.Part, step.Technique));
        SourceMusic result = draft.ResultOf("pl_1").Music;
        Assert.All(result.Notes.Where(static note => note.Hand?.Value == Hand.Right), note => Assert.StartsWith("v_2.", note.Id, StringComparison.Ordinal));
        Assert.All(result.Notes.Where(static note => note.Hand?.Value == Hand.Left), note => Assert.StartsWith("v_1.", note.Id, StringComparison.Ordinal));
        Assert.Equal([TechniqueState.Taken, TechniqueState.Open], draft.TechniquesOf("pl_1").Select(static technique => technique.State));
        Assert.Throws<InvalidOperationException>(() => DraftTakes.TakeFrom(draft, "pl_1", "v_2", DraftPart.Right, null, Later));
        Assert.Throws<ArgumentException>(() => DraftTakes.TakeFrom(draft, "pl_1", "v_1", DraftPart.Left, null, Later));
    }

    [Fact]
    public void A_selection_taken_from_a_variant_takes_only_its_span()
    {
        Draft draft = DraftTakes.TakeFrom(Drafted(OctavesAndBass()), "pl_1", "v_2", DraftPart.All, Bars(4, 4), Later);

        DraftTake step = Assert.IsType<DraftTake>(Assert.Single(draft.Steps));
        Assert.Equal(["v_1.n3l", "v_1.n3r0", "v_1.n3r1", "v_1.n3r2", "v_1.n3r3"], step.Removed.Order(StringComparer.Ordinal));
        Assert.Contains("v_2.h3l", step.Added);
        Assert.Equal(43, draft.ResultOf("pl_1").Music.Notes.Single(note => note.Id == "v_2.h3l").Pitch.MidiNumber);
    }

    [Fact]
    public void Undo_takes_the_last_step_back_and_an_empty_journal_has_none()
    {
        Draft drafted = Drafted(OctavesAndBass());
        Draft twice = DraftTakes.Take(DraftTakes.Take(drafted, "pl_1", 1, null, Later), "pl_1", 2, null, Later);

        Draft once = twice.Undone();

        Assert.Equal([1], once.Steps.Select(static step => step.Number));
        Assert.Equal(3, once.Next);
        Assert.Throws<InvalidOperationException>(() => drafted.Undone());
    }

    [Fact]
    public void A_confirmed_anchor_is_kept_by_the_alignment_the_rebuild_and_the_techniques_read()
    {
        // The variant is four bars of the same music: lined up by itself it falls on the base's first bars, so nothing of it
        // lands in bar 3; confirmed that its bar 1 is the base's bar 3, the alignment holds there and its notes go there.
        SourceMusic heard = Score(bars: 4, prefix: "h");
        DraftVariant variant = Variant("v_2", heard, 1, 4, notes: "perf_1") with { Anchors = [new DraftAnchor(new Tick(2 * BarLength), Tick.Zero)] };
        Draft draft = Draft.Start("d_1", "Предложение", At, "pl_1", Variant("v_1", Score(), 1, 8)).With(variant, DraftDrop.Variant, "pl_1");

        Draft unconfirmed = Draft.Start("d_1", "Предложение", At, "pl_1", Variant("v_1", Score(), 1, 8)).With(variant with { Anchors = [] }, DraftDrop.Variant, "pl_1");

        Draft taken = DraftTakes.TakeFrom(draft, "pl_1", "v_2", DraftPart.Right, Bars(3, 3), Later);

        Assert.Equal(new Tick(2 * BarLength), taken.ResultOf("pl_1").Music.Notes.Single(note => note.Id == "v_2.h0r0").Span.Onset);
        Assert.Throws<InvalidOperationException>(() => DraftTakes.TakeFrom(unconfirmed, "pl_1", "v_2", DraftPart.Right, Bars(3, 3), Later));
    }

    [Fact]
    public void A_new_base_drops_the_anchors_of_every_variant_of_its_place()
    {
        DraftVariant anchored = Variant("v_3", Score(prefix: "k"), 1, 8) with { Anchors = [new DraftAnchor(new Tick(BarLength), new Tick(BarLength))] };
        Draft draft = Drafted(Octaves()).With(anchored, DraftDrop.Variant, "pl_1");

        Draft rebased = draft.WithBase("pl_1", "v_2");

        Assert.All(rebased.FindPlace("pl_1")!.Variants, variant => Assert.Empty(variant.Anchors));
    }
}