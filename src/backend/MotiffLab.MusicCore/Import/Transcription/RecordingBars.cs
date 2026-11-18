using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Transcription;

/// <summary>
/// The bars of a recording (decision record 0016): found from its onsets (<see cref="Find"/>, and section by section for a
/// live performance, <see cref="FindLive"/>), weighed for odd bars (<see cref="OddMeters"/>), and applied to the piece
/// (<see cref="Apply"/>). The grid found is a <see cref="BarGrid"/>. Everything is integer arithmetic and deterministic.
/// </summary>
public static partial class RecordingBars
{
    /// <summary>The fewest separate onsets a grid is fitted to.</summary>
    public const int MinOnsets = 16;

    /// <summary>The shortest 16th the search tries: 80 ms, a quarter at 187.5 BPM.</summary>
    public const int ShortestSearched = 80_000;

    /// <summary>The longest 16th the search tries: 500 ms, a quarter at 30 BPM.</summary>
    public const int LongestSearched = 500_000;

    /// <summary>A 16th at a quarter of 60 a minute: the search takes a longer step for an eighth.</summary>
    private const int SlowestUsualSixteenth = 250_000;

    /// <summary>
    /// The grid that fits the transcription's onsets best, or null when there are too few onsets or
    /// they follow no beat, even one that bends with the playing.
    /// </summary>
    /// <param name="signature">The bars to lay out; null picks the one the bass supports best.</param>
    /// <param name="stepsPerBeat">How many 16ths a beat is written as, for example twice or half as many as were found; null takes the ones found.</param>
    /// <param name="search">How to follow a live performance (<see cref="FindLive"/>); null finds the beat once, over the whole recording.</param>
    public static BarGrid? Find(MidiImport import, TimeSignature? signature = null, int? stepsPerBeat = null, GridSearch? search = null)
    {
        ArgumentNullException.ThrowIfNull(import);
        if (search is not null)
        {
            return FindLive(import, search, signature, stepsPerBeat).Grid;
        }

        Validate(signature, stepsPerBeat);
        var clock = new Clock(import.Tempos);
        Onset[] onsets = Cluster(import.Notes, clock);
        if (onsets.Length < MinOnsets)
        {
            return null;
        }

        Tick origin = onsets[0].Tick;
        long originUnits = onsets[0].Units;
        long[] times = [.. onsets.Select(onset => (onset.Units - originUnits) / Clock.UnitsPerMicrosecond)];
        long[] lows = Lows(onsets, times);
        long[] beats;
        long[]? plain = null;
        int found;
        long? bassBeat = BassBeat(lows);
        if (bassBeat is { } period)
        {
            // The bass keeps a steady beat: follow it, and write it in 16ths, or in triplets of
            // them when those fit far better.
            beats = TrackBass(times, lows, period);
            found = Divisions(origin, times, beats, period);
        }
        else
        {
            // The opening gives the 16th; the beat, an eighth, then follows the playing.
            (long step, _) = Search(times);
            // The shortest notes found are taken as 16ths. When they are eighths, that reads as a
            // quarter slower than 60 a minute; such slow tempos are rare, so halve the step.
            while (step > SlowestUsualSixteenth && step / 2 >= ShortestSearched)
            {
                step /= 2;
            }

            beats = Track(times, 2 * step);
            plain = Track(times, 2 * step, null, out _, relock: false);
            found = 2;
        }

        int perBeat = stepsPerBeat ?? found;
        long[] sorted = [.. beats.Zip(beats.Skip(1), (a, b) => b - a).Order()];
        long sixteenth = Math.Clamp(sorted[sorted.Length / 2] / perBeat, BarGrid.ShortestSixteenth, BarGrid.LongestSixteenth);
        var grid = new BarGrid(origin, (int)sixteenth, TimeSignature.CommonTime, [0], beats, perBeat);

        // A bass that keeps the beat is evidence enough, however freely the hands around it play.
        if (bassBeat is null && stepsPerBeat is null && !Steady(origin, times, plain!, perBeat))
        {
            return null;
        }

        long[] positions = [.. times.Select(grid.ThousandthsOfSteps)];

        int[] steps = [.. positions.Select(position => (int)((position + 500) / 1000))];
        int[] lowest = [.. onsets.Select(onset => onset.Lowest)];
        TimeSignature picked;
        Layout layout;
        if (signature is { } wanted)
        {
            picked = wanted;
            layout = Layout.Of(steps, lowest, (int)(wanted.BarTicks / BarGrid.StepTicks), perBeat);
        }
        else
        {
            (picked, layout) = Pick(steps, lowest, perBeat);
        }

        return new BarGrid(origin, (int)sixteenth, picked, layout.Starts, beats, perBeat);
    }

    /// <summary>
    /// Whether the onsets follow <paramref name="beats"/>, tracked without finding a slipped beat again: onsets that ignore
    /// the beat sit a quarter of a step from it on average, as at random, and a beat that bends to follow them still leaves
    /// them an eighth of a step away. Found again, a beat would follow even onsets at random, so it is not asked.
    /// </summary>
    private static bool Steady(Tick origin, long[] times, long[] beats, int perBeat)
    {
        long[] gaps = [.. beats.Zip(beats.Skip(1), (a, b) => b - a).Order()];
        long sixteenth = Math.Clamp(gaps[gaps.Length / 2] / perBeat, BarGrid.ShortestSixteenth, BarGrid.LongestSixteenth);
        var grid = new BarGrid(origin, (int)sixteenth, TimeSignature.CommonTime, [0], beats, perBeat);
        return 8 * Distance([.. times.Select(grid.ThousandthsOfSteps)]) <= times.Length * 1000L;
    }

    /// <summary>The summed distance of positions, in thousandths of a 16th, to the nearest 16th.</summary>
    private static long Distance(long[] positions) => positions.Sum(position => Math.Min(position % 1000, 1000 - (position % 1000)));

    private static void Validate(TimeSignature? signature, int? stepsPerBeat)
    {
        if (signature is { } chosen && !BarGrid.Signatures.Contains(chosen))
        {
            throw new ArgumentException($"Bars of {chosen} are not offered.", nameof(signature));
        }

        if (stepsPerBeat is < BarGrid.FewestStepsPerBeat or > BarGrid.MostStepsPerBeat)
        {
            throw new ArgumentOutOfRangeException(nameof(stepsPerBeat), stepsPerBeat, "A beat is written as 1 to 16 16ths.");
        }
    }

    /// <summary>The bars the bass supports best: the shortest bar that does nearly as well as the best.</summary>
    private static (TimeSignature Signature, Layout Layout) Pick(int[] steps, int[] lowest, int beat)
    {
        // By 16ths in a bar, in the order that breaks a near tie: when the bass does as well on
        // every half bar as on the bar line, the shorter bar says as much, except that 4/4 is far
        // more common than 2/4.
        int[] sizes = [16, 6, 12, 8];
        Layout[] layouts = [.. sizes.Select(size => Layout.Of(steps, lowest, size, beat))];
        long[] scores = [.. layouts.Select(layout => layout.Score)];
        long best = scores.Max();
        int index = best <= 0 ? 0 : Array.FindIndex(scores, score => 10 * score >= 9 * best);
        Layout chosen = layouts[index];
        TimeSignature signature = sizes[index] switch
        {
            6 => new TimeSignature(3, 8),
            8 => new TimeSignature(2, 4),
            12 => chosen.CompoundTwelve ? new TimeSignature(6, 8) : new TimeSignature(3, 4),
            _ => TimeSignature.CommonTime,
        };
        return (signature, chosen);
    }
}