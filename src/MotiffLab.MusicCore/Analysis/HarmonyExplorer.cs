using MotiffLab.MusicCore.Notation;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>How the explorer's right hand plays each chord (version 0.5), a bar of quavers over the bass held in the left.</summary>
public enum ExplorerTexture
{
    /// <summary>The chord struck at once and held.</summary>
    Together,

    /// <summary>1 3 5 8 5 3 1 3: up to the octave and back.</summary>
    Arpeggio,

    /// <summary>1 5 3 8, twice: broken by skips.</summary>
    Broken,

    /// <summary>1 5 3 5, twice: the Alberti bass, low, high, middle, high.</summary>
    Alberti,
}

/// <summary>A chord of the key to try in the explorer.</summary>
/// <param name="Id">Its numeral, which names it: "i", "ii°", "V7".</param>
/// <param name="Numeral">The Roman numeral: upper case for major, lower for minor, ° for diminished, + for augmented.</param>
/// <param name="Symbol">The chord symbol in letters: "Am", "Bdim", "E7".</param>
public sealed record ExplorerChord(string Id, string Numeral, SpelledName Root, ChordType Type, string Symbol)
{
    /// <summary>How many positions it has: root position and an inversion for each tone above the root.</summary>
    public int Positions => ChordReference.Turning(Type);
}

/// <summary>One bar of a progression the player built: a chord of the key by its id, in a position.</summary>
public sealed record ExplorerStep(string ChordId, int Inversion);

/// <summary>A bar of the progression as the explorer shows it over the notes.</summary>
/// <param name="Figure">The inversion's figures, written after the numeral ("6", "6/4", "6/5", "4/3", "2"); null in root position.</param>
/// <param name="Symbol">The chord symbol with its bass when inverted: "Dm/F", "E7/G♯".</param>
public sealed record ExplorerBar(ExplorerChord Chord, int Inversion, string? Figure, string Symbol);

/// <summary>The progression as it sounds: a bar for each step, the bass held in the left hand, the texture in the right.</summary>
public sealed record ExplorerProgression(MusicalKey Key, IReadOnlyList<ExplorerBar> Bars, ReferenceMusic Music);

/// <summary>
/// The harmonic explorer (version 0.5, Andrei, 2026-11-12: inside the Reference, a tool to try harmony in isolation): a key,
/// its chords, a progression built from them, and a texture to hear it in. Nothing here reads or writes a piece. The bass
/// sits in the left hand around the second octave; the right hand takes the chord's other three tones (all three of a
/// triad), each chord placed as near the one before as it can be, so the hand moves as little as possible.
/// </summary>
public static class HarmonyExplorer
{
    /// <summary>The most bars a progression holds.</summary>
    public const int MaxBars = 8;

    private const int LowestBass = 40;
    private const int LowestRightHand = 55;

    private static readonly string[] s_numerals = ["I", "II", "III", "IV", "V", "VI", "VII"];

    /// <summary>The thirty keys to explore: the fifteen signatures major, then minor, from none through the sharps and the flats.</summary>
    public static IReadOnlyList<MusicalKey> Keys { get; } =
    [
        .. new[] { KeyMode.Major, KeyMode.Minor }.SelectMany(mode =>
            Enumerable.Range(0, 8).Concat(Enumerable.Range(1, 7).Select(flats => -flats)).Select(fifths => new MusicalKey(fifths, mode))),
    ];

    /// <summary>
    /// The chords of a key: in major the triads on each degree with V7 after V; in minor the natural minor's, with the
    /// harmonic minor's V and V7 after v.
    /// </summary>
    public static IReadOnlyList<ExplorerChord> Chords(MusicalKey key)
    {
        bool major = key.Mode == KeyMode.Major;
        IReadOnlyList<SpelledName> scale = KeyReference.ScaleOf(key, major ? ScaleForm.Major : ScaleForm.NaturalMinor);
        IReadOnlyList<SpelledName> harmonic = KeyReference.ScaleOf(key, ScaleForm.HarmonicMinor);
        var chords = new List<ExplorerChord>();
        for (int degree = 0; degree < 7; degree++)
        {
            chords.Add(Triad(scale, degree));
            if (degree == 4)
            {
                if (!major)
                {
                    chords.Add(Triad(harmonic, degree));
                }

                SpelledName root = (major ? scale : harmonic)[4];
                chords.Add(new ExplorerChord("V7", "V7", root, ChordType.Dominant7, $"{root.Letter}{ChordReference.Suffix(ChordType.Dominant7)}"));
            }
        }

        return chords;
    }

    /// <summary>The progression a key starts with: tonic, subdominant, dominant, tonic, each in root position.</summary>
    public static IReadOnlyList<ExplorerStep> Start(MusicalKey key)
    {
        IReadOnlyList<ExplorerChord> chords = Chords(key);
        return [new(chords[0].Id, 0), new(chords[3].Id, 0), new("V", 0), new(chords[0].Id, 0)];
    }

