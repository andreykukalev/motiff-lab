using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The rhythm lens measured (S1-A1): lengths, onset grid, silences, how beats are subdivided, syncopations, accents
/// where the file marks them, and rhythms that repeat inside the motif. Each measure is a record; the sentences are
/// written from them in <c>MotifAnalyzer.Rhythm.Facts.cs</c>.
/// </summary>
/// <remarks>
/// An onset is where one or more notes of the motif start; notes that start together count once,
/// and the longest of them decides how long the onset is held.
/// </remarks>
internal static partial class MotifAnalyzer
{
    /// <summary>One onset of the motif and how long its longest note sounds.</summary>
    private readonly record struct Hit(Tick At, Tick End);

    /// <summary>A beat of the selection: where it starts and ends, and the bar it is in.</summary>
    private readonly record struct BeatSlot(int Bar, Tick Start, Tick End);

    /// <summary>The notes of each note value, the most common first (the longer on a tie), and the notes of no single value.</summary>
    private sealed record DurationMeasure(IReadOnlyList<(NoteValue Value, Note[] Notes)> Values, IReadOnlyList<Note> Other);

    /// <summary>
    /// The onsets against the grids: how many there are, fall on a beat and fall off every grid (with the notes there),
    /// the grid they fit (<see cref="s_grids"/>' index) and the triplet grid they also use, and where the first is.
    /// </summary>
    private sealed record GridMeasure(int Onsets, int OnBeat, int OffGrid, Note[] OffGridNotes, int Grid, int? Triplets, MetricPosition Start, Tick First);

    /// <summary>Each silence between the motif's notes, and the silence the selection opens with, if any.</summary>
    private sealed record SilenceMeasure(IReadOnlyList<TickSpan> Gaps, TickSpan? Opening);

    /// <summary>The onsets in each beat with its bar, and the most common count (the larger on a tie) with how many beats have it.</summary>
    private sealed record GroupingMeasure(IReadOnlyList<(int Bar, int Count)> Beats, int Common, int CommonBeats);

    /// <summary>
    /// A syncopated onset: where it is, whether it is held across the bar line rather than into the next beat, and the
    /// notes of the onset that sound past the beat or the bar line.
    /// </summary>
    private sealed record Syncopation(MetricPosition At, bool AcrossBar, Note[] Notes);

    /// <summary>
    /// A figure inside one beat: the rest before its first onset (0 for none) and each onset's length to the next onset
    /// or the end of the beat, a length that is no single note value as -1.
    /// </summary>
    private sealed record BeatFigure(long Rest, long[] Lengths)
    {
        /// <summary>Two figures are the same figure when their keys are equal.</summary>
        public string Key => $"{Rest}:{string.Join(",", Lengths)}";
    }

    /// <summary>The bars wholly inside the selection that share a rhythm, in groups by their first bar; the beat figure that repeats most, with the beats it fills.</summary>
    private sealed record PatternMeasure(IReadOnlyList<int[]> RepeatedBars, BeatFigure? Figure, IReadOnlyList<BeatSlot> FigureBeats);

    /// <summary>The rhythm facts in display order, each measured and then written.</summary>
    private static List<MotifFact> RhythmFacts(Note[] chosen, Hit[] hits, MeterMap meter, TickSpan span, MusicalKey key, IEnumerable<AccentMark>? accents, Words w)
    {
        bool offGrid = OffGridCount(chosen, meter) > 0;
        var facts = new List<MotifFact>
        {
            DurationsFact(MeasureDurations(chosen), offGrid, w),
            GridFact(MeasureGrid(chosen, hits, meter), chosen, w),
            SilenceFact(MeasureSilences(chosen, span), w),
            GroupingFact(MeasureGrouping(hits, meter, span), offGrid, w),
            SyncopationFact(Syncopations(chosen, hits, meter), offGrid, w),
        };
        if (accents is not null)
        {
            facts.Add(AccentFact(MeasureAccents(chosen, accents), chosen.Length, meter, key, w));
        }

        facts.Add(PatternFact(MeasurePatterns(hits, meter, span), chosen, meter, offGrid, w));
        return facts;
    }

    private static Hit[] Hits(IEnumerable<IGrouping<Tick, Note>> onsets) =>
        [.. onsets.Select(group => new Hit(group.Key, group.Max(note => note.Span.End))).OrderBy(hit => hit.At)];

