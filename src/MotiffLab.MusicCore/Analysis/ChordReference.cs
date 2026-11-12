using MotiffLab.MusicCore.Notation;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The chords of the Reference (part 2; Andrei, 2026-11-11): the four triads, the two suspended chords, the five common
/// sevenths, the two sixths and the added ninth. Apart from the chord finder's <see cref="ChordQuality"/>, which names
/// what a piece holds; this one names what a player looks up.
/// </summary>
public enum ChordType
{
    Major,
    Minor,
    Diminished,
    Augmented,
    Sus2,
    Sus4,
    Dominant7,
    Major7,
    Minor7,
    HalfDiminished7,
    Diminished7,
    Major6,
    Minor6,
    Add9,
}

/// <summary>How the Reference groups its chords (version 0.5): the triads, the sevenths, and the sixths with the added ninth.</summary>
public enum ChordFamily
{
    Triad,
    Seventh,
    SixthOrNinth,
}

/// <summary>A chord the Reference offers: its id ("minor7"), the symbol after the root ("m7") and its name in words.</summary>
public sealed record ChordTypeChoice(ChordType Type, string Id, string Suffix, string Name)
{
    /// <summary>The group it is offered in.</summary>
    public ChordFamily Family => ChordReference.FamilyOf(Type);
}

/// <summary>A chord in one position on the keyboard: root position or an inversion, struck at once or broken.</summary>
/// <param name="Inversion">0 for root position, 1 to 3 for the first to the third inversion.</param>
/// <param name="Title">"root position", "first inversion"; in Russian the classical name where there is one: «секстаккорд», «терцквартаккорд».</param>
/// <param name="Figure">The figures of a triad or a seventh in this position ("6", "6/4", "6/5", "4/3", "2"); null in root position and for the other chords.</param>
/// <param name="Notes">The notes from the bass up, spelled for the naming.</param>
/// <param name="Block">The chord struck at once and held two beats.</param>
/// <param name="Arpeggio">The chord broken from the bass up, a quaver a note, each note let ring.</param>
public sealed record ChordPosition(int Inversion, string Title, string? Figure, IReadOnlyList<string> Notes, ReferenceSound Block, ReferenceSound Arpeggio);

/// <summary>A chord's own card: its symbol and name, its formula, its notes spelled by letter, and each position to hear and see.</summary>
/// <param name="Symbol">"F♯m7", "B♭+", "Dsus4": in letters in both languages, as chord symbols are written.</param>
/// <param name="Name">"minor seventh"; «малый минорный септаккорд».</param>
/// <param name="Formula">Each tone's degree against the major scale of the root: "1 ♭3 5 ♭7".</param>
/// <param name="Notes">The tones from the root, spelled for the naming.</param>
/// <param name="Positions">Root position, then each inversion; every position on the same stretch of keyboard.</param>
public sealed record ChordCard(
    SpelledName Root,
    ChordType Type,
    string Symbol,
    string Name,
    IReadOnlyList<string> Formula,
    IReadOnlyList<string> Notes,
    IReadOnlyList<ChordPosition> Positions)
{
    /// <summary>The position's sound with this id ("block.1", "arpeggio.0"), or null.</summary>
    public ReferenceSound? Sound(string id) =>
        Positions.SelectMany(position => new[] { position.Block, position.Arpeggio }).FirstOrDefault(sound => sound.Id == id);
}

/// <summary>Spells chords from any root of the Reference (<see cref="KeyReference.Roots"/>), with their inversions to hear. Pure theory.</summary>
public static class ChordReference
{
    /// <summary>A quaver: the time from one note of a broken chord to the next.</summary>
    public const long ArpeggioStep = ScaleReference.Step;

    /// <summary>The chords in the order the Reference offers them, with their ids, symbols and names.</summary>
    public static IReadOnlyList<ChordTypeChoice> Types(Words words = default) =>
        [.. Enum.GetValues<ChordType>().Select(type => new ChordTypeChoice(type, Id(type), Suffix(type), Name(type, words)))];

