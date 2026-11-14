using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Transcription;

/// <summary>
/// A stretch of a recording's bars that reads better in another signature (version 0.5, «Смена размера в записи», a
/// preview): from the 16th <paramref name="From"/> to <paramref name="To"/> after the grid's origin, bars of
/// <paramref name="Signature"/> fit where the bass falls at least a quarter better than any bar the grid offers.
/// </summary>
public sealed record MeterStretch(int From, int To, TimeSignature Signature);

public sealed partial record BarGrid
{
    /// <summary>The odd bars tried in a recording, by their 16ths: 5/8, 7/8, 9/8, 5/4 and 12/8.</summary>
    public static IReadOnlyList<TimeSignature> OddSignatures { get; } =
        [new(5, 8), new(7, 8), new(9, 8), new(5, 4), new(12, 8)];

    /// <summary>How many of the grid's bars a window weighs, and how far one window is from the next.</summary>
    private const int MeterWindowBars = 8;

    private const int MeterHopBars = 4;

    /// <summary>The fewest likely downbeats a window needs for its bar to be weighed.</summary>
    private const int FewestMeterLows = 8;

    /// <summary>
    /// Where the bars of a recording heard read better in an odd signature than in this grid's (version 0.5, a preview
    /// only: nothing is changed or saved). The grid's bars are weighed eight at a time, four bars apart, as <see cref="Find"/>
    /// weighs a whole piece: an odd bar is taken only when the bass falls on its bar lines at least 1.25 times as well as on
    /// the best of the usual bars, and two windows or more in a row with the same odd bar make one stretch. Deterministic.
    /// </summary>
    /// <param name="import">The engine's MIDI the grid was found for, on the engine's clock.</param>
    public IReadOnlyList<MeterStretch> OddMeters(MidiImport import)
    {
        ArgumentNullException.ThrowIfNull(import);
        var clock = new Clock(import.Tempos);
        Onset[] onsets = Cluster(import.Notes, clock);
        int first = Array.FindIndex(onsets, onset => onset.Tick >= Origin);
        if (first < 0)
        {
            return [];
        }

        // Counted from the grid's origin as Apply counts, so a stretch's 16ths are its ticks over StepTicks.
        long originUnits = clock.Units(Origin);
        Onset[] heard = onsets[first..];
        int[] steps = [.. heard.Select(onset => (int)((ThousandthsOfSteps((onset.Units - originUnits) / Clock.UnitsPerMicrosecond) + 500) / 1000))];
        int[] lowest = [.. heard.Select(onset => onset.Lowest)];
        int bar = (int)(Signature.BarTicks / StepTicks), window = MeterWindowBars * bar;
        var found = new List<(MeterStretch Stretch, int Windows)>();
        int last = steps.Length == 0 ? 0 : steps[^1] + 1;
        for (int from = BarStarts[0]; from < last; from += MeterHopBars * bar)
        {
            // Only whole windows are weighed, but the last reaches to the end and a piece shorter than one is one.
            int to = from + window + (MeterHopBars * bar) >= last ? last : from + window;
            if (from > BarStarts[0] && from + window > last)
            {
                break;
            }

            int[] inside = [.. Enumerable.Range(0, steps.Length).Where(i => steps[i] >= from && steps[i] < to)];
            if (inside.Length < MinOnsets || OddIn([.. inside.Select(i => steps[i] - from)], [.. inside.Select(i => lowest[i])]) is not { } odd)
            {
                continue;
            }

            // A window that overlaps the stretch before it in the same bar carries it on.
            if (found.Count > 0 && found[^1].Stretch.Signature == odd && found[^1].Stretch.To >= from)
            {
                found[^1] = (found[^1].Stretch with { To = to }, found[^1].Windows + 1);
            }
            else
            {
                found.Add((new MeterStretch(from, to, odd), 1));
            }
        }

        // A change holds for two windows running, twelve bars at least, unless one window is the whole piece.
        return [.. found.Where(each => each.Windows >= 2 || (each.Stretch.From == BarStarts[0] && each.Stretch.To == last)).Select(each => each.Stretch)];
    }

    /// <summary>
    /// The odd bar whose bar lines, at one place in the window, catch the likely downbeats at least 1.25 times as well as
    /// those of every usual bar, each counted against chance (hits × bar length / downbeats); null when none does.
    /// </summary>
    private static TimeSignature? OddIn(int[] steps, int[] lowest)
    {
        int[] lows = Layout.Lows(steps, lowest);
        if (lows.Length < FewestMeterLows)
        {
            return null;
        }

        long Score(int size)
        {
            var counts = new int[size];
            foreach (int low in lows)
            {
                counts[low % size]++;
            }

            return (long)counts.Max() * size * 1000 / lows.Length;
        }

        long usual = new[] { 16, 6, 12, 8 }.Max(Score);
        // By 16ths in a bar; a near tie goes to the shorter bar, as in Find: 5/8 before 5/4, which holds two of them.
        (TimeSignature Signature, long Score)[] odd = [.. OddSignatures
            .Select(signature => (signature, Score((int)(signature.BarTicks / StepTicks))))
            .OrderBy(candidate => candidate.signature.BarTicks)];
        long most = odd.Max(candidate => candidate.Score);
        (TimeSignature Signature, long Score) best = odd.First(candidate => 10 * candidate.Score >= 9 * most);
        return 4 * best.Score > 5 * usual ? best.Signature : null;
    }
}