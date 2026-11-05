using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Performance;

/// <summary>A note of a performance: what a recording's transcription or a performance MIDI file played, at its time in the recording.</summary>
/// <param name="Id">The note's id in the performance, stable within it.</param>
/// <param name="Onset">When the key was struck, in microseconds from the start of the recording.</param>
/// <param name="Velocity">MIDI velocity, 1–127.</param>
public sealed record PlayedNote(string Id, Pitch Pitch, long Onset, int Velocity);

/// <summary>A point where the score and the performance agree: this score position sounds at this time of the recording.</summary>
public readonly record struct Anchor(Tick Score, long Microseconds);

/// <summary>
/// How a performance lines up with the score (decision record 0022, section 2): anchors where they
/// agree, and between them a monotone map from score ticks to recording microseconds and back.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Align"/> groups the notes that start together on each side, aligns the two sequences of
/// groups with gaps (Needleman–Wunsch), scoring exact pitches and pitch classes in common, and keeps the
/// matched groups that share at least half of their notes as anchors. Everything is in integers, so the
/// same notes always give the same anchors.
/// </para>
/// <para>
/// Anchors are kept with the performance in the project, so a later change to the alignment never moves
/// what the player already took into the score; <see cref="FromAnchors"/> rebuilds the map from them.
/// </para>
/// </remarks>
public sealed class PerformanceAlignment
{
    /// <summary>Played notes that start within this many microseconds of a group's first note join the group: 70 ms.</summary>
    public const long GroupWindowMicroseconds = 70_000;

    /// <summary>The cost of leaving a group of either side unmatched, against 1000 for groups with nothing in common.</summary>
    public const int GapCost = 350;

    /// <summary>The least similarity, in thousandths, at which matched groups become an anchor.</summary>
    public const int AnchorSimilarity = 500;

    /// <summary>The tempo assumed outside the anchors when there is only one: a quarter at 120 a minute.</summary>
    public const long DefaultMicrosecondsPerQuarter = 500_000;

    private readonly Anchor[] _anchors;

    private PerformanceAlignment(Anchor[] anchors) => _anchors = anchors;

    /// <summary>The anchors in order; both their ticks and their times strictly increase.</summary>
    public IReadOnlyList<Anchor> Anchors => _anchors;

    /// <summary>Aligns a performance with the score's notes.</summary>
    public static PerformanceAlignment Align(IReadOnlyList<Note> score, IReadOnlyList<PlayedNote> played)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(played);

        List<(long At, int[] Pitches)> printed = ScoreGroups(score);
        List<(long At, int[] Pitches)> heard = PlayedGroups(played);
        List<Anchor> anchors = [];
        foreach ((int i, int j) in AlignGroups(printed, heard))
        {
            if (Similarity(printed[i].Pitches, heard[j].Pitches) < AnchorSimilarity)
            {
                continue;
            }

            Anchor anchor = new(new Tick(printed[i].At), heard[j].At);
            if (anchors.Count == 0 || (anchor.Score > anchors[^1].Score && anchor.Microseconds > anchors[^1].Microseconds))
            {
                anchors.Add(anchor);
            }
        }

