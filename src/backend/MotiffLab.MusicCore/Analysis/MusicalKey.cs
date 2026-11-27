using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>A major or minor key, named by its key signature.</summary>
public readonly record struct MusicalKey
{
    private static readonly string[] s_steps = ["C", "D", "E", "F", "G", "A", "B"];

    private static readonly string[] s_russianSteps = ["до", "ре", "ми", "фа", "соль", "ля", "си"];

    /// <param name="fifths">Sharps (positive) or flats (negative) of its key signature, −7 to 7.</param>
    public MusicalKey(int fifths, KeyMode mode)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fifths, -7);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fifths, 7);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode.");
        }

        Fifths = fifths;
        Mode = mode;
    }

    /// <summary>C major: the key note names are spelt in when a passage's key is not known.</summary>
    public static MusicalKey CMajor { get; } = new(0, KeyMode.Major);

    public int Fifths { get; }

    public KeyMode Mode { get; }

    /// <summary>0 for C up to 11 for B.</summary>
    public int TonicPitchClass => Mod12((Fifths * 7) + (Mode == KeyMode.Minor ? 9 : 0));

    /// <summary>For example "A minor" or "E♭ major".</summary>
    public string Name => $"{SpellPitchClass(TonicPitchClass)} {(Mode == KeyMode.Major ? "major" : "minor")}";

    /// <summary>The name in a language: "A minor"; "ля минор", "ми-бемоль мажор".</summary>
    public string NameIn(Words words)
    {
        if (!words.IsRussian)
        {
            return Name;
        }

        SpelledPitch tonic = PitchSpeller.Spell(60 + TonicPitchClass, Fifths);
        string accidental = tonic.Alter switch
        {
            -1 => "-бемоль",
            1 => "-диез",
            _ => "",
        };
        return $"{s_russianSteps[tonic.Step]}{accidental} {(Mode == KeyMode.Major ? "мажор" : "минор")}";
    }

    /// <summary>The key with the same signature and the other mode.</summary>
    public MusicalKey Relative => new(Fifths, Mode == KeyMode.Major ? KeyMode.Minor : KeyMode.Major);

    /// <summary>The key on a tonic, written with at most six flats or sharps (F♯ major rather than G♭ major).</summary>
    public static MusicalKey OnTonic(int tonicPitchClass, KeyMode mode)
    {
        int majorTonic = Mod12(tonicPitchClass + (mode == KeyMode.Minor ? 3 : 0));
        int fifths = Mod12(majorTonic * 7);
        return new MusicalKey(fifths > 6 ? fifths - 12 : fifths, mode);
    }

    /// <summary>A note name spelled for this key, for example "D♯5" in A minor.</summary>
    public string Spell(int midiNumber) => Spell(midiNumber, Fifths);

    /// <summary>A pitch class spelled for this key without an octave, for example "G♯" in A minor; as chord symbols name roots.</summary>
    public string SpellClass(int pitchClass) => SpellPitchClass(Mod12(pitchClass));

    public override string ToString() => Name;

    internal static string Spell(int midiNumber, int fifths)
    {
        SpelledPitch spelled = PitchSpeller.Spell(midiNumber, fifths);
        return $"{s_steps[spelled.Step]}{Accidental(spelled.Alter)}{spelled.Octave}";
    }

    private string SpellPitchClass(int pitchClass)
    {
        SpelledPitch spelled = PitchSpeller.Spell(60 + pitchClass, Fifths);
        return $"{s_steps[spelled.Step]}{Accidental(spelled.Alter)}";
    }

    private static string Accidental(int alter) => alter switch
    {
        -1 => "♭",
        1 => "♯",
        _ => "",
    };

    /// <summary>
    /// The key to write notes heard in a recording in, which the engine does not give: the major or
    /// minor key whose profile best fits how long each pitch class sounds (decision record 0016), with
    /// notes below middle C counting twice, since the bass carries the roots of the harmony.
    /// An inference for the score's key signature only; analysis still treats the piece as having none.
    /// Null without notes.
    /// </summary>
    public static MusicalKey? OfHeard(IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        double[] weights = new double[12];
        foreach (Note note in notes)
        {
            int bass = note.Pitch.MidiNumber < 60 ? 2 : 1;
            weights[note.Pitch.MidiNumber % 12] += bass * Math.Min(note.Span.Duration, 4 * CanonicalTime.TicksPerQuarter);
        }

        return weights.All(weight => weight == 0) ? null : KeyFinder.Rank(weights)[0].Key;
    }

    private static int Mod12(int value) => ((value % 12) + 12) % 12;
}