using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// The note values a span of a bar is written in: each the longest that fits and starts on a multiple of its own length or
/// of the pulse (the dotted quarter in compound meters), and does not cross the middle of a 4/4 or 12/8 bar unless it
/// starts the bar; inside a triplet beat, three values in the time of two.
/// </summary>
internal static class BarValues
{
    // Note values from longest to shortest: ticks, MEI @dur, dots. The last three are only for
    // bars whose beats are shorter than the grid (x/64, x/128), so every bar can be filled.
    private static readonly (long Ticks, int Dur, int Dots)[] s_values =
    [
        (1920, 1, 0), (1440, 2, 1), (960, 2, 0), (720, 4, 1), (480, 4, 0), (360, 8, 1), (240, 8, 0),
        (180, 16, 1), (120, 16, 0), (60, 32, 0), (30, 64, 0), (15, 128, 0),
    ];

    /// <summary>The span a note value has to start on a multiple of: the beat, or three beats in compound meters (6/8, 9/8, 12/16).</summary>
    public static long Pulse(TimeSignature signature) =>
        IsCompound(signature) ? 3 * signature.BeatTicks : signature.BeatTicks;

    /// <summary>
    /// The middle of a bar of four beats or pulses (4/4, 12/8): only a value that starts the bar may
    /// cross it, so the second half of the bar stays visible. Zero for other meters.
    /// </summary>
    public static long Middle(TimeSignature signature)
    {
        int pulses = IsCompound(signature) ? signature.Numerator / 3 : signature.Numerator;
        return pulses >= 4 && pulses % 2 == 0 ? signature.BarTicks / 2 : 0;
    }

    public static bool IsCompound(TimeSignature signature) =>
        signature.Denominator >= 8 && signature.Numerator > 3 && signature.Numerator % 3 == 0;

    public static IEnumerable<(long Start, long Ticks, int Dur, int Dots)> Decompose(long from, long to, ValueGrid grid)
    {
        for (long position = from; position < to;)
        {
            long offset = position - grid.Origin;
            (long ticks, int dur, int dots) = s_values.FirstOrDefault(value => value.Ticks <= to - position
                && offset % Math.Min(value.Ticks, grid.Pulse) == 0
                && (grid.Middle == 0 || offset == 0 || offset >= grid.Middle || offset + value.Ticks <= grid.Middle));
            if (ticks == 0)
            {
                throw new InvalidOperationException($"No note value fits at {position}t before {to}t.");
            }

            yield return (position, ticks, dur, dots);
            position += ticks;
        }
    }

    public static IEnumerable<Element> Fill(long from, long to, ValueGrid grid, bool space) =>
        Decompose(from, to, grid).Select(value => new Rest(value.Start, value.Ticks, value.Dur, value.Dots, space));

    public static IEnumerable<Element> Fill(long from, long to, ValueGrid grid, bool space, TupletSpan[] tuplets) =>
        Pieces(from, to, grid, tuplets).Select(value => new Rest(value.Start, value.Ticks, value.Dur, value.Dots, space) { Tuplet = value.Tuplet });

    /// <summary>
    /// Note values from one tick to another, as <see cref="Decompose"/> writes them, except inside a triplet
    /// beat: there, three values take the time of two, so each is written as its length times 3/2.
    /// </summary>
    public static IEnumerable<(long Start, long Ticks, int Dur, int Dots, TupletSpan? Tuplet)> Pieces(long from, long to, ValueGrid grid, TupletSpan[] tuplets)
    {
        for (long position = from; position < to;)
        {
            if (tuplets.FirstOrDefault(span => span.Start <= position && position < span.End) is { Length: > 0 } span)
            {
                long stop = Math.Min(to, span.End);
                var written = new ValueGrid(0, span.Step * 3 / 2, 0);
                foreach ((long start, long ticks, int dur, int dots) in Decompose((position - span.Start) * 3 / 2, (stop - span.Start) * 3 / 2, written))
                {
                    yield return (span.Start + (start * 2 / 3), ticks * 2 / 3, dur, dots, span);
                }

                position = stop;
            }
            else
            {
                long stop = Math.Min(to, tuplets.Where(next => next.Start > position).Select(next => next.Start).DefaultIfEmpty(to).Min());
                foreach ((long start, long ticks, int dur, int dots) in Decompose(position, stop, grid))
                {
                    yield return (start, ticks, dur, dots, null);
                }

                position = stop;
            }
        }
    }
}