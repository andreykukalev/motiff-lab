using System.Globalization;

using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Scan;

public static partial class ScanRepair
{
    /// <summary>The most times the shared notehead is looked for on one staff of a bar.</summary>
    private const int MostSharedHeads = 16;

    /// <summary>
    /// The rules one engine's reading of a page gets before the merge (ADR 0035, section 5; <see cref="ScanMerge"/>), beside
    /// those every scan gets: a whole rest alone on its staff lasts the bar, and two voices sharing a notehead the engine wrote
    /// one after the other start together. Each changes a bar only where the bar then adds up, from the bar alone.
    /// </summary>
    /// <param name="PrintedMeters">
    /// The engine reads the printed time signatures (all but homr, which writes the median of its bars). When such a
    /// reading changes meter, each bar's length is its printed meter and no signature is rewritten from the bars; without
    /// a change, its bars settle their length as any scan's do.
    /// </param>
    internal sealed record ReadingRules(bool PrintedMeters);

    /// <summary>One engine's reading repaired by the rules for a reading (<see cref="ReadingRules"/>); the same array when nothing changes.</summary>
    internal static byte[] RepairReading(byte[] score, bool printedMeters) => Repair(score, new ReadingRules(printedMeters));

    /// <summary>
    /// Whether the bar's notes and rests on each staff (or on <paramref name="staff"/> alone), at <paramref name="onsets"/>,
    /// fill it from its start to <paramref name="length"/> with no gap; grace notes take no time. A staff with nothing written
    /// is not counted.
    /// </summary>
    internal static bool AddsUp(Bar bar, QuarterFraction length, IReadOnlyList<long> onsets, int? staff = null)
    {
        if (InDivisions(length, bar.Divisions) is not { } end)
        {
            return false;
        }

        foreach (IGrouping<int, int> sounds in Timed(bar).Where(i => staff is null || bar.Sounds[i].Staff == staff).GroupBy(i => bar.Sounds[i].Staff))
        {
            long reach = 0;
            foreach (int i in sounds.OrderBy(i => onsets[i]))
            {
                if (onsets[i] > reach)
                {
                    return false;
                }

                reach = Math.Max(reach, onsets[i] + bar.Sounds[i].Duration);
            }

            if (reach != end)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A bar that does not add up to <paramref name="length"/>, put right by the first rule that makes it add up.</summary>
    private static void MendBar(Bar bar, QuarterFraction length, Changes changes)
    {
        if (AddsUp(bar, length, bar.Onsets))
        {
            return;
        }

        if (WholeBarRest(bar, length))
        {
            changes.BarRests++;
        }
        else if (SharedHead(bar, length))
        {
            changes.SharedHeads++;
        }
    }

    /// <summary>
    /// A whole rest alone on its staff lasts the bar, whatever the meter (a rule of notation): homr writes it as four
    /// quarters in any meter. Each such rest takes the bar's length, when the bar then adds up.
    /// </summary>
    private static bool WholeBarRest(Bar bar, QuarterFraction length)
    {
        if (InDivisions(length, bar.Divisions) is not { } end)
        {
            return false;
        }

        int[] rests = [.. Timed(bar).GroupBy(i => bar.Sounds[i].Staff)
            .Where(staff => staff.Count() == 1 && bar.Sounds[staff.First()].IsWholeRest && bar.Sounds[staff.First()].Duration != end)
            .Select(staff => staff.First())];
        if (rests.Length == 0)
        {
            return false;
        }

        Sound[] read = [.. bar.Sounds];
        foreach (int i in rests)
        {
            bar.Sounds[i] = bar.Sounds[i] with { Duration = end };
        }

        if (!AddsUp(bar, length, bar.Onsets))
        {
            bar.Sounds.Clear();
            bar.Sounds.AddRange(read);
            return false;
        }

        var notes = bar.Measure.Elements("note").ToList();
        foreach (int i in rests)
        {
            notes[i].Element("duration")!.Value = end.ToString(CultureInfo.InvariantCulture);
        }

        bar.WriteOnsets([.. bar.Onsets]);
        return true;
    }

    /// <summary>
    /// Two voices of a staff sharing a notehead, a melody note of a quarter or longer on the first note of a run of eighths
    /// or shorter: the engine writes the run after the long note instead of under it, so the staff runs long. The run and
    /// everything after it on the staff move back to one short note after the long note's start, as often as the staff still
    /// runs long; the other staves' moments the engine placed by the same clock move with them. Kept when the bar then adds up.
    /// </summary>
    private static bool SharedHead(Bar bar, QuarterFraction length)
    {
        if (InDivisions(length, bar.Divisions) is not { } end)
        {
            return false;
        }

        long[] onsets = [.. bar.Onsets];
        var moved = new Dictionary<long, long>();
        foreach (int staff in Timed(bar).Select(i => bar.Sounds[i].Staff).Distinct().ToList())
        {
            int[] own = [.. Enumerable.Range(0, bar.Sounds.Count).Where(i => bar.Sounds[i].Staff == staff)];
            for (int step = 0; step < MostSharedHeads && own.Max(i => onsets[i] + bar.Sounds[i].Duration) > end; step++)
            {
                if (SharedHeadAt(bar, onsets, own) is not { } shared)
                {
                    break;
                }

                foreach (int i in own.Where(i => onsets[i] >= shared.At))
                {
                    onsets[i] -= shared.Shift;
                }
            }

            foreach (int i in own.Where(i => onsets[i] != bar.Onsets[i]))
            {
                moved[bar.Onsets[i]] = onsets[i];
            }
        }

        if (moved.Count == 0)
        {
            return false;
        }

        HashSet<int> fixedStaves = [.. Enumerable.Range(0, bar.Sounds.Count).Where(i => onsets[i] != bar.Onsets[i]).Select(i => bar.Sounds[i].Staff)];
        for (int i = 0; i < onsets.Length; i++)
        {
            if (!fixedStaves.Contains(bar.Sounds[i].Staff) && moved.TryGetValue(bar.Onsets[i], out long to))
            {
                onsets[i] = to;
            }
        }

        if (!AddsUp(bar, length, onsets))
        {
            return false;
        }

        bar.WriteOnsets(onsets);
        return true;
    }

    /// <summary>
    /// The first shared notehead on a staff's sounds (<paramref name="own"/>): where the run written after the long note
    /// starts, and how far it moves back; null when there is none.
    /// </summary>
    private static (long At, long Shift)? SharedHeadAt(Bar bar, long[] onsets, int[] own)
    {
        long quarter = bar.Divisions;
        int[] timed = [.. own.Where(i => !bar.Sounds[i].IsGrace)];
        foreach (long onset in timed.Select(i => onsets[i]).Distinct().Order())
        {
            int[] longs = [.. timed.Where(i => onsets[i] == onset && !bar.Sounds[i].IsRest && bar.Sounds[i].Duration >= quarter)];
            if (longs.Length == 0)
            {
                continue;
            }

            long at = onset + bar.Sounds[longs[0]].Duration;
            int[] next = [.. timed.Where(i => onsets[i] == at)];
            bool between = timed.Any(i => onsets[i] > onset && onsets[i] < at);
            if (!between && next.Length > 0 && next.All(i => !bar.Sounds[i].IsRest && bar.Sounds[i].Duration * 2 <= quarter))
            {
                return (at, bar.Sounds[longs[0]].Duration - bar.Sounds[next[0]].Duration);
            }
        }

        return null;
    }

    /// <summary>The sounds of the bar that take time: all but grace notes.</summary>
    private static IEnumerable<int> Timed(Bar bar) => Enumerable.Range(0, bar.Sounds.Count).Where(i => !bar.Sounds[i].IsGrace);

    /// <summary>A length in divisions, when it is a whole number of them.</summary>
    private static long? InDivisions(QuarterFraction length, long divisions) =>
        length.Numerator * divisions % length.Denominator == 0 ? length.Numerator * divisions / length.Denominator : null;
}