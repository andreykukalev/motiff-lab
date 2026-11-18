using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// A staff laid out in a bar: each voice's chords cut at the bar lines and tied, written in note values with rests or
/// spaces between (<see cref="BarValues"/>), their triplet beats as tuplets, beamed (<see cref="Beaming"/>), with the
/// bar's accidentals (<see cref="BarAccidentals"/>).
/// </summary>
internal static class StaffLayout
{
    public static StaffMeasure LayOut(StaffPlan staff, Bar bar, int fifths, TransposedSpelling? spelling)
    {
        var grid = new ValueGrid(bar.IsPickup ? bar.Span.End.Value - bar.Signature.BarTicks : bar.Span.Onset.Value, BarValues.Pulse(bar.Signature), BarValues.Middle(bar.Signature));
        long beamGroup = Beaming.BeamGroup(bar.Signature);
        long start = bar.Span.Onset.Value;
        long stop = bar.Span.End.Value;

        var layers = new List<List<Element>>();
        for (int index = 0; index < Math.Max(1, staff.Voices.Count); index++)
        {
            Chord[] inBar = index < staff.Voices.Count
                ? [.. staff.Voices[index].Where(chord => chord.Onset < stop && chord.End > start)]
                : [];
            layers.Add(inBar.Length == 0 ? EmptyLayer(staff, bar, index, grid) : Layer(staff, inBar, index > 0, start, stop, grid, beamGroup));
        }

        BarAccidentals.Write(layers, fifths, spelling);
        return new StaffMeasure(layers);
    }

    /// <summary>A voice with no chord in the bar: left out, but for the top voice, which rests for the whole bar when no voice plays.</summary>
    private static List<Element> EmptyLayer(StaffPlan staff, Bar bar, int index, ValueGrid grid)
    {
        long start = bar.Span.Onset.Value;
        long stop = bar.Span.End.Value;
        if (index == 0 && staff.Voices.All(voice => !voice.Any(chord => chord.Onset < stop && chord.End > start)))
        {
            return bar.IsComplete && !bar.IsPickup ? [new MeasureRest()] : [.. BarValues.Fill(start, stop, grid, space: false)];
        }

        return [];
    }

    /// <summary>A voice's chords in the bar, with rests between, or spaces in a lower voice (<paramref name="space"/>).</summary>
    private static List<Element> Layer(StaffPlan staff, Chord[] inBar, bool space, long start, long stop, ValueGrid grid, long beamGroup)
    {
        TupletSpan[] tuplets = WrittenTuplets(staff.Tuplets, inBar, start, stop);
        var elements = new List<Element>();
        long cursor = start;
        foreach (Chord chord in inBar)
        {
            long from = Math.Max(chord.Onset, start);
            long to = Math.Min(chord.End, stop);
            elements.AddRange(BarValues.Fill(cursor, from, grid, space, tuplets));
            if (from == chord.Onset)
            {
                TupletSpan? around = tuplets.FirstOrDefault(span => span.Start <= from && from < span.End) is { Length: > 0 } found ? found : null;
                elements.AddRange(chord.Graces.Select(grace => new ChordPiece(from, 0, 8, 0, grace, 0, tieIn: false, tieOut: false) { IsGrace = true, Tuplet = around }));
            }

            foreach ((long pieceStart, long ticks, int dur, int dots, TupletSpan? tuplet) in BarValues.Pieces(from, to, grid, tuplets))
            {
                elements.Add(new ChordPiece(pieceStart, ticks, dur, dots, chord, chord.NextPiece++, pieceStart > chord.Onset, pieceStart + ticks < chord.End) { Tuplet = tuplet });
            }

            cursor = to;
        }

        elements.AddRange(BarValues.Fill(cursor, stop, grid, space, tuplets));
        return Beaming.Group(elements, grid.Origin, beamGroup);
    }

    /// <summary>The staff's triplet beats in the bar that a voice writes as tuplets: only where it starts or ends a note between the 16ths.</summary>
    private static TupletSpan[] WrittenTuplets(TupletSpan[] tuplets, Chord[] inBar, long start, long stop) =>
        [.. tuplets.Where(span => span.Start >= start && span.End <= stop && inBar.Any(chord =>
            (chord.Onset > span.Start && chord.Onset < span.End && (chord.Onset - span.Start) % (span.Length / 4) != 0)
            || (chord.End > span.Start && chord.End < span.End && (chord.End - span.Start) % (span.Length / 4) != 0)))];
}