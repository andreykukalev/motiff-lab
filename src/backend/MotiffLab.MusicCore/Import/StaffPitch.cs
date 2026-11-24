using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Import;

/// <summary>
/// Written pitches on a staff, for the readers that see notes as heads at a height (the vector reader, the scan merge's
/// page check and own assembly) and the MusicXML reader: diatonic steps (C0 = 0, seven an octave), the step under a clef,
/// the alteration a key gives a step, and the MIDI pitch of a written note.
/// </summary>
internal static class StaffPitch
{
    private const string Steps = "CDEFGAB";

    private static readonly int[] s_semitones = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>The diatonic step of the note on a clef's own line: G4, F3 or C4 (any other sign), shifted by its octaves.</summary>
    public static int Reference(char sign, int octave) => (sign switch
    {
        'G' => (4 * 7) + 4,
        'F' => (3 * 7) + 3,
        _ => 4 * 7,
    }) + (7 * octave);

    /// <summary>The diatonic step of the middle line under a clef on its line (from the bottom): B4 under a treble clef.</summary>
    public static int Middle(char sign, int line, int octave) => Reference(sign, octave) + ((3 - line) * 2);

    /// <summary>The diatonic step of a written step letter in an octave.</summary>
    public static int Diatonic(char step, int octave) => (octave * 7) + Steps.IndexOf(step, StringComparison.Ordinal);

    public static char StepOf(int diatonic) => Steps[((diatonic % 7) + 7) % 7];

    public static int OctaveOf(int diatonic) => (diatonic - (((diatonic % 7) + 7) % 7)) / 7;

    /// <summary>The alteration the key signature gives a step letter (<see cref="PitchSpeller.KeyAlterations"/>).</summary>
    public static int KeyAlter(int fifths, char step) => PitchSpeller.KeyAlterations(Math.Clamp(fifths, -7, 7))[Steps.IndexOf(step, StringComparison.Ordinal)];

    /// <summary>The MIDI number of a written note: its step letter, octave and alteration in semitones.</summary>
    public static long Midi(char step, long octave, int alter) => ((octave + 1) * 12) + s_semitones[Steps.IndexOf(step, StringComparison.Ordinal)] + alter;
}