    /// <summary>"major", "halfDiminished7", "add9": the id the facade takes.</summary>
    public static string Id(ChordType type)
    {
        string name = type.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>The chord with this id, or null.</summary>
    public static ChordType? Parse(string? id) => Enum.GetValues<ChordType>().Cast<ChordType?>().FirstOrDefault(type => Id(type!.Value) == id);

    /// <summary>What follows the root in the symbol, as the app's chord finder writes it: "m", "dim", "+", "m7♭5".</summary>
    public static string Suffix(ChordType type) => type switch
    {
        ChordType.Major => "",
        ChordType.Minor => "m",
        ChordType.Diminished => "dim",
        ChordType.Augmented => "+",
        ChordType.Sus2 => "sus2",
        ChordType.Sus4 => "sus4",
        ChordType.Dominant7 => "7",
        ChordType.Major7 => "maj7",
        ChordType.Minor7 => "m7",
        ChordType.HalfDiminished7 => "m7♭5",
        ChordType.Diminished7 => "dim7",
        ChordType.Major6 => "6",
        ChordType.Minor6 => "m6",
        _ => "add9",
    };

    /// <summary>The chord's name in words: "dominant seventh"; «малый мажорный септаккорд».</summary>
    public static string Name(ChordType type, Words words = default) => type switch
    {
        ChordType.Major => words.T("major triad", "мажорное трезвучие"),
        ChordType.Minor => words.T("minor triad", "минорное трезвучие"),
        ChordType.Diminished => words.T("diminished triad", "уменьшённое трезвучие"),
        ChordType.Augmented => words.T("augmented triad", "увеличенное трезвучие"),
        ChordType.Sus2 => words.T("suspended second: a second in place of the third", "с секундой вместо терции"),
        ChordType.Sus4 => words.T("suspended fourth: a fourth in place of the third", "с квартой вместо терции"),
        ChordType.Dominant7 => words.T("dominant seventh", "малый мажорный септаккорд"),
        ChordType.Major7 => words.T("major seventh", "большой мажорный септаккорд"),
        ChordType.Minor7 => words.T("minor seventh", "малый минорный септаккорд"),
        ChordType.HalfDiminished7 => words.T("half-diminished seventh", "малый уменьшённый септаккорд"),
        ChordType.Diminished7 => words.T("diminished seventh", "уменьшённый септаккорд"),
        ChordType.Major6 => words.T("major sixth", "мажорное трезвучие с секстой"),
        ChordType.Minor6 => words.T("minor sixth", "минорное трезвучие с секстой"),
        _ => words.T("added ninth", "трезвучие с добавленной ноной"),
    };

    /// <summary>The chord's tones from <paramref name="root"/>, each on its own letter: G♯ augmented is G♯ B♯ D𝄪.</summary>
    public static IReadOnlyList<SpelledName> Spell(SpelledName root, ChordType type) =>
        [.. Tones(type).Select(tone => SpelledName.On(root.Step + tone.Steps, root.PitchClass + tone.Semitones))];

    /// <summary>The card of a chord: root position from around middle C, then each inversion with the lower tones an octave up.</summary>
    public static ChordCard For(SpelledName root, ChordType type, NoteNaming naming = NoteNaming.Letters, Words words = default)
    {
        (int Steps, int Semitones)[] tones = Tones(type);
        IReadOnlyList<SpelledName> notes = Spell(root, type);
        int bass = KeyReference.Around(root.PitchClass);
        int[] rootPosition = [.. tones.Select(tone => bass + tone.Semitones)];
        // An added ninth is no bass of its own: the inversions turn the triad under it, and it stays on top.
        int turning = Turning(type);
        int[][] voicings = [.. Enumerable.Range(0, turning).Select(inversion => Voicing(rootPosition, turning, inversion))];
        int low = voicings.Min(voicing => voicing.Min());
        int high = voicings.Max(voicing => voicing.Max());
        return new ChordCard(
            root,
            type,
            $"{root.Letter}{Suffix(type)}",
            Name(type, words),
            [.. tones.Select(tone => ScaleReference.DegreeLabel(tone.Steps, tone.Semitones))],
            [.. notes.Select(note => note.Name(naming))],
            [.. voicings.Select((voicing, inversion) => Position(type, inversion, voicing, (low, high), rootPosition, notes, naming, words))]);
    }

    /// <summary>
    /// A position of the chord in close position from around middle C, as its card shows it: each pitch from the bass up with
    /// the tone it is, spelled. <paramref name="inversion"/> 0 is root position; an added ninth turns only the triad under it.
    /// </summary>
    internal static IReadOnlyList<WrittenTone> Close(SpelledName root, ChordType type, int inversion)
    {
        (int Steps, int Semitones)[] tones = Tones(type);
        IReadOnlyList<SpelledName> notes = Spell(root, type);
        int bass = KeyReference.Around(root.PitchClass);
        int[] rootPosition = [.. tones.Select(tone => bass + tone.Semitones)];
        int turning = Turning(type);
        ArgumentOutOfRangeException.ThrowIfNegative(inversion);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(inversion, turning);
        return [.. Voicing(rootPosition, turning, inversion).Select(pitch => new WrittenTone(pitch, notes[Array.FindIndex(rootPosition, tone => (((pitch - tone) % 12) + 12) % 12 == 0)]))];
    }

    /// <summary>How many positions the chord has: one for each tone, but an added ninth is no bass of its own.</summary>
    public static int Turning(ChordType type) => type == ChordType.Add9 ? 3 : Tones(type).Length;

    /// <summary>The tones of an inversion from the bass up: those below the new bass an octave higher, an added ninth above them all.</summary>
    private static int[] Voicing(int[] rootPosition, int turning, int inversion)
    {
        var pitches = new List<int>();
        for (int k = 0; k < turning; k++)
        {
            int index = (inversion + k) % turning;
            pitches.Add(rootPosition[index] + (index < inversion ? 12 : 0));
        }

        for (int added = turning; added < rootPosition.Length; added++)
        {
            int pitch = rootPosition[added];
            while (pitch <= pitches[^1])
            {
                pitch += 12;
            }

            pitches.Add(pitch);
        }

        return [.. pitches];
    }

    /// <summary>One position, on the stretch of keyboard <paramref name="range"/> that every position of the chord shares.</summary>
    private static ChordPosition Position(
        ChordType type, int inversion, int[] voicing, (int Low, int High) range, int[] rootPosition, IReadOnlyList<SpelledName> notes, NoteNaming naming, Words words)
    {
        // Each pitch is one of the chord's tones an octave or two away: its name is that tone's.
        string[] names = [.. voicing.Select(pitch => notes[Array.FindIndex(rootPosition, tone => (((pitch - tone) % 12) + 12) % 12 == 0)].Name(naming))];
        int low = range.Low - (range.Low % 12);
        int high = range.High + (11 - (range.High % 12));
        ReferenceSound block = KeyReference.Sound($"block.{inversion}", voicing, together: true) with { KeyboardLow = low, KeyboardHigh = high };
        ReferenceSound arpeggio = block with { Id = $"arpeggio.{inversion}", Together = false, Step = ArpeggioStep, Ringing = true };
        return new ChordPosition(inversion, Title(type, inversion, words), Figure(type, inversion), names, block, arpeggio);
    }

    /// <summary>The group a chord is offered in: the four triads and the two suspended chords, the five sevenths, the rest.</summary>
    public static ChordFamily FamilyOf(ChordType type) =>
        type <= ChordType.Sus4 ? ChordFamily.Triad : IsSeventh(type) ? ChordFamily.Seventh : ChordFamily.SixthOrNinth;

    private static bool IsTriad(ChordType type) => type is ChordType.Major or ChordType.Minor or ChordType.Diminished or ChordType.Augmented;

    private static bool IsSeventh(ChordType type) =>
        type is ChordType.Dominant7 or ChordType.Major7 or ChordType.Minor7 or ChordType.HalfDiminished7 or ChordType.Diminished7;

    private static string? Figure(ChordType type, int inversion)
    {
        if (inversion == 0)
        {
            return null;
        }

        string[]? figures = IsTriad(type) ? ["6", "6/4"] : IsSeventh(type) ? ["6/5", "4/3", "2"] : null;
        return figures?[inversion - 1];
    }

    private static string Title(ChordType type, int inversion, Words words)
    {
        string[] english = ["root position", "first inversion", "second inversion", "third inversion"];
        string[] russian = IsTriad(type)
            ? ["основной вид", "секстаккорд", "квартсекстаккорд"]
            : IsSeventh(type)
                ? ["основной вид", "квинтсекстаккорд", "терцквартаккорд", "секундаккорд"]
                : ["основной вид", "первое обращение", "второе обращение", "третье обращение"];
        return words.T(english[inversion], russian[inversion]);
    }

    /// <summary>Each tone of a chord: the letters above the root and the semitones.</summary>
    private static (int Steps, int Semitones)[] Tones(ChordType type) => type switch
    {
        ChordType.Major => [(0, 0), (2, 4), (4, 7)],
        ChordType.Minor => [(0, 0), (2, 3), (4, 7)],
        ChordType.Diminished => [(0, 0), (2, 3), (4, 6)],
        ChordType.Augmented => [(0, 0), (2, 4), (4, 8)],
        ChordType.Sus2 => [(0, 0), (1, 2), (4, 7)],
        ChordType.Sus4 => [(0, 0), (3, 5), (4, 7)],
        ChordType.Dominant7 => [(0, 0), (2, 4), (4, 7), (6, 10)],
        ChordType.Major7 => [(0, 0), (2, 4), (4, 7), (6, 11)],
        ChordType.Minor7 => [(0, 0), (2, 3), (4, 7), (6, 10)],
        ChordType.HalfDiminished7 => [(0, 0), (2, 3), (4, 6), (6, 10)],
        ChordType.Diminished7 => [(0, 0), (2, 3), (4, 6), (6, 9)],
        ChordType.Major6 => [(0, 0), (2, 4), (4, 7), (5, 9)],
        ChordType.Minor6 => [(0, 0), (2, 3), (4, 7), (5, 9)],
        _ => [(0, 0), (2, 4), (4, 7), (8, 14)],
    };
}