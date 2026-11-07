using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>What a chord does in the key: tonic, subdominant or dominant; none for a chord outside these.</summary>
public enum HarmonicFunction
{
    None,
    Tonic,
    Subdominant,
    Dominant,
}

/// <summary>How a passage's harmony ends, read from its last two chords.</summary>
public enum Cadence
{
    /// <summary>One chord throughout: the harmony stands still.</summary>
    Static,

    /// <summary>Dominant to tonic: the passage comes to rest.</summary>
    Authentic,

    /// <summary>Subdominant to tonic: a softer close.</summary>
    Plagal,

    /// <summary>Ends on the dominant: open, asking for an answer.</summary>
    Half,

    /// <summary>Dominant to the sixth step instead of the tonic.</summary>
    Deceptive,

    /// <summary>Ends on the tonic by another way.</summary>
    Home,

    /// <summary>Ends away from the tonic on another chord.</summary>
    Open,
}

/// <summary>
/// The harmonic motion of a selection (testing item 14): its chords as steps of the key, what each does (tonic,
/// subdominant, dominant) and what the motion gives, read from how it ends. A rule over the chords the finder read,
/// so it is inferred, and uncertain where a chord is.
/// </summary>
public static partial class HarmonyAnalyzer
{
    /// <summary>The steps of a progression a fact names, then "…".</summary>
    private const int MaxNamedSteps = 8;

    /// <summary>One chord of a progression, consecutive equal chords joined.</summary>
    private sealed record Step(ChordLabel Chord, TickSpan Span, IReadOnlyList<string> NoteIds, bool Uncertain);

    /// <summary>
    /// The chord's step of the key as a Roman numeral: upper case for a major chord, lower for a minor one, with ° for
    /// diminished, + for augmented and the seventh: "V7", "i", "vii°", "♭VI".
    /// </summary>
    public static string Numeral(ChordLabel chord, MusicalKey key)
    {
        int interval = ((chord.Root - key.TonicPitchClass) % 12 + 12) % 12;
        (int degree, string accidental) = key.Mode == KeyMode.Major
            ? interval switch
            {
                0 => (1, ""),
                1 => (2, "♭"),
                2 => (2, ""),
                3 => (3, "♭"),
                4 => (3, ""),
                5 => (4, ""),
                6 => (4, "♯"),
                7 => (5, ""),
                8 => (6, "♭"),
                9 => (6, ""),
                10 => (7, "♭"),
                _ => (7, ""),
            }
            : interval switch
            {
                0 => (1, ""),
                1 => (2, "♭"),
                2 => (2, ""),
                3 => (3, ""),
                4 => (3, "♯"),
                5 => (4, ""),
                6 => (4, "♯"),
                7 => (5, ""),
                8 => (6, ""),
                9 => (6, "♯"),
                _ => (7, ""),
            };
        string roman = degree switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", _ => "VII" };
        bool upper = chord.Quality is ChordQuality.Major or ChordQuality.Augmented or ChordQuality.Dominant7 or ChordQuality.Major7 or ChordQuality.Suspended4;
        string suffix = chord.Quality switch
        {
            ChordQuality.Diminished => "°",
            ChordQuality.Augmented => "+",
            ChordQuality.Suspended4 => "sus4",
            ChordQuality.Dominant7 => "7",
            ChordQuality.Major7 => "maj7",
            ChordQuality.Minor7 => "7",
            ChordQuality.HalfDiminished7 => "ø7",
            ChordQuality.Diminished7 => "°7",
            _ => "",
        };
        return $"{accidental}{(upper ? roman : roman.ToLowerInvariant())}{suffix}";
    }

