namespace MotiffLab.MusicCore.Import.Scan;

public static partial class ScanRepair
{
    /// <summary>The moments of a bar as the engine writes them, and where they would start with split moments put back.</summary>
    private static class Moments
    {
        /// <summary>
        /// Where each sound would start with each split moment put back with the one before it; null when no moment is
        /// split, or when the sounds do not come in the order of their onsets, as the engine writes them.
        /// </summary>
        /// <param name="sounds">The bar's notes and rests in the order written.</param>
        /// <param name="onsets">Where each starts as read, in divisions.</param>
        /// <param name="end">Where the bar ends as read.</param>
        public static long[]? Retime(IReadOnlyList<Sound> sounds, IReadOnlyList<long> onsets, long end)
        {
            if (Group(onsets) is not { Count: > 1 } groups)
            {
                return null;
            }

            var retimed = new long[sounds.Count];
            var covered = new Dictionary<int, long>();
            long blockStart = 0, blockAdvance = 0;
            var blockStaves = new HashSet<int>();
            bool joined = false;
            for (int g = 0; g < groups.Count; g++)
            {
                (int first, int count) = groups[g];
                long advance = (g + 1 < groups.Count ? onsets[groups[g + 1].First] : end) - onsets[first];
                HashSet<int> staves = [.. Enumerable.Range(first, count).Where(i => !sounds[i].IsGrace).Select(i => sounds[i].Staff)];
                if (g > 0 && Joins(staves, blockStaves, blockStart, blockAdvance, covered))
                {
                    joined = true;
                    blockAdvance = Math.Min(blockAdvance, advance);
                    blockStaves.UnionWith(staves);
                }
                else
                {
                    blockStart = g == 0 ? onsets[first] : blockStart + blockAdvance;
                    blockAdvance = advance;
                    blockStaves = staves;
                }

                for (int i = first; i < first + count; i++)
                {
                    retimed[i] = blockStart;
                    if (!sounds[i].IsGrace)
                    {
                        covered[sounds[i].Staff] = Math.Max(Covered(covered, sounds[i].Staff), blockStart + sounds[i].Duration);
                    }
                }
            }

            return joined ? retimed : null;
        }

        /// <summary>
        /// Whether a moment was split from the block before it: it is on other staves, its staves are silent when the block
        /// starts, and the block's staves are silent by the time the block moves on, with no rest written on either.
        /// </summary>
        private static bool Joins(HashSet<int> staves, HashSet<int> blockStaves, long blockStart, long blockAdvance, Dictionary<int, long> covered) =>
            staves.Count > 0
            && blockStaves.Count > 0
            && !staves.Overlaps(blockStaves)
            && staves.All(staff => Covered(covered, staff) <= blockStart)
            && blockStaves.All(staff => Covered(covered, staff) <= blockStart + blockAdvance);

        private static long Covered(Dictionary<int, long> covered, int staff) => covered.TryGetValue(staff, out long until) ? until : 0;

        /// <summary>Runs of sounds that start together, as (first, count); null when a sound starts before the run it follows.</summary>
        private static List<(int First, int Count)>? Group(IReadOnlyList<long> onsets)
        {
            var groups = new List<(int First, int Count)>();
            for (int i = 0; i < onsets.Count; i++)
            {
                if (groups.Count > 0 && onsets[i] == onsets[groups[^1].First])
                {
                    groups[^1] = (groups[^1].First, groups[^1].Count + 1);
                }
                else if (groups.Count > 0 && onsets[i] < onsets[groups[^1].First])
                {
                    return null;
                }
                else
                {
                    groups.Add((i, 1));
                }
            }

            return groups;
        }
    }
}