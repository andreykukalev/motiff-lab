using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>One bar's tempo: how many tenths of a quarter note a minute it was played or written at.</summary>
public readonly record struct BarTempo(int Number, TickSpan Span, int Tenths);

/// <summary>A stretch of at least three bars that goes slower or faster than usual, with its mean tempo in tenths.</summary>
public sealed record TempoChange(int FirstBar, int LastBar, TickSpan Span, bool Slower, int Tenths);

/// <summary>
/// The tempo of a piece bar by bar (version 0.5, «Темп живого исполнения»): computed from the tempo map and the bars, never
/// stored. The usual tempo is the median bar, the range the 10th to 90th percentile, and a change a stretch of at least
/// three bars where three bars together are a seventh slower or faster than usual, the rule "Как сыграно" writes rit. and
/// accel. by (decision record 0016). A recording's profile is inferred, as its beat is; a score's is read from its file.
/// Everything is integer arithmetic: the same bars and tempos give the same profile.
/// </summary>
public sealed record TempoProfile(IReadOnlyList<BarTempo> Bars, int Usual, int Low, int High, IReadOnlyList<TempoChange> Changes, bool Inferred)
{
    private const int ShortestChange = 3;

    /// <summary>The profile of the bars of <paramref name="meter"/> that start within <paramref name="within"/> (all before <paramref name="end"/> when null); null without a bar.</summary>
    /// <param name="tempos">The tempo changes in order, as <see cref="PlaybackScore.Tempos"/> gives them; empty means 120 BPM throughout.</param>
    public static TempoProfile? Of(MeterMap meter, IReadOnlyList<PlaybackTempo> tempos, Tick end, bool inferred, TickSpan? within = null)
    {
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(tempos);
        PlaybackTempo[] ordered = [.. tempos.OrderBy(tempo => tempo.At)];
        BarTempo[] bars = [.. meter.BarsBefore(end)
            .Where(bar => bar.Span.Duration > 0 && (within is not { } span || (bar.Span.Onset >= span.Onset && bar.Span.Onset < span.End)))
            .Select(bar => new BarTempo(bar.Number, bar.Span, Tenths(bar.Span, ordered)))];
        if (bars.Length == 0)
        {
            return null;
        }

        int[] sorted = [.. bars.Select(bar => bar.Tenths).Order()];
        int usual = sorted[sorted.Length / 2];
        return new TempoProfile(bars, usual, sorted[(sorted.Length - 1) / 10], sorted[(sorted.Length - 1) * 9 / 10], ChangesOf(bars, usual), inferred);
    }

    /// <summary>Whether the tempo changes at all: some bar is played or written at another tempo.</summary>
    public bool Varies => Low != High;

    /// <summary>The facts the Insights stream and the assistant cite: <c>piece.tempo.profile</c>, then <c>piece.tempo.change.N</c> from 1.</summary>
    public IReadOnlyList<MotifFact> Facts(Words words)
    {
        FactCertainty certainty = Inferred ? FactCertainty.Inferred : FactCertainty.Detected;
        string basis = Inferred
            ? words.T(
                "Worked out from the beat found in the recording, bar by bar, in quarter notes per minute: the usual tempo is the median bar, the range the 10th to 90th percentile.",
                "Выведено из доли, найденной в записи, по тактам, в четвертях в минуту: обычный темп — медианный такт, диапазон — от 10-го до 90-го процентиля.")
            : words.T(
                "Read from the file's tempo map, bar by bar, in quarter notes per minute: the usual tempo is the median bar, the range the 10th to 90th percentile.",
                "Прочитано по темповой карте файла, по тактам, в четвертях в минуту: обычный темп — медианный такт, диапазон — от 10-го до 90-го процентиля.");
        string usual = Bpm(Usual, words);
        string text = Varies
            ? words.T(
                $"Tempo: usually a quarter at {usual} BPM, from {Bpm(Low, words)} to {Bpm(High, words)} BPM; {Counted(words)}.",
                $"Темп: обычно четверть {usual} BPM, от {Bpm(Low, words)} до {Bpm(High, words)} BPM; {Counted(words)}.")
            : words.T($"Tempo: a quarter at {usual} BPM throughout.", $"Темп: четверть {usual} BPM на всём протяжении.");
        var facts = new List<MotifFact>
        {
            new("piece.tempo.profile", FactLens.Character, certainty, text, basis)
            {
                Places = [.. Changes.Select(change => new FactPlace(Place(change, words), change.Span, []))],
            },
        };
        for (int i = 0; i < Changes.Count; i++)
        {
            TempoChange change = Changes[i];
            string at = Bpm(change.Tenths, words);
            string said = change.Slower
                ? words.T($"Slower in {BarsOf(change, words)}: a quarter at about {at} BPM.", $"Медленнее в {BarsOf(change, words)}: четверть около {at} BPM.")
                : words.T($"Faster in {BarsOf(change, words)}: a quarter at about {at} BPM.", $"Быстрее в {BarsOf(change, words)}: четверть около {at} BPM.");
            facts.Add(new MotifFact($"piece.tempo.change.{i + 1}", FactLens.Character, certainty, said, basis)
            {
                Places = [new FactPlace(Place(change, words), change.Span, [])],
            });
        }

        return facts;
    }

