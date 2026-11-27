using MotiffLab.MusicCore.Events;
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
internal enum Cadence
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
internal static partial class HarmonyAnalyzer
{
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

    /// <summary>The chords as steps of a progression: consecutive equal chords joined, uncertain when any of them is.</summary>
    private static List<Step> Steps(IReadOnlyList<ChordSpan> chords)
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

        return steps;
    }
}