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
/// <param name="LengthenedNoteCount">Notes written as heard that are held to the next note on their staff, because the gap was shorter than a quarter.</param>
/// <param name="CutNoteCount">Notes written as heard that end at the next note on their staff, where the pedal or a held key kept them sounding.</param>
/// <param name="GraceNoteCount">Notes written as heard that are grace notes: shorter than a 32nd, just before the next.</param>
/// <param name="WrittenAsHeard">True when the notes were written as heard: on a 16th grid, one line per staff where possible.</param>
public sealed record GeneratedNotation(
    string Mei,
    IReadOnlyList<NotationStaff> Staves,
    int NoteCount,
    int ApproximatedNoteCount,
    int ShortenedNoteCount,
    int LengthenedNoteCount = 0,
    int CutNoteCount = 0,
    int GraceNoteCount = 0,
    bool WrittenAsHeard = false)
{
    /// <summary>Notes written with an accent because they were played clearly louder than those around them.</summary>
    public int AccentCount { get; init; }

    /// <summary>Dynamics written from how loud the bars were played.</summary>
    public int DynamicCount { get; init; }

    /// <summary>Tempo words ("rit.", "accel.", "a tempo") written where the playing slowed down or sped up.</summary>
    public int TempoWordCount { get; init; }

    /// <summary>Crescendos and diminuendos written where the melody grew louder or softer: hairpins, or "cresc." and "dim." where longer than a bar.</summary>
    public int HairpinCount { get; init; }

    /// <summary>Slurs written where the melody's keys were held into the next note.</summary>
    public int SlurCount { get; init; }

    /// <summary>Times the sustain pedal is written as held, each with "Ped." and "*".</summary>
    public int PedalCount { get; init; }
}

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