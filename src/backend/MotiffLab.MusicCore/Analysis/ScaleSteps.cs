namespace MotiffLab.MusicCore.Analysis;

/// <summary>A pitch as a step of a key's scale, counted from the tonic of octave −1, plus a chromatic offset.</summary>
/// <param name="Step">Scale steps from the tonic below MIDI 0; seven steps make an octave.</param>
/// <param name="Alteration">Semitones above the scale note, 0 or 1; 1 marks a note outside the scale.</param>
public readonly record struct ScalePosition(int Step, int Alteration);

/// <summary>
/// The scale-step pitch space that diatonic operations move in (V4, V9, V10): "up a 3rd" means two
/// steps of the key's scale, whatever the semitones. Major keys use the major scale and minor keys
/// the natural minor, as the scale-degree facts do (S1-A2).
/// </summary>
/// <remarks>
/// <para>
/// A note outside the scale sits on the scale note below it with an alteration of 1, so G♯ in A
/// minor is G raised. Moving it keeps the alteration, so chromatic notes stay chromatic. Where the
/// raised note would fall on the next scale note (B raised is C in A minor), the alteration is
/// dropped and the plain scale note is used, so a moved note never lands on the step above.
/// </para>
/// <para>Pure integer arithmetic: no floating point and no spelling choices.</para>
/// </remarks>
public static class ScaleSteps
{
    public const int StepsPerOctave = 7;

    private static readonly int[] s_major = [0, 2, 4, 5, 7, 9, 11];
    private static readonly int[] s_minor = [0, 2, 3, 5, 7, 8, 10];

    /// <summary>Where a MIDI note sits in the key's scale.</summary>
    public static ScalePosition Locate(MusicalKey key, int midiNumber)
    {
        int[] scale = Scale(key);
        int fromTonic = midiNumber - key.TonicPitchClass;
        int octave = FloorDiv(fromTonic, 12);
        int inOctave = fromTonic - (octave * 12);
        int degree = Array.FindLastIndex(scale, semitones => semitones <= inOctave);
        return new ScalePosition((octave * StepsPerOctave) + degree, inOctave - scale[degree]);
    }

    /// <summary>The MIDI note of a scale position; a raised note that would land on the next scale note is the plain scale note.</summary>
    public static int MidiNumber(MusicalKey key, ScalePosition position)
    {
        int[] scale = Scale(key);
        int octave = FloorDiv(position.Step, StepsPerOctave);
        int degree = position.Step - (octave * StepsPerOctave);
        int plain = key.TonicPitchClass + (octave * 12) + scale[degree];
        int raised = plain + position.Alteration;
        return position.Alteration != 0 && InScale(scale, raised - key.TonicPitchClass) ? plain : raised;
    }

    /// <summary>The note <paramref name="steps"/> scale steps above (or below, when negative) a MIDI note, keeping its alteration.</summary>
    public static int Move(MusicalKey key, int midiNumber, int steps)
    {
        ScalePosition position = Locate(key, midiNumber);
        return MidiNumber(key, position with { Step = position.Step + steps });
    }

    private static int[] Scale(MusicalKey key) => key.Mode == KeyMode.Major ? s_major : s_minor;

    private static bool InScale(int[] scale, int fromTonic) => scale.Contains(((fromTonic % 12) + 12) % 12);

    private static int FloorDiv(int value, int divisor) => value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
}