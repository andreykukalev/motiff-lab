using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>A note name spelled by its letter, as theory writes it: F♯ major has E♯, not F; D♯ minor's leading note is C𝄪.</summary>
/// <param name="Step">0 for C up to 6 for B.</param>
/// <param name="Alter">Semitones from the natural letter, −2 to 2.</param>
public readonly record struct SpelledName(int Step, int Alter)
{
    private static readonly int[] s_natural = [0, 2, 4, 5, 7, 9, 11];
    private static readonly string[] s_letters = ["C", "D", "E", "F", "G", "A", "B"];
    private static readonly string[] s_syllables = ["до", "ре", "ми", "фа", "соль", "ля", "си"];

    /// <summary>0 for C up to 11 for B.</summary>
    public int PitchClass => (((s_natural[Step] + Alter) % 12) + 12) % 12;

    /// <summary>The letter with its accidental, as chord symbols name roots: "F♯", "B♭", "C𝄪".</summary>
    public string Letter => $"{s_letters[Step]}{Accidental}";

    /// <summary>"F♯" with letters, "фа♯" with the Russian syllables (the note naming setting, <see cref="NoteNames"/>).</summary>
    public string Name(NoteNaming naming) => naming == NoteNaming.Syllables ? $"{s_syllables[Step]}{Accidental}" : Letter;

    private string Accidental => Alter switch
    {
        -2 => "𝄫",
        -1 => "♭",
        1 => "♯",
        2 => "𝄪",
        _ => "",
    };

    /// <summary>The letter <paramref name="step"/> spelled so that it sounds <paramref name="pitchClass"/>.</summary>
    internal static SpelledName On(int step, int pitchClass)
    {
        int letter = ((step % 7) + 7) % 7;
        int alter = (((pitchClass - s_natural[letter]) % 12) + 12) % 12;
        return new SpelledName(letter, alter > 6 ? alter - 12 : alter);
    }
}

/// <summary>Notes the player can hear and see on a small keyboard: a scale one after the other, or a chord at once.</summary>
/// <param name="Id">"scale.major", "scale.naturalMinor", "scale.harmonicMinor", "chord.I", "chord.iv", …: what the facade's reference.sound takes.</param>
/// <param name="Pitches">MIDI numbers from the lowest, around middle C.</param>
/// <param name="Together">A chord, struck at once; else one note after another, <see cref="Step"/> apart.</param>
/// <param name="KeyboardLow">The C at or below the lowest note, where a small keyboard that shows them starts.</param>
/// <param name="KeyboardHigh">The B at or above the highest note, where it ends.</param>
public sealed record ReferenceSound(string Id, IReadOnlyList<int> Pitches, bool Together, int KeyboardLow, int KeyboardHigh)
{
    /// <summary>Ticks from one note to the next when they are not struck at once: a beat unless set.</summary>
    public long Step { get; init; } = CanonicalTime.TicksPerQuarter;

    /// <summary>One note after another, each held until the last ends, as a broken chord is let ring.</summary>
    public bool Ringing { get; init; }

    /// <summary>The keys the keyboard marks when they are not all the notes heard: a scale up and down marks its way up.</summary>
    public IReadOnlyList<int>? Keys { get; init; }

    /// <summary>How long it sounds, in ticks: the last note, or the chord, is held two beats.</summary>
    public long Length => (Together ? 0 : (Pitches.Count - 1) * Step) + (2 * CanonicalTime.TicksPerQuarter);

    /// <summary>Whether a key of the keyboard is one of the notes, for its highlight.</summary>
    public bool Sounds(int midiNumber) => (Keys ?? Pitches).Contains(midiNumber);
}

/// <summary>A scale of a key: its name, its notes spelled by letter from the tonic, and the notes to hear.</summary>
/// <param name="Title">"Major scale", "natural minor", "harmonic minor"; in Russian «мажорная гамма», «натуральный минор», «гармонический минор».</param>
/// <param name="Notes">The seven notes from the tonic, spelled for the naming.</param>
public sealed record ReferenceScale(ScaleForm Form, string Title, IReadOnlyList<string> Notes, ReferenceSound Sound)
{
    /// <summary>The key's tonic, where the scale's own card (<see cref="ScaleReference"/>) starts.</summary>
    public SpelledName Tonic { get; init; }
}

