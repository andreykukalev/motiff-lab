using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import;

/// <summary>What the timing of an imported file says about how it was made (S1-I6, S1-R1, S1-R2).</summary>
public enum TimingKind
{
    /// <summary>Too few notes to judge; nothing is offered.</summary>
    TooFewNotes,

    /// <summary>Written or quantised: the onsets sit on the grid, so nothing needs restoring.</summary>
    Quantised,

    /// <summary>Played to the file's tempo map: onsets cluster near a grid, off by small amounts. Restore rhythm can apply (S1-R1).</summary>
    Performance,

    /// <summary>The playing does not follow the tempo map at any common grid; Stage 1 cannot restore it (S1-R2).</summary>
    FreeTempo,
}

/// <summary>
/// How the onsets sit against one grid, measured from each onset's bar line: the offset to the
/// nearest grid point, late positive and early negative. All figures are whole ticks or counts.
/// </summary>
/// <param name="Name">"16th" or "triplet 8th".</param>
/// <param name="Step">The grid's spacing in ticks.</param>
/// <param name="Near">Onsets within <see cref="PerformanceTiming.Tolerance"/> of a grid point.</param>
/// <param name="TotalOffset">The sum of every onset's distance to its nearest grid point.</param>
/// <param name="Histogram">
/// Onsets by signed offset in bins of <see cref="PerformanceTiming.BinTicks"/>, from half a step
/// early to half a step late; bin i holds offsets from −Step/2 + i·BinTicks, inclusive.
/// </param>
public sealed record GridFit(string Name, long Step, int Near, long TotalOffset, IReadOnlyList<int> Histogram)
{
    /// <summary>The mean distance to the grid in whole ticks, rounded to the nearest, for <paramref name="onsets"/> onsets.</summary>
    public long MeanOffset(int onsets) => onsets == 0 ? 0 : ((2 * TotalOffset) + onsets) / (2L * onsets);

    /// <summary>
    /// Whether the onsets cluster around this grid: on average they are at most an eighth of a step
    /// away. Onsets that ignore the grid land a quarter of a step away on average, as at random.
    /// </summary>
    public bool Fits(int onsets) => onsets > 0 && 8 * TotalOffset <= onsets * Step;
}

/// <summary>
/// The timing statistics of a file's notes (S1-R1, S1-R2), exposed for the import screen and for
/// S1-AI4: the share of onsets off the 16th and triplet-8th grids, each grid's offset histogram and
/// mean offset, the swing of the offbeats, and whether the tempo map fits the playing.
/// </summary>
/// <param name="Onsets">The number of onsets measured: every note's onset.</param>
/// <param name="OffGrid">Onsets more than <see cref="Tolerance"/> from both grids.</param>
/// <param name="Grids">The 16th and the triplet-8th grid, in that order.</param>
/// <param name="Swing">The offbeats' median position through the beat, when enough beats hold two onsets; null otherwise.</param>
public sealed record PerformanceTiming(TimingKind Kind, int Onsets, int OffGrid, IReadOnlyList<GridFit> Grids, SwingMeasure? Swing)
{
    /// <summary>The fewest onsets the figures are judged on.</summary>
    public const int MinOnsets = 8;

    /// <summary>How far an onset may sit from a grid point and still count as on it: 10 ticks, a 48th of a quarter.</summary>
    public const long Tolerance = 10;

    /// <summary>The width of a histogram bin.</summary>
    public const long BinTicks = 10;

    /// <summary>The fewest beats of two onsets the swing is measured on.</summary>
    public const int MinSwingBeats = 4;

    private const long Sixteenth = CanonicalTime.TicksPerQuarter / 4;
    private const long TripletEighth = CanonicalTime.TicksPerQuarter / 3;

    /// <summary>The finest written grids, 32nds and triplet 16ths (S1-R3): a file whose onsets all sit on them is quantised.</summary>
    private static readonly long[] s_written = [CanonicalTime.TicksPerQuarter / 8, CanonicalTime.TicksPerQuarter / 6];

