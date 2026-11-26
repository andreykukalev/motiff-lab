using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// A staff laid out in a bar as the score prints it (<see cref="PrintedCell"/>): a layer for each of its voices, the score's
/// notes, rests and spaces in their tuplets and under its beams (by the beat where it writes none), and the voices the
/// generator writes (<see cref="StaffLayout.Layer"/>).
/// </summary>
internal static class PrintedLayout
{
    public static List<List<Element>> Layers(StaffPlan staff, PrintedCell cell, Bar bar, ValueGrid grid, long beamGroup)
    {
        var layers = new List<List<Element>>();
        foreach (PrintedVoice voice in cell.Voices)
        {
            layers.Add(voice.Generated is { } chords
                ? StaffLayout.Layer(staff, chords, layers.Count > 0, bar.Span.Onset.Value, bar.Span.End.Value, grid, beamGroup)
                : Group(voice, [.. voice.Items.Select(item => Elements(staff, item, grid))], grid.Origin, beamGroup));
        }

        return layers;
    }

    /// <summary>An item's elements: one, or the values that fill a space or a rest the generator works out.</summary>
    private static List<Element> Elements(StaffPlan staff, PrintedItem item, ValueGrid grid)
    {
        switch (item.Kind)
        {
            case PrintedKind.Chord:
                var chord = new Chord(item.Start, item.End, item.Notes);
                return [new ChordPiece(item.Start, item.End - item.Start, item.Dur, item.Dots, chord, staff.TakePiece(chord.Notes), item.TieIn, item.TieOut) { Tuplet = item.Tuplet, StemUp = item.Writing?.StemUp }];
            case PrintedKind.Rest:
                return [new Rest(item.Start, item.End - item.Start, item.Dur, item.Dots, space: false) { Tuplet = item.Tuplet }];
            case PrintedKind.MeasureRest:
                return [new MeasureRest()];
            default:
                return [.. BarValues.Fill(item.Start, item.End, grid, space: item.Kind == PrintedKind.Space)];
        }
    }

    /// <summary>
    /// The voice's elements in their tuplets and under the score's beams: a beam inside a tuplet, or around whole tuplets;
    /// where the score writes no beams, or one that crosses a tuplet's edge, eighths and shorter beamed by the beat.
    /// </summary>
    private static List<Element> Group(PrintedVoice voice, List<Element>[] units, long origin, long beamGroup)
    {
        (int First, int Last)[] tuplets = Runs(voice.Items);
        bool Crosses((int First, int Last) beam) => tuplets.Any(tuplet => tuplet.First <= beam.Last && beam.First <= tuplet.Last
            && !(tuplet.First <= beam.First && beam.Last <= tuplet.Last) && !(beam.First <= tuplet.First && tuplet.Last <= beam.Last));
        if (voice.Beams is not { } beams || beams.Any(Crosses))
        {
            return Beaming.Group([.. units.SelectMany(unit => unit)], origin, beamGroup);
        }

        bool Inside((int First, int Last) beam) => tuplets.Any(tuplet => tuplet.First <= beam.First && beam.Last <= tuplet.Last);
        var result = new List<Element>();
        for (int index = 0; index < units.Length;)
        {
            if (EndOf(beams.Where(beam => !Inside(beam)), index) is int outer)
            {
                result.Add(new Beamed([.. Tupled(voice, units, tuplets, index, outer)]));
                index = outer + 1;
            }
            else if (EndOf(tuplets, index) is int last)
            {
                result.Add(new TupletGroup([.. Beamed(units, beams, index, last)], voice.Items[index].Tuplet!.Value));
                index = last + 1;
            }
            else
            {
                result.AddRange(units[index]);
                index++;
            }
        }

        return result;
    }

    /// <summary>The units from <paramref name="first"/> to <paramref name="last"/> under one beam, each whole tuplet among them grouped.</summary>
    private static IEnumerable<Element> Tupled(PrintedVoice voice, List<Element>[] units, (int First, int Last)[] tuplets, int first, int last)
    {
        for (int index = first; index <= last;)
        {
            if (EndOf(tuplets, index) is int end)
            {
                yield return new TupletGroup([.. units[index..(end + 1)].SelectMany(unit => unit)], voice.Items[index].Tuplet!.Value);
                index = end + 1;
            }
            else
            {
                foreach (Element element in units[index])
                {
                    yield return element;
                }

                index++;
            }
        }
    }

    /// <summary>The units from <paramref name="first"/> to <paramref name="last"/> of a tuplet, the beams inside it grouped.</summary>
    private static IEnumerable<Element> Beamed(List<Element>[] units, (int First, int Last)[] beams, int first, int last)
    {
        for (int index = first; index <= last;)
        {
            if (EndOf(beams.Where(beam => beam.Last <= last), index) is int end)
            {
                yield return new Beamed([.. units[index..(end + 1)].SelectMany(unit => unit)]);
                index = end + 1;
            }
            else
            {
                foreach (Element element in units[index])
                {
                    yield return element;
                }

                index++;
            }
        }
    }

    /// <summary>The last item of the span that starts at <paramref name="index"/>; null when none does.</summary>
    private static int? EndOf(IEnumerable<(int First, int Last)> spans, int index) =>
        spans.Where(span => span.First == index).Select(span => (int?)span.Last).FirstOrDefault();

    /// <summary>The first and last item of each tuplet, in order.</summary>
    private static (int First, int Last)[] Runs(PrintedItem[] items)
    {
        var runs = new List<(int, int)>();
        for (int index = 0; index < items.Length;)
        {
            int last = index;
            while (items[index].Tuplet is not null && last + 1 < items.Length && items[last + 1].Tuplet == items[index].Tuplet)
            {
                last++;
            }

            if (items[index].Tuplet is not null)
            {
                runs.Add((index, last));
            }

            index = last + 1;
        }

        return [.. runs];
    }
}