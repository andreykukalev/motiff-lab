using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>
/// What a score writes beside its sounding notes, as source facts on the canonical timeline: grace notes, rests, slurs,
/// repeat signs (the phrase evidence, S1-I5), bar lines other than single, and words and rehearsal marks.
/// </summary>
internal static class ScoreMarks
{
    /// <summary>The grace notes of every part in time order, then by part and document order; source facts, not sounding notes.</summary>
    public static List<MusicXmlGraceNote> GraceNotes(List<PartReader> parts, Func<int, QuarterFraction, QuarterFraction> at) => parts
        .SelectMany(part => part.GraceNotes.Select(grace =>
        {
            QuarterFraction onset = at(grace.MeasureIndex, grace.Offset);
            return new MusicXmlGraceNote(part.Index, part.Id, grace.MeasureIndex, grace.NoteIndex, grace.Staff, grace.Voice, grace.Pitch, onset, CanonicalTime.ToTick(onset).Tick, grace.IsSlashed);
        }))
        .OrderBy(grace => grace.Onset)
        .ThenBy(grace => grace.Track)
        .ThenBy(grace => grace.MeasureIndex)
        .ThenBy(grace => grace.NoteIndex)
        .ToList();

    /// <summary>The rests of every part in time order, then by part and document order.</summary>
    public static List<MusicXmlRest> Rests(List<PartReader> parts, Func<int, QuarterFraction, QuarterFraction> at) => parts
        .SelectMany(part => part.Rests.Select(rest =>
        {
            QuarterFraction onset = at(rest.MeasureIndex, rest.Offset);
            return new MusicXmlRest(part.Index, part.Id, rest.MeasureIndex, rest.NoteIndex, rest.Staff, rest.Voice, onset, rest.Duration, CanonicalTime.ToSpan(onset, onset + rest.Duration).Span, rest.IsMeasureRest) { Writing = rest.Writing };
        }))
        .OrderBy(rest => rest.Onset)
        .ThenBy(rest => rest.Track)
        .ThenBy(rest => rest.MeasureIndex)
        .ThenBy(rest => rest.NoteIndex)
        .ToList();

    /// <summary>The time voices skip without a sign, in every part, in time order, then by part.</summary>
    public static List<MusicXmlSpace> Spaces(List<PartReader> parts, Func<int, QuarterFraction, QuarterFraction> at) => parts
        .SelectMany(part => part.Spaces.Select(space =>
        {
            QuarterFraction onset = at(space.MeasureIndex, space.Offset);
            return new MusicXmlSpace(part.Index, space.MeasureIndex, space.Staff, space.Voice, CanonicalTime.ToSpan(onset, onset + space.Duration).Span);
        }))
        .OrderBy(space => space.Span.Onset)
        .ThenBy(space => space.Track)
        .ToList();

    /// <summary>The slurs of every part, paired (<see cref="PairSlurs"/>), by start, part and stop.</summary>
    public static List<MusicXmlSlur> Slurs(List<PartReader> parts, Func<int, QuarterFraction, QuarterFraction> at, ImportIssueCollector issues) => parts
        .SelectMany(part => PairSlurs(part, at, issues))
        .OrderBy(slur => slur.Start)
        .ThenBy(slur => slur.Track)
        .ThenBy(slur => slur.Stop)
        .ToList();

    /// <summary>The repeat signs at their bar lines, one per position and direction, in time order.</summary>
    public static List<MusicXmlRepeat> Repeats(List<PartReader> parts, QuarterFraction[] starts) => parts
        .SelectMany(part => part.Repeats)
        .Select(repeat => new MusicXmlRepeat(
            CanonicalTime.ToTick(starts[repeat.IsAtEnd ? repeat.MeasureIndex + 1 : repeat.MeasureIndex]).Tick,
            repeat.MeasureIndex,
            repeat.Direction))
        .Distinct()
        .OrderBy(repeat => repeat.At)
        .ThenBy(repeat => repeat.Direction)
        .ToList();

    /// <summary>The bar lines drawn other than single, one per position and style, in time order.</summary>
    public static List<MusicXmlBarline> Barlines(List<PartReader> parts, QuarterFraction[] starts) => [.. parts
        .SelectMany(part => part.Barlines)
        .Select(barline => new MusicXmlBarline(
            CanonicalTime.ToTick(starts[barline.IsAtEnd ? barline.MeasureIndex + 1 : barline.MeasureIndex]).Tick,
            barline.IsAtEnd ? barline.MeasureIndex + 1 : barline.MeasureIndex,
            barline.Style,
            barline.IsRepeat))
        .Distinct()
        .OrderBy(barline => barline.At)
        .ThenBy(barline => barline.Style, StringComparer.Ordinal)
        .ThenBy(barline => barline.IsRepeat)];

    /// <summary>The words and rehearsal marks written over the music, in time order, then by part.</summary>
    public static List<MusicXmlText> Texts(List<PartReader> parts, Func<int, QuarterFraction, QuarterFraction> at) => [.. parts
        .SelectMany(part => part.Texts.Select(text => new MusicXmlText(
            CanonicalTime.ToTick(at(text.MeasureIndex, text.Offset)).Tick,
            part.Index,
            text.MeasureIndex,
            text.Text,
            text.IsRehearsal,
            text.IsBold)))
        .OrderBy(text => text.At)
        .ThenBy(text => text.Track)];

    /// <summary>
    /// Pairs the slur starts and stops of a part by their <c>number</c>, in document order. A start
    /// whose number is already open replaces the open one; a stop with nothing open and a start never
    /// stopped are left out and reported.
    /// </summary>
    private static List<MusicXmlSlur> PairSlurs(PartReader part, Func<int, QuarterFraction, QuarterFraction> at, ImportIssueCollector issues)
    {
        var slurs = new List<MusicXmlSlur>();
        var open = new Dictionary<int, PartSlurMark>();
        foreach (PartSlurMark mark in part.Slurs)
        {
            if (mark.IsStart)
            {
                if (open.ContainsKey(mark.Number))
                {
                    issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.SlurUnmatched, part.Index);
                }

                open[mark.Number] = mark;
                continue;
            }

            if (!open.Remove(mark.Number, out PartSlurMark? start))
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.SlurUnmatched, part.Index);
                continue;
            }

            Tick first = CanonicalTime.ToTick(at(start.MeasureIndex, start.Offset)).Tick;
            Tick last = CanonicalTime.ToTick(at(mark.MeasureIndex, mark.Offset)).Tick;
            (first, last) = first <= last ? (first, last) : (last, first);
            slurs.Add(new MusicXmlSlur(part.Index, part.Id, mark.Number, start.MeasureIndex, first, last));
        }

        issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.SlurUnmatched, part.Index, open.Count);
        return slurs;
    }
}