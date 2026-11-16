namespace MotiffLab.MusicCore.Notation;

/// <summary>How note names are written: letters with the scientific octave, or the Russian syllables with the octave as Russian music schools write it.</summary>
public enum NoteNaming
{
    /// <summary>"C4", "F♯3", "B♭5": middle C is C4.</summary>
    Letters,

    /// <summary>"до¹", "фа♯", "Си♭": middle C is до¹ (до первой октавы), the small octave has no mark, the great octave a capital.</summary>
    Syllables,
}

/// <summary>
/// The names the piano roll writes on its rows and notes (0018 section 4): spelled for the key in force, as the score spells
/// them (<see cref="PitchSpeller"/>), so a note reads F♯ in G major and G♭ in D♭ major on the roll and on the score alike.
/// </summary>
public static class NoteNames
{
    /// <summary>The setting that chooses the naming (SettingsCatalogue): auto follows the interface language.</summary>
    public const string Setting = "notes.naming";

    private static readonly string[] s_letters = ["C", "D", "E", "F", "G", "A", "B"];

    private static readonly string[] s_syllables = ["до", "ре", "ми", "фа", "соль", "ля", "си"];

    private static readonly string[] s_superscripts = ["", "¹", "²", "³", "⁴", "⁵", "⁶"];

    private static readonly string[] s_subscripts = ["", "₁", "₂", "₃"];

    private static readonly string[] s_russianOctaves =
    [
        "субсубконтроктава", "субконтроктава", "контроктава", "большая октава", "малая октава",
        "первая октава", "вторая октава", "третья октава", "четвёртая октава", "пятая октава", "шестая октава",
    ];

    /// <summary>The naming a setting value stands for: "letters", "syllables", or "auto" (syllables in Russian, letters otherwise).</summary>
    public static NoteNaming For(string? value, Words words) => value switch
    {
        "letters" => NoteNaming.Letters,
        "syllables" => NoteNaming.Syllables,
        _ => words.IsRussian ? NoteNaming.Syllables : NoteNaming.Letters,
    };

    /// <summary>A note's name spelled for a key signature: "F♯4" or "фа♯¹" with one sharp, "G♭4" or "соль♭¹" with six flats.</summary>
    /// <param name="fifths">Sharps (positive) or flats (negative) of the key signature in force, −7 to 7.</param>
    public static string Name(int midiNumber, int fifths, NoteNaming naming) => Name(midiNumber, fifths, naming, octave: true);

    /// <summary>
    /// A note's name spelled for a key signature, with its octave or without it: "F♯" or "фа♯" for the piano roll's notes, whose
    /// octave the roll shows once at its side rather than on every note (testing item 13).
    /// </summary>
    /// <param name="octave">Whether the name carries the octave: the number, or the Russian mark and capital.</param>
    public static string Name(int midiNumber, int fifths, NoteNaming naming, bool octave)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(midiNumber);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(midiNumber, 127);
        ArgumentOutOfRangeException.ThrowIfLessThan(fifths, -7);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fifths, 7);
        SpelledPitch spelled = PitchSpeller.Spell(midiNumber, fifths);
        string accidental = spelled.Alter switch
        {
            -1 => "♭",
            1 => "♯",
            _ => "",
        };
        if (naming == NoteNaming.Letters)
        {
            return octave ? $"{s_letters[spelled.Step]}{accidental}{spelled.Octave}" : $"{s_letters[spelled.Step]}{accidental}";
        }

        // The written octave decides the mark, so C♭ in the first octave is до♭¹ though it sounds as the small octave's B.
        string syllable = s_syllables[spelled.Step];
        if (!octave)
        {
            return $"{syllable}{accidental}";
        }

        return spelled.Octave switch
        {
            >= 4 => $"{syllable}{accidental}{s_superscripts[Math.Min(spelled.Octave - 3, s_superscripts.Length - 1)]}",
            3 => $"{syllable}{accidental}",
            _ => $"{char.ToUpperInvariant(syllable[0])}{syllable[1..]}{accidental}{s_subscripts[Math.Min(2 - spelled.Octave, s_subscripts.Length - 1)]}",
        };
    }

    /// <summary>The octave a key is in, in words: "octave 4" or "первая октава" for middle C; for a row's or a key's spoken name.</summary>
    public static string OctaveOf(int midiNumber, Words words)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(midiNumber);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(midiNumber, 127);
        int octave = (midiNumber / 12) - 1;
        return words.IsRussian ? s_russianOctaves[octave + 1] : $"octave {octave}";
    }
}