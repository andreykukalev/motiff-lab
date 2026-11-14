namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>
/// Times the bars of a page and checks that each adds up: every staff with notes lasts as long as the others, and as long
/// as the meter asks. A bar may be short when it is the piece's pickup or the bar that completes it; a bar every staff
/// reads alike but at another length than its meter is a bar the notation program was told to make longer or shorter, and
/// is kept as printed.
/// </summary>
internal static class BarCheck
{
    /// <returns>How many bars do not add up.</returns>
    public static int Check(List<VectorBar> bars, List<string> issues, ReadingState state, double space)
    {
        int wrong = 0;
        for (int index = 0; index < bars.Count; index++, state.Bars++)
        {
            VectorBar bar = bars[index];
            int wanted = bar.InForce?.Ticks ?? 1920;
            int[] ends = BarTiming.Place(bar, space);
            if (!Agree(ends, out _) || ends.Max() > wanted)
            {
                if (UnmarkedTriplets(bar, space, wanted))
                {
                    issues.Add($"bar {index + 1}: groups of three with no number were read as triplets");
                    ends = BarTiming.Place(bar, space);
                }
            }

            if (Judge(bar, ends, wanted, state) is { } issue)
            {
                bool counts = !issue.StartsWith('~');
                wrong += counts ? 1 : 0;
                issues.Add($"bar {index + 1} {issue.TrimStart('~')}");
            }
        }

        return wrong;
    }

    /// <summary>What is wrong with a timed bar, null for nothing; a note that does not count as wrong starts with "~".</summary>
    private static string? Judge(VectorBar bar, int[] ends, int wanted, ReadingState state)
    {
        if (!Agree(ends, out int length))
        {
            return $"has staves lasting {string.Join(", ", ends)} ticks, not alike";
        }

        if (length < wanted && state.Bars == 0)
        {
            state.Pickup = length;
            return null;
        }

        if (length == wanted || length + (state.Pickup ?? 0) == wanted)
        {
            return null;
        }

        // Every staff alike at a whole number of beats: a bar made longer or shorter on purpose.
        bool alike = ends.Count(end => end > 0) > 1 && length % (1920 / (bar.InForce?.BeatType ?? 4)) == 0;
        return alike
            ? $"~is printed {length} ticks long, every staff alike, where its meter needs {wanted}"
            : $"adds up to {length} ticks where its meter needs {wanted}";
    }

    /// <summary>Whether every staff with notes lasts as long; the length they share.</summary>
    private static bool Agree(int[] ends, out int length)
    {
        int[] read = [.. ends.Where(end => end > 0)];
        length = read.Length > 0 ? read[0] : 0;
        return read.All(end => end == read[0]);
    }

    /// <summary>
    /// Reads a bar again with its beamed groups of three or six like notes as triplets, as a piece goes on writing triplets
    /// without their number once the first are marked. Kept only when the staves then agree with each other and with the meter
    /// or each other.
    /// </summary>
    private static bool UnmarkedTriplets(VectorBar bar, double space, int wanted)
    {
        List<VectorEvent> changed = [.. bar.Staves.SelectMany(events => events)
            .Where(item => item.Beam >= 0 && !item.Grace && item.Tuplet == (1, 1))
            .GroupBy(item => item.Beam)
            .Where(group => group.Count() is 3 or 6 && group.All(item => item.Dots == 0 && item.Written == group.First().Written))
            .SelectMany(group => group)];
        if (changed.Count == 0)
        {
            return false;
        }

        changed.ForEach(item => item.Tuplet = (3, 2));
        int[] ends = BarTiming.Place(bar, space);
        if (Agree(ends, out int length) && (length == wanted || ends.Count(end => end > 0) > 1))
        {
            return true;
        }

        changed.ForEach(item => item.Tuplet = (1, 1));
        BarTiming.Place(bar, space);
        return false;
    }
}