        return new PerformanceAlignment([.. anchors]);
    }

    /// <summary>The alignment from anchors kept in a project. They must strictly increase in both ticks and time.</summary>
    public static PerformanceAlignment FromAnchors(IEnumerable<Anchor> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        Anchor[] list = [.. anchors];
        for (int i = 1; i < list.Length; i++)
        {
            if (list[i].Score <= list[i - 1].Score || list[i].Microseconds <= list[i - 1].Microseconds)
            {
                throw new ArgumentException("Anchors must strictly increase in both score position and time.", nameof(anchors));
            }
        }

        return new PerformanceAlignment(list);
    }

    /// <summary>When a score position sounds in the recording, in microseconds. Outside the anchors the nearest stretch's tempo continues.</summary>
    public long MicrosecondsAt(Tick position)
    {
        if (_anchors.Length == 0)
        {
            return Scale(position.Value, DefaultMicrosecondsPerQuarter, CanonicalTime.TicksPerQuarter);
        }

        (Anchor a, Anchor b) = Stretch(position.Value, anchor => anchor.Score.Value);
        if (a == b)
        {
            return a.Microseconds + Scale(position.Value - a.Score.Value, DefaultMicrosecondsPerQuarter, CanonicalTime.TicksPerQuarter);
        }

        return a.Microseconds + Scale(position.Value - a.Score.Value, b.Microseconds - a.Microseconds, b.Score.Value - a.Score.Value);
    }

    /// <summary>The score position that sounds at a time of the recording. Outside the anchors the nearest stretch's tempo continues.</summary>
    public Tick TickAt(long microseconds)
    {
        if (_anchors.Length == 0)
        {
            return new Tick(Math.Max(0, Scale(microseconds, CanonicalTime.TicksPerQuarter, DefaultMicrosecondsPerQuarter)));
        }

        (Anchor a, Anchor b) = Stretch(microseconds, anchor => anchor.Microseconds);
        long ticks = a == b
            ? a.Score.Value + Scale(microseconds - a.Microseconds, CanonicalTime.TicksPerQuarter, DefaultMicrosecondsPerQuarter)
            : a.Score.Value + Scale(microseconds - a.Microseconds, b.Score.Value - a.Score.Value, b.Microseconds - a.Microseconds);
        return new Tick(Math.Max(0, ticks));
    }

    /// <summary>Similarity of two groups of pitches in thousandths: exact pitches count twice, shared pitch classes once.</summary>
    internal static int Similarity(int[] a, int[] b)
    {
        int exact = a.Count(b.Contains);
        int classes = a.Select(p => p % 12).Distinct().Count(c => b.Any(p => p % 12 == c));
        int most = Math.Max(a.Length, b.Length);
        return most == 0 ? 0 : (int)(((2L * exact) + classes) * 1000 / (3L * most));
    }

    /// <summary>The anchors around a value: the two that hold it, or the nearest two at either end, or the one anchor twice.</summary>
    private (Anchor A, Anchor B) Stretch(long value, Func<Anchor, long> key)
    {
        if (_anchors.Length == 1)
        {
            return (_anchors[0], _anchors[0]);
        }

        int lo = 0, hi = _anchors.Length - 1;
        if (value <= key(_anchors[0]))
        {
            return (_anchors[0], _anchors[1]);
        }

        if (value >= key(_anchors[hi]))
        {
            return (_anchors[hi - 1], _anchors[hi]);
        }

        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (key(_anchors[mid]) <= value)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return (_anchors[lo], _anchors[hi]);
    }

    /// <summary><paramref name="value"/> × <paramref name="numerator"/> / <paramref name="denominator"/>, rounded half away from zero.</summary>
    private static long Scale(long value, long numerator, long denominator)
    {
        Int128 product = (Int128)value * numerator;
        Int128 half = denominator / 2;
        Int128 result = product >= 0 ? (product + half) / denominator : -((-product + half) / denominator);
        return (long)result;
    }

    private static List<(long At, int[] Pitches)> ScoreGroups(IReadOnlyList<Note> score) =>
        [.. score
            .GroupBy(note => note.Span.Onset.Value)
            .OrderBy(group => group.Key)
            .Select(group => (group.Key, group.Select(note => note.Pitch.MidiNumber).Distinct().Order().ToArray()))];

    private static List<(long At, int[] Pitches)> PlayedGroups(IReadOnlyList<PlayedNote> played)
    {
        List<(long At, List<int> Pitches)> groups = [];
        foreach (PlayedNote note in played.OrderBy(note => note.Onset).ThenBy(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal))
        {
            if (groups.Count > 0 && note.Onset - groups[^1].At <= GroupWindowMicroseconds)
            {
                if (!groups[^1].Pitches.Contains(note.Pitch.MidiNumber))
                {
                    groups[^1].Pitches.Add(note.Pitch.MidiNumber);
                }
            }
            else
            {
                groups.Add((note.Onset, [note.Pitch.MidiNumber]));
            }
        }

        return [.. groups.Select(group => (group.At, group.Pitches.Order().ToArray()))];
    }

    /// <summary>
    /// The matched pairs of a global alignment with gaps, in order. Ties prefer leaving a played group out, then
    /// a match: traced back from the end, the score then sits as early in the performance as it fits, so a scanned
    /// first page lines up with the start of a recording of the whole piece, not with a later repeat of its music.
    /// </summary>
    private static List<(int I, int J)> AlignGroups(List<(long At, int[] Pitches)> printed, List<(long At, int[] Pitches)> heard)
    {
        int n = printed.Count, m = heard.Count;
        byte[] path = new byte[(n + 1) * (m + 1)];
        long[] previous = new long[m + 1];
        long[] current = new long[m + 1];
        for (int j = 1; j <= m; j++)
        {
            previous[j] = (long)j * GapCost;
            path[j] = 2;
        }

        for (int i = 1; i <= n; i++)
        {
            current[0] = (long)i * GapCost;
            path[i * (m + 1)] = 1;
            int[] a = printed[i - 1].Pitches;
            for (int j = 1; j <= m; j++)
            {
                long match = previous[j - 1] + 1000 - Similarity(a, heard[j - 1].Pitches);
                long skipPrinted = previous[j] + GapCost;
                long skipHeard = current[j - 1] + GapCost;
                byte step = 0;
                long best = match;
                if (skipPrinted < best)
                {
                    best = skipPrinted;
                    step = 1;
                }

                if (skipHeard <= best)
                {
                    best = skipHeard;
                    step = 2;
                }

                current[j] = best;
                path[(i * (m + 1)) + j] = step;
            }

            (previous, current) = (current, previous);
        }

        List<(int, int)> pairs = [];
        int x = n, y = m;
        while (x > 0 || y > 0)
        {
            switch (path[(x * (m + 1)) + y])
            {
                case 0:
                    pairs.Add((x - 1, y - 1));
                    x--;
                    y--;
                    break;
                case 1:
                    x--;
                    break;
                default:
                    y--;
                    break;
            }
        }

        pairs.Reverse();
        return pairs;
    }
}