using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>The bars of the piece, and which of them are written: all of them, or an excerpt.</summary>
internal sealed class WrittenBars(Bar[] bars, int firstIndex, int lastIndex)
{
    public Bar[] Bars => bars;

    public int FirstIndex => firstIndex;

    public int LastIndex => lastIndex;

    public int Count => lastIndex - firstIndex + 1;

    /// <summary>The tick where the first bar written starts.</summary>
    public long Start => bars[firstIndex].Span.Onset.Value;

    /// <summary>The tick after the last bar written.</summary>
    public long End => bars[lastIndex].Span.End.Value;

    /// <summary>The numbers of the first and last bar written.</summary>
    public BarRange Range => new(bars[firstIndex].Number, bars[lastIndex].Number);

    public bool Holds(int index) => index >= firstIndex && index <= lastIndex;

    /// <summary>The bars up to <paramref name="last"/>, and the ones in <paramref name="bars"/> written, or all of them when it is null.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The piece has none of the bars asked for.</exception>
    public static WrittenBars Of(MeterMap meter, long last, BarRange? bars)
    {
        Bar[] allBars = [.. meter.BarsBefore(new Tick(last))];
        int firstIndex = 0, lastIndex = allBars.Length - 1;
        if (bars is { } range)
        {
            firstIndex = Array.FindIndex(allBars, bar => bar.Number >= range.FirstBar);
            lastIndex = Array.FindLastIndex(allBars, bar => bar.Number <= range.LastBar);
            if (firstIndex < 0 || lastIndex < firstIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(bars), $"The piece has bars {MeiWriter.Number(allBars[0].Number)} to {MeiWriter.Number(allBars[^1].Number)}.");
            }
        }

        return new WrittenBars(allBars, firstIndex, lastIndex);
    }
}