/// <summary>One of the key's three main chords: tonic, subdominant, dominant.</summary>
/// <param name="Numeral">"I", "IV", "V" in major; "i", "iv", "V" in minor (the dominant of the harmonic minor).</param>
/// <param name="FunctionName">"tonic", "subdominant", "dominant"; «тоника», «субдоминанта», «доминанта».</param>
/// <param name="Symbol">The chord symbol, in letters as chord symbols are written in both languages: "F♯", "G♯m".</param>
/// <param name="Notes">Its notes from the root, spelled for the naming: "F♯ A♯ C♯".</param>
public sealed record ReferenceChord(string Numeral, HarmonicFunction Function, string FunctionName, string Symbol, IReadOnlyList<string> Notes, ReferenceSound Sound)
{
    /// <summary>The chord's root, where its own card (<see cref="ChordReference"/>) starts.</summary>
    public SpelledName Root { get; init; }

    /// <summary>Major or minor, for its own card.</summary>
    public ChordType Type { get; init; }
}

/// <summary>A key signature: how many sharps or flats, which ones in the order they are written, and where they sit on the treble staff.</summary>
/// <param name="Count">"5 sharps", "1 flat", "no sharps or flats"; «5 диезов», «1 бемоль», «без знаков».</param>
/// <param name="Accidentals">The notes the signature alters, in the order they are written: "F♯", "C♯", "G♯"…</param>
/// <param name="TrebleSteps">Each accidental's place on the treble staff, in diatonic steps above its bottom line (E4 is 0, F5 on the top line is 8).</param>
public sealed record SignatureCard(int Fifths, string Label, string Count, IReadOnlyList<string> Accidentals, IReadOnlyList<int> TrebleSteps)
{
    /// <summary>Sharps rather than flats; neither when <see cref="Fifths"/> is 0.</summary>
    public bool Sharps => Fifths > 0;
}

/// <summary>A choice of the reverse lookup: a number of sharps or flats, with its short and its spoken label.</summary>
/// <param name="Label">"0", "5♯", "2♭".</param>
/// <param name="Spoken">"5 sharps"; «5 диезов».</param>
public sealed record SignatureChoice(int Fifths, string Label, string Spoken);

/// <summary>
/// The reference card of a key (the Reference experiment, Andrei, 2026-11-11): the key signature, the key with its relative,
/// parallel and enharmonic keys, its scales spelled by letter, and its three main chords with their notes, each to hear and
/// to see on a small keyboard. Pure theory: nothing here reads a piece.
/// </summary>
/// <param name="Position">The key's place on the circle of fifths (<see cref="KeyCircle.PositionOf"/>), shared with its relative and its enharmonic key.</param>
/// <param name="Enharmonic">The key that sounds the same with another signature (B major and C♭ major), for five to seven sharps or flats; else null.</param>
/// <param name="Parallel">The same tonic in the other mode; when that would need more than seven sharps or flats, its enharmonic key (A♯ minor's is B♭ major).</param>
public sealed record KeyCard(
    MusicalKey Key,
    string Name,
    int Position,
    SignatureCard Signature,
    MusicalKey Relative,
    string RelativeName,
    MusicalKey Parallel,
    string ParallelName,
    MusicalKey? Enharmonic,
    string? EnharmonicName,
    IReadOnlyList<ReferenceScale> Scales,
    IReadOnlyList<ReferenceChord> Chords)
{
    /// <summary>The scale or chord with this id, or null.</summary>
    public ReferenceSound? Sound(string id) =>
        Scales.Select(scale => scale.Sound).Concat(Chords.Select(chord => chord.Sound)).FirstOrDefault(sound => sound.Id == id);
}

/// <summary>Builds the key cards of the Reference (<see cref="KeyCard"/>) and the notes to hear from them.</summary>
public static class KeyReference
{
    /// <summary>The tempo the reference plays at: a quarter at 100 beats a minute.</summary>
    public const int MicrosecondsPerQuarter = 600_000;

    // The letters sharps (F C G D A E B) and flats (B E A D G C F) are written in, and their places on the treble staff.
    private static readonly int[] s_sharpSteps = [3, 0, 4, 1, 5, 2, 6];
    private static readonly int[] s_flatSteps = [6, 2, 5, 1, 4, 0, 3];
    private static readonly int[] s_sharpStaff = [8, 5, 9, 6, 3, 7, 4];
    private static readonly int[] s_flatStaff = [4, 7, 3, 6, 2, 5, 1];

    /// <summary>
    /// The roots a chord or a scale of the Reference starts on, in the order of the keyboard: the seven letters, and each
    /// black key spelled both ways (C♯ and D♭), seventeen in all.
    /// </summary>
    public static IReadOnlyList<SpelledName> Roots { get; } =
    [
        new(0, 0), new(0, 1), new(1, -1), new(1, 0), new(1, 1), new(2, -1), new(2, 0), new(3, 0), new(3, 1),
        new(4, -1), new(4, 0), new(4, 1), new(5, -1), new(5, 0), new(5, 1), new(6, -1), new(6, 0),
    ];

