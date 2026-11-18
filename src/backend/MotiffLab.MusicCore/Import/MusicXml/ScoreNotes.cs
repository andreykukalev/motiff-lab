using System.Globalization;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>A note with the written notes tied to it: exact onset and end, and how many were merged.</summary>
internal sealed record TiedNote(PartNote Note, QuarterFraction Onset, QuarterFraction End, int Written);

/// <summary>The sounding notes of a score: each chain of tied notes merged into one, on the canonical timeline, then as shared-model notes.</summary>
internal static class ScoreNotes
{
    /// <summary>
    /// The source notes of every part, ties merged, in time order (then part, pitch, measure and document order), with
    /// how many each part has; reports notes widened from no length, rounded, and played at the assumed velocity.
    /// </summary>
    public static (List<MusicXmlNote> Notes, int[] Counts) SourceNotes(List<PartReader> parts, Func<int, QuarterFraction, QuarterFraction> at, ImportIssueCollector issues)
    {
        var sourceNotes = new List<MusicXmlNote>();
        var noteCounts = new int[parts.Count];
        foreach (PartReader part in parts)
        {
            List<TiedNote> merged = MergeTies(part, at, issues);
            noteCounts[part.Index] = merged.Count;
            foreach ((PartNote note, QuarterFraction onset, QuarterFraction noteEnd, int written) in merged)
            {
                QuarterFraction duration = noteEnd - onset;
                TickSpan span;
                bool exact;
                if (duration == QuarterFraction.Zero)
                {
                    issues.Add(ImportIssueKind.Repaired, MusicXmlIssueCodes.ZeroLengthWidened, part.Index);
                    span = new TickSpan(CanonicalTime.ToTick(onset).Tick, 1);
                    exact = false;
                }
                else
                {
                    ConvertedSpan converted = CanonicalTime.ToSpan(onset, noteEnd);
                    span = converted.Span;
                    exact = converted.IsExact;
                    if (!exact)
                    {
                        issues.Add(ImportIssueKind.Rounded, MusicXmlIssueCodes.NotesRounded);
                    }
                }

                sourceNotes.Add(new MusicXmlNote(
                    part.Index,
                    part.Id,
                    note.MeasureIndex,
                    note.NoteIndex,
                    note.Staff,
                    note.Voice,
                    note.Pitch,
                    note.Velocity ?? MusicXmlImport.DefaultVelocity,
                    note.Velocity is null,
                    onset,
                    duration,
                    span,
                    exact,
                    written,
                    note.Tuplet,
                    note.Accent));
            }
        }

        sourceNotes.Sort(InScoreOrder);

        issues.Add(ImportIssueKind.Assumed, MusicXmlIssueCodes.VelocityDefault, count: sourceNotes.Count(note => note.IsVelocityAssumed));

        return (sourceNotes, noteCounts);
    }

    /// <summary>
    /// Merges each chain of tied notes in a part into its first note (S1-I2, AC-S1-02). A tie stop
    /// continues the open tie of the same pitch that ends where it starts, else the one in the same
    /// voice and staff; with neither, the note stays on its own and is reported.
    /// </summary>
    public static List<TiedNote> MergeTies(PartReader part, Func<int, QuarterFraction, QuarterFraction> at, ImportIssueCollector issues)
    {
        var merged = new List<TiedNote>();
        var open = new List<int>(); // indexes into merged of notes whose tie continues
        foreach (PartNote note in part.Notes)
        {
            QuarterFraction onset = at(note.MeasureIndex, note.Offset);
            QuarterFraction end = onset + note.Duration;
            int match = -1;
            if (note.TieStop)
            {
                match = open.FindIndex(i => merged[i].Note.Pitch == note.Pitch && merged[i].End == onset);
                if (match < 0)
                {
                    match = open.FindIndex(i => merged[i].Note.Pitch == note.Pitch && merged[i].Note.Voice == note.Voice && merged[i].Note.Staff == note.Staff);
                }
            }

            if (match >= 0)
            {
                int target = open[match];
                TiedNote tied = merged[target];
                merged[target] = tied with { End = end > tied.End ? end : tied.End, Written = tied.Written + 1 };
                if (!note.TieStart)
                {
                    open.RemoveAt(match);
                }

                continue;
            }

            if (note.TieStop)
            {
                issues.Add(ImportIssueKind.Repaired, MusicXmlIssueCodes.TieWithoutStart, part.Index);
            }

            // A new note of the same pitch in the same voice ends a tie that was never stopped.
            open.RemoveAll(i => merged[i].Note.Pitch == note.Pitch && merged[i].Note.Voice == note.Voice && merged[i].Note.Staff == note.Staff);
            merged.Add(new TiedNote(note, onset, end, 1));
            if (note.TieStart)
            {
                open.Add(merged.Count - 1);
            }
        }

        return merged;
    }

    /// <summary>Each part as imported: its names, its staves and how many notes it has.</summary>
    public static List<MusicXmlPartInfo> Parts(List<PartReader> parts, int[] noteCounts) => parts
        .Select(part => new MusicXmlPartInfo(part.Index, part.Id, part.Name, part.InstrumentName, part.Staves, noteCounts[part.Index]))
        .ToList();

    /// <summary>The source notes as the shared model's notes, numbered in their order.</summary>
    public static List<Note> Notes(List<MusicXmlNote> sourceNotes, List<MusicXmlPartInfo> parts) =>
        sourceNotes.Select((note, index) => ToNote(note, index, parts[note.Track])).ToList();

    /// <summary>A source note as the shared model's note: imported, with its hand on a grand staff and where it was written.</summary>
    private static Note ToNote(MusicXmlNote source, int index, MusicXmlPartInfo part) => new()
    {
        Id = $"n_{index + 1:D4}",
        Pitch = new Pitch(source.Pitch),
        Span = source.Span,
        Velocity = source.Velocity,
        Track = source.Track,
        Voice = int.TryParse(source.Voice, NumberStyles.None, CultureInfo.InvariantCulture, out int voice) && voice >= 1 ? voice : null,
        Hand = part.HasHands && source.Staff is 1 or 2
            ? new Attributed<Hand>(source.Staff == 1 ? Hand.Right : Hand.Left, Attribution.Imported)
            : null,
        Attribution = Attribution.Imported,
        Source = new MusicXmlSourceLocation(source.PartId, source.MeasureIndex, source.NoteIndex, source.Staff, source.Voice),
    };

    /// <summary>Time order, then part, pitch, measure and document order.</summary>
    private static int InScoreOrder(MusicXmlNote a, MusicXmlNote b) =>
        (a.Span.Onset, a.Track, a.Pitch, a.MeasureIndex, a.NoteIndex).CompareTo((b.Span.Onset, b.Track, b.Pitch, b.MeasureIndex, b.NoteIndex));
}