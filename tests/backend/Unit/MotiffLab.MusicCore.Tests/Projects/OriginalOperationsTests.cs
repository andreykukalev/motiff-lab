using System.Text;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// The operations on a piece's originals (decision record 0030, section 4): another PDF beside the first, removing an
/// original with or without its notes and reviving it with the same file; <see cref="NotesOperationsTests"/> for the Notes.
/// </summary>
public class OriginalOperationsTests
{
    internal static readonly DateTimeOffset s_now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    internal static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    private static readonly byte[] s_otherPdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog /Lang (de) >> endobj\n%%EOF\n");

    internal static byte[] Scanned() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"));

    internal static byte[] Original() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", "fur-elise-original.musicxml"));

    internal static PageMap Pages(byte[] musicXml, int pages = 2) => PageMap.Of(PieceImporter.Import("a.musicxml", musicXml).Piece!, pages);

    /// <summary>A PDF piece read by a scan, with a mark on its first page and one correction of its first note.</summary>
    internal static Project ScannedPdf()
    {
        byte[] musicXml = Scanned();
        Project project = Project.CreateDocument("p_pdf", "Nocturne.pdf", s_pdf, s_now).Recognised(musicXml, "homr 0.7.0", Pages(musicXml), s_now.AddMinutes(1));
        Note first = PieceImporter.Import(project.FileName, musicXml).Piece!.Notes[0];
        NoteValues before = NoteValues.Of(first);
        return project with
        {
            Edits = new NoteEditLog([new NoteEdit(first.Id, before, before with { Pitch = new Pitch(before.Pitch.MidiNumber + 1) })]),
            DocumentMarks = DocumentMarks.Empty.AddBookmark(1, "Coda", s_now),
        };
    }

    private static PieceOriginal Pdf(string id, string name) => new(id, OriginalKind.Pdf, name, OriginalKept.File, s_now.AddHours(1));

    internal static Project RoundTrip(Project project) => ProjectFile.Read(ProjectFile.Write(project));

    [Fact]
    public void Another_pdf_is_a_new_original_beside_the_first_with_its_bytes_in_the_file_and_never_online()
    {
        Project project = ScannedPdf();
        string id = project.NextOriginalId(PieceOriginal.DocumentId);

        Project added = project.AddOriginal(Pdf(id, "Nocturne (Henle).pdf"), s_otherPdf);

        Assert.Equal("src_pdf_2", id);
        Assert.Equal(s_pdf, added.Document);
        PieceOriginal second = added.FindOriginal(id)!;
        Assert.Equal((PieceOriginal.Fingerprint(s_otherPdf), (long?)s_otherPdf.Length), (second.Sha256, second.Bytes));
        Assert.True(added.HasInFile(second));
        Project read = RoundTrip(added);
        Assert.Equal(s_otherPdf, read.Sources.Data[id]);
        Assert.Equal(ProjectFile.Write(added), ProjectFile.Write(read));
        JsonObject online = JsonNode.Parse(ProjectFile.Write(added.ForOnline()!))!.AsObject();
        Assert.All(online["originals"]!.AsArray(), original => Assert.Null(original!["data"]));
        Assert.Throws<ArgumentException>(() => added.AddOriginal(Pdf(id, "again.pdf"), s_otherPdf));
        Assert.Equal("src_pdf_3", added.NextOriginalId(PieceOriginal.DocumentId));
    }

    [Fact]
    public void Removing_the_pdf_keeps_its_notes_and_a_tombstone_with_its_marks_and_the_same_file_revives_it()
    {
        Project project = ScannedPdf();

        Project removed = project.RemoveOriginal(PieceOriginal.DocumentId, s_now.AddHours(2));

        Assert.Null(removed.Document);
        Assert.Null(removed.DocumentPages);
        Assert.True(removed.DocumentMarks.IsEmpty);
        Assert.Null(removed.FindOriginal(PieceOriginal.DocumentId));
        Assert.Equal(project.Source, removed.Source);
        Assert.Equal(project.Edits, removed.Edits);
        RemovedOriginal tombstone = Assert.Single(removed.Sources.Removed);
        Assert.Equal((PieceOriginal.Fingerprint(s_pdf), s_now.AddHours(2)), (tombstone.Original.Sha256, tombstone.At));
        Assert.Equal(project.DocumentMarks, tombstone.Marks);
        Assert.Equal(PieceOriginal.Fingerprint(s_pdf), removed.WorkingProvenance.Original!.Sha256);
        Assert.Equal(ProjectFile.Write(removed), ProjectFile.Write(RoundTrip(removed)));

        Assert.Throws<ArgumentException>(() => removed.AddOriginal(Pdf("src_pdf_9", "Nocturne.pdf"), s_pdf));
        Project revived = removed.AddOriginal(Pdf("src_pdf_9", "Nocturne.pdf"), s_pdf, project.DocumentPages);
        Assert.Empty(revived.Sources.Removed);
        Assert.Equal(s_pdf, revived.Document);
        Assert.Equal(project.DocumentMarks, revived.DocumentMarks);
        Assert.Equal(project.DocumentPages, revived.DocumentPages);
        Assert.Equal(PieceOriginal.DocumentId, Assert.Single(revived.Originals, original => original.Kind == OriginalKind.Pdf).Id);
    }

