using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// Marks that say how notes heard in a recording were played (decision record 0016): dynamics from
/// how loud each bar is, accents on notes played clearly louder than those around them, and tempo
/// words where the playing slows down or speeds up. All of it is inferred from velocities and the
/// tempo that follows the playing, and is written only on request; and hairpins where the melody
/// grows steadily louder or softer. Pure and deterministic.
/// </summary>
internal sealed class PlayingMarks
{
    /// <summary>The loudest velocity below each level's upper bound gets that level.</summary>
    private static readonly (int Below, string Level)[] s_levels =
        [(36, "pp"), (52, "p"), (68, "mp"), (84, "mf"), (100, "f"), (128, "ff")];

    private const int MiddleC = 60;

    /// <summary>The fewest onsets a bar needs for its level to count.</summary>
    private const int FewestOnsets = 3;

    /// <summary>How much louder than the bars around it a note must be to be accented, in velocity and as a share.</summary>
    private const int AccentMargin = 16;

    /// <summary>How much the melody's loudness, averaged over five onsets, must rise or fall for a hairpin.</summary>
    private const int HairpinChange = 12;

    /// <summary>The fewest onsets a hairpin spans, and the shortest it is: a half note.</summary>
    private const int HairpinOnsets = 4;

    private const long HairpinTicks = 2 * CanonicalTime.TicksPerQuarter;

    private PlayingMarks(HashSet<string> accented, Dictionary<int, (long At, string Level)> dynamics, Dictionary<int, string> tempoWords, IReadOnlyList<(long Start, long End, bool Crescendo)> hairpins)
    {
        Accented = accented;
        Dynamics = dynamics;
        TempoWords = tempoWords;
        Hairpins = hairpins;
    }

    /// <summary>Ids of the notes written with an accent.</summary>
    public HashSet<string> Accented { get; }

    /// <summary>By bar index: the onset a dynamic is written at, and the dynamic.</summary>
    public Dictionary<int, (long At, string Level)> Dynamics { get; }

    /// <summary>By bar index: the tempo word written at the bar's start.</summary>
    public Dictionary<int, string> TempoWords { get; }

    /// <summary>Crescendo and diminuendo hairpins, from one onset to another, in order.</summary>
    public IReadOnlyList<(long Start, long End, bool Crescendo)> Hairpins { get; }

    public static PlayingMarks Of(IReadOnlyList<Note> notes, IReadOnlyList<Bar> bars, IReadOnlyList<PlaybackTempo> tempos)
    {
        // Each onset by its loudest note, which is the one heard.
        var onsets = notes
            .GroupBy(note => note.Span.Onset.Value)
            .Select(group => group.OrderByDescending(note => note.Velocity).ThenBy(note => note.Id, StringComparer.Ordinal).First())
            .OrderBy(note => note.Span.Onset)
            .ToArray();
        var inBar = new List<Note>[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            inBar[i] = [];
        }

        int bar = 0;
        foreach (Note onset in onsets)
        {
            while (bar + 1 < bars.Count && onset.Span.Onset >= bars[bar + 1].Span.Onset)
            {
                bar++;
            }

            inBar[bar].Add(onset);
        }

        // A bar's loudness: the median of the onsets in it and the bars either side, so that one loud
        // chord does not change the dynamic. The melody, at or above middle C, is what is heard, so the
        // dynamic follows it, and a note is accented against its own register.
        static bool High(Note note) => note.Pitch.MidiNumber >= MiddleC;
        int? Median(int i, bool high)
        {
            int[] velocities = [.. inBar[Math.Max(0, i - 1)..Math.Min(bars.Count, i + 2)].SelectMany(list => list).Where(note => High(note) == high).Select(note => note.Velocity).Order()];
            return velocities.Length == 0 ? null : velocities[velocities.Length / 2];
        }

        string?[] levels = new string?[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            levels[i] = inBar[i].Count >= FewestOnsets && (Median(i, high: true) ?? Median(i, high: false)) is { } loudness
                ? s_levels.First(entry => loudness < entry.Below).Level
                : null;
        }

        var dynamics = new Dictionary<int, (long, string)>();
        var accented = new HashSet<string>(StringComparer.Ordinal);
        string? written = null;
        for (int i = 0; i < bars.Count; i++)
        {
            // A new dynamic when the level changes and holds for the next bar too.
            if (levels[i] is { } level && level != written && (i + 1 == bars.Count || levels[i + 1] is null || levels[i + 1] == level))
            {
                dynamics[i] = (inBar[i][0].Span.Onset.Value, level);
                written = level;
            }

            foreach (bool high in new[] { true, false })
            {
                if (Median(i, high) is not { } median)
                {
                    continue;
                }

                foreach (Note onset in inBar[i].Where(onset => High(onset) == high && onset.Velocity >= median + AccentMargin && 4 * onset.Velocity >= 5 * median))
                {
                    accented.Add(onset.Id);
                }
            }
        }

        Note[] melody = onsets.Any(High) ? [.. onsets.Where(High)] : onsets;
        return new PlayingMarks(accented, dynamics, WordsFor(bars, tempos), HairpinsFor(melody));
    }