    /// <summary>The fitting grid the onsets sit closest to: the default for Restore rhythm (S1-R3), or null when none fits.</summary>
    public GridFit? DefaultGrid => Grids.Where(grid => grid.Fits(Onsets)).OrderBy(static grid => grid.TotalOffset).FirstOrDefault();

    /// <summary>The share of onsets off both grids, as a whole percentage rounded down.</summary>
    public int OffGridPercent => Onsets == 0 ? 0 : OffGrid * 100 / Onsets;

    /// <summary>Measures the notes' onsets against the meter's bar lines. Pure and deterministic.</summary>
    public static PerformanceTiming Measure(IReadOnlyList<Note> notes, MeterMap meter)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        long[] fromBar = [.. notes.Select(note => FromBarLine(note.Span.Onset, meter))];
        GridFit[] grids = [Grid("16th", Sixteenth, fromBar), Grid("triplet 8th", TripletEighth, fromBar)];
        int offGrid = fromBar.Count(offset => Math.Abs(Offset(offset, Sixteenth)) > Tolerance && Math.Abs(Offset(offset, TripletEighth)) > Tolerance);
        TimingKind kind = fromBar.Length < MinOnsets ? TimingKind.TooFewNotes
            : Written(fromBar) || offGrid * 10 <= fromBar.Length ? TimingKind.Quantised
            : grids.Any(grid => grid.Fits(fromBar.Length)) ? TimingKind.Performance
            : TimingKind.FreeTempo;
        return new PerformanceTiming(kind, fromBar.Length, offGrid, grids, SwingMeasure.Measure(notes, meter));
    }

    /// <summary>The import screen's sentence about the timing, with the figures, or null when there is nothing to say.</summary>
    public string? Describe() => Describe(Words.English);

    /// <summary>The import screen's sentence about the timing in a language, or null when there is nothing to say.</summary>
    public string? Describe(Words words)
    {
        GridFit sixteenths = Grids[0];
        long mean = sixteenths.MeanOffset(Onsets);
        if (words.IsRussian)
        {
            string разброс = $"от сетки шестнадцатых они отстоят в среднем на {words.Count((int)mean, "tick", "ticks", "тик", "тика", "тиков")}";
            return Kind switch
            {
                TimingKind.Performance when DefaultGrid is { } grid =>
                    $"Этот файл сыгран, а не записан нотами: {OffGridPercent}% начал нот отстоят больше чем на {Tolerance} тиков от сеток шестнадцатых и восьмых триолей, и {разброс}. "
                    + $"Игра следует темпу файла, поэтому «Восстановить ритм» притянет её к {(grid.Step == Sixteenth ? "шестнадцатым" : "восьмым триолям")}: к этой сетке она ближе всего.",
                TimingKind.FreeTempo =>
                    $"Этот файл сыгран без следования его темпу: {OffGridPercent}% начал нот не попадают в сетки шестнадцатых и восьмых триолей, и {разброс}, примерно четверть шага, как будто сетки нет. "
                    + "В этой версии Motiff Lab не умеет восстанавливать игру в свободном темпе, поэтому файл звучит так, как был записан.",
                _ => null,
            };
        }

        string spread = $"on the 16th grid they are {Ticks(mean)} off on average";
        return Kind switch
        {
            TimingKind.Performance when DefaultGrid is { } grid =>
                $"This file was played rather than written: {OffGridPercent}% of onsets are more than {Tolerance} ticks off the 16th and triplet-8th grids, and {spread}. "
                + $"The playing follows the file's tempo, so Restore rhythm would snap it to {grid.Name}s, the grid it sits closest to.",
            TimingKind.FreeTempo =>
                $"This file was played without following its tempo: {OffGridPercent}% of onsets are off the 16th and triplet-8th grids, and {spread}, about a quarter of the step, as if the grid were not there. "
                + "Motiff Lab cannot restore free-tempo playing in this version, so the file plays as it was recorded.",
            _ => null,
        };
    }

    private static string Ticks(long ticks) => ticks == 1 ? "1 tick" : $"{ticks} ticks";

    /// <summary>The onset's distance from its bar line, counting a pickup bar from where its full bar would start.</summary>
    private static long FromBarLine(Tick onset, MeterMap meter)
    {
        MetricPosition position = meter.ToPosition(onset);
        return ((position.Beat - 1L) * meter.SignatureAt(onset).BeatTicks) + position.BeatOffset;
    }

    /// <summary>The signed offset to the nearest point of a grid: late positive, early negative; exactly half a step counts as late.</summary>
    private static long Offset(long fromBar, long step)
    {
        long r = fromBar % step;
        return r * 2 <= step ? r : r - step;
    }

    private static bool Written(long[] fromBar) => fromBar.All(offset => s_written.Any(grid => offset % grid == 0));

    private static GridFit Grid(string name, long step, long[] fromBar)
    {
        var histogram = new int[(int)((step + BinTicks - 1) / BinTicks) + 1];
        long total = 0;
        int near = 0;
        foreach (long offset in fromBar.Select(offset => Offset(offset, step)))
        {
            total += Math.Abs(offset);
            near += Math.Abs(offset) <= Tolerance ? 1 : 0;
            histogram[(int)((offset + (step / 2)) / BinTicks)]++;
        }

        return new GridFit(name, step, near, total, histogram);
    }
}

