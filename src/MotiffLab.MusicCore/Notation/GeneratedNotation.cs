using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// Notation generated from shared-model notes, as MEI for the score view to engrave (S1-W7,
/// decision record 0008). Everything in it beyond the notes themselves is inferred: note values on
/// the display grid, voices, spelling and, for a single-track piano part, the staff split.
/// </summary>
/// <param name="Mei">The score as an MEI 5 document. Each note's <c>xml:id</c> is its shared-model id; the pieces of a tied note after the first add <c>-2</c>, <c>-3</c>, ….</param>
/// <param name="Staves">The staves, top to bottom.</param>
/// <param name="NoteCount">Notes in the input.</param>
/// <param name="ApproximatedNoteCount">Notes whose onset or end moved to the nearest 32nd note to be written.</param>
/// <param name="ShortenedNoteCount">Notes written shorter than they are because more than four voices on one staff would have been needed.</param>
public sealed record GeneratedNotation(
    string Mei,
    IReadOnlyList<NotationStaff> Staves,
    int NoteCount,
    int ApproximatedNoteCount,
    int ShortenedNoteCount);

/// <param name="Number">The MEI staff number, from 1 at the top.</param>
/// <param name="Track">The track whose notes the staff shows.</param>
/// <param name="Hand">For a part split into two staves, the hand this staff holds.</param>
/// <param name="IsSplitInferred">True when a part without hands was split at middle C, rather than by imported or confirmed hands.</param>
public sealed record NotationStaff(int Number, int Track, Clef Clef, Hand? Hand, bool IsSplitInferred);

public enum Clef
{
    Treble,
    Bass,
}

/// <summary>A key signature from a position onwards.</summary>
/// <param name="Fifths">Sharps (positive) or flats (negative), −7 to 7.</param>
public readonly record struct KeyChange
{
    public KeyChange(Tick at, int fifths)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fifths, -7);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fifths, 7);
        At = at;
        Fifths = fifths;
    }

    public Tick At { get; }

    public int Fifths { get; }
}