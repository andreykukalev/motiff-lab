using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// The tuplets of a printed voice (<see cref="PrintedBars"/>): its notes and rests with a ratio, grouped as the score's
/// brackets group them; in a score that writes no brackets, a group ends where it reaches a beat (the pulse of the bar).
/// </summary>
internal static class PrintedTuplets
{
    /// <summary>The items, each in a tuplet with its ratio set to the group it is in.</summary>
    public static PrintedItem[] Group(List<PrintedItem> items, Bar bar)
    {
        PrintedItem[] grouped = [.. items];
        bool brackets = grouped.Any(item => item.Writing?.TupletStart == true);
        long pulse = BarValues.Pulse(bar.Signature);
        int open = -1;
        for (int index = 0; index < grouped.Length; index++)
        {
            PrintedItem item = grouped[index];
            MusicXmlTuplet? ratio = Ratio(item);
            if (open >= 0 && (ratio is null || ratio != Ratio(grouped[open]) || (brackets && item.Writing!.TupletStart)))
            {
                Close(grouped, open, index - 1);
                open = -1;
            }

            if (ratio is null)
            {
                continue;
            }

            open = open < 0 ? index : open;
            if (brackets ? item.Writing!.TupletStop : (item.End - bar.Origin) % pulse == 0)
            {
                Close(grouped, open, index);
                open = -1;
            }
        }

        if (open >= 0)
        {
            Close(grouped, open, grouped.Length - 1);
        }

        return grouped;
    }

    /// <summary>The tuplet ratio a note or rest is written with; null for one outside tuplets, a space or a bar's rest.</summary>
    private static MusicXmlTuplet? Ratio(PrintedItem item) => item.Kind is PrintedKind.Chord or PrintedKind.Rest ? item.Writing?.Tuplet : null;

    /// <summary>Puts the items from <paramref name="first"/> to <paramref name="last"/> in one tuplet: the score's ratio over their time.</summary>
    private static void Close(PrintedItem[] items, int first, int last)
    {
        MusicXmlTuplet ratio = Ratio(items[first])!;
        var span = new TupletSpan(items[first].Start, items[last].End - items[first].Start, ratio.Actual, ratio.Normal);
        for (int index = first; index <= last; index++)
        {
            items[index] = items[index] with { Tuplet = span };
        }
    }
}