    /// <summary>A tempo in tenths written as quarter notes a minute: "72.5".</summary>
    public static string Bpm(int tenths, Words words) => words.Decimal(tenths / 10m);

    /// <summary>"bars 12–15" or "тактах 12–15".</summary>
    public static string BarsOf(TempoChange change, Words words) => words.T($"bars {change.FirstBar}–{change.LastBar}", $"тактах {change.FirstBar}–{change.LastBar}");

    private static string Place(TempoChange change, Words words) => change.Slower
        ? words.T($"Slower, bars {change.FirstBar}–{change.LastBar}", $"Медленнее, такты {change.FirstBar}–{change.LastBar}")
        : words.T($"Faster, bars {change.FirstBar}–{change.LastBar}", $"Быстрее, такты {change.FirstBar}–{change.LastBar}");

    private string Counted(Words words)
    {
        int slower = Changes.Count(change => change.Slower), faster = Changes.Count - slower;
        string Stretches(int count) => words.Count(count, "stretch", "stretches", "отрезке", "отрезках", "отрезках");
        var parts = new List<string>();
        if (slower > 0)
        {
            parts.Add(words.T($"slower in {Stretches(slower)}", $"медленнее в {Stretches(slower)}"));
        }

        if (faster > 0)
        {
            parts.Add(words.T($"faster in {Stretches(faster)}", $"быстрее в {Stretches(faster)}"));
        }

        return parts.Count == 0 ? words.T("no stretch is markedly slower or faster", "заметно медленнее или быстрее нигде") : words.Joined(parts);
    }

    /// <summary>A bar's tempo in tenths of a quarter a minute: its ticks over its microseconds (times 480), rounded.</summary>
    private static int Tenths(TickSpan span, PlaybackTempo[] tempos)
    {
        long units = Units(span.End.Value, tempos) - Units(span.Onset.Value, tempos);
        return (int)(((span.Duration * 600_000_000L) + (units / 2)) / Math.Max(1, units));
    }

    /// <summary>Microseconds from the start to <paramref name="tick"/>, times 480, by tempo changes in order: exact integers.</summary>
    internal static long Units(long tick, PlaybackTempo[] tempos)
    {
        long units = 0, at = 0, perQuarter = PlaybackTempo.DefaultMicrosecondsPerQuarter;
        foreach (PlaybackTempo tempo in tempos)
        {
            if (tempo.At.Value >= tick)
            {
                break;
            }

            units += (tempo.At.Value - at) * perQuarter;
            at = tempo.At.Value;
            perQuarter = tempo.MicrosecondsPerQuarter;
        }

        return units + ((tick - at) * perQuarter);
    }

    /// <summary>The stretches of three bars or more where each bar's three-bar mean is a seventh slower or faster than usual.</summary>
    private static List<TempoChange> ChangesOf(BarTempo[] bars, int usual)
    {
        // A bar's length in time goes with 1 / tempo: slower by a seventh is 7 × length > 8 × usual length.
        int[] smooth = [.. bars.Select((_, i) =>
        {
            int from = Math.Max(0, i - 1), to = Math.Min(bars.Length, i + 2);
            return (int)(bars[from..to].Sum(bar => (long)bar.Tenths) / (to - from));
        })];
        var changes = new List<TempoChange>();
        int start = 0;
        for (int i = 1; i <= bars.Length; i++)
        {
            if (i < bars.Length && State(smooth[i], usual) == State(smooth[start], usual))
            {
                continue;
            }

            int state = State(smooth[start], usual);
            if (state != 0 && i - start >= ShortestChange)
            {
                BarTempo[] run = bars[start..i];
                changes.Add(new TempoChange(run[0].Number, run[^1].Number, TickSpan.FromBounds(run[0].Span.Onset, run[^1].Span.End), state > 0, (int)(run.Sum(bar => (long)bar.Tenths) / run.Length)));
            }

            start = i;
        }

        return changes;
    }

    /// <summary>1 when a bar at <paramref name="tenths"/> is a seventh slower than usual, -1 when that much faster, else 0.</summary>
    private static int State(int tenths, int usual) => 8 * (long)tenths < 7 * (long)usual ? 1 : 6 * (long)tenths > 7 * (long)usual ? -1 : 0;
}