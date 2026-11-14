using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Tests.Facade;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// A piece that is only a PDF syncs too, without the PDF (version 0.5; Andrei, 2026-11-12 17:45 UTC, amending 0026 C3), so a
/// folder imported on the iPad reaches the web: its title, composer, tags, playlists, the PDF's description with its
/// fingerprint and the marks on its pages go online. A device without the PDF lists the piece and opens it once the same
/// file is linked there; the device that has the PDF keeps it when the online copy comes back.
/// </summary>
public class PdfPiecesOnlineTests
{
    private const string Golden = "v15-pdf-online.motiff.json";

    private static readonly DateTimeOffset s_now = new(2026, 11, 12, 18, 30, 0, TimeSpan.Zero);

    private static readonly byte[] s_pdf = "%PDF-1.4 Nocturne in E-flat, two pages"u8.ToArray();

    /// <summary>A PDF piece on the iPad, from a folder import: the player's details, a bookmark on page 2, the PDF held by the iPad.</summary>
    private static Project OnIpad() =>
        (Project.CreateDocument("p_9c3d4e5f6a7b4c8d9e0f1a2b3c4d5e6f", "Chopin - Nocturne Op. 9 No. 2.pdf", s_pdf, s_now) with
        {
            Metadata = new PieceMetadata("Nocturne Op. 9 No. 2", "Chopin", ["Шопен"], ["Концерт в мае"]),
            DocumentMarks = DocumentMarks.Empty.AddBookmark(2, "Reprise", s_now),
        }).MarkHeld(PieceOriginal.DocumentId, HoldingDevice.Ipad, held: true, s_now);

    private static Project RoundTrip(Project project) => ProjectFile.Read(ProjectFile.Write(project));

    private static string Text(byte[] bytes) => GoldenFiles.Text(bytes);

    private static byte[] Kept(string name) => GoldenFiles.Kept(name);

    [Fact]
    public void The_online_copy_of_a_pdf_piece_is_the_golden_and_reads_back_unchanged()
    {
        GoldenFiles.AssertIs(Golden, ProjectFile.Write(OnIpad().ForOnline()!));
    }

    [Fact]
    public void The_online_copy_carries_the_details_the_pdfs_description_and_its_marks_and_never_the_pdf()
    {
        Project online = ProjectFile.Read(Kept(Golden));

        Assert.False(online.HasNotes);
        Assert.Null(online.Document);
        Assert.DoesNotContain("\"document\"", Text(Kept(Golden)), StringComparison.Ordinal);
        Assert.Equal(OnIpad().Metadata, online.Metadata);
        Assert.Equal(OnIpad().DocumentMarks, online.DocumentMarks);
        PieceOriginal pdf = Assert.Single(online.Originals);
        Assert.Equal((PieceOriginal.Fingerprint(s_pdf), (long?)s_pdf.Length, true), (pdf.Sha256, pdf.Bytes, pdf.IsHeldBy(HoldingDevice.Ipad)));
        Assert.Equal(OriginalWhere.OnIpad, Assert.Single(online.OriginalSummaries).Where(HoldingDevice.Web, heldHere: false));
        Assert.Equal(PieceOrigin.Pdf, online.Summary.Origin);
    }

    [Fact]
    public void A_pdf_piece_saved_before_its_fingerprint_was_kept_gets_one_in_its_online_copy()
    {
        Project old = Project.CreateDocument("p_old", "a.pdf", s_pdf, s_now) with { KeptOriginals = [] };
        Assert.Null(old.FindOriginal(PieceOriginal.DocumentId)!.Sha256);

        Assert.Equal(PieceOriginal.Fingerprint(s_pdf), RoundTrip(old.ForOnline()!).FindOriginal(PieceOriginal.DocumentId)!.Sha256);
    }

    [Fact]
    public void A_device_without_the_pdf_lists_the_piece_opens_nothing_and_opens_it_once_the_same_file_is_linked()
    {
        Project web = RoundTrip(ProjectSync.Pulled(ProjectFile.Read(Kept(Golden)), null, HoldingDevice.Web));
        Assert.Null(web.Document);

        var facade = new MusicCoreFacade();
        string file = Encoding.UTF8.GetString(ProjectFile.Write(web));
        JsonElement summary = FacadeCalls.Json(facade.Call(0, "project.summary", $$"""{"file": {{JsonSerializer.Serialize(file)}}}""")).GetProperty("project");
        Assert.Equal("Nocturne Op. 9 No. 2", summary.GetProperty("title").GetString());
        Assert.Equal("originalAway", FacadeCalls.Json(facade.Open("p.motiff.json", ProjectFile.Write(web))).GetProperty("error").GetString());

        Assert.Null(web.RelinkDocument("%PDF-1.4 another file"u8.ToArray(), new PageMap([Tick.Zero])));
        Project linked = web.RelinkDocument(s_pdf, new PageMap([Tick.Zero]))!;
        Assert.Equal(s_pdf, linked.Document);
        Assert.Null(linked.DocumentPages);
        Assert.False(linked.WritesDocumentMarks(HoldingDevice.Web), "the iPad holds the PDF, so it writes the marks");
    }

    [Fact]
    public void The_device_that_has_the_pdf_keeps_it_and_its_marks_when_the_online_copy_comes_back()
    {
        Project ipad = OnIpad();
        Project online = RoundTrip(ipad.ForOnline()!) with { Metadata = new PieceMetadata("Nocturne", "Chopin", ["Шопен"], ["Концерт в мае", "Экзамен"]) };

        Project pulled = ProjectSync.Pulled(online with { DocumentMarks = DocumentMarks.Empty }, ipad, HoldingDevice.Ipad);

        Assert.Equal(s_pdf, pulled.Document);
        Assert.Equal(ipad.DocumentMarks, pulled.DocumentMarks);
        Assert.Equal(["Концерт в мае", "Экзамен"], pulled.Metadata.Playlists);
    }

    /// <summary>What a build before 0.5 makes of the online copy: it has no notes and no PDF, so they refuse it (see the golden README).</summary>
    [Fact]
    public void Without_its_pdfs_description_the_online_copy_is_damaged_as_every_build_before_0_5_reads_it()
    {
        JsonObject file = JsonNode.Parse(Kept(Golden))!.AsObject();
        _ = file.Remove("originals");

        Assert.Contains("source", Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString()))).Message, StringComparison.Ordinal);
    }
}