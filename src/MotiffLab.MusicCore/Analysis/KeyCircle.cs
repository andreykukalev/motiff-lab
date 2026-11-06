using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>How a key next to the selection's key on the circle of fifths is related to it.</summary>
public enum KeyRelation
{
    /// <summary>One place clockwise: one sharp more, or one flat fewer.</summary>
    Dominant,

    /// <summary>One place anticlockwise: one flat more, or one sharp fewer.</summary>
    Subdominant,

    /// <summary>The same key signature, the other mode: A minor for C major (in Russian, «параллельная»).</summary>
    Relative,

    /// <summary>The same tonic, the other mode: C minor for C major (in Russian, «одноимённая»).</summary>
    Parallel,
}

/// <summary>One of the twelve places on the circle of fifths: a major key outside, its relative minor inside, and the key signature they share.</summary>
/// <param name="Position">0 for C major and A minor at the top, then clockwise by fifths: 1 for G major, 11 for F major.</param>
/// <param name="Fifths">The sharps (positive) or flats (negative) of the signature, written with at most six: F♯ major rather than G♭ major.</param>
/// <param name="Major">The major key's tonic, as a chord symbol names it: "C", "F♯", "B♭".</param>
/// <param name="Minor">The relative minor's tonic, as a chord symbol names it: "Am", "D♯m".</param>
/// <param name="Signature">The signature in short: "0", "3♯", "2♭".</param>
public sealed record CircleSector(int Position, int Fifths, string Major, string Minor, string Signature);

/// <summary>A key next to the selection's key: where it sits on the circle, its name and the word for the relation.</summary>
public sealed record CircleNeighbour(KeyRelation Relation, MusicalKey Key, int Position, string Name, string Label);

/// <summary>A chord of the passage placed on the circle: on the sector of its root, outside for a major third, inside for a minor third.</summary>
/// <param name="Number">The chord's place in the passage, from 1.</param>
/// <param name="Degree">Its scale degree in the key as a Roman numeral ("V", "i", "vii°", "V7"); null when the root is outside the scale.</param>
/// <param name="Place">Where it sounds and its notes, to ring on the score.</param>
public sealed record CircleChord(int Number, string Symbol, string? Degree, int Position, KeyMode Ring, FactPlace Place);

/// <summary>
/// The circle of fifths for a selection (the redesign's reference card, Andrei, 2026-11-06): the twelve places with their
/// signatures, the key of the selection with how sure the reading is and the key that fits almost as well, the keys next
/// to it, the passage's chords on the sectors of their roots, the notes outside the scale to ring on the score, a scale to
/// hear, and two or three lines that say what the card shows. Everything comes from the key reading and the chord finder;
/// nothing here reads the notes again for a key or a chord.
/// </summary>
/// <param name="Position">The key's place on the circle; <see cref="Key"/>'s mode says which ring.</param>
/// <param name="RunnerUpNote">"D minor fits almost as well." when the reading is not detected and has a runner-up; else null.</param>
/// <param name="Outside">The selected notes outside the key's scale, labelled with how many; null when every note is in it.</param>
/// <param name="ScalePitches">The key's scale from the tonic to the octave as MIDI numbers, around middle C, to hear.</param>
/// <param name="Lines">What the card shows in two or three short sentences: the signature and the relative key, the neighbours, the chords.</param>
public sealed record KeyCircleView(
    MusicalKey Key,
    string Name,
    FactCertainty Certainty,
    int Position,
    MusicalKey? RunnerUp,
    int? RunnerUpPosition,
    string? RunnerUpNote,
    IReadOnlyList<CircleSector> Sectors,
    IReadOnlyList<CircleNeighbour> Neighbours,
    IReadOnlyList<CircleChord> Chords,
    FactPlace? Outside,
    IReadOnlyList<int> ScalePitches,
    IReadOnlyList<string> Lines)
{
    /// <summary>
    /// The scale as notes to hear, one beat each and the octave held for two, from <paramref name="at"/>: inferred, since
    /// the app plays them, not the piece. Their ids start with "circle_".
    /// </summary>
    public IReadOnlyList<Note> ScaleNotes(Tick at)
    {
        long beat = CanonicalTime.TicksPerQuarter;
        return [.. ScalePitches.Select((pitch, index) => new Note
        {
            Id = $"circle_{index}",
            Pitch = new Pitch(pitch),
            Span = new TickSpan(new Tick(at.Value + (index * beat)), index == ScalePitches.Count - 1 ? beat * 2 : beat),
            Velocity = 80,
            Track = 0,
            Attribution = Attribution.Inferred(1),
        })];
    }
}