    /// <summary>
    /// The progression in a key as it sounds with a texture and a spacing at a tempo. A step naming no chord of the key, or a
    /// position the chord has not, is left out; at most <see cref="MaxBars"/> bars.
    /// </summary>
    public static ExplorerProgression Play(MusicalKey key, IReadOnlyList<ExplorerStep> steps, ExplorerTexture texture, ChordSpacing spacing, int microsecondsPerQuarter)
    {
        ArgumentNullException.ThrowIfNull(steps);
        Dictionary<string, ExplorerChord> chords = Chords(key).ToDictionary(chord => chord.Id);
        var bars = new List<ExplorerBar>();
        var score = new ReferenceScore(grandStaff: true, fifths: key.Fifths);
        WrittenTone[]? previous = null;
        foreach (ExplorerStep step in steps)
        {
            if (bars.Count == MaxBars || !chords.TryGetValue(step.ChordId, out ExplorerChord? chord) || step.Inversion < 0 || step.Inversion >= chord.Positions)
            {
                continue;
            }

            IReadOnlyList<WrittenTone> position = ChordReference.Close(chord.Root, chord.Type, step.Inversion);
            WrittenTone bass = position[0] with { Midi = LowestBass + ((((position[0].Midi - LowestBass) % 12) + 12) % 12) };
            WrittenTone[] hand = RightHand(chord, position, previous);
            previous = hand;
            WrittenTone[] played = spacing == ChordSpacing.Open ? [.. ReferenceSounding.Spread(hand).Select(tone => tone with { Midi = tone.Midi + 12 })] : hand;
            int bar = bars.Count;
            score.Chord(bar, [bass], staff: 2);
            if (texture == ExplorerTexture.Together)
            {
                score.Chord(bar, played, staff: 1);
            }
            else
            {
                score.Run(bar, 0, Figure(played, texture), ringing: false, staff: 1);
            }

            bars.Add(Bar(chord, step.Inversion, position[0]));
        }

        return new ExplorerProgression(key, bars, score.Build(microsecondsPerQuarter, []));
    }

    private static ExplorerChord Triad(IReadOnlyList<SpelledName> scale, int degree)
    {
        SpelledName root = scale[degree];
        int third = Interval(root, scale[(degree + 2) % 7]);
        int fifth = Interval(root, scale[(degree + 4) % 7]);
        (ChordType type, string numeral) = (third, fifth) switch
        {
            (4, 8) => (ChordType.Augmented, $"{s_numerals[degree]}+"),
            (4, _) => (ChordType.Major, s_numerals[degree]),
            (3, 6) => (ChordType.Diminished, $"{s_numerals[degree].ToLowerInvariant()}°"),
            _ => (ChordType.Minor, s_numerals[degree].ToLowerInvariant()),
        };
        return new ExplorerChord(numeral, numeral, root, type, $"{root.Letter}{ChordReference.Suffix(type)}");
    }

    private static int Interval(SpelledName low, SpelledName high) => (((high.PitchClass - low.PitchClass) % 12) + 12) % 12;

    /// <summary>
    /// The right hand's three tones in close position: a triad's three, or a seventh's without the bass. Of the three ways
    /// to stack them, the one nearest the hand before (the first from the G below middle C up).
    /// </summary>
    private static WrittenTone[] RightHand(ExplorerChord chord, IReadOnlyList<WrittenTone> position, WrittenTone[]? previous)
    {
        WrittenTone[] tones = position.Count > 3 ? [.. position.Skip(1)] : [.. position];
        WrittenTone[][] stacks = [.. Enumerable.Range(0, tones.Length).Select(turn => Stack(tones, turn))];
        if (previous is null)
        {
            return stacks.OrderBy(stack => stack[0].Midi).First();
        }

        return stacks.OrderBy(stack => stack.Select((tone, index) => Math.Abs(tone.Midi - previous[index].Midi)).Sum()).ThenBy(stack => stack[0].Midi).First();
    }

    /// <summary>The tones in close position from the one at <paramref name="turn"/>, its lowest from <see cref="LowestRightHand"/> up.</summary>
    private static WrittenTone[] Stack(WrittenTone[] tones, int turn)
    {
        var stack = new WrittenTone[tones.Length];
        int previous = LowestRightHand - 1;
        for (int index = 0; index < tones.Length; index++)
        {
            WrittenTone tone = tones[(turn + index) % tones.Length];
            int pitch = previous + 1 + ((((tone.Midi - previous - 1) % 12) + 12) % 12);
            stack[index] = tone with { Midi = pitch };
            previous = pitch;
        }

        return stack;
    }

    /// <summary>A bar of quavers over the hand's tones from the lowest, with the chord tone next above the top as the octave.</summary>
    private static WrittenTone[] Figure(WrittenTone[] hand, ExplorerTexture texture)
    {
        WrittenTone top = hand[^1];
        WrittenTone octave = hand.Select(tone => tone with { Midi = tone.Midi + 12 }).Where(tone => tone.Midi > top.Midi).OrderBy(tone => tone.Midi).First();
        WrittenTone[] tones = [.. hand, octave];
        int[] order = texture switch
        {
            ExplorerTexture.Arpeggio => [0, 1, 2, 3, 2, 1, 0, 1],
            ExplorerTexture.Broken => [0, 2, 1, 3, 0, 2, 1, 3],
            _ => [0, 2, 1, 2, 0, 2, 1, 2],
        };
        return [.. order.Select(index => tones[index])];
    }

    private static ExplorerBar Bar(ExplorerChord chord, int inversion, WrittenTone bass)
    {
        bool seventh = ChordReference.FamilyOf(chord.Type) == ChordFamily.Seventh;
        string? figure = inversion == 0 ? null : seventh ? new[] { "6/5", "4/3", "2" }[inversion - 1] : new[] { "6", "6/4" }[inversion - 1];
        string symbol = inversion == 0 ? chord.Symbol : $"{chord.Symbol}/{bass.Name.Letter}";
        return new ExplorerBar(chord, inversion, figure, symbol);
    }
}