    /// <summary>
    /// What a chord does in the key: the tonic on the first step, the subdominant on the fourth and second, the dominant
    /// on the fifth and the seventh (the leading note's diminished chords); none for a chord with a note outside the key's
    /// scale (the minor's raised sixth and seventh count as in it).
    /// </summary>
    public static HarmonicFunction FunctionOf(ChordLabel chord, MusicalKey key)
    {
        if (!IsDiatonic(chord, key))
        {
            return HarmonicFunction.None;
        }

        int interval = ((chord.Root - key.TonicPitchClass) % 12 + 12) % 12;
        return interval switch
        {
            0 => HarmonicFunction.Tonic,
            2 or 5 => HarmonicFunction.Subdominant,
            7 => HarmonicFunction.Dominant,
            11 when chord.Quality is ChordQuality.Diminished or ChordQuality.Diminished7 or ChordQuality.HalfDiminished7 => HarmonicFunction.Dominant,
            _ => HarmonicFunction.None,
        };
    }

    /// <summary>How a progression of chords ends in a key, from its last two different chords.</summary>
    public static Cadence CadenceOf(IReadOnlyList<ChordLabel> progression, MusicalKey key)
    {
        ArgumentNullException.ThrowIfNull(progression);
        ChordLabel[] steps = [.. progression.Where((chord, i) => i == 0 || chord != progression[i - 1])];
        if (steps.Length <= 1)
        {
            return Cadence.Static;
        }

        HarmonicFunction last = FunctionOf(steps[^1], key);
        HarmonicFunction before = FunctionOf(steps[^2], key);
        int lastStep = ((steps[^1].Root - key.TonicPitchClass) % 12 + 12) % 12;
        int sixth = key.Mode == KeyMode.Major ? 9 : 8;
        return (before, last) switch
        {
            (HarmonicFunction.Dominant, HarmonicFunction.Tonic) => Cadence.Authentic,
            (HarmonicFunction.Subdominant, HarmonicFunction.Tonic) => Cadence.Plagal,
            (HarmonicFunction.Dominant, _) when lastStep == sixth && IsDiatonic(steps[^1], key) => Cadence.Deceptive,
            (_, HarmonicFunction.Dominant) => Cadence.Half,
            (_, HarmonicFunction.Tonic) => Cadence.Home,
            _ => Cadence.Open,
        };
    }

    private static bool IsDiatonic(ChordLabel chord, MusicalKey key)
    {
        int[] scale = key.Mode == KeyMode.Major ? [0, 2, 4, 5, 7, 9, 11] : [0, 2, 3, 5, 7, 8, 9, 10, 11];
        return chord.PitchClasses.All(pitchClass => scale.Contains(((pitchClass - key.TonicPitchClass) % 12 + 12) % 12));
    }

    private static string FunctionName(HarmonicFunction function, Words w) => function switch
    {
        HarmonicFunction.Tonic => w.T("tonic", "тоника"),
        HarmonicFunction.Subdominant => w.T("subdominant", "субдоминанта"),
        HarmonicFunction.Dominant => w.T("dominant", "доминанта"),
        _ => "",
    };

