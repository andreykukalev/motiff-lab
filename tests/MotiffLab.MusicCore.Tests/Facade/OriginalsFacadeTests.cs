using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The originals on the iPad through the facade (version 0.3, within contract 12): where each one's bytes are in the
/// summary and the sources, «Привязать оригинал» as a copy (sources.relink) and the iPad's own mark (sources.held), which
/// keeps a link to a file in Files without a copy and without anything new in the project file.
/// </summary>
public class OriginalsFacadeTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 12, 1, 0, 0, TimeSpan.Zero);
    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");
    private static readonly byte[] s_otherPdf = Encoding.ASCII.GetBytes("%PDF-1.4\nanother score\n%%EOF\n");

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    private static JsonElement Call(MusicCoreFacade facade, string name, string? request = null)
    {
        JsonElement answer = Json(facade.Call(1, name, request));
        Assert.False(answer.TryGetProperty("error", out JsonElement error), $"{name}: {error} {answer}");
        return answer;
    }

    private static string Error(MusicCoreFacade facade, string name, string? request = null) =>
        Json(facade.Call(1, name, request)).GetProperty("error").GetString()!;

    /// <summary>A PDF read by the scan server on the web, as the iPad gets it from the sync: the notes, and the PDF only described, held by the web.</summary>
    private static byte[] FromTheWeb()
    {
        byte[] musicXml = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"));
        Project read = Project.CreateDocument("p_9f2c4e1a0b7d4c3e8f6a5b4c3d2e1f00", "Nocturne.pdf", s_pdf, s_now)
            .Recognised(musicXml, "homr 0.7.0", PageMap.Of(PieceImporter.Import("a.musicxml", musicXml).Piece!, 1), s_now)
            .MarkHeld(PieceOriginal.DocumentId, HoldingDevice.Web, held: true, s_now);
        return ProjectFile.Write(read.ForOnline()!);
    }

    private static MusicCoreFacade Opened()
    {
        var facade = new MusicCoreFacade();
        JsonElement opened = Json(facade.Open("Nocturne.motiff.json", FromTheWeb(), """{"language": "en"}"""));
        Assert.Equal(1, opened.GetProperty("piece").GetInt32());
        return facade;
    }

    private static JsonElement Pdf(JsonElement answer) =>
        answer.GetProperty("originals").EnumerateArray().Single(original => original.GetProperty("id").GetString() == PieceOriginal.DocumentId);

    private static string[] Held(JsonElement original) =>
        [.. original.GetProperty("held").EnumerateArray().Select(mark => mark.GetProperty("device").GetString()!)];

    private static string Pick(byte[] pdf) => JsonSerializer.Serialize(new { pdf = Convert.ToBase64String(pdf), pages = 1, now = "2026-11-12T02:00:00Z" });

    [Fact]
    public void The_summary_and_the_sources_say_the_pdf_is_described_here_and_held_by_the_web()
    {
        MusicCoreFacade facade = Opened();
        string file = Encoding.UTF8.GetString(FromTheWeb());
        JsonElement summary = Json(facade.Call(0, "project.summary", JsonSerializer.Serialize(new { file }))).GetProperty("project");
        JsonElement sources = Call(facade, "sources");

        Assert.Equal("p_9f2c4e1a0b7d4c3e8f6a5b4c3d2e1f00", sources.GetProperty("projectId").GetString());
        Assert.Equal(JsonValueKind.Null, sources.GetProperty("document").ValueKind);
        foreach (JsonElement answer in new[] { summary, sources })
        {
            JsonElement pdf = Pdf(answer);
            Assert.Equal("pdf", pdf.GetProperty("kind").GetString());
            Assert.Equal("Nocturne.pdf", pdf.GetProperty("shown").GetString());
            Assert.Equal(PieceOriginal.Fingerprint(s_pdf), pdf.GetProperty("sha256").GetString());
            Assert.Equal(s_pdf.Length, pdf.GetProperty("bytes").GetInt64());
            Assert.Equal(JsonValueKind.Null, pdf.GetProperty("durationMs").ValueKind);
            Assert.Equal("file", pdf.GetProperty("kept").GetString());
            Assert.False(pdf.GetProperty("inFile").GetBoolean());
            Assert.Equal(["web"], Held(pdf));
        }
    }

    [Fact]
    public void A_different_pdf_is_refused_and_the_same_one_is_kept_in_the_piece_marked_held_by_the_ipad()
    {
        MusicCoreFacade facade = Opened();

        JsonElement other = Call(facade, "sources.relink", Pick(s_otherPdf));
        Assert.False(other.GetProperty("linked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, other.GetProperty("document").ValueKind);
        Assert.Single(other.GetProperty("originals").EnumerateArray());

        JsonElement same = Call(facade, "sources.relink", Pick(s_pdf));
        Assert.True(same.GetProperty("linked").GetBoolean());
        Assert.Equal(1, same.GetProperty("document").GetProperty("pages").GetInt32());
        JsonElement pdf = Pdf(same);
        Assert.True(pdf.GetProperty("inFile").GetBoolean());
        Assert.Equal(["ipad", "web"], Held(pdf));
        Assert.Single(same.GetProperty("originals").EnumerateArray());
        Assert.Equal("unavailable", Error(facade, "sources.relink", Pick(s_pdf)));

        Project saved = ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));
        Assert.Equal(s_pdf, saved.Document);
        Assert.True(saved.FindOriginal(PieceOriginal.DocumentId)!.IsHeldBy(HoldingDevice.Ipad));
    }

    [Fact]
    public void The_ipad_sets_and_takes_back_only_its_own_mark_and_the_pdf_stays_out_of_the_file()
    {
        MusicCoreFacade facade = Opened();

        JsonElement linked = Call(facade, "sources.held", """{"id": "src_pdf", "held": true, "now": "2026-11-12T02:00:00Z"}""");
        Assert.Equal(["ipad", "web"], Held(Pdf(linked)));
        Assert.False(Pdf(linked).GetProperty("inFile").GetBoolean());
        Project saved = ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));
        Assert.Null(saved.Document);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 2, 0, 0, TimeSpan.Zero), saved.FindOriginal(PieceOriginal.DocumentId)!.Held[0].At);

        JsonElement gone = Call(facade, "sources.held", """{"id": "src_pdf", "held": false}""");
        Assert.Equal(["web"], Held(Pdf(gone)));
        Assert.Equal("badRequest", Error(facade, "sources.held", """{"id": "src_none", "held": true}"""));
        Assert.Equal("badRequest", Error(facade, "sources.held", """{"id": "src_pdf"}"""));
    }
}