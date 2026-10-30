using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

public enum KeyMode
{
    Major,
    Minor,
}

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

    private static int Mod12(int value) => ((value % 12) + 12) % 12;
}

/// <summary>A key signature as the source file states it.</summary>
/// <param name="Fifths">Sharps (positive) or flats (negative), −7 to 7.</param>
/// <param name="Mode">The mode when the file states it; null when it gives only the sharps or flats.</param>
public sealed record KeySignature(Tick At, int Fifths, KeyMode? Mode)
{
    /// <summary>A MIDI key signature always states its mode.</summary>
    public static IReadOnlyList<KeySignature> FromMidi(MidiImport import)
    {
        ArgumentNullException.ThrowIfNull(import);
        return [.. import.KeySignatures
            .Select(key => new KeySignature(key.At, Math.Clamp(key.Sharps, -7, 7), key.IsMinor ? KeyMode.Minor : KeyMode.Major))
            .OrderBy(key => key.At)];
    }

    /// <summary>The key signatures of the first part that has any, as the score view uses them. Modes other than major and minor count as not stated.</summary>
    public static IReadOnlyList<KeySignature> FromMusicXml(MusicXmlImport import)
    {
        ArgumentNullException.ThrowIfNull(import);
        int firstTrack = import.KeySignatures.Count == 0 ? 0 : import.KeySignatures.Min(key => key.Track);
        return [.. import.KeySignatures
            .Where(key => key.Track == firstTrack)
            .Select(key => new KeySignature(key.At, Math.Clamp(key.Fifths, -7, 7), ModeOf(key.Mode)))
            .OrderBy(key => key.At)];
    }

    /// <summary>The key signature in force at a position, or null before the first.</summary>
    public static KeySignature? InForce(IEnumerable<KeySignature> keys, Tick position)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return keys.Where(key => key.At <= position).OrderBy(key => key.At).LastOrDefault();
    }

    private static KeyMode? ModeOf(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "major" or "ionian" => KeyMode.Major,
        "minor" or "aeolian" => KeyMode.Minor,
        _ => null,
    };
}