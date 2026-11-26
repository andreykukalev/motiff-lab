using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Notation.NotationParts;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>The beams and tuplets of a layer (<see cref="Beaming"/>).</summary>
public class BeamingTests
{
    private static ChordPiece Piece(long start, long ticks, int dur, TupletSpan? tuplet = null) =>
        new(start, ticks, dur, 0, new Chord(start, start + ticks, [Shown($"n_{start:D4}", 72, start, ticks)]), 0, tieIn: false, tieOut: false) { Tuplet = tuplet };

    [Theory]
    [InlineData(4, 4, 480)]
    [InlineData(2, 2, 960)]
    [InlineData(6, 8, 720)]
    [InlineData(3, 8, 720)]
    [InlineData(2, 8, 480)]
    [InlineData(5, 8, 480)]
    [InlineData(7, 8, 480)]
    public void Eighths_are_beamed_by_the_beat_the_dotted_beat_or_the_bar(int numerator, int denominator, long group)
    {
        Assert.Equal(group, Beaming.BeamGroup(new TimeSignature(numerator, denominator)));
    }

    [Fact]
    public void Eighths_within_a_beat_share_a_beam_and_a_quarter_stands_alone()
    {
        List<Element> grouped = Beaming.Group([Piece(0, 240, 8), Piece(240, 240, 8), Piece(480, 480, 4), Piece(960, 240, 8)], 0, 480);

        Assert.Equal([typeof(Beamed), typeof(ChordPiece), typeof(ChordPiece)], grouped.Select(element => element.GetType()));
        Assert.Equal(2, ((Beamed)grouped[0]).Elements.Length);
    }

    [Fact]
    public void A_triplet_beat_is_one_tuplet_with_its_own_beam()
    {
        TupletSpan triplet = new(0, 480, 3, 2);

        List<Element> grouped = Beaming.Group([Piece(0, 160, 8, triplet), Piece(160, 160, 8, triplet), Piece(320, 160, 8, triplet), Piece(480, 480, 4)], 0, 480);

        TupletGroup tuplet = Assert.IsType<TupletGroup>(grouped[0]);
        Assert.Equal(triplet, tuplet.Span);
        Assert.Equal(3, Assert.IsType<Beamed>(Assert.Single(tuplet.Elements)).Elements.Length);
        Assert.IsType<ChordPiece>(grouped[1]);
    }

    [Fact]
    public void A_rest_breaks_a_beam()
    {
        List<Element> grouped = Beaming.Group([Piece(0, 120, 16), new Rest(120, 120, 16, 0, space: false), Piece(240, 240, 8)], 0, 480);

        Assert.DoesNotContain(grouped, element => element is Beamed);
    }
}