/// <summary>
/// Where offbeats land in beats that hold two onsets, the first within <see cref="PerformanceTiming.Tolerance"/>
/// of the beat: half way through is straight, two-thirds through is a triplet swing (S1-AI4).
/// </summary>
/// <param name="Beats">The beats measured.</param>
/// <param name="MedianOffbeat">The median position of the second onset, in ticks from the beat.</param>
/// <param name="BeatTicks">The length of the beats measured; when the meter changes beat length, the most common.</param>
public sealed record SwingMeasure(int Beats, long MedianOffbeat, long BeatTicks)
{
    /// <summary>The median offbeat's position as a whole percentage of the beat, rounded down: 50 is straight, 66 a triplet swing.</summary>
    public int Percent => (int)(MedianOffbeat * 100 / BeatTicks);

    internal static SwingMeasure? Measure(IReadOnlyList<Note> notes, MeterMap meter)
    {
        var offbeats = new List<(long Position, long Beat)>();
        Tick[] onsets = [.. notes.Select(static note => note.Span.Onset).Distinct().Order()];
        foreach (IGrouping<(int Bar, int Beat), Tick> beat in onsets.GroupBy(onset => Slot(onset, meter)))
        {
            Tick[] inBeat = [.. beat];
            long length = meter.SignatureAt(inBeat[0]).BeatTicks;
            long first = meter.ToPosition(inBeat[0]).BeatOffset;
            if (inBeat.Length == 2 && Math.Min(first, length - first) <= PerformanceTiming.Tolerance)
            {
                offbeats.Add((meter.ToPosition(inBeat[1]).BeatOffset, length));
            }
        }

        if (offbeats.Count < PerformanceTiming.MinSwingBeats)
        {
            return null;
        }

        long beatTicks = offbeats.GroupBy(static offbeat => offbeat.Beat).OrderByDescending(static group => group.Count()).ThenBy(static group => group.Key).First().Key;
        long[] positions = [.. offbeats.Where(offbeat => offbeat.Beat == beatTicks).Select(static offbeat => offbeat.Position).Order()];
        return new SwingMeasure(positions.Length, positions[(positions.Length - 1) / 2], beatTicks);
    }

    /// <summary>The beat an onset belongs to, counting an onset just before a beat as on it.</summary>
    private static (int Bar, int Beat) Slot(Tick onset, MeterMap meter)
    {
        long beat = meter.SignatureAt(onset).BeatTicks;
        MetricPosition position = meter.ToPosition(onset);
        if (beat - position.BeatOffset <= PerformanceTiming.Tolerance)
        {
            MetricPosition next = meter.ToPosition(onset.Offset(beat - position.BeatOffset));
            return (next.Bar, next.Beat);
        }

        return (position.Bar, position.Beat);
    }
}