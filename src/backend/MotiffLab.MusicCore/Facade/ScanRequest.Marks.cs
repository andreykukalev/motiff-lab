using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// scan.join with the piece's project file (within contract 16, fields added; version 1.6, c-14b2, the 1.6 plan's decision 16):
/// the notes the joined score makes with the working notes' marks «что не так» put in, as sources.notes.recognise makes
/// them, and the variants of a bar to check as those notes would be once each is chosen.
/// </summary>
internal static partial class ScanRequest
{
    /// <summary>
    /// With "file" (the piece's project file) whose working notes have marks, and "original" (the PDF read, its PDF by
    /// default): how a score becomes the Notes with the marks put in (<see cref="SourcesSession.Marked"/>); null otherwise.
    /// </summary>
    private static Func<byte[], ReadScore>? Marking(Request fields)
    {
        if (!fields.Has("file"))
        {
            return null;
        }

        Project project = MusicCoreFacade.ProjectIn(fields);
        if (project.Scan is not { Marks.Count: > 0 })
        {
            return null;
        }

        ImportedPiece working = PieceImporter.Import(project.FileName, project.Source).Piece ?? throw FacadeException.BadRequest("The piece's notes could not be read.");
        string original = fields.Text("original") ?? PieceOriginal.DocumentId;
        Words words = Words.For(fields.Text("language"));
        return score => SourcesSession.Marked(project, working, original, "notes.musicxml", SourcesSession.Read("notes.musicxml", score, words)).Read;
    }

    /// <summary>
    /// The merged pages' variants as the notes would be with each chosen and the marks put in (<paramref name="marking"/>):
    /// each variant's staves where its notes then differ from <paramref name="piece"/>'s, a variant left out where none does,
    /// so choosing one never takes back what a mark put right.
    /// </summary>
    private static void MarkVariants(Dictionary<int, ScanMergedPage> merged, byte[] score, ImportedPiece piece, PageMap map, Func<byte[], ReadScore> marking)
    {
        foreach ((int page, ScanMergedPage merge) in merged.ToList())
        {
            MusicXmlMeasure[] measures = [.. piece.MusicXml!.Measures.Where(measure => map.PageOf(measure.Span.Onset) == page)];
            merged[page] = merge with
            {
                Bars = [.. merge.Bars.Select((bar, index) => bar.Variants.Count == 0 || index >= measures.Length ? bar : bar with
                {
                    Variants = [.. bar.Variants.Select(variant =>
                    {
                        byte[] chosen = ScanJoin.Replace(score, new Dictionary<int, byte[]> { [page] = merge.WithVariant(index, variant) });
                        return Marked(variant, piece, marking(chosen).Piece, measures[index].Index);
                    }).OfType<ScanVariant>()],
                })],
            };
        }
    }

    /// <summary>The variant as the notes <paramref name="chosen"/> give its measure: their staves that differ from <paramref name="piece"/>'s; null where none does.</summary>
    private static ScanVariant? Marked(ScanVariant variant, ImportedPiece piece, ImportedPiece chosen, int measure)
    {
        ILookup<(int Part, int Staff), MusicXmlNote> were = piece.MusicXml!.SourceNotes.Where(note => note.MeasureIndex == measure).ToLookup(static note => (note.Track, note.Staff));
        ILookup<(int Part, int Staff), MusicXmlNote> are = chosen.MusicXml!.SourceNotes.Where(note => note.MeasureIndex == measure).ToLookup(static note => (note.Track, note.Staff));
        TickSpan Bar(ImportedPiece of) => of.MusicXml!.Measures[measure].Span;
        ScanVariantStaff[] staves = [.. are.Select(static group => group.Key).Union(were.Select(static group => group.Key)).Order()
            .Select(staff => (Staff: staff, Notes: ScanMerge.InBar(Bar(chosen), are[staff]).Order().ToArray(), Were: ScanMerge.InBar(Bar(piece), were[staff]).Order().ToArray()))
            .Where(static each => !each.Notes.SequenceEqual(each.Were))
            .Select(static each => new ScanVariantStaff(each.Staff.Part + 1, each.Staff.Staff, [.. each.Notes.Select(static note => new ScanVariantNote(note.Onset, note.Pitch, note.Length))]))];
        return staves.Length == 0 ? null : new ScanVariant(variant.Engines, staves);
    }
}