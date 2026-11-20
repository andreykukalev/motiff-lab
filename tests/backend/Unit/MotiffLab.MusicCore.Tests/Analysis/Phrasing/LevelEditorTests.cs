using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis.Phrasing;

/// <summary>The one edit algebra of the phrase levels (D13): how an edge snaps to a place, which dragging a boundary relies on, and a boundary moved as one edit.</summary>
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

    [Fact]
    public void A_boundary_moves_as_one_edit_and_moved_back_onto_a_found_one_is_found_again()
    {
        var found = new HashSet<Tick> { new(4 * Quarter) };
        var editor = new LevelEditor(PhraseEdits.None, [new Tick(4 * Quarter)], static tick => tick.Value is > 0 and < 16 * Quarter, found);

        PhraseEdits moved = editor.Move(new Tick(4 * Quarter), new Tick(5 * Quarter));
        Assert.Equal(new PhraseEdits([new Tick(5 * Quarter)], [new Tick(4 * Quarter)]), moved);

        var back = new LevelEditor(moved, [new Tick(5 * Quarter)], static tick => tick.Value is > 0 and < 16 * Quarter, found);
        Assert.Equal(PhraseEdits.None, back.Move(new Tick(5 * Quarter), new Tick(4 * Quarter)));
        // Where the level allows no boundary, the move only takes it away.
        Assert.Equal(new PhraseEdits([], [new Tick(4 * Quarter)]), editor.Move(new Tick(4 * Quarter), new Tick(16 * Quarter)));
    }
}