    [Fact]
    public void An_original_goes_with_its_notes_only_when_the_working_notes_were_not_made_from_it()
    {
        byte[] musicXml = Scanned();
        Project project = ScannedPdf().AddOriginal(Pdf("src_pdf_2", "Henle.pdf"), s_otherPdf).RecognisedAgain("src_pdf_2", musicXml, "homr 0.8.0", null, s_now.AddHours(3));
        Assert.Equal("n_2", Assert.Single(project.Sources.Notes).Id);
        Assert.Throws<InvalidOperationException>(() => project.RemoveOriginal(PieceOriginal.DocumentId, s_now, withNotes: true));

        Project kept = project.RemoveOriginal("src_pdf_2", s_now.AddHours(4));
        Project gone = project.RemoveOriginal("src_pdf_2", s_now.AddHours(4), withNotes: true);

        Assert.Single(kept.Sources.Notes);
        Assert.False(kept.Sources.Data.ContainsKey("src_pdf_2"));
        Assert.Empty(gone.Sources.Notes);
        Assert.Equal("src_pdf_2", Assert.Single(gone.Sources.Removed).Original.Id);
        Assert.Throws<KeyNotFoundException>(() => gone.RemoveOriginal("src_pdf_2", s_now));
    }

    [Fact]
    public void A_piece_that_is_only_its_pdf_is_not_left_empty_and_a_linked_pdf_is_only_unlinked()
    {
        Project only = Project.CreateDocument("p_only", "Nocturne.pdf", s_pdf, s_now);
        Assert.Throws<InvalidOperationException>(() => only.RemoveOriginal(PieceOriginal.DocumentId, s_now));

        Project linked = Project.Create("p_link", "fur-elise.musicxml", Original(), s_now)
            .AddOriginal(new PieceOriginal("src_scan", OriginalKind.Pdf, "Für Elise.pdf", OriginalKept.Link, s_now) { Folder = "Beethoven", Sha256 = PieceOriginal.Fingerprint(s_pdf) }, null);
        Project unlinked = linked.RemoveOriginal("src_scan", s_now.AddHours(1));

        Assert.Equal(OriginalKept.Link, Assert.Single(unlinked.Sources.Removed).Original.Kept);
        Assert.Null(unlinked.FindOriginal("src_scan"));
        Assert.Throws<ArgumentException>(() => linked.AddOriginal(Pdf("src_kept", "x.pdf"), null));
    }

    [Fact]
    public void A_pdf_put_in_place_of_the_one_the_notes_were_read_from_shows_them_made_from_another_file()
    {
        Project project = ScannedPdf();

        Project replaced = project.WithDocument(s_otherPdf, project.DocumentPages!);

        Assert.Equal(PieceOriginal.Fingerprint(s_pdf), replaced.WorkingProvenance.Original!.Sha256);
        Assert.Equal(PieceOriginal.Fingerprint(s_otherPdf), replaced.FindOriginal(PieceOriginal.DocumentId)!.Sha256);
    }

    [Fact]
    public void A_piece_with_two_pdfs_other_notes_and_a_removed_original_is_written_as_its_golden()
    {
        byte[] musicXml = Scanned();
        Project project = ScannedPdf()
            .AddOriginal(Pdf("src_pdf_2", "Nocturne (Henle).pdf"), s_otherPdf)
            .RecognisedAgain("src_pdf_2", musicXml, "homr 0.8.0", null, s_now.AddHours(1))
            .MakeWorking("n_2", out _)
            .RemoveOriginal(PieceOriginal.DocumentId, s_now.AddHours(2))
            .ViewHidden(Project.FirstNotesId, hidden: true);

        Assert.Equal(("n_2", "n_1"), (project.NotesId, Assert.Single(project.Sources.Notes).Id));
        Assert.Equal(ScannedPdf().Edits, project.Sources.Notes[0].Edits);
        GoldenFiles.AssertIs("v18-sources.motiff.json", ProjectFile.Write(project), ["v16-sources.motiff.json", "v17-sources.motiff.json"]);
    }
}