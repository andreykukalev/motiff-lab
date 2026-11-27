using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// scan.join and the notes the Notes made from its score have (within contract 16, fields added; version 1.6, c-14b2, the
/// 1.6 plan's decisions 15 and 16): with the piece's project file, the working notes' marks «что не так» put in as
/// sources.notes.recognise puts them, the variants of a bar to check as those notes would be once each is chosen, and a
/// bar read badly compared with the notes as they were. Each comparison joins and imports the whole score again (a mark
/// from an earlier page holds on and the marks are placed on the piece's bars), one per variant and per group of readings
/// of a bad bar: a few bars to check cost a few readings of the piece.
/// </summary>
internal static partial class ScanRequest
{
    /// <summary>
    /// How a joined score becomes the Notes sources.notes.recognise makes of it (<see cref="SourcesSession.Read"/>: repaired),
    /// and whether with marks: with "file" (the piece's project file) whose working notes have marks, and "original" (the
    /// PDF read, its PDF by default), the marks are put in too (<see cref="SourcesSession.Marked"/>).
    /// </summary>
    private static (Func<byte[], ReadScore> Notes, bool Marks) NotesOf(Request fields)
    {
        Words words = Words.For(fields.Text("language"));
        Project? project = fields.Has("file") ? MusicCoreFacade.ProjectIn(fields) : null;
        if (project?.Scan is not { Marks.Count: > 0 })
        {
            return (score => SourcesSession.Read("notes.musicxml", score, words), false);
        }

        ImportedPiece working = PieceImporter.Import(project.FileName, project.Source).Piece ?? throw FacadeException.BadRequest("The piece's notes could not be read.");
        string original = fields.Text("original") ?? PieceOriginal.DocumentId;
        return (score => SourcesSession.Marked(project, working, original, "notes.musicxml", SourcesSession.Read("notes.musicxml", score, words)).Read, true);
    }

    /// <summary>
    /// The merged pages' variants as the notes would be with each chosen and the marks put in (<paramref name="notesOf"/>):
    /// each variant's staves where its notes then differ from <paramref name="piece"/>'s, a variant left out where none does,
    /// so choosing one never takes back what a mark put right.
    /// </summary>
    private static void MarkVariants(Dictionary<int, ScanMergedPage> merged, byte[] score, ImportedPiece piece, PageMap map, Func<byte[], ReadScore> notesOf)
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
                        return Marked(variant, piece, notesOf(chosen).Piece, measures[index].Index);
                    }).OfType<ScanVariant>()],
                })],
            };
        }
    }

    /// <summary>The variant as the notes <paramref name="chosen"/> give its measure: their staves that differ from <paramref name="piece"/>'s; null where none does.</summary>
    private static ScanVariant? Marked(ScanVariant variant, ImportedPiece piece, ImportedPiece chosen, int measure) =>
        Differing(piece, measure, chosen, measure) is { Length: > 0 } staves ? new ScanVariant(variant.Engines, staves) : null;

    /// <summary>The staves of <paramref name="are"/>'s measure <paramref name="areMeasure"/> whose notes (as the merge compares them) are not those of <paramref name="were"/>'s measure <paramref name="wereMeasure"/>, with their notes.</summary>
    private static ScanVariantStaff[] Differing(ImportedPiece were, int wereMeasure, ImportedPiece are, int areMeasure)
    {
        static Dictionary<(int Part, int Staff), (long Onset, int Pitch, long Length)[]> Staves(ImportedPiece piece, int measure) =>
            piece.MusicXml!.SourceNotes.Where(note => note.MeasureIndex == measure).GroupBy(static note => (note.Track, note.Staff))
                .ToDictionary(static group => group.Key, group => ScanMerge.InBar(piece.MusicXml.Measures[measure].Span, group).Order().ToArray());
        Dictionary<(int Part, int Staff), (long Onset, int Pitch, long Length)[]> before = wereMeasure < were.MusicXml!.Measures.Count ? Staves(were, wereMeasure) : [];
        Dictionary<(int Part, int Staff), (long Onset, int Pitch, long Length)[]> after = areMeasure < are.MusicXml!.Measures.Count ? Staves(are, areMeasure) : [];
        return [.. after.Keys.Union(before.Keys).Order()
            .Select(staff => (Staff: staff, Notes: after.GetValueOrDefault(staff, []), Were: before.GetValueOrDefault(staff, [])))
            .Where(static each => !each.Notes.SequenceEqual(each.Were))
            .Select(static each => new ScanVariantStaff(each.Staff.Part + 1, each.Staff.Staff, [.. each.Notes.Select(static note => new ScanVariantNote(note.Onset, note.Pitch, note.Length))]))];
    }
}