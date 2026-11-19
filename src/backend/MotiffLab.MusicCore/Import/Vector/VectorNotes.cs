namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>A clef as MusicXML writes it: its sign, the staff line it sits on counted from the bottom, and an octave shift.</summary>
internal readonly record struct VectorClef(char Sign, int Line, int Octave)
{
    public static readonly VectorClef Treble = new('G', 2, 0);

    public static readonly VectorClef Bass = new('F', 4, 0);

    /// <summary>The diatonic step (C0 = 0, seven an octave) of the note on the clef's own line: G4, F3 or C4, shifted by its octave.</summary>
    public int Reference => (Sign switch
    {
        'G' => (4 * 7) + 4,
        'F' => (3 * 7) + 3,
        _ => 4 * 7,
    }) + (7 * Octave);

    /// <summary>The clef a glyph prints, on the staff line nearest its origin.</summary>
    public static VectorClef Of(MusicGlyph glyph, VectorStaff staff, double y)
    {
        int nearest = Enumerable.Range(0, 5).MinBy(index => Math.Abs(staff.Lines[index] - y));
        int line = 5 - nearest;
        return glyph switch
        {
            MusicGlyph.ClefG8vb => new('G', line, -1),
            MusicGlyph.ClefG8va => new('G', line, 1),
            MusicGlyph.ClefF => new('F', line, 0),
            MusicGlyph.ClefF8vb => new('F', line, -1),
            MusicGlyph.ClefC => new('C', line, 0),
            _ => new('G', line, 0),
        };
    }

    /// <summary>The y of the clef's line on a staff.</summary>
    public double LineY(VectorStaff staff) => staff.Lines[5 - Line];
}

/// <summary>A written pitch: step letter, alteration in semitones, octave, and whether a tie joins it to the next note or the one before.</summary>
internal readonly record struct VectorPitch(char Step, int Alter, int Octave, bool TieStart = false, bool TieStop = false)
{
    private const string Steps = "CDEFGAB";

    /// <summary>The diatonic step (C0 = 0) of the pitch.</summary>
    public int Diatonic => (Octave * 7) + Steps.IndexOf(Step, StringComparison.Ordinal);

    public static char StepOf(int diatonic) => Steps[((diatonic % 7) + 7) % 7];

    public static int OctaveOf(int diatonic) => (int)Math.Floor(diatonic / 7.0);
}

/// <summary>A note, chord or rest read from one staff, placed in its bar by <see cref="BarTiming"/>.</summary>
internal sealed class VectorEvent
{
    /// <summary>Where it stands: the left edge of its leftmost notehead or of the rest.</summary>
    public required double X { get; init; }

    /// <summary>The staff of the system it stands on, from 0 at the top.</summary>
    public required int Staff { get; init; }

    public bool Rest { get; init; }

    /// <summary>The notehead or rest glyph.</summary>
    public required MusicGlyph Glyph { get; init; }

    /// <summary>Beams or flags on its stem.</summary>
    public int Beams { get; init; }

    /// <summary>
    /// Where its column is: the left edge a notehead has beside its stem when not pushed aside by a second, or the left
    /// edge of a stemless note or a rest. Notes that start together on any staff share it.
    /// </summary>
    public double Anchor { get; init; }

    /// <summary>The right edge of its rightmost notehead or of the rest, where its dots start.</summary>
    public required double Right { get; init; }

    /// <summary>The y of the rest glyph; a note's heights are its heads'.</summary>
    public double RestY { get; init; }

    public int Dots { get; set; }

    /// <summary>The tuplet it is in, as actual notes in the time of normal ones (3 in the time of 2); 1 and 1 outside one.</summary>
    public (int Actual, int Normal) Tuplet { get; set; } = (1, 1);

    public bool Grace { get; init; }

    public bool? StemUp { get; init; }

    /// <summary>The x of its stem; null for a stemless note or a rest.</summary>
    public double? StemX { get; init; }

    /// <summary>The y of its stem's tip, where beams and flags are.</summary>
    public double? Tip { get; init; }

    /// <summary>The beam nearest its stem's tip, by its place in the page's filled shapes; -1 when unbeamed.</summary>
    public int Beam { get; init; } = -1;

    /// <summary>A whole rest alone in the bar: it lasts the bar, whatever the meter.</summary>
    public bool WholeBar { get; init; }

    /// <summary>The noteheads of a note or chord, lowest first.</summary>
    public List<PlacedGlyph> Heads { get; init; } = [];

    /// <summary>The pitches of the heads, spelled with the clef, key signature and accidentals in force.</summary>
    public List<VectorPitch> Pitches { get; } = [];

    /// <summary>How long it lasts, in ticks (480 a quarter); 0 for a grace note.</summary>
    public int Ticks { get; set; }

    /// <summary>Where it starts in its bar, in ticks.</summary>
    public int Onset { get; set; }

    /// <summary>The voice it is written in, numbered from 1 for the whole part.</summary>
    public int Voice { get; set; }

    /// <summary>Ticks before dots: what its type is written as.</summary>
    public int Written => Rest ? Glyph.RestTicks() : Glyph == MusicGlyph.NoteheadBlack ? 480 >> Beams : Glyph.HeadTicks();
}

/// <summary>A meter as printed: beats over beat type.</summary>
internal readonly record struct VectorMeter(int Beats, int BeatType)
{
    public int Ticks => Beats * 1920 / BeatType;
}

/// <summary>A clef printed in a bar: on which staff, where, and which.</summary>
internal readonly record struct ClefChange(int Staff, double X, VectorClef Clef);

/// <summary>One bar of a system, every staff of it: what it holds and what is printed at its start.</summary>
internal sealed class VectorBar
{
    public required List<VectorEvent>[] Staves { get; init; }

    /// <summary>A meter printed at the bar's start.</summary>
    public VectorMeter? Meter { get; set; }

    /// <summary>A key signature printed at the bar's start, in fifths.</summary>
    public int? Fifths { get; set; }

    /// <summary>Clefs printed in the bar.</summary>
    public List<ClefChange> Clefs { get; } = [];

    /// <summary>The meter in force: the printed one or the last before it.</summary>
    public VectorMeter? InForce { get; set; }
}