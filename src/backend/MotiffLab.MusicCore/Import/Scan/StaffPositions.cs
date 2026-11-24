namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>
/// The clef and key in force on a staff: the diatonic index of the middle line (C0 = 0, a step one) and the key's fifths.
/// A staff position counts steps from the middle line, as the scan server's evidence does.
/// </summary>
internal readonly record struct StaffKey(int Middle, int Fifths)
{
    /// <summary>A treble clef: B4 on the middle line, in C.</summary>
    public static StaffKey Treble { get; } = new(StaffPositions.Middle('G', 2, 0)!.Value, 0);

    /// <summary>A bass clef: D3 on the middle line, in C.</summary>
    public static StaffKey Bass { get; } = new(StaffPositions.Middle('F', 4, 0)!.Value, 0);
}

/// <summary>Staff positions and pitches under a clef, for the scan merge's page check and own assembly (ADR 0035, section 5).</summary>
internal static class StaffPositions
{
    private const string Steps = "CDEFGAB";
    private const string Sharps = "FCGDAEB";
    private const string Flats = "BEADGCF";
    private static readonly int[] s_natural = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>The diatonic index of the middle line under a clef: its sign (G, F or C), its line from the bottom, its octaves up (+) or down (−); null for another sign.</summary>
    public static int? Middle(char sign, int line, int octaves)
    {
        int? clef = sign switch
        {
            'G' => Diatonic('G', 4),
            'F' => Diatonic('F', 3),
            'C' => Diatonic('C', 4),
            _ => null,
        };
        return clef + ((3 - line) * 2) + (7 * octaves);
    }

    /// <summary>The diatonic index of a written step (C to B) in an octave.</summary>
    public static int Diatonic(char step, int octave) => (octave * 7) + Steps.IndexOf(step, StringComparison.Ordinal);

    /// <summary>The MIDI pitch of a diatonic index with an alteration in semitones.</summary>
    public static int Pitch(int diatonic, int alter) => (12 * (((diatonic - Step(diatonic)) / 7) + 1)) + s_natural[Step(diatonic)] + alter;

    /// <summary>The alteration the key gives a diatonic index: a sharp or a flat of the signature, else none.</summary>
    public static int KeyAlter(int diatonic, int fifths)
    {
        char step = Steps[Step(diatonic)];
        return fifths > 0 && Sharps.IndexOf(step, StringComparison.Ordinal) < fifths ? 1
            : fifths < 0 && Flats.IndexOf(step, StringComparison.Ordinal) < -fifths ? -1
            : 0;
    }

    private static int Step(int diatonic) => ((diatonic % 7) + 7) % 7;
}