/// <summary>Builds the circle of fifths for a selection from music-core's key reading and chords (<see cref="KeyCircleView"/>).</summary>
public static class KeyCircle
{
    public const int Places = 12;

    /// <summary>The most chords the lines name; the circle shows them all.</summary>
    private const int MaxChordsInLine = 6;

    private static readonly string[] s_numerals = ["I", "II", "III", "IV", "V", "VI", "VII"];

    /// <summary>The twelve places, C major and A minor at the top, then clockwise by fifths.</summary>
    public static IReadOnlyList<CircleSector> Sectors { get; } = [.. Enumerable.Range(0, Places).Select(Sector)];

    /// <summary>A key's place on the circle: the same for a major key and its relative minor.</summary>
    public static int PositionOf(MusicalKey key) => Mod12(key.Fifths);

    /// <summary>
    /// The circle for a selection, or null when no key was read (an empty selection). <paramref name="reading"/> is the
    /// motif's key reading (<see cref="MotifAnalysis.Key"/>); <paramref name="harmony"/> the selection's chords.
    /// </summary>
    public static KeyCircleView? For(MotifSelection selection, IReadOnlyList<Note> notes, MeterMap meter, KeyReading? reading, HarmonyAnalysis harmony, Words words = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(harmony);
        if (reading is null)
        {
            return null;
        }

        MusicalKey key = reading.Key;
        string name = key.NameIn(words);
        CircleNeighbour[] neighbours =
        [
            Neighbour(KeyRelation.Dominant, MusicalKey.OnTonic(key.TonicPitchClass + 7, key.Mode), words),
            Neighbour(KeyRelation.Subdominant, MusicalKey.OnTonic(key.TonicPitchClass + 5, key.Mode), words),
            Neighbour(KeyRelation.Relative, key.Relative, words),
            Neighbour(KeyRelation.Parallel, MusicalKey.OnTonic(key.TonicPitchClass, key.Mode == KeyMode.Major ? KeyMode.Minor : KeyMode.Major), words),
        ];

        CircleChord[] chords = [.. harmony.Chords.Select((chord, index) => Chord(chord, index + 1, key, meter, words))];

        var ids = selection.NoteIds.ToHashSet(StringComparer.Ordinal);
        Note[] outside = [.. notes.Where(note => ids.Contains(note.Id) && ScaleSteps.Locate(key, note.Pitch.MidiNumber).Alteration != 0)
            .OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal)];
        FactPlace? outsidePlace = outside.Length == 0
            ? null
            : new FactPlace(
                words.IsRussian
                    ? $"{Words.Capitalize(words.Count(outside.Length, "", "", "нота", "ноты", "нот"))} вне звукоряда"
                    : $"{(outside.Length == 1 ? "1 note" : $"{outside.Length} notes")} outside the scale",
                TickSpan.FromBounds(outside.Min(note => note.Span.Onset), outside.Max(note => note.Span.End)),
                [.. outside.Select(note => note.Id)]);

        MusicalKey? runnerUp = reading.Certainty == FactCertainty.Detected ? null : reading.RunnerUp;
        string? runnerUpNote = runnerUp is { } other
            ? words.T($"{other.NameIn(words)} fits almost as well.", $"{Words.Capitalize(other.NameIn(words))} подходит почти так же.")
            : null;

