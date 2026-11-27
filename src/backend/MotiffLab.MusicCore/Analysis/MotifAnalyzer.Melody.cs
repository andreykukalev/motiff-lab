using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The melody lens measured (S1-A2): the line, its intervals and contour, range, scale degrees, the interval size that
/// leads, repeated notes, melody notes that sound with lower notes, and the phrase boundaries the source marks or the
/// player set. Each measure is a record; the sentences are written from them in <c>MotifAnalyzer.Melody.Facts.cs</c>.
/// </summary>
internal static partial class MotifAnalyzer
{
    /// <summary>
    /// The melody: the texture it was read from, the line (the top line when notes sound together), the semitones from
    /// each note to the next, and their directions.
    /// </summary>
    private sealed record LineMeasure(MotifTexture Texture, Note[] Line, bool TopNotes, int[] Intervals, ContourStep[] Contour)
    {
        /// <summary>A top line is inferred; notes read one at a time are detected.</summary>
        public FactCertainty Certainty => TopNotes ? FactCertainty.Inferred : FactCertainty.Detected;
    }

    /// <summary>The intervals as steps (1 or 2 semitones), leaps and repeated notes, and the largest, the higher one on a tie.</summary>
    private sealed record StepMeasure(int Steps, int Leaps, int Repeats, int Largest);

    /// <summary>
    /// The contour's runs (the directions once each, leaving out repeated notes), the semitones from the first note to
    /// the last, and where the line turns: the index of each interval that leaves in a new direction.
    /// </summary>
    private sealed record ContourMeasure(IReadOnlyList<ContourStep> Runs, int Net, IReadOnlyList<int> Turns);

    /// <summary>The interval size, up or down, that clearly leads; how many intervals have it, and the notes of each.</summary>
    private sealed record CommonSize(int Size, int Count, Note[] Notes);

    /// <summary>Each run of a pitch repeated straight away (its first index and the index after it), and the pitch that clearly sounds most often.</summary>
    private sealed record RepeatMeasure(IReadOnlyList<(int Start, int End)> Runs, IGrouping<int, Note>? MostOften);

    /// <summary>How many melody notes are struck with lower notes or sound over notes still held, and each one whose pitch class a lower note shares.</summary>
    private sealed record OverMeasure(int Struck, int Held, IReadOnlyList<(Note Note, Note[] Doubled)> Shared);

    /// <summary>The phrase boundaries after the motif's first onset up to its last, the one at its first onset, and whether any cited is a reading of the notes.</summary>
    private sealed record PhraseMeasure(PhraseBoundary[] Inside, PhraseBoundary? AtStart, bool Inferred);

    /// <summary>The melody facts in display order, each measured and then written.</summary>
    private static List<MotifFact> MelodyFacts(Note[] chosen, LineMeasure line, KeyReading key, MeterMap meter, IEnumerable<PhraseBoundary>? phrases, Words w)
    {
        var facts = new List<MotifFact> { LineFact(line, key.Key, w) };
        if (line.Intervals.Length > 0)
        {
            facts.Add(IntervalsFact(line, MeasureSteps(line.Intervals), key.Key, w));
            facts.Add(ContourFact(line, MeasureContour(line.Contour, line.Intervals), key.Key, w));
        }

        facts.Add(RangeFact(MeasureRegister(chosen), chosen, key.Key, w));
        facts.Add(DegreesFact(line, [.. line.Line.Select(note => Degree(note.Pitch, key.Key))], key, w));
        if (line.Intervals.Length > 0)
        {
            facts.Add(IntervalNamesFact(line, MeasureCommonSize(line.Line, line.Intervals), w));
        }

        facts.Add(RepeatedNotesFact(line, MeasureRepeats(line.Line), meter, key.Key, w));
        if (line.TopNotes)
        {
            facts.Add(SoundingWithFact(line.Line, MeasureOver(line.Texture), key.Key, w));
        }

        if (phrases is not null)
        {
            facts.Add(PhraseFact(MeasurePhrases(chosen, phrases), chosen, meter, w));
        }

        return facts;
    }

    private static LineMeasure MeasureLine(Note[] notes)
    {
        MotifTexture texture = MotifTexture.Read(notes);
        Note[] line = [.. texture.Melody];
        int[] intervals = [.. line.Zip(line.Skip(1), (from, to) => to.Pitch.MidiNumber - from.Pitch.MidiNumber)];
        ContourStep[] contour = [.. intervals.Select(step => step > 0 ? ContourStep.Up : step < 0 ? ContourStep.Down : ContourStep.Same)];
        return new LineMeasure(texture, line, !texture.IsSingleLine, intervals, contour);
    }

