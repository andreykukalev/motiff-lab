namespace MotiffLab.MusicCore.Import.Transcription;

internal static partial class RecordingBars
{
    /// <summary>Bar lines for one bar length, from where the bass falls.</summary>
    private sealed record Layout(int[] Starts, long Score, bool CompoundTwelve)
    {
        /// <summary>How far, in 16ths, a likely downbeat is the lowest onset: a dotted eighth.</summary>
        private const int LowReach = 3;

        /// <param name="beat">16ths in a beat: when the bar holds whole beats, its downbeat falls on one, never between two (issue #455).</param>
        public static Layout Of(int[] steps, int[] lowest, int size, int beat)
        {
            int[] lows = Lows(steps, lowest);
            int last = steps[^1] + 1;

            // The downbeat's place in the bar, judged over four bars around each bar start.
            int windows = (last / size) + 1;
            var phases = new int?[windows];
            var counts = new int[size];
            for (int w = 0; w < windows; w++)
            {
                Array.Clear(counts);
                long from = ((long)w * size) - (2 * size), to = ((long)w * size) + (2 * size);
                foreach (int low in lows.Where(low => low >= from && low < to))
                {
                    counts[low % size]++;
                }

                phases[w] = Downbeat(counts, beat);
            }

            int current = phases.FirstOrDefault(phase => phase is not null) ?? 0;
            var starts = new List<int> { current };
            var lines = new HashSet<int>();
            int position = current;
            while (position < last)
            {
                lines.Add(position);
                int next = (position / size) + 1;
                int? ahead = next + 1 < windows ? phases[next] : null;
                if (ahead is { } moved && moved != current && phases[next + 1] == moved)
                {
                    // The downbeat moves for two windows running: this bar is cut short.
                    position += ((moved - current) % size + size) % size;
                    current = moved;
                    starts.Add(position);
                    continue;
                }

                position += size;
            }

            // How much better than chance the lows land on bar lines, less a cost for each short bar.
            int hits = lows.Count(lines.Contains) - (2 * (starts.Count - 1));
            long score = lows.Length == 0 ? 0 : Math.Max(0, (long)hits * size * 1000 / lows.Length);

            return new Layout([.. starts], score, size == 12 && IsCompound(lines, lows));
        }

        /// <summary>Twelve 16ths: 6/8 when the bass falls on the half bar more than on the second and third quarters.</summary>
        private static bool IsCompound(HashSet<int> lines, int[] lows)
        {
            const int size = 12;
            var offsets = new int[size];
            int[] sortedStarts = [.. lines.Order()];
            foreach (int low in lows)
            {
                int index = Array.BinarySearch(sortedStarts, low);
                int bar = index >= 0 ? sortedStarts[index] : (~index > 0 ? sortedStarts[~index - 1] : -1);
                if (bar >= 0 && low - bar < size)
                {
                    offsets[low - bar]++;
                }
            }

            return 2 * offsets[6] > offsets[4] + offsets[8];
        }

        /// <summary>
        /// Where in the bar the most likely downbeats fall, from how many fall at each place; null when none do. Between two
        /// beats only when that place catches twice the likely downbeats of any place on a beat, when the bar holds whole beats.
        /// </summary>
        private static int? Downbeat(int[] counts, int beat)
        {
            int max = counts.Max();
            if (max == 0)
            {
                return null;
            }

            int most = Array.IndexOf(counts, max);
            if (counts.Length % beat != 0 || most % beat == 0)
            {
                return most;
            }

            int onBeat = Enumerable.Range(0, counts.Length / beat).MaxBy(index => counts[index * beat]) * beat;
            return 2 * counts[onBeat] > max ? onBeat : most;
        }

        /// <summary>
        /// Onsets whose lowest note is the lowest within <see cref="LowReach"/> 16ths either way and
        /// below middle C: likely downbeats. Without any below middle C, the lowest within that reach.
        /// The reach is the same for every bar length, so their scores compare.
        /// </summary>
        public static int[] Lows(int[] steps, int[] lowest)
        {
            var lows = new List<int>();
            var fallback = new List<int>();
            for (int i = 0; i < steps.Length; i++)
            {
                bool isLowest = true;
                for (int j = i - 1; j >= 0 && steps[i] - steps[j] <= LowReach && isLowest; j--)
                {
                    isLowest = lowest[j] >= lowest[i];
                }

                for (int j = i + 1; j < steps.Length && steps[j] - steps[i] <= LowReach && isLowest; j++)
                {
                    isLowest = lowest[j] >= lowest[i];
                }

                if (isLowest)
                {
                    (lowest[i] < MiddleC ? lows : fallback).Add(steps[i]);
                }
            }

            return [.. (lows.Count > 0 ? lows : fallback).Distinct()];
        }
    }
}