    /// <summary>The fifteen key signatures in the order the lookup offers them: none, one to seven sharps, one to seven flats.</summary>
    public static IReadOnlyList<SignatureChoice> Signatures(Words words = default) =>
        [.. Enumerable.Range(0, 8).Concat(Enumerable.Range(1, 7).Select(flats => -flats)).Select(fifths => new SignatureChoice(fifths, Label(fifths), CountOf(fifths, words)))];

    /// <summary>"0", "5♯", "2♭", as the circle's sectors write a signature.</summary>
    public static string Label(int fifths) => fifths switch
    {
        0 => "0",
        > 0 => $"{fifths}♯",
        _ => $"{-fifths}♭",
    };

    /// <summary>The key signature of <paramref name="fifths"/> sharps (positive) or flats (negative), −7 to 7.</summary>
    public static SignatureCard Signature(int fifths, NoteNaming naming = NoteNaming.Letters, Words words = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fifths, -7);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fifths, 7);
        int[] steps = fifths >= 0 ? s_sharpSteps : s_flatSteps;
        int[] staff = fifths >= 0 ? s_sharpStaff : s_flatStaff;
        int count = Math.Abs(fifths);
        string[] accidentals = [.. steps.Take(count).Select(step => new SpelledName(step, Math.Sign(fifths)).Name(naming))];
        return new SignatureCard(fifths, Label(fifths), CountOf(fifths, words), accidentals, [.. staff.Take(count)]);
    }

    /// <summary>The card of a key.</summary>
    public static KeyCard For(MusicalKey key, NoteNaming naming = NoteNaming.Letters, Words words = default)
    {
        MusicalKey relative = key.Relative;
        MusicalKey parallel = ParallelOf(key);
        MusicalKey? enharmonic = EnharmonicOf(key);
        SpelledName tonic = TonicOf(key);
        ScaleForm[] forms = key.Mode == KeyMode.Major ? [ScaleForm.Major] : [ScaleForm.NaturalMinor, ScaleForm.HarmonicMinor];
        return new KeyCard(
            key,
            key.NameIn(words),
            KeyCircle.PositionOf(key),
            Signature(key.Fifths, naming, words),
            relative,
            relative.NameIn(words),
            parallel,
            parallel.NameIn(words),
            enharmonic,
            enharmonic?.NameIn(words),
            [.. forms.Select(form => Scale(tonic, form, naming, words))],
            Chords(key, naming, words));
    }

    /// <summary>The spelled notes of a scale from its tonic, seven of them.</summary>
    public static IReadOnlyList<SpelledName> ScaleOf(MusicalKey key, ScaleForm form) => ScaleReference.Spell(TonicOf(key), form);

    /// <summary>
    /// The notes of a scale or chord to hear from <paramref name="at"/>: a chord struck at once and held two beats; else one
    /// note after another, <see cref="ReferenceSound.Step"/> apart, the last held two beats, and each held until then when
    /// the sound rings (<see cref="ReferenceSound.Ringing"/>). They are inferred, since the app plays them, not a piece;
    /// their ids start with "reference_".
    /// </summary>
    public static IReadOnlyList<Note> Notes(ReferenceSound sound, Tick at = default)
    {
        ArgumentNullException.ThrowIfNull(sound);
        long end = at.Value + sound.Length;
        return [.. sound.Pitches.Select((pitch, index) =>
        {
            long onset = sound.Together ? at.Value : at.Value + (index * sound.Step);
            long length = sound.Together || sound.Ringing || index == sound.Pitches.Count - 1 ? end - onset : sound.Step;
            return new Note
            {
                Id = $"reference_{index}",
                Pitch = new Pitch(pitch),
                Span = new TickSpan(new Tick(onset), length),
                Velocity = 80,
                Track = 0,
                Attribution = Attribution.Inferred(1),
            };
        })];
    }

    /// <summary>A scale or chord as a playback score of its own at the reference tempo, for a screen with no piece open (the library).</summary>
    public static PlaybackScore Playback(ReferenceSound sound)
    {
        IReadOnlyList<Note> notes = Notes(sound);
        Tick end = notes.Count == 0 ? Tick.Zero : notes.Max(note => note.Span.End);
        return PlaybackScore.Create(notes, [new PlaybackTempo(Tick.Zero, MicrosecondsPerQuarter)], MeterMap.Default, end);
    }

    private static ReferenceScale Scale(SpelledName tonic, ScaleForm form, NoteNaming naming, Words words)
    {
        IReadOnlyList<SpelledName> notes = ScaleReference.Spell(tonic, form);
        int[] pitches = Upward([.. notes.Select(note => note.PitchClass), tonic.PitchClass], Around(tonic.PitchClass));
        string title = form == ScaleForm.Major ? words.T("major scale", "мажорная гамма") : ScaleReference.Title(form, words);
        return new ReferenceScale(form, title, [.. notes.Select(note => note.Name(naming))], Sound($"scale.{ScaleReference.Id(form)}", pitches, together: false)) { Tonic = tonic };
    }

    /// <summary>I, IV and V on the major scale; i and iv on the natural minor and V on the harmonic minor, whose leading note makes it major.</summary>
    private static ReferenceChord[] Chords(MusicalKey key, NoteNaming naming, Words words)
    {
        bool major = key.Mode == KeyMode.Major;
        SpelledName tonic = TonicOf(key);
        IReadOnlyList<SpelledName> chordScale = ScaleReference.Spell(tonic, major ? ScaleForm.Major : ScaleForm.NaturalMinor);
        IReadOnlyList<SpelledName> dominantScale = ScaleReference.Spell(tonic, major ? ScaleForm.Major : ScaleForm.HarmonicMinor);
        return
        [
            Chord(chordScale, 0, major ? "I" : "i", HarmonicFunction.Tonic, naming, words),
            Chord(chordScale, 3, major ? "IV" : "iv", HarmonicFunction.Subdominant, naming, words),
            Chord(dominantScale, 4, "V", HarmonicFunction.Dominant, naming, words),
        ];
    }

    private static ReferenceChord Chord(IReadOnlyList<SpelledName> scale, int degree, string numeral, HarmonicFunction function, NoteNaming naming, Words words)
    {
        SpelledName[] notes = [.. new[] { 0, 2, 4 }.Select(third => scale[(degree + third) % scale.Count])];
        bool minorThird = (((notes[1].PitchClass - notes[0].PitchClass) % 12) + 12) % 12 == 3;
        int[] pitches = Upward([.. notes.Select(note => note.PitchClass)], Around(notes[0].PitchClass));
        return new ReferenceChord(
            numeral,
            function,
            HarmonyAnalyzer.FunctionName(function, words),
            $"{notes[0].Letter}{(minorThird ? "m" : "")}",
            [.. notes.Select(note => note.Name(naming))],
            Sound($"chord.{numeral}", pitches, together: true))
        {
            Root = notes[0],
            Type = minorThird ? ChordType.Minor : ChordType.Major,
        };
    }

    /// <summary>A sound of these notes on a keyboard from the C at or below the lowest to the B at or above the highest.</summary>
    internal static ReferenceSound Sound(string id, int[] pitches, bool together) =>
        new(id, pitches, together, pitches.Min() - (pitches.Min() % 12), pitches.Max() + (11 - (pitches.Max() % 12)));

    /// <summary>The pitch classes as MIDI numbers, each the nearest above the one before, the first at <paramref name="first"/>.</summary>
    internal static int[] Upward(int[] pitchClasses, int first)
    {
        var pitches = new int[pitchClasses.Length];
        pitches[0] = first;
        for (int i = 1; i < pitches.Length; i++)
        {
            int up = (((pitchClasses[i] - pitches[i - 1]) % 12) + 12) % 12;
            pitches[i] = pitches[i - 1] + (up == 0 ? 12 : up);
        }

        return pitches;
    }

    /// <summary>A pitch class around middle C: from C4 up to F♯4, and from G3 for G to B, as the circle's scale starts.</summary>
    internal static int Around(int pitchClass) => 60 + pitchClass - (pitchClass > 6 ? 12 : 0);

    private static SpelledName TonicOf(MusicalKey key)
    {
        SpelledPitch spelled = PitchSpeller.Spell(60 + key.TonicPitchClass, key.Fifths);
        return new SpelledName(spelled.Step, spelled.Alter);
    }

    private static MusicalKey ParallelOf(MusicalKey key)
    {
        int fifths = key.Fifths + (key.Mode == KeyMode.Major ? -3 : 3);
        KeyMode mode = key.Mode == KeyMode.Major ? KeyMode.Minor : KeyMode.Major;
        return Math.Abs(fifths) <= 7 ? new MusicalKey(fifths, mode) : new MusicalKey(fifths - (12 * Math.Sign(fifths)), mode);
    }

    private static MusicalKey? EnharmonicOf(MusicalKey key)
    {
        if (Math.Abs(key.Fifths) < 5)
        {
            return null;
        }

        int other = key.Fifths - (12 * Math.Sign(key.Fifths));
        return Math.Abs(other) <= 7 ? new MusicalKey(other, key.Mode) : null;
    }

    private static string CountOf(int fifths, Words words) => fifths switch
    {
        0 => words.T("no sharps or flats", "без знаков"),
        > 0 => words.Count(fifths, "sharp", "sharps", "диез", "диеза", "диезов"),
        _ => words.Count(-fifths, "flat", "flats", "бемоль", "бемоля", "бемолей"),
    };
}