using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis.Phrasing;

/// <summary>The one edit algebra of the phrase levels (D13): how an edge snaps to a place, which dragging a boundary relies on.</summary>
public class LevelEditorTests
{
    private const long Quarter = 480;

    private static LevelEditor Editor(params long[] places) =>
        new(PhraseEdits.None, [.. places.Select(static place => new Tick(place))], static _ => true, null);

    [Fact]
    public void An_edge_snaps_to_the_nearest_place_less_than_a_beat_away_and_to_the_earlier_of_two_as_near()
    {
        LevelEditor editor = Editor(4 * Quarter, 5 * Quarter);

        // Exactly between two places half a beat away from each: the earlier one.
        Assert.Equal(new Tick(4 * Quarter), editor.Snap(new Tick((4 * Quarter) + (Quarter / 2)), MeterMap.Default));
        // Nearer to the later one: the later one.
        Assert.Equal(new Tick(5 * Quarter), editor.Snap(new Tick((5 * Quarter) - 100), MeterMap.Default));
        // A beat or more from every place: the edge itself.
        Assert.Equal(new Tick(6 * Quarter), editor.Snap(new Tick(6 * Quarter), MeterMap.Default));
        Assert.Equal(new Tick(3 * Quarter), editor.Snap(new Tick(3 * Quarter), MeterMap.Default));
    }
}