    /// <summary>
    /// "Harmonic motion: Am → E → Am, i → V → i (tonic → dominant → tonic). It comes to rest: the dominant resolves to
    /// the tonic, an authentic cadence." The places are the chords, consecutive equal chords joined.
    /// </summary>
    private static MotifFact MotionFact(IReadOnlyList<ChordSpan> chords, MeterMap meter, MusicalKey key, Words w)
    {
        var steps = new List<Step>();
        foreach (ChordSpan chord in chords)
        {
            if (steps.Count > 0 && steps[^1].Chord == chord.Chord)
            {
                Step joined = steps[^1];
                steps[^1] = joined with
                {
                    Span = TickSpan.FromBounds(joined.Span.Onset, chord.Span.End),
                    NoteIds = [.. joined.NoteIds, .. chord.NoteIds],
                    Uncertain = joined.Uncertain || chord.Confidence < ClearConfidence,
                };
                continue;
            }

            steps.Add(new Step(chord.Chord, chord.Span, chord.NoteIds, chord.Confidence < ClearConfidence));
        }

        Cadence cadence = CadenceOf([.. steps.Select(static step => step.Chord)], key);
        string more = steps.Count > MaxNamedSteps ? " → …" : "";
        Step[] named = [.. steps.Take(MaxNamedSteps)];
        string symbols = string.Join(" → ", named.Select(step => step.Chord.Symbol(key))) + more;
        string numerals = string.Join(" → ", named.Select(step => Numeral(step.Chord, key))) + more;
        bool anyFunction = named.Any(step => FunctionOf(step.Chord, key) != HarmonicFunction.None);
        string functions = string.Join(" → ", named.Select(step =>
            FunctionOf(step.Chord, key) is var function and not HarmonicFunction.None ? FunctionName(function, w) : Numeral(step.Chord, key))) + more;

        Step last = steps[^1];
        string lastNumeral = Numeral(last.Chord, key);
        string gives = cadence switch
        {
            Cadence.Static => w.T(
                "The harmony stands still: the line and the rhythm carry the motion.",
                "Гармония стоит на месте: движение несут линия и ритм."),
            Cadence.Authentic => w.T(
                "It comes to rest: the dominant pulls to the tonic and resolves into it, an authentic cadence.",
                "Отрывок приходит к устою: доминанта тяготеет к тонике и разрешается в неё — автентический каданс."),
            Cadence.Plagal => w.T(
                "It closes softly: the subdominant goes to the tonic without the dominant's pull, a plagal cadence.",
                "Отрывок закрывается мягко: субдоминанта переходит в тонику без тяготения доминанты — плагальный каданс."),
            Cadence.Half => w.T(
                "It stops on the dominant and stays open, a half cadence: the music asks for an answer.",
                "Отрывок останавливается на доминанте и остаётся открытым — половинный каданс: музыка ждёт ответа."),
            Cadence.Deceptive => w.T(
                $"The dominant promises the tonic, but {lastNumeral} comes instead, a deceptive cadence: the music goes on.",
                $"Доминанта обещает тонику, но приходит {lastNumeral} — прерванный каданс: музыка продолжается."),
            Cadence.Home => w.T(
                "It returns to the tonic, the key's point of rest.",
                "Гармония возвращается к тонике, устою тональности."),
            _ => w.T(
                $"It ends away from the tonic, on {lastNumeral}: the motion leads on to what follows.",
                $"Гармония заканчивается вне тоники, на {lastNumeral}: движение ведёт дальше."),
        };

        string progression = cadence == Cadence.Static
            ? w.T($"one chord throughout, {symbols} ({numerals})", $"один аккорд на всём протяжении, {symbols} ({numerals})")
            : anyFunction ? $"{symbols}, {numerals} ({functions})" : $"{symbols}, {numerals}";
        string text = w.T($"Harmonic motion: {progression}. {gives}", $"Гармоническое движение: {progression}. {gives}");
        string brief = w.T($"Harmonic motion: {numerals}. ", $"Гармоническое движение: {numerals}. ") + gives;
        string basis = w.T(
            $"Inferred from the chords read from the notes, as steps of {key.NameIn(w)}: the first step is the tonic, the fourth and second the subdominant, the fifth and the leading note's chord the dominant; a chord with a note outside the key has no function. The last two chords name the cadence.",
            $"Предположено по аккордам, прочитанным из нот, как ступеням тональности {key.NameIn(w)}: первая ступень — тоника, четвёртая и вторая — субдоминанта, пятая и аккорд вводного тона — доминанта; у аккорда с нотой вне тональности функции нет. Последние два аккорда называют каданс.");
        return new MotifFact("harmony.motion", FactLens.Harmony, steps.Any(static step => step.Uncertain) ? FactCertainty.Uncertain : FactCertainty.Inferred, text, basis)
        {
            Brief = cadence == Cadence.Static ? null : brief,
            Places = [.. steps.Select(step =>
            {
                string function = FunctionName(FunctionOf(step.Chord, key), w);
                string label = $"{step.Chord.Symbol(key)} ({Numeral(step.Chord, key)}{(function.Length > 0 ? $", {function}" : "")}), {From(step.Span.Onset, meter, w)}";
                return new FactPlace(label, step.Span, step.NoteIds);
            })],
        };
    }
}