    private static StepMeasure MeasureSteps(int[] intervals) => new(
        intervals.Count(step => Math.Abs(step) is 1 or 2),
        intervals.Count(step => Math.Abs(step) > 2),
        intervals.Count(step => step == 0),
        intervals.OrderByDescending(Math.Abs).ThenByDescending(step => step).First());

    private static ContourMeasure MeasureContour(ContourStep[] contour, int[] intervals)
    {
        var runs = new List<ContourStep>();
        foreach (ContourStep step in contour.Where(step => step != ContourStep.Same))
        {
            if (runs.Count == 0 || runs[^1] != step)
            {
                runs.Add(step);
            }
        }

        var turns = new List<int>();
        ContourStep? direction = null;
        for (int i = 0; i < contour.Length; i++)
        {
            if (contour[i] == ContourStep.Same)
            {
                continue;
            }

            if (direction is not null && contour[i] != direction)
            {
                turns.Add(i);
            }

            direction = contour[i];
        }

        return new ContourMeasure(runs, intervals.Sum(), turns);
    }

    /// <summary>The group with the most items when it has more than one and more than any other group; null when no group leads.</summary>
    private static IGrouping<int, T>? ClearlyMost<T>(IEnumerable<T> items, Func<T, int> key)
    {
        IGrouping<int, T>[] ranked = [.. items.GroupBy(key).OrderByDescending(group => group.Count()).ThenBy(group => group.Key)];
        return ranked[0].Count() > 1 && (ranked.Length == 1 || ranked[0].Count() > ranked[1].Count()) ? ranked[0] : null;
    }

    private static CommonSize? MeasureCommonSize(Note[] line, int[] intervals)
    {
        if (ClearlyMost(intervals, Math.Abs) is not { } leader)
        {
            return null;
        }

        Note[] notes = [.. Enumerable.Range(0, intervals.Length)
            .Where(i => Math.Abs(intervals[i]) == leader.Key)
            .SelectMany(i => new[] { line[i], line[i + 1] })
            .Distinct()];
        return new CommonSize(leader.Key, leader.Count(), notes);
    }

    private static RepeatMeasure MeasureRepeats(Note[] line)
    {
        var runs = new List<(int Start, int End)>();
        for (int start = 0; start < line.Length;)
        {
            int end = start + 1;
            while (end < line.Length && line[end].Pitch.MidiNumber == line[start].Pitch.MidiNumber)
            {
                end++;
            }

            if (end - start > 1)
            {
                runs.Add((start, end));
            }

            start = end;
        }

        return new RepeatMeasure(runs, ClearlyMost(line, note => note.Pitch.MidiNumber));
    }

    private static OverMeasure MeasureOver(MotifTexture texture)
    {
        int struck = 0;
        int held = 0;
        var shared = new List<(Note Note, Note[] Doubled)>();
        foreach (Note note in texture.Melody)
        {
            Note[] lower = [.. texture.SoundingWith(note).Where(other => other.Pitch < note.Pitch)];
            if (lower.Length == 0)
            {
                continue;
            }

            if (lower.Any(other => other.Span.Onset == note.Span.Onset))
            {
                struck++;
            }
            else
            {
                held++;
            }

            Note[] doubled = [.. lower.Where(other => other.Pitch.PitchClass == note.Pitch.PitchClass).OrderByDescending(other => other.Pitch.MidiNumber)];
            if (doubled.Length > 0)
            {
                shared.Add((note, doubled));
            }
        }

        return new OverMeasure(struck, held, shared);
    }

    private static PhraseMeasure MeasurePhrases(Note[] notes, IEnumerable<PhraseBoundary> phrases)
    {
        Tick first = notes[0].Span.Onset;
        Tick last = notes.Max(note => note.Span.Onset);
        PhraseBoundary[] all = [.. phrases.OrderBy(boundary => boundary.At)];
        PhraseBoundary[] inside = [.. all.Where(boundary => boundary.At > first && boundary.At <= last)];
        PhraseBoundary? atStart = all.FirstOrDefault(boundary => boundary.At == first);
        PhraseBoundary[] cited = atStart is null ? inside : [atStart, .. inside];
        return new PhraseMeasure(inside, atStart, cited.Any(boundary => boundary.Attribution.Provenance is Provenance.Inferred or Provenance.Detected));
    }
}