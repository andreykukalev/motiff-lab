using System.Xml.Linq;

using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Scan;

public static partial class ScanRepair
{
    /// <summary>
    /// The stretches of a part whose bars are judged together (v0.5.7, issue #543): each range under one written time
    /// signature, except that the ranges starting a page with the lower number already in force are judged with the
    /// stretch they continue, unless their own bars say they are in another meter.
    /// </summary>
    /// <remarks>
    /// The engine reads each page on its own and writes, where it sees a signature printed, the median length of that
    /// page's bars (<see cref="ScanJoin"/> joins the pages, each keeping its own). A page of a few bars, or one where most
    /// moments are split, cannot settle its meter alone, and its signature was kept as the engine wrote it, 5/4 in a 4/4
    /// piece; judged with the pages before and after it, it is 4/4. A page whose own bars settle another length is a real
    /// change and keeps it, as does any signature written inside a page or with another lower number:
    /// the engine writes a signature only where one is printed, and inside a page only when its lower number changes.
    /// </remarks>
    private static List<List<Bar>> Stretches(List<List<Bar>> ranges)
    {
        var stretches = new List<List<Bar>>();
        foreach (List<List<Bar>> pool in Pools(ranges))
        {
            List<Bar> all = [.. pool.SelectMany(range => range)];
            if (pool.Count == 1 || Most(all, Written(pool[0][0].Time!)) is not { } most)
            {
                stretches.AddRange(pool);
                continue;
            }

            List<List<Bar>> apart = [.. pool.Skip(1).Where(range => !Fits(range, most))];
            stretches.Add([.. pool.Where(range => !apart.Contains(range)).SelectMany(range => range)]);
            stretches.AddRange(apart);
        }

        return stretches;
    }

    /// <summary>Consecutive ranges where each after the first starts a page under a signature with the first one's lower number.</summary>
    private static List<List<List<Bar>>> Pools(List<List<Bar>> ranges)
    {
        var pools = new List<List<List<Bar>>>();
        foreach (List<Bar> range in ranges)
        {
            if (pools.Count > 0 && Continues(pools[^1][0][0].Time, range[0]))
            {
                pools[^1].Add(range);
            }
            else
            {
                pools.Add([range]);
            }
        }

        return pools;
    }

    private static bool Continues(XElement? time, Bar bar) =>
        time is not null && Written(time) is not null && bar.StartsPage && bar.Time is { } next && BeatType(next) == BeatType(time);

    /// <summary>
    /// Whether a page's range goes with the length its stretch agrees on: its bars have it, as read or re-timed, or they
    /// settle no length of their own (the signature the engine wrote there is only the median of those few bars).
    /// </summary>
    private static bool Fits(List<Bar> range, QuarterFraction most) =>
        Settled(range, most) || Most(range, Written(range[0].Time!)) is not { } own || own == most || !Settled(range, own);

    /// <summary>Whether enough of the bars, and half of them, have the length, as read or re-timed.</summary>
    private static bool Settled(List<Bar> bars, QuarterFraction length)
    {
        int count = bars.Count(bar => bar.Length == length || (bar.Retimed is not null && bar.RetimedLength == length));
        return count >= FewestBarsForMeter && count * 2 >= bars.Count;
    }

    private static string BeatType(XElement time) => time.Element("beat-type")!.Value.Trim();
}