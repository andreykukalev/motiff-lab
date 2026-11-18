using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// The marks of the playing a part writes after its staves, bar by bar (<see cref="ControlEvents"/>): hairpins, fermatas,
/// slurs and pedal marks, each in the bar it starts in. Only the bars written get those that end inside them.
/// </summary>
internal static class ControlPlan
{
    /// <summary>How much earlier than the next note a key may be let go, still legato: 30 ticks, a 64th.</summary>
    private const long LegatoGap = CanonicalTime.TicksPerQuarter / 16;

    /// <summary>The longest step between two notes under one slur: a half note.</summary>
    private const long MostLegatoStep = 2 * CanonicalTime.TicksPerQuarter;

    /// <summary>
    /// The control events of each bar of the piece, with how many slurs and pedal marks are written: none without
    /// <paramref name="marks"/>; with <paramref name="significant"/> marks no slurs or pedal.
    /// </summary>
    public static (ControlEvents[] Controls, int Slurs, int Pedals) Of(List<StaffPlan> staves, WrittenBars written, PlayingMarks? marks, IEnumerable<TickSpan>? pedal, bool significant)
    {
        Bar[] bars = written.Bars;
        var controls = new ControlEvents[bars.Length];
        for (int index = 0; index < bars.Length; index++)
        {
            controls[index] = new ControlEvents();
        }

        if (marks is null)
        {
            return (controls, 0, 0);
        }

        AddHairpins(controls, marks, written);
        AddFermatas(controls, marks, written);
        int slurCount = significant ? 0 : AddSlurs(controls, staves, written);
        int pedalCount = AddPedals(controls, staves, written, significant || pedal is null ? [] : pedal);
        return (controls, slurCount, pedalCount);
    }

    /// <summary>The index of the bar that holds <paramref name="tick"/>: the last bar for a tick at or after the end.</summary>
    public static int BarIndex(Bar[] bars, long tick)
    {
        int low = 0, high = bars.Length - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (bars[middle].Span.Onset.Value <= tick)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>
    /// Legato on the top staff of each part: the melody (the highest note at each onset) under a slur
    /// wherever at least three of its notes follow each other with the key held to the next one
    /// (within <see cref="LegatoGap"/>), at most two bars to a slur, ending at a bar line.
    /// </summary>
    public static IEnumerable<(Chord From, Chord To, int Staff)> Slurs(List<StaffPlan> staves, Bar[] bars)
    {
        foreach (StaffPlan staff in staves.GroupBy(staff => staff.Track).Select(track => track.MinBy(staff => staff.Number)!))
        {
            Chord[] line = [.. staff.Voices.SelectMany(voice => voice)
                .GroupBy(chord => chord.Onset)
                .Select(group => group.MaxBy(chord => chord.Notes[^1].Note.Pitch.MidiNumber)!)
                .OrderBy(chord => chord.Onset)];
            int start = 0;
            for (int i = 1; i <= line.Length; i++)
            {
                bool joined = i < line.Length
                    && line[i].Onset - line[i - 1].Onset <= MostLegatoStep
                    && line[i - 1].Notes[^1].Note.Span.End.Value + LegatoGap >= line[i].Notes[^1].Note.Span.Onset.Value
                    && BarIndex(bars, line[i].Onset) - BarIndex(bars, line[start].Onset) < 2;
                if (joined)
                {
                    continue;
                }

                if (i - start >= 3)
                {
                    yield return (line[start], line[i - 1], staff.Number);
                }

                start = i;
            }
        }
    }

    /// <summary>Adds each hairpin to the bar it starts in.</summary>
    private static void AddHairpins(ControlEvents[] controls, PlayingMarks marks, WrittenBars written)
    {
        Bar[] bars = written.Bars;
        foreach ((long start, long stop, bool crescendo) in marks.Hairpins)
        {
            // Longer than a bar a hairpin is thin and hard to follow, so it is written as a word; so is one that ends beyond the bars written.
            int first = BarIndex(bars, start), final = BarIndex(bars, stop);
            bool word = stop - start > bars[first].Signature.BarTicks || !written.Holds(final);
            controls[first].Hairpins.Add(new HairpinPlan(start, crescendo, word ? null : $"{MeiWriter.Number(final - first)}m+{MeiWriter.Beat(bars[final], stop)}"));
        }
    }

    /// <summary>Adds each fermata in the bars written to its bar.</summary>
    private static void AddFermatas(ControlEvents[] controls, PlayingMarks marks, WrittenBars written)
    {
        foreach (long held in marks.Fermatas.Where(at => at >= written.Start && at < written.End))
        {
            controls[BarIndex(written.Bars, held)].Fermatas.Add(held);
        }
    }

    /// <summary>Adds each slur that ends in the bars written to the bar it starts in; returns how many.</summary>
    private static int AddSlurs(ControlEvents[] controls, List<StaffPlan> staves, WrittenBars written)
    {
        int slurCount = 0;
        foreach ((Chord from, Chord to, int staff) in Slurs(staves, written.Bars))
        {
            // A slur to a chord beyond the bars written has nowhere to end.
            if (to.Onset >= written.End)
            {
                continue;
            }

            controls[BarIndex(written.Bars, from.Onset)].Slurs.Add((from, to, staff));
            slurCount++;
        }

        return slurCount;
    }

    /// <summary>Adds "Ped." and "*" under the bottom staff for each time the pedal was held a 32nd or more in the bars written; returns how many.</summary>
    private static int AddPedals(ControlEvents[] controls, List<StaffPlan> staves, WrittenBars written, IEnumerable<TickSpan> pedals)
    {
        Bar[] bars = written.Bars;
        int pedalCount = 0;
        int bottom = staves.Max(staff => staff.Number);
        foreach (TickSpan held in pedals.OrderBy(span => span.Onset))
        {
            long down = NotationGenerator.Snap(held.Onset.Value), up = Math.Min(NotationGenerator.Snap(held.End.Value), written.End);
            if (up - down < NotationGenerator.Grid || down >= written.End)
            {
                continue;
            }

            controls[BarIndex(bars, down)].Pedals.Add((down, true, bottom));
            controls[BarIndex(bars, Math.Min(up, written.End - 1))].Pedals.Add((up, false, bottom));
            pedalCount++;
        }

        return pedalCount;
    }
}