    /// <summary>The beats a beat-aligned span covers, each cut at its bar line.</summary>
    private static List<BeatSlot> Beats(MeterMap meter, TickSpan span)
    {
        var beats = new List<BeatSlot>();
        Tick start = span.Onset;
        while (start < span.End)
        {
            Bar bar = meter.BarAt(start);
            MetricPosition position = meter.ToPosition(start);
            long end = Math.Min(start.Value + bar.Signature.BeatTicks - position.BeatOffset, Math.Min(bar.Span.End.Value, span.End.Value));
            beats.Add(new BeatSlot(bar.Number, start, new Tick(end)));
            start = new Tick(end);
        }

        return beats;
    }

    private static DurationMeasure MeasureDurations(Note[] notes)
    {
        var counts = new List<(NoteValue Value, Note[] Notes)>();
        var other = new List<Note>();
        foreach (IGrouping<long, Note> group in notes.GroupBy(note => note.Span.Duration))
        {
            if (ValueOf(group.Key) is { } value)
            {
                counts.Add((value, [.. group]));
            }
            else
            {
                other.AddRange(group);
            }
        }

        return new DurationMeasure([.. counts.OrderByDescending(value => value.Notes.Length).ThenByDescending(value => value.Value.Ticks)], other);
    }

    private static GridMeasure MeasureGrid(Note[] notes, Hit[] hits, MeterMap meter)
    {
        int onBeat = hits.Count(hit => OffsetInBar(meter, hit.At) % meter.SignatureAt(hit.At).BeatTicks == 0);
        int offGrid = 0;
        int finestStraight = -1;
        int finestTriplet = -1;
        foreach (Hit hit in hits)
        {
            long offset = OffsetInBar(meter, hit.At);
            int index = Array.FindIndex(s_grids, grid => offset % grid.Ticks == 0);
            if (index < 0)
            {
                offGrid++;
            }
            else if (s_grids[index].Triplet)
            {
                finestTriplet = Math.Max(finestTriplet, index);
            }
            else
            {
                finestStraight = Math.Max(finestStraight, index);
            }
        }

        // A triplet grid covers straight onsets that fall on its lines, such as quarters on the eighth-note triplet grid.
        (int grid, int? triplets) = (finestStraight, finestTriplet) switch
        {
            ( < 0, _) => (finestTriplet, (int?)null),
            (_, < 0) => (finestStraight, null),
            _ when s_grids[finestStraight].Ticks % s_grids[finestTriplet].Ticks == 0 => (finestTriplet, null),
            _ => (finestStraight, finestTriplet),
        };
        Note[] offGridNotes = [.. notes.Where(note => !OnGrid(meter, note.Span.Onset))];
        return new GridMeasure(hits.Length, onBeat, offGrid, offGridNotes, grid, triplets, meter.ToPosition(hits[0].At), hits[0].At);
    }

    private static SilenceMeasure MeasureSilences(Note[] notes, TickSpan span)
    {
        var gaps = new List<TickSpan>();
        Tick soundingUntil = notes[0].Span.End;
        foreach (Note note in notes.Skip(1))
        {
            if (note.Span.Onset > soundingUntil)
            {
                gaps.Add(TickSpan.FromBounds(soundingUntil, note.Span.Onset));
            }

            if (note.Span.End > soundingUntil)
            {
                soundingUntil = note.Span.End;
            }
        }

        return new SilenceMeasure(gaps, notes[0].Span.Onset > span.Onset ? TickSpan.FromBounds(span.Onset, notes[0].Span.Onset) : null);
    }