        return new KeyCircleView(
            key,
            name,
            reading.Certainty,
            PositionOf(key),
            runnerUp,
            runnerUp is { } second ? PositionOf(second) : null,
            runnerUpNote,
            Sectors,
            neighbours,
            chords,
            outsidePlace,
            Scale(key),
            Lines(key, name, neighbours, chords, words));
    }

    private static CircleSector Sector(int position)
    {
        int fifths = position <= 6 ? position : position - Places;
        var major = new MusicalKey(fifths, KeyMode.Major);
        var minor = new MusicalKey(fifths, KeyMode.Minor);
        string signature = fifths switch
        {
            0 => "0",
            > 0 => $"{fifths}♯",
            _ => $"{-fifths}♭",
        };
        return new CircleSector(position, fifths, major.SpellClass(major.TonicPitchClass), $"{minor.SpellClass(minor.TonicPitchClass)}m", signature);
    }

    private static CircleNeighbour Neighbour(KeyRelation relation, MusicalKey key, Words words)
    {
        string label = relation switch
        {
            KeyRelation.Dominant => words.T("dominant", "доминанта"),
            KeyRelation.Subdominant => words.T("subdominant", "субдоминанта"),
            KeyRelation.Relative => words.T("relative key", "параллельная"),
            _ => words.T("parallel key", "одноимённая"),
        };
        return new CircleNeighbour(relation, key, PositionOf(key), key.NameIn(words), label);
    }

    private static CircleChord Chord(ChordSpan chord, int number, MusicalKey key, MeterMap meter, Words words)
    {
        int root = chord.Chord.Root;
        KeyMode ring = chord.Chord.Intervals[1] == 3 ? KeyMode.Minor : KeyMode.Major;
        string symbol = chord.Symbol(key);
        var place = new FactPlace($"{symbol}, {HarmonyAnalyzer.From(chord.Span.Onset, meter, words)}", chord.Span, chord.NoteIds);
        return new CircleChord(number, symbol, Degree(chord.Chord, key), PositionOf(MusicalKey.OnTonic(root, ring)), ring, place);
    }

    /// <summary>The chord's degree in the key: upper case for a major third, lower case for a minor one, ° diminished, + augmented.</summary>
    private static string? Degree(ChordLabel chord, MusicalKey key)
    {
        ScalePosition position = ScaleSteps.Locate(key, 60 + chord.Root);
        if (position.Alteration != 0)
        {
            return null;
        }

        string numeral = s_numerals[Mod(position.Step, ScaleSteps.StepsPerOctave)];
        return chord.Quality switch
        {
            ChordQuality.Major or ChordQuality.Suspended4 => numeral,
            ChordQuality.Dominant7 => $"{numeral}7",
            ChordQuality.Major7 => $"{numeral}maj7",
            ChordQuality.Augmented => $"{numeral}+",
            ChordQuality.Minor => numeral.ToLowerInvariant(),
            ChordQuality.Minor7 => $"{numeral.ToLowerInvariant()}7",
            ChordQuality.Diminished => $"{numeral.ToLowerInvariant()}°",
            ChordQuality.HalfDiminished7 => $"{numeral.ToLowerInvariant()}ø7",
            _ => $"{numeral.ToLowerInvariant()}°7",
        };
    }

    /// <summary>The scale from the tonic at or above C4 (A minor from A3) to its octave.</summary>
    private static int[] Scale(MusicalKey key)
    {
        int tonic = 60 + key.TonicPitchClass - (key.TonicPitchClass > 6 ? 12 : 0);
        ScalePosition start = ScaleSteps.Locate(key, tonic);
        return [.. Enumerable.Range(0, ScaleSteps.StepsPerOctave + 1).Select(step => ScaleSteps.MidiNumber(key, start with { Step = start.Step + step }))];
    }

    private static string[] Lines(MusicalKey key, string name, CircleNeighbour[] neighbours, CircleChord[] chords, Words words)
    {
        string relative = neighbours.Single(neighbour => neighbour.Relation == KeyRelation.Relative).Name;
        string dominant = neighbours.Single(neighbour => neighbour.Relation == KeyRelation.Dominant).Name;
        string subdominant = neighbours.Single(neighbour => neighbour.Relation == KeyRelation.Subdominant).Name;
        string signature = key.Fifths switch
        {
            0 => words.T("no sharps or flats", "без диезов и бемолей"),
            > 0 => words.Count(key.Fifths, "sharp", "sharps", "диез", "диеза", "диезов"),
            _ => words.Count(-key.Fifths, "flat", "flats", "бемоль", "бемоля", "бемолей"),
        };
        List<string> lines =
        [
            words.T(
                $"{Words.Capitalize(name)}: {signature}, the same as its relative key, {relative}.",
                $"{Words.Capitalize(name)}: {signature}, как у параллельной тональности, {relative}."),
            words.T(
                $"Its neighbours on the circle differ by one sharp or flat: {dominant} (dominant) and {subdominant} (subdominant).",
                $"Соседи по кругу отличаются на один знак: {dominant} (доминанта) и {subdominant} (субдоминанта)."),
        ];
        if (chords.Length > 0)
        {
            string list = string.Join(" → ", chords.Take(MaxChordsInLine).Select(chord => chord.Degree is { } degree ? $"{chord.Symbol} ({degree})" : chord.Symbol))
                + (chords.Length > MaxChordsInLine ? " …" : "");
            lines.Add(words.T($"The chords of the passage: {list}.", $"Аккорды отрывка: {list}."));
        }

        return [.. lines];
    }

    private static int Mod12(int value) => Mod(value, Places);

    private static int Mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;
}