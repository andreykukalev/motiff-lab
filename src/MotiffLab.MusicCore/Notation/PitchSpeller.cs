namespace MotiffLab.MusicCore.Notation;

/// <summary>A written pitch: a letter step, its alteration and its octave.</summary>
/// <param name="Step">0 for C up to 6 for B.</param>
/// <param name="Alter">Semitones from the natural step: −1 flat, 1 sharp.</param>
/// <param name="Octave">Scientific octave of the written step: C♭4 sounds as B3 but is written in octave 4.</param>
internal readonly record struct SpelledPitch(int Step, int Alter, int Octave);

/// <summary>
/// Spells a MIDI pitch from the key signature alone: notes of the key as the key has them, other
/// notes as naturals where possible, and otherwise sharpened in sharp keys and C major and
/// flattened in flat keys. Spelling from harmony can replace it once chord analysis exists.
/// </summary>
internal static class PitchSpeller
{
    private static readonly int[] s_stepPitchClass = [0, 2, 4, 5, 7, 9, 11];

    // Steps in the order sharps (F C G D A E B) and flats (B E A D G C F) are added.
    private static readonly int[] s_sharpOrder = [3, 0, 4, 1, 5, 2, 6];
    private static readonly int[] s_flatOrder = [6, 2, 5, 1, 4, 0, 3];

    /// <summary>The alteration of each step, C to B, in a key with the given sharps or flats.</summary>
    public static int[] KeyAlterations(int fifths)
    {
        var alterations = new int[7];
        int[] order = fifths >= 0 ? s_sharpOrder : s_flatOrder;
        for (int i = 0; i < Math.Abs(fifths); i++)
        {
            alterations[order[i]] = Math.Sign(fifths);
        }

        return alterations;
    }

    public static SpelledPitch Spell(int midiNumber, int fifths)
    {
        int[] key = KeyAlterations(fifths);
        int pitchClass = midiNumber % 12;
        int direction = fifths >= 0 ? 1 : -1;

        // In the key, then natural, then altered in the key's direction, then the other way.
        foreach (Func<int, int> alterFor in (Func<int, int>[])[step => key[step], _ => 0, _ => direction, _ => -direction])
        {
            for (int step = 0; step < 7; step++)
            {
                int alter = alterFor(step);
                if (Mod12(s_stepPitchClass[step] + alter) == pitchClass)
                {
                    int octave = ((midiNumber - alter - s_stepPitchClass[step]) / 12) - 1;
                    return new SpelledPitch(step, alter, octave);
                }
            }
        }

        throw new InvalidOperationException("Every pitch class has a natural or singly altered spelling.");
    }

    private static int Mod12(int value) => ((value % 12) + 12) % 12;
}