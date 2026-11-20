using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Projects.Drafts;

/// <summary>Anchors the player confirms (decision record 0034, section 2; the plan's answer 4): kept in order, never on a base, the alignment worked out again around them.</summary>
public class DraftAnchorsTests
{
    /// <summary>Eight bars of the notes as the base and four bars of the same music as its variant: lined up by itself, it falls on the base's first bars.</summary>
    private static Draft Repeated() =>
        Draft.Start("d_1", "Предложение", At, "pl_1", Variant("v_1", Score(), 1, 8)).With(Variant("v_2", Score(bars: 4, prefix: "h"), 1, 4, notes: "perf_1"), DraftDrop.Variant, "pl_1");

    private static IReadOnlyList<DraftAnchor> AnchorsOf(Draft draft) => draft.FindPlace("pl_1")!.Find("v_2")!.Anchors;

    private static DraftAnchor Anchor(int baseBar, int variantBar) => new(new Tick((baseBar - 1) * BarLength), new Tick((variantBar - 1) * BarLength));

    [Fact]
    public void A_confirmed_anchor_moves_where_the_variant_lines_up_and_so_what_a_take_puts_in()
    {
        Draft draft = DraftAnchors.Confirm(Repeated(), "pl_1", "v_2", Anchor(3, 1));

        Draft taken = DraftTakes.TakeFrom(draft, "pl_1", "v_2", DraftPart.Right, Bars(3, 3), At);

        Assert.Equal(new Tick(2 * BarLength), taken.ResultOf("pl_1").Music.Notes.Single(note => note.Id == "v_2.h0r0").Span.Onset);
        Assert.Throws<InvalidOperationException>(() => DraftTakes.TakeFrom(Repeated(), "pl_1", "v_2", DraftPart.Right, Bars(3, 3), At));
    }

    [Fact]
    public void Anchors_are_kept_in_order_and_one_at_the_same_tick_takes_the_place_of_the_one_there()
    {
        Draft draft = DraftAnchors.Confirm(DraftAnchors.Confirm(Repeated(), "pl_1", "v_2", Anchor(5, 3)), "pl_1", "v_2", Anchor(3, 1));
        Assert.Equal([Anchor(3, 1), Anchor(5, 3)], AnchorsOf(draft));

        Draft moved = DraftAnchors.Confirm(draft, "pl_1", "v_2", Anchor(4, 3));

        Assert.Equal([Anchor(3, 1), Anchor(4, 3)], AnchorsOf(moved));
    }

    [Fact]
    public void An_anchor_that_crosses_another_on_a_base_or_outside_the_music_is_refused()
    {
        Draft draft = DraftAnchors.Confirm(Repeated(), "pl_1", "v_2", Anchor(3, 2));

        Assert.Throws<ArgumentException>(() => DraftAnchors.Confirm(draft, "pl_1", "v_2", Anchor(5, 1)));
        Assert.Throws<ArgumentException>(() => DraftAnchors.Confirm(draft, "pl_1", "v_1", Anchor(1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DraftAnchors.Confirm(draft, "pl_1", "v_2", Anchor(9, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DraftAnchors.Confirm(draft, "pl_1", "v_2", Anchor(1, 5)));
        Assert.Throws<KeyNotFoundException>(() => DraftAnchors.Confirm(draft, "pl_1", "v_9", Anchor(1, 1)));
        Assert.Throws<KeyNotFoundException>(() => DraftAnchors.Confirm(draft, "pl_9", "v_2", Anchor(1, 1)));
    }

    [Fact]
    public void Removed_an_anchor_is_gone_and_one_not_there_is_refused()
    {
        Draft draft = DraftAnchors.Confirm(Repeated(), "pl_1", "v_2", Anchor(3, 1));

        Draft removed = DraftAnchors.Remove(draft, "pl_1", "v_2", new Tick(2 * BarLength));

        Assert.Empty(AnchorsOf(removed));
        Assert.Throws<KeyNotFoundException>(() => DraftAnchors.Remove(removed, "pl_1", "v_2", new Tick(2 * BarLength)));
    }

    [Fact]
    public void The_steps_stay_and_the_приёмы_are_worked_out_again_around_the_anchors()
    {
        // The variant plays bar 2 an octave higher; lined up by itself (all its bars alike the base's) it falls on bar 6.
        // Confirmed only that its bar 1 is the base's bar 3, the octave is the base's bar 4 and nothing in bars 3 and 4 is simplified.
        SourceMusic octave = Score(bars: 4, melody: bar => bar == 1 ? [84, 86, 88, 89] : [72, 74, 76, 77], prefix: "h");
        Draft draft = Draft.Start("d_1", "x", At, "pl_1", Variant("v_1", Score(), 1, 8)).With(Variant("v_2", octave, 1, 4, notes: "perf_1"), DraftDrop.Variant, "pl_1");
        draft = DraftSeams.Tempo(draft, "pl_1", 60, At);

        Draft anchored = DraftAnchors.Confirm(draft, "pl_1", "v_2", Anchor(3, 1));

        Assert.Equal([6], Assert.Single(draft.TechniquesOf("pl_1"), static technique => technique.Kind == TechniqueKind.Octave).Occurrences.Select(static occurrence => occurrence.Bar));
        Assert.Equal([4], Assert.Single(anchored.TechniquesOf("pl_1"), static technique => technique.Kind == TechniqueKind.Octave).Occurrences.Select(static occurrence => occurrence.Bar));
        Assert.DoesNotContain(anchored.TechniquesOf("pl_1"), static technique => technique.Kind == TechniqueKind.Simplify);
        Assert.Equal(draft.Steps, anchored.Steps);
    }
}