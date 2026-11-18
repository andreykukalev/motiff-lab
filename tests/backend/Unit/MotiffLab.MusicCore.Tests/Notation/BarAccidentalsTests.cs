using MotiffLab.MusicCore.Notation;

using static MotiffLab.MusicCore.Tests.Notation.NotationParts;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>The accidentals a staff shows in a bar (<see cref="BarAccidentals"/>).</summary>
public class BarAccidentalsTests
{
    private static ChordPiece Piece(string id, int pitch, long start, bool tieIn = false) =>
        new(start, 480, 4, 0, new Chord(start, start + 480, [Shown(id, pitch, start, 480)]), tieIn ? 1 : 0, tieIn, tieOut: false);

    [Fact]
    public void An_accidental_shows_once_in_a_bar_and_a_natural_shows_when_the_note_returns()
    {
        ChordPiece[] pieces = [Piece("n_0001", 61, 0), Piece("n_0002", 61, 480), Piece("n_0003", 60, 960)];

        BarAccidentals.Write([[.. pieces]], 0, null);

        Assert.Equal([true, false, true], pieces.Select(piece => piece.ShowAccidental[0]));
        Assert.Equal([1, 1, 0], pieces.Select(piece => piece.Spellings[0].Alter));
    }

    [Fact]
    public void The_key_signature_holds_its_sharps_and_a_tied_continuation_shows_none()
    {
        ChordPiece fSharp = Piece("n_0001", 66, 0);
        ChordPiece tied = Piece("n_0002", 61, 480, tieIn: true);

        BarAccidentals.Write([[fSharp, tied]], 1, null);

        Assert.False(fSharp.ShowAccidental[0]);
        Assert.False(tied.ShowAccidental[0]);
    }

    [Fact]
    public void Voices_are_read_in_time_order_inside_beams()
    {
        ChordPiece upper = Piece("n_0001", 61, 480);
        ChordPiece lower = Piece("n_0002", 61, 0);

        BarAccidentals.Write([[new Beamed([upper])], [lower]], 0, null);

        Assert.Equal((false, true), (upper.ShowAccidental[0], lower.ShowAccidental[0]));
    }
}