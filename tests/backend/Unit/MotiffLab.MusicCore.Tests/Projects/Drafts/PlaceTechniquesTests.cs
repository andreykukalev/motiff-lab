using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Projects.Drafts;

/// <summary>The приёмы of a place: a variant's differences from its base grouped by kind and shape, numbered, sure at 85 % (decision record 0034, section 4).</summary>
public class PlaceTechniquesTests
{
    [Fact]
    public void Differences_of_one_kind_and_shape_in_different_bars_are_one_numbered_sure_technique()
    {
        PlaceTechnique octave = Assert.Single(Drafted(Octaves()).TechniquesOf("pl_1"));

        Assert.Equal((1, "v_2", TechniqueKind.Octave), (octave.Number, octave.Variant, octave.Kind));
        Assert.Equal([2, 6], octave.Occurrences.Select(static occurrence => occurrence.Bar));
        Assert.True(octave.IsSure);
        Assert.True(octave.Confidence >= PlaceTechnique.SureConfidence);
        Assert.Equal(TechniqueState.Open, octave.State);
        TechniqueOccurrence second = octave.Occurrences[0];
        Assert.Equal(["v_1.n1r0", "v_1.n1r1", "v_1.n1r2", "v_1.n1r3"], second.Removed);
        Assert.Equal(["v_2.h1r0", "v_2.h1r1", "v_2.h1r2", "v_2.h1r3"], second.Added);
        Assert.Equal(Bars(2, 2), second.Span);
    }

    [Fact]
    public void Another_kind_is_another_technique_numbered_in_the_order_of_its_first_bar()
    {
        IReadOnlyList<PlaceTechnique> techniques = Drafted(OctavesAndBass()).TechniquesOf("pl_1");

        Assert.Equal([(1, TechniqueKind.Octave, 2), (2, TechniqueKind.Bass, 4)], techniques.Select(static technique => (technique.Number, technique.Kind, technique.Occurrences[0].Bar)));
    }

    [Fact]
    public void A_place_of_one_variant_or_the_same_music_has_no_techniques()
    {
        Assert.Empty(Draft.Start("d_1", "Предложение", At, "pl_1", Variant("v_1", Score(), 1, 8)).TechniquesOf("pl_1"));
        Assert.Empty(Drafted(Score(prefix: "h")).TechniquesOf("pl_1"));
    }

    [Fact]
    public void Bars_the_variant_adds_are_a_technique_of_their_own_never_sure_put_before_the_bar_they_fall_on()
    {
        SourceMusic heard = WithABarAdded(after: 4);

        PlaceTechnique insert = Assert.Single(Drafted(heard).TechniquesOf("pl_1"));

        Assert.Equal(TechniqueKind.InsertBars, insert.Kind);
        Assert.False(insert.IsSure);
        TechniqueOccurrence occurrence = Assert.Single(insert.Occurrences);
        Assert.Equal(5, occurrence.Bar);
        Assert.Equal(new DraftInsert(new Tick(4 * BarLength), new TickSpan(new Tick(4 * BarLength), BarLength)), occurrence.Insert);
        Assert.Empty(occurrence.Added);
    }

    [Fact]
    public void The_same_place_and_steps_give_the_same_techniques()
    {
        Draft draft = Drafted(OctavesAndBass());

        Assert.Equal(Describe(draft.TechniquesOf("pl_1")), Describe(draft.TechniquesOf("pl_1")));
    }

    private static string Describe(IReadOnlyList<PlaceTechnique> techniques) =>
        string.Join(";", techniques.Select(static technique => $"{technique.Number}:{technique.Kind}:{technique.Confidence}:{string.Join(",", technique.Occurrences.Select(static occurrence => $"{occurrence.Bar}/{string.Join("+", occurrence.Removed)}/{string.Join("+", occurrence.Added)}"))}"));
}