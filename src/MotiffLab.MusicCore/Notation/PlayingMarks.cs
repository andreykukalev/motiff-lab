using MotiffLab.MusicCore.Analysis;
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

    /// <summary>For «Для чтения»: how much the bars, three at a time, must grow longer or shorter than usual for "rit." or "accel.": a fifth.</summary>
    private const int ReadingTempoShare = 5;

    /// <summary>For «Как сыграно»: a seventh, as decision record 0016 has it.</summary>
    private const int PlayedTempoShare = 7;

    /// <summary>For «Для чтения»: the fewest steps between two dynamics for the second to be written (p to mf, not mp to mf).</summary>
    private const int ReadingDynamicSteps = 2;

    /// <summary>How many beats around a beat its length is weighed against, and how many times their median it must last for a fermata.</summary>
    private const int FermataNeighbours = 8;

    private const int FermataTimes = 2;

    private PlayingMarks(HashSet<string> accented, Dictionary<int, (long At, string Level)> dynamics, Dictionary<int, string> tempoWords, IReadOnlyList<(long Start, long End, bool Crescendo)> hairpins, IReadOnlyList<long>? fermatas = null)
    {
        Accented = accented;
        Dynamics = dynamics;
        TempoWords = tempoWords;
        Hairpins = hairpins;
        Fermatas = fermatas ?? [];
    }

    /// <summary>Ids of the notes written with an accent.</summary>
    public HashSet<string> Accented { get; }

    /// <summary>By bar index: the onset a dynamic is written at, and the dynamic.</summary>
    public Dictionary<int, (long At, string Level)> Dynamics { get; }

    /// <summary>By bar index: the tempo word written at the bar's start.</summary>
    public Dictionary<int, string> TempoWords { get; }

    /// <summary>Crescendo and diminuendo hairpins, from one onset to another, in order.</summary>
    public IReadOnlyList<(long Start, long End, bool Crescendo)> Hairpins { get; }

    /// <summary>The onsets of the chords held with a fermata, in order (only <see cref="Significant"/> finds them).</summary>
    public IReadOnlyList<long> Fermatas { get; }

    public static PlayingMarks Of(IReadOnlyList<Note> notes, IReadOnlyList<Bar> bars, IReadOnlyList<PlaybackTempo> tempos)
    {
        (Note[] onsets, List<Note>[] inBar, string?[] levels, Func<int, bool, int?> median) = Loudness(notes, bars);
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
                if (median(i, high) is not { } middle)
                {
                    continue;
                }

                foreach (Note onset in inBar[i].Where(onset => High(onset) == high && onset.Velocity >= middle + AccentMargin && 4 * onset.Velocity >= 5 * middle))
                {
                    accented.Add(onset.Id);
                }
            }
        }

        Note[] melody = onsets.Any(High) ? [.. onsets.Where(High)] : onsets;
        return new PlayingMarks(accented, dynamics, WordsFor(bars, tempos, PlayedTempoShare), HairpinsFor(melody));
    }

    /// <summary>
    /// «Для чтения» (version 0.5, <c>experimental.readingMarks</c>): only what a player reading the score needs of how it
    /// was played, picked from what <see cref="Of"/> works out. The first dynamic, then a dynamic only where the level moves
    /// two steps or more (p to mf, not mp to mf) and holds for the next bar too; "rit." and "accel." where the bars, three at
    /// a time, grow a fifth longer or shorter than usual, and "a tempo" once back; and a fermata over a chord held through a
    /// beat that lasts at least twice the median of the eight beats around it with no new onset within it. No accents,
    /// hairpins, slurs or pedal: those stay in «Как сыграно». All of it is inferred. Pure and deterministic.
    /// </summary>
    public static PlayingMarks Significant(IReadOnlyList<Note> notes, IReadOnlyList<Bar> bars, IReadOnlyList<PlaybackTempo> tempos)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(tempos);
        (_, List<Note>[] inBar, string?[] levels, _) = Loudness(notes, bars);
        var dynamics = new Dictionary<int, (long, string)>();
        int? written = null;
        for (int i = 0; i < bars.Count; i++)
        {
            if (levels[i] is not { } level)
            {
                continue;
            }

            int step = Array.FindIndex(s_levels, entry => entry.Level == level);
            if (written is null || (Math.Abs(step - written.Value) >= ReadingDynamicSteps && i + 1 < bars.Count && levels[i + 1] == level))
            {
                dynamics[i] = (inBar[i][0].Span.Onset.Value, level);
                written = step;
            }
        }

        return new PlayingMarks(new HashSet<string>(StringComparer.Ordinal), dynamics, WordsFor(bars, tempos, ReadingTempoShare), [], FermatasOf(notes, bars, tempos));
    }

    private static bool High(Note note) => note.Pitch.MidiNumber >= MiddleC;

    /// <summary>Each onset by its loudest note, the onsets of each bar, each bar's level and the median loudness by bar and register.</summary>
    private static (Note[] Onsets, List<Note>[] InBar, string?[] Levels, Func<int, bool, int?> Median) Loudness(IReadOnlyList<Note> notes, IReadOnlyList<Bar> bars)
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

        return (onsets, inBar, levels, Median);
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
    /// "rit." where the bars, taken three at a time, grow a <paramref name="share"/>th longer than usual for the piece
    /// (a seventh as played, a fifth for reading), "accel." where they grow that much shorter, and "a tempo" once they are
    /// back within a fourteenth of usual.
    /// </summary>
    private static Dictionary<int, string> WordsFor(IReadOnlyList<Bar> bars, IReadOnlyList<PlaybackTempo> tempos, int share)
    {
        var words = new Dictionary<int, string>();
        if (bars.Count < 3 || tempos.Count < 2)
        {
            return words;
        }

        // Microseconds a quarter, times 480, for each bar: exact integers.
        PlaybackTempo[] ordered = [.. tempos.OrderBy(tempo => tempo.At)];
        long UnitsAt(long tick) => TempoProfile.Units(tick, ordered);

        long[] perTick = [.. bars.Select(bar => (UnitsAt(bar.Span.End.Value) - UnitsAt(bar.Span.Onset.Value)) / Math.Max(1, bar.Span.Duration))];
        long[] smooth = [.. perTick.Select((_, i) => perTick[Math.Max(0, i - 1)..Math.Min(perTick.Length, i + 2)].Sum() / (Math.Min(perTick.Length, i + 2) - Math.Max(0, i - 1)))];
        long usual = perTick.Order().ElementAt(perTick.Length / 2);
        int state = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            int now = share * smooth[i] > (share + 1) * usual ? 1 : share * smooth[i] < (share - 1) * usual ? -1 : 0;
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

    /// <summary>
    /// The chords held with a fermata: a beat (the signature's own unit) that lasts at least <see cref="FermataTimes"/> times
    /// the median of the <see cref="FermataNeighbours"/> beats around it, with no onset after its start and before its end,
    /// over a chord still sounding at its start; the fermata goes on that chord's onset, once however many beats it holds.
    /// None without a tempo that follows the playing.
    /// </summary>
    private static List<long> FermatasOf(IReadOnlyList<Note> notes, IReadOnlyList<Bar> bars, IReadOnlyList<PlaybackTempo> tempos)
    {
        var fermatas = new List<long>();
        if (tempos.Count < 2)
        {
            return fermatas;
        }

        PlaybackTempo[] ordered = [.. tempos.OrderBy(tempo => tempo.At)];
        var beats = new List<(long Start, long End)>();
        foreach (Bar bar in bars)
        {
            long unit = 4L * CanonicalTime.TicksPerQuarter / bar.Signature.Denominator;
            for (long at = bar.Span.Onset.Value; at < bar.Span.End.Value; at += unit)
            {
                beats.Add((at, Math.Min(at + unit, bar.Span.End.Value)));
            }
        }

        long[] lengths = [.. beats.Select(beat => TempoProfile.Units(beat.End, ordered) - TempoProfile.Units(beat.Start, ordered))];
        long[] onsets = [.. notes.Select(note => note.Span.Onset.Value).Distinct().Order()];
        for (int i = 0; i < beats.Count; i++)
        {
            int from = Math.Max(0, i - (FermataNeighbours / 2)), to = Math.Min(beats.Count, i + (FermataNeighbours / 2) + 1);
            long[] around = [.. Enumerable.Range(from, to - from).Where(k => k != i).Select(k => lengths[k]).Order()];
            (long start, long end) = beats[i];
            if (around.Length < FermataNeighbours / 2 || lengths[i] < FermataTimes * around[around.Length / 2])
            {
                continue;
            }

            int next = Array.BinarySearch(onsets, start + 1);
            next = next < 0 ? ~next : next;
            bool struck = next < onsets.Length && onsets[next] < end;
            long? held = notes.Where(note => note.Span.Onset.Value <= start && note.Span.End.Value > start).Select(note => (long?)note.Span.Onset.Value).Max();
            if (!struck && held is { } chord && (fermatas.Count == 0 || fermatas[^1] != chord))
            {
                fermatas.Add(chord);
            }
        }

        return fermatas;
    }
}