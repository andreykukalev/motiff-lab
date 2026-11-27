using System.Xml.Linq;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>
/// Puts the parts read together (<see cref="MusicXmlImporter"/>): measures aligned across parts by index, each as long
/// as its longest part; the notes (<see cref="ScoreNotes"/>), what is written beside them (<see cref="ScoreMarks"/>),
/// the tempos, bars and keys (<see cref="MusicXmlTimeline"/>), and the report.
/// </summary>
internal static class ScoreBuilder
{
    public static MusicXmlImport Build(XElement root, List<PartReader> parts, ImportIssueCollector issues)
    {
        int measureCount = parts.Max(part => part.Measures.Count);

        // Measures align across parts by index; each lasts as long as its longest part.
        var times = parts
            .SelectMany(part => part.Times)
            .OrderBy(time => time.MeasureIndex)
            .ThenBy(time => time.Offset)
            .ThenBy(time => time.Track)
            .ThenBy(time => time.Order)
            .ToList();
        QuarterFraction[] starts = MeasureStarts(parts, times, measureCount);
        QuarterFraction At(int measureIndex, QuarterFraction offset) => starts[measureIndex] + offset;

        (List<MusicXmlNote> sourceNotes, int[] noteCounts, List<MusicXmlWrittenNote> written) = ScoreNotes.SourceNotes(parts, At, issues);

        // Grace notes and rests: source facts, not sounding notes; slurs and repeat signs: the source's phrase evidence (S1-I5).
        List<MusicXmlGraceNote> graceNotes = ScoreMarks.GraceNotes(parts, At);
        List<MusicXmlRest> rests = ScoreMarks.Rests(parts, At);
        List<MusicXmlSlur> slurs = ScoreMarks.Slurs(parts, At, issues);
        List<MusicXmlRepeat> repeats = ScoreMarks.Repeats(parts, starts);

        List<MusicXmlTempoChange> tempos = MusicXmlTimeline.Tempos(parts, At, issues);
        Tick[] boundaries = Boundaries(starts);
        (List<MusicXmlTimeSignature> timeSignatures, MeterMap meter) = MusicXmlTimeline.Meter(times, At, issues, boundaries);
        List<MusicXmlKeySignature> keys = MusicXmlTimeline.Keys(parts, At, issues);

        List<MusicXmlMeasure> measures = Measures(parts, starts, measureCount);
        Tick end = End(boundaries[^1], sourceNotes);

        List<MusicXmlPartInfo> partInfos = ScoreNotes.Parts(parts, noteCounts);
        List<Note> events = ScoreNotes.Notes(sourceNotes, partInfos);
        string? version = XmlValues.Trimmed(root.Attribute("version")?.Value);
        ImportReport report = Report(version, parts.Count, sourceNotes.Count, meter.BarsBefore(end).Count(), issues);

        return Beside(new MusicXmlImport(version, partInfos, events, sourceNotes, graceNotes, rests, slurs, repeats, measures, tempos, timeSignatures, meter, keys, end, report), parts, starts, At, written);
    }

    /// <summary>
    /// The import with what the score writes beside its notes: bar lines, texts, how the notes are written and the time
    /// voices skip, and the measures a scan merge marked.
    /// </summary>
    private static MusicXmlImport Beside(MusicXmlImport import, List<PartReader> parts, QuarterFraction[] starts, Func<int, QuarterFraction, QuarterFraction> at, List<MusicXmlWrittenNote> written) => import with
    {
        Barlines = ScoreMarks.Barlines(parts, starts),
        Texts = ScoreMarks.Texts(parts, at),
        WrittenNotes = written,
        Spaces = ScoreMarks.Spaces(parts, at),
        EnginesDisagree = [.. import.Measures.Where(measure => FirstOf(parts, measure).EnginesDisagree).Select(measure => measure.Index)],
        ReadAgain = [.. import.Measures.Where(measure => FirstOf(parts, measure).ReadAgain).Select(measure => measure.Index)],
    };

    /// <summary>
    /// Where each measure starts, exactly, and the end of the last: each lasts as long as its longest part, and one with no
    /// content at all (not even a rest) a full bar of the signature in force.
    /// </summary>
    /// <summary>The measure as the first part that has it reads it, where a scan merge's marks are.</summary>
    private static MeasureInfo FirstOf(List<PartReader> parts, MusicXmlMeasure measure) =>
        parts.First(part => measure.Index < part.Measures.Count).Measures[measure.Index];

    private static QuarterFraction[] MeasureStarts(List<PartReader> parts, List<Timed<TimeSignature>> times, int measureCount)
    {
        var starts = new QuarterFraction[measureCount + 1];
        TimeSignature inForce = TimeSignature.CommonTime;
        int nextTime = 0;
        for (int i = 0; i < measureCount; i++)
        {
            while (nextTime < times.Count && (times[nextTime].MeasureIndex < i || (times[nextTime].MeasureIndex == i && times[nextTime].Offset == QuarterFraction.Zero)))
            {
                inForce = times[nextTime].Value;
                nextTime++;
            }

            QuarterFraction length = parts
                .Where(part => i < part.Measures.Count)
                .Select(part => part.Measures[i].Length)
                .Max();

            // A measure with no content at all (not even a rest) lasts a full bar.
            if (length == QuarterFraction.Zero)
            {
                length = new QuarterFraction(inForce.BarTicks, CanonicalTime.TicksPerQuarter);
            }

            starts[i + 1] = starts[i] + length;
        }

        return starts;
    }

    /// <summary>Where each measure starts on the canonical timeline, and the end of the last.</summary>
    private static Tick[] Boundaries(QuarterFraction[] starts) => [.. starts.Select(start => CanonicalTime.ToTick(start).Tick)];

    /// <summary>The import report: the format with its version, the counts and the issues raised.</summary>
    private static ImportReport Report(string? version, int trackCount, int noteCount, int measureCount, ImportIssueCollector issues) => new(
        Format: version is null ? "MusicXML" : $"MusicXML {version}",
        TrackCount: trackCount,
        NoteCount: noteCount,
        MeasureCount: measureCount,
        Issues: issues.Build(MusicXmlIssueCodes.Describe));

    /// <summary>Each measure: its number and whether it is implicit, from the first part that has it, and where it lies.</summary>
    private static List<MusicXmlMeasure> Measures(List<PartReader> parts, QuarterFraction[] starts, int measureCount)
    {
        var measures = new List<MusicXmlMeasure>(measureCount);
        for (int i = 0; i < measureCount; i++)
        {
            MeasureInfo info = parts.First(part => i < part.Measures.Count).Measures[i];
            measures.Add(new MusicXmlMeasure(i, info.Number, info.IsImplicit, starts[i], CanonicalTime.ToSpan(starts[i], starts[i + 1]).Span));
        }

        return measures;
    }

    /// <summary>The end of the piece: the end of the last measure, or of the last note if that is later.</summary>
    private static Tick End(Tick lastMeasureEnd, List<MusicXmlNote> sourceNotes)
    {
        Tick end = lastMeasureEnd;
        if (sourceNotes.Count > 0)
        {
            Tick lastNoteEnd = sourceNotes.Max(note => note.Span.End);
            end = lastNoteEnd > end ? lastNoteEnd : end;
        }

        return end;
    }
}