    private static GroupingMeasure MeasureGrouping(Hit[] hits, MeterMap meter, TickSpan span)
    {
        (int Bar, int Count)[] beats = [.. Beats(meter, span).Select(beat => (beat.Bar, hits.Count(hit => hit.At >= beat.Start && hit.At < beat.End)))];
        IGrouping<int, int> common = beats.Select(beat => beat.Count).GroupBy(count => count).OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key).First();
        return new GroupingMeasure(beats, common.Key, common.Count());
    }

    /// <summary>
    /// Each syncopated onset in time order: off the beat and still sounding on the next beat, or on a beat other than
    /// the first and sounding across the bar line; shared by the syncopation fact and the syncopation share (S1-A5).
    /// </summary>
    private static List<Syncopation> Syncopations(Note[] notes, Hit[] hits, MeterMap meter)
    {
        var found = new List<Syncopation>();
        foreach (Hit hit in hits)
        {
            Bar bar = meter.BarAt(hit.At);
            MetricPosition position = meter.ToPosition(hit.At);
            (bool AcrossBar, Tick Past)? syncopation = null;
            if (position.BeatOffset != 0)
            {
                Tick nextBeat = hit.At.Offset(bar.Signature.BeatTicks - position.BeatOffset);
                if (hit.End > nextBeat)
                {
                    syncopation = (nextBeat >= bar.Span.End, nextBeat);
                }
            }
            else if (position.Beat != 1 && hit.End > bar.Span.End)
            {
                syncopation = (true, bar.Span.End);
            }

            if (syncopation is { } held)
            {
                found.Add(new Syncopation(position, held.AcrossBar, [.. notes.Where(note => note.Span.Onset == hit.At && note.Span.End > held.Past)]));
            }
        }

        return found;
    }

    /// <summary>The motif's notes that carry an accent mark, in order, each with its accent; a strong accent wins over a plain one on the same note.</summary>
    private static List<(Note Note, AccentKind Kind)> MeasureAccents(Note[] notes, IEnumerable<AccentMark> accents)
    {
        var marks = new Dictionary<string, AccentKind>(StringComparer.Ordinal);
        foreach (AccentMark mark in accents)
        {
            if (!marks.TryGetValue(mark.NoteId, out AccentKind kind) || mark.Kind > kind)
            {
                marks[mark.NoteId] = mark.Kind;
            }
        }

        return [.. notes.Where(note => marks.ContainsKey(note.Id)).Select(note => (note, marks[note.Id]))];
    }

    private static PatternMeasure MeasurePatterns(Hit[] hits, MeterMap meter, TickSpan span)
    {
        (BeatFigure? figure, BeatSlot[] beats) = RepeatedFigure(hits, meter, span);
        return new PatternMeasure(RepeatedBars(hits, meter, span), figure, beats);
    }

    /// <summary>Bars wholly inside the selection with the same onsets from the bar line, in groups ordered by their first bar.</summary>
    private static int[][] RepeatedBars(Hit[] hits, MeterMap meter, TickSpan span)
    {
        var bars = new List<(int Number, string Rhythm)>();
        for (Bar bar = meter.BarAt(span.Onset); bar.Span.Onset < span.End; bar = meter.GetBar(bar.Number + 1))
        {
            Hit[] inBar = [.. hits.Where(hit => hit.At >= bar.Span.Onset && hit.At < bar.Span.End)];
            if (bar.Span.Onset >= span.Onset && bar.Span.End <= span.End && inBar.Length > 0)
            {
                bars.Add((bar.Number, $"{bar.Span.Duration}:{string.Join(",", inBar.Select(hit => hit.At.Value - bar.Span.Onset.Value))}"));
            }
        }

        return [.. bars
            .GroupBy(bar => bar.Rhythm)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.First().Number)
            .Select(group => group.Select(bar => bar.Number).ToArray())];
    }

    /// <summary>
    /// The figure inside a beat that fills more beats than any other, the first on a tie, with the beats it fills;
    /// none when no figure repeats. A beat holding one note on the beat is no figure.
    /// </summary>
    private static (BeatFigure? Figure, BeatSlot[] Beats) RepeatedFigure(Hit[] hits, MeterMap meter, TickSpan span)
    {
        var figures = new List<BeatFigure>();
        var beats = new List<BeatSlot>();
        foreach (BeatSlot beat in Beats(meter, span))
        {
            Hit[] inBeat = [.. hits.Where(hit => hit.At >= beat.Start && hit.At < beat.End)];
            if (inBeat.Length == 0 || (inBeat.Length == 1 && inBeat[0].At == beat.Start))
            {
                continue;
            }

            long rest = inBeat[0].At > beat.Start ? AsNoteValue(inBeat[0].At.Value - beat.Start.Value) : 0;
            long[] lengths = [.. inBeat.Select((hit, i) => AsNoteValue((i + 1 < inBeat.Length ? inBeat[i + 1].At : beat.End).Value - hit.At.Value))];
            figures.Add(new BeatFigure(rest, lengths));
            beats.Add(beat);
        }

        string[] keys = [.. figures.Select(figure => figure.Key)];
        IGrouping<string, string>? repeated = keys
            .GroupBy(key => key)
            .Where(group => group.Count() > 1)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => Array.IndexOf(keys, group.Key))
            .FirstOrDefault();
        return repeated is null
            ? (null, [])
            : (figures[Array.IndexOf(keys, repeated.Key)], [.. beats.Where((beat, i) => keys[i] == repeated.Key)]);
    }

    /// <summary>A length in ticks when it is a single note value (<see cref="s_values"/>), otherwise -1.</summary>
    private static long AsNoteValue(long ticks) => ValueOf(ticks) is null ? -1 : ticks;
}