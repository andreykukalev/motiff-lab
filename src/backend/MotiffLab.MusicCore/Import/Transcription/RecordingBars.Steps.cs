using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Import.Transcription;

internal static partial class RecordingBars
{
    /// <summary>The 16th and phase in microseconds that leave the onsets closest to the grid, relative to its step.</summary>
    private static (long Step, long Phase) Search(long[] times) => Search(times, ShortestSearched, LongestSearched);

    /// <param name="shortest">The shortest 16th tried, from <see cref="ShortestSearched"/>.</param>
    /// <param name="longest">The longest 16th tried, up to <see cref="LongestSearched"/>.</param>
    private static (long Step, long Phase) Search(long[] times, long shortest, long longest)
    {
        // A step that is slightly off drifts a whole step away within a minute or two, so the search
        // looks at the start of the piece: a coarse pass over the first 16 seconds, a fine pass over
        // the first 32, then a straight-line fit over ever longer stretches up to the whole piece.
        (long step, long phase, long cost) best = (0, 0, 0);
        long[] start = Opening(times, 16_000_000);
        for (long step = shortest; step <= longest; step += 500)
        {
            Consider(start, step, 16, ref best);
        }

        long coarse = best.step;
        best = (0, 0, 0);
        start = Opening(times, 32_000_000);
        for (long step = Math.Max(shortest, coarse - 1_000); step <= Math.Min(longest, coarse + 1_000); step += 50)
        {
            Consider(start, step, 32, ref best);
        }

        (long Step, long Phase) fit = (best.step, best.phase);
        foreach (long seconds in new long[] { 32, 64, 128, 256 })
        {
            fit = Refine(Opening(times, seconds * 1_000_000), fit.Step, fit.Phase);
        }

        return Refine(times, fit.Step, fit.Phase);
    }

    /// <summary>The onsets within a time of the first, and at least the first 64.</summary>
    private static long[] Opening(long[] times, long microseconds)
    {
        int count = 0;
        while (count < times.Length && (count < 64 || times[count] - times[0] < microseconds))
        {
            count++;
        }

        return times[..count];
    }

    private static void Consider(long[] times, long step, int phases, ref (long Step, long Phase, long Cost) best)
    {
        for (int k = 0; k < phases; k++)
        {
            long phase = step * k / phases;
            long cost = Distance(times, step, phase);
            // Relative to the step: cost / step < best.Cost / best.Step.
            if (best.Step == 0 || (Int128)cost * best.Step < (Int128)best.Cost * step)
            {
                best = (step, phase, cost);
            }
        }
    }

    /// <summary>A least-squares line through the onsets near the grid: the step and phase that fit them best.</summary>
    private static (long Step, long Phase) Refine(long[] times, long step, long phase)
    {
        Int128 n = 0, sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach (long time in times)
        {
            long index = DivideRounded(time - phase, step);
            if (4 * Math.Abs(time - phase - (index * step)) >= step)
            {
                continue;
            }

            n++;
            sx += index;
            sy += time;
            sxx += (Int128)index * index;
            sxy += (Int128)index * time;
        }

        Int128 denominator = (n * sxx) - (sx * sx);
        if (n < MinOnsets || denominator == 0)
        {
            return (step, phase);
        }

        Int128 numerator = (n * sxy) - (sx * sy);
        long refined = (long)(((2 * numerator) + denominator) / (2 * denominator));
        if (10 * Math.Abs(refined - step) > step || refined < BarGrid.ShortestSixteenth)
        {
            return (step, phase);
        }

        long intercept = (long)(((2 * (sy - (refined * sx))) + n) / (2 * n));
        return (refined, intercept);
    }

    /// <summary>The sum of every onset's distance to the nearest grid point.</summary>
    private static long Distance(long[] times, long step, long phase)
    {
        long sum = 0;
        foreach (long time in times)
        {
            long offset = (time - phase) % step;
            offset = offset < 0 ? offset + step : offset;
            sum += Math.Min(offset, step - offset);
        }

        return sum;
    }

    /// <summary>a / b rounded to the nearest whole number, halves away from zero; b is positive.</summary>
    private static long DivideRounded(long a, long b) => a >= 0 ? ((2 * a) + b) / (2 * b) : -(((-2 * a) + b) / (2 * b));
}