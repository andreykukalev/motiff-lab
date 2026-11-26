using MotiffLab.MusicCore.Notation;

using static MotiffLab.MusicCore.Tests.Notation.NotationParts;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>Which beats are written as triplets, and where notes go in them (<see cref="TupletBeats"/>).</summary>
public class TupletBeatsTests
{
    private static ShownNote[] Onsets(params long[] onsets) => [.. onsets.Select((onset, i) => Shown($"n_{i + 1:D4}", 72, onset, 120))];

    [Fact]
    public void Three_onsets_on_thirds_of_a_beat_a_little_late_are_a_triplet()
    {
        TupletSpan tuplet = Assert.Single(TupletBeats.Find(Onsets(0, 165, 325), Bars(1)));

        Assert.Equal(new TupletSpan(0, 480, 3, 2), tuplet);
        Assert.Equal(160, tuplet.Step);
    }

    [Fact]
    public void Six_onsets_on_sixths_of_a_beat_are_a_sextuplet()
    {
        Assert.Equal([new TupletSpan(480, 480, 6, 4)], TupletBeats.Find(Onsets(480, 560, 640, 720, 800, 880), Bars(1)));
    }

    [Fact]
    public void Sixteenths_and_a_dotted_rhythm_of_two_onsets_alone_stay_as_they_are()
    {
        Assert.Empty(TupletBeats.Find(Onsets(0, 120, 240, 360), Bars(1)));
        Assert.Empty(TupletBeats.Find(Onsets(0, 330), Bars(1)));
    }

    [Fact]
    public void Two_onsets_on_thirds_count_next_to_a_triplet_of_three()
    {
        Assert.Equal([new TupletSpan(0, 480, 3, 2), new TupletSpan(480, 480, 3, 2)], TupletBeats.Find(Onsets(0, 160, 320, 480, 800), Bars(1)));
    }

    [Fact]
    public void A_compound_meter_already_divides_by_three()
    {
        Assert.Empty(TupletBeats.Find(Onsets(0, 80, 160), Bars(1, 6, 8)));
    }

    [Fact]
    public void A_tick_goes_on_the_triplets_grid_in_a_triplet_beat_and_on_the_grid_outside()
    {
        TupletSpan[] tuplets = [new(0, 480, 3, 2)];

        Assert.Equal(160, TupletBeats.SnapIn(150, tuplets, NotationGenerator.HeardGrid));
        Assert.Equal(600, TupletBeats.SnapIn(590, tuplets, NotationGenerator.HeardGrid));
        Assert.Equal(160, TupletBeats.StepAt(320, tuplets));
        Assert.Equal(NotationGenerator.Grid, TupletBeats.StepAt(480, tuplets));
    }

    [Fact]
    public void On_tuplets_moves_only_the_notes_in_triplet_beats()
    {
        ShownNote[] notes = [Shown("n_0001", 72, 155, 150), Shown("n_0002", 72, 480, 240)];

        ShownNote[] written = TupletBeats.OnTuplets(notes, [new TupletSpan(0, 480, 3, 2)]);

        Assert.Equal([(160L, 320L), (480L, 720L)], written.Select(note => (note.Onset, note.End)));
        Assert.Same(notes, TupletBeats.OnTuplets(notes, []));
    }
}