    /// <summary>
    /// The melody's loudness averaged over five onsets, split into legs that each rise or fall by at
    /// least <see cref="HairpinChange"/> (a turn counts once the loudness has come back that much from
    /// its highest or lowest); a leg over enough onsets and time is a hairpin.
    /// </summary>
    private static List<(long Start, long End, bool Crescendo)> HairpinsFor(Note[] melody)
    {
        var hairpins = new List<(long, long, bool)>();
        int count = melody.Length;
        if (count < HairpinOnsets)
        {
            return hairpins;
        }

        int[] loudness = new int[count];
        for (int i = 0; i < count; i++)
        {
            int from = Math.Max(0, i - 2), to = Math.Min(count, i + 3);
            loudness[i] = melody[from..to].Sum(note => note.Velocity) / (to - from);
        }

        void Leg(int start, int end)
        {
            if (end - start + 1 >= HairpinOnsets && melody[end].Span.Onset.Value - melody[start].Span.Onset.Value >= HairpinTicks)
            {
                hairpins.Add((melody[start].Span.Onset.Value, melody[end].Span.Onset.Value, loudness[end] > loudness[start]));
            }
        }

        // Before the first leg: the quietest and loudest so far, either of which may start it.
        int low = 0, high = 0, start = 0, extreme = 0, direction = 0;
        for (int i = 1; i < count; i++)
        {
            if (direction == 0)
            {
                if (loudness[i] - loudness[low] >= HairpinChange)
                {
                    (start, extreme, direction) = (low, i, 1);
                }
                else if (loudness[high] - loudness[i] >= HairpinChange)
                {
                    (start, extreme, direction) = (high, i, -1);
                }
                else
                {
                    low = loudness[i] < loudness[low] ? i : low;
                    high = loudness[i] > loudness[high] ? i : high;
                }
            }
            else if (direction * (loudness[i] - loudness[extreme]) >= 0)
            {
                extreme = i;
            }
            else if (direction * (loudness[extreme] - loudness[i]) >= HairpinChange)
            {
                Leg(start, extreme);
                (start, extreme, direction) = (extreme, i, -direction);
            }
        }

        if (direction != 0 && Math.Abs(loudness[extreme] - loudness[start]) >= HairpinChange)
        {
            Leg(start, extreme);
        }

        return hairpins;
    }

    /// <summary>
    /// "rit." where the bars, taken three at a time, grow a seventh longer than usual for the piece,
    /// "accel." where they grow that much shorter, and "a tempo" once they are back within a
    /// fourteenth of usual.
    /// </summary>
    private static Dictionary<int, string> WordsFor(IReadOnlyList<Bar> bars, IReadOnlyList<PlaybackTempo> tempos)
    {
        var words = new Dictionary<int, string>();
        if (bars.Count < 3 || tempos.Count < 2)
        {
            return words;
        }

        // Microseconds a quarter, times 480, for each bar: exact integers.
        PlaybackTempo[] ordered = [.. tempos.OrderBy(tempo => tempo.At)];
        long UnitsAt(long tick)
        {
            long units = 0, at = 0, perQuarter = PlaybackTempo.DefaultMicrosecondsPerQuarter;
            foreach (PlaybackTempo tempo in ordered)
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

        long[] perTick = [.. bars.Select(bar => (UnitsAt(bar.Span.End.Value) - UnitsAt(bar.Span.Onset.Value)) / Math.Max(1, bar.Span.Duration))];
        long[] smooth = [.. perTick.Select((_, i) => perTick[Math.Max(0, i - 1)..Math.Min(perTick.Length, i + 2)].Sum() / (Math.Min(perTick.Length, i + 2) - Math.Max(0, i - 1)))];
        long usual = perTick.Order().ElementAt(perTick.Length / 2);
        int state = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            int now = 7 * smooth[i] > 8 * usual ? 1 : 7 * smooth[i] < 6 * usual ? -1 : 0;
            bool back = 14 * Math.Abs(smooth[i] - usual) <= usual;
            if (state == 0 && now != 0)
            {
                words[i] = now > 0 ? "rit." : "accel.";
                state = now;
            }
            else if (state != 0 && back)
            {
                words[i] = "a tempo";
                state = 0;
            }
        }

        return words;
    }
}