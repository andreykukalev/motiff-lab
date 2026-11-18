using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Transcription;

public static partial class RecordingBars
{
    /// <summary>The shortest and longest beat the bass can mark: 300 ms and 2 s.</summary>
    private const long ShortestBassBeat = 300_000;

    private const long LongestBassBeat = 2_000_000;

    /// <summary>The longest 16th a bass beat is divided into: a quarter at 50 a minute.</summary>
    private const long SlowestBassSixteenth = 300_000;

    private const int MiddleC = 60;

    /// <summary>
    /// When the onsets that are the lowest of their neighbours and below middle C fall: where the bass
    /// plays, in microseconds after the first onset.
    /// </summary>
    private static long[] Lows(Onset[] onsets, long[] times)
    {
        var lows = new List<long>();
        for (int i = 0; i < onsets.Length; i++)
        {
            int from = Math.Max(0, i - 3), to = Math.Min(onsets.Length, i + 4);
            if (onsets[i].Lowest < MiddleC && onsets[from..to].All(other => other.Lowest >= onsets[i].Lowest))
            {
                lows.Add(times[i]);
            }
        }

        return [.. lows];
    }

    /// <summary>
    /// The beat the bass keeps, or null when it keeps none: the typical time between bass notes, when
    /// most of those times are about that beat, half of it or twice it.
    /// </summary>
    private static long? BassBeat(long[] lows)
    {
        if (lows.Length < MinOnsets)
        {
            return null;
        }

        long[] gaps = [.. lows.Zip(lows.Skip(1), (a, b) => b - a)];
        long[] usable = [.. gaps.Where(gap => gap is >= ShortestBassBeat and <= LongestBassBeat).Order()];
        if (usable.Length < MinOnsets)
        {
            return null;
        }

        long beat = usable[usable.Length / 2];
        static bool Near(long gap, long length) => 5 * Math.Abs(gap - length) < length;
        int steady = gaps.Count(gap => Near(gap, beat) || Near(gap, beat / 2) || Near(gap, 2 * beat));
        return 10 * steady >= 6 * gaps.Length ? beat : null;
    }

    /// <summary>
    /// Beats of the bass, each moved to the bass note nearest where it is expected, or failing that to
    /// any onset close to it, with the expected length following the last few beats. The beats before
    /// the first bass note count back from it, so that the first beat is at the first onset.
    /// </summary>
    private static long[] TrackBass(long[] times, long[] lows, long period) => TrackBass(times, lows, period, null, out _);

    /// <param name="jump">With a run length, tracking stops where that many beats in a row wanted to be longer or shorter than the clamp lets them: the time of the next onset, else null.</param>
    /// <param name="relock">Whether a beat that slipped is found again (<see cref="Follow"/>).</param>
    private static long[] TrackBass(long[] times, long[] lows, long period, int? jump, out long? jumped, bool relock = true)
    {
        var beats = new List<long> { 0 };
        long first = lows[0];
        for (long back = DivideRounded(first, period) - 1; back >= 1; back--)
        {
            if (first - (back * period) >= beats[^1] + (period / 2))
            {
                beats.Add(first - (back * period));
            }
        }

        if (first > beats[^1])
        {
            beats.Add(first);
        }

        jumped = Follow(beats, times, period, jump, relock, (at, expected, beat) =>
            Nearest(lows, expected, beat / 4, at + (beat * 3 / 10)) ?? Nearest(times, expected, beat / 8, at));
        return [.. beats];
    }

    /// <summary>
    /// How many 16ths a bass beat is written as: the fewest of 2, 4, 8 or 16 that leave a 16th no
    /// longer than <see cref="SlowestBassSixteenth"/>, or half as many again when triplets of them
    /// leave the onsets far closer to the grid.
    /// </summary>
    private static int Divisions(Tick origin, long[] times, long[] beats, long period)
    {
        int straight = 2;
        while (straight < 16 && period / straight > SlowestBassSixteenth)
        {
            straight *= 2;
        }

        int triple = straight * 3 / 2;
        long Cost(int steps)
        {
            var grid = new BarGrid(origin, BarGrid.ShortestSixteenth, TimeSignature.CommonTime, [0], beats, steps);
            return Distance([.. times.Select(grid.ThousandthsOfSteps)]);
        }

        return triple <= BarGrid.MostStepsPerBeat && 10 * Cost(triple) < 6 * Cost(straight) ? triple : straight;
    }

