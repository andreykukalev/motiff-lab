using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>The diff shared by every operation: notes paired by id, added and removed notes (S1-E4).</summary>
public class VariationFactsTests
{
    private static readonly TickSpan s_span = new(Tick.Zero, 960);

    private static Note Note(string id, int pitch, long onset) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), 240),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    [Fact]
    public void Notes_are_paired_by_id_and_the_rest_are_added_or_removed()
    {
        Note[] before = [Note("a", 60, 0), Note("b", 62, 240), Note("c", 64, 480)];
        Note[] after = [Note("a", 60, 0), Note("a+", 61, 120), Note("c", 65, 480)];

        Variation variation = VariationFacts.Build("V0", s_span, before, after, new HashSet<KeepProperty>(), []);

        Assert.Equal(["a", "c"], variation.Changes.Select(change => change.Before.Id));
        Assert.Equal(["a+"], variation.Added.Select(note => note.Id));
        Assert.Equal(["b"], variation.Removed.Select(note => note.Id));
        Assert.Equal(["a", "a+", "c"], variation.Notes.Select(note => note.Id));
        Assert.StartsWith("1 note is added. 1 note is removed. 1 of 2 notes change pitch.", variation.Explanation);
    }
}