    /// <summary>The time nearest to <paramref name="expected"/>, closer than <paramref name="reach"/> and later than <paramref name="after"/>.</summary>
    private static long? Nearest(long[] times, long expected, long reach, long after)
    {
        int index = Array.BinarySearch(times, expected);
        index = index >= 0 ? index : ~index;
        long? found = null;
        long nearest = reach;
        foreach (int candidate in new[] { index - 1, index })
        {
            if (candidate >= 0 && candidate < times.Length && times[candidate] > after && Math.Abs(times[candidate] - expected) < nearest)
            {
                nearest = Math.Abs(times[candidate] - expected);
                found = times[candidate];
            }
        }

        return found;
    }

    /// <summary>
    /// Beats an eighth apart at first, each moved to the onset nearest where it is expected, within a
    /// quarter of a beat, with the expected length following the last few beats: a tempo that bends
    /// with the playing. Where nothing is played, the beat goes on at the tempo it had.
    /// </summary>
    private static long[] Track(long[] times, long period) => Track(times, period, null, out _);

    /// <param name="jump">With a run length, tracking stops where that many beats in a row wanted to be longer or shorter than the clamp lets them: the time of the next onset, else null.</param>
    /// <param name="relock">Whether a beat that slipped is found again (<see cref="Follow"/>).</param>
    private static long[] Track(long[] times, long period, int? jump, out long? jumped, bool relock = true)
    {
        var beats = new List<long> { 0 };
        jumped = Follow(beats, times, period, jump, relock, (at, expected, beat) => Nearest(times, expected, beat / 4, at));
        return [.. beats];
    }

    /// <summary>
    /// Adds beats to <paramref name="beats"/> to the last onset, each where <paramref name="near"/> hears it (from the last
    /// beat, where it is expected and how long the beat is) or else where it was expected, the length following the last
    /// few beats within the clamp. A beat that slips: when two beats in a row hear nothing though notes were played
    /// between them, the playing has held back or pressed on by more than the reach, and every later onset would fall
    /// between the beats (issue #455); the beats go back to the last one heard and the next is looked for within 3/8 of
    /// a beat, once for each beat heard. Returns where tracking stopped for a jump in tempo, as <see cref="Track(long[], long, int?, out long?, bool)"/>.
    /// </summary>
    private static long? Follow(List<long> beats, long[] times, long period, int? jump, bool relock, Func<long, long, long, long?> near)
    {
        var clamp = new ClampWatch(period, jump);
        long beat = period, at = beats[^1], last = times[^1];
        int heardAt = beats.Count - 1, missed = 0;
        long heardBeat = beat;
        bool wide = false, gone = false;
        while (at < last)
        {
            long expected = at + beat;
            long? heard = near(at, expected, beat);
            // Found again further away, the beat moves to the playing but keeps its length, so it never runs away.
            bool moved = heard is null && wide && (heard = Nearest(times, expected, beat * 3 / 8, at + (beat / 4))) is not null;
            wide = false;
            missed = heard is null && NextAfter(times, at + (beat / 4)) is { } played && played < expected + (beat / 2) ? missed + 1 : 0;
            if (relock && missed >= 2 && !gone)
            {
                beats.RemoveRange(heardAt + 1, beats.Count - heardAt - 1);
                (at, beat, missed, wide, gone) = (beats[^1], heardBeat, 0, true, true);
                continue;
            }

            long next = heard ?? expected;
            beat = moved ? beat : clamp.Next(beat, next - at, heard is not null);
            beats.Add(next);
            at = next;
            if (heard is not null)
            {
                (heardAt, heardBeat, gone) = (beats.Count - 1, beat, false);
            }

            if (clamp.Jumped && NextAfter(times, at) is { } onset)
            {
                return onset;
            }
        }

        return null;
    }

    /// <summary>The first time later than <paramref name="after"/>, or null.</summary>
    private static long? NextAfter(long[] times, long after)
    {
        int index = Array.BinarySearch(times, after + 1);
        index = index >= 0 ? index : ~index;
        return index < times.Length ? times[index] : null;
    }

    /// <summary>
    /// The beat's length following the last few beats, held within 3/5 and 8/5 of where it started; with a run length,
    /// whether that many beats in a row wanted to go past the clamp, as when the tempo jumps without a pause (version 0.5).
    /// </summary>
    private sealed class ClampWatch(long period, int? run)
    {
        private readonly long _shortest = period * 3 / 5;
        private readonly long _longest = period * 8 / 5;
        private int _held;

        public bool Jumped => run is { } length && _held >= length;

        /// <param name="heard">Whether an onset marked the beat; a beat where nothing is played neither adds to the run nor ends it.</param>
        public long Next(long beat, long played, bool heard)
        {
            long wanted = ((3 * beat) + played) / 4;
            if (heard)
            {
                _held = wanted < _shortest || wanted > _longest ? _held + 1 : 0;
            }

            return Math.Clamp(wanted, _shortest, _longest);
        }
    }
}