using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The iPad's «Фото страниц» experiment (v0.4): photos of pages put together as one PDF open as an ordinary PDF piece whose
/// original is described as photos, and a page photographed again replaces the PDF while no notes are read from it.
/// </summary>
public sealed class PhotoPagesFacadeTests
{
    private static readonly byte[] Photos = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");
    private static readonly byte[] Again = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog /Again true >> endobj\n%%EOF\n");

    [Fact]
    public void Photos_of_pages_open_as_a_pdf_piece_whose_original_is_kept_as_photos_of_that_many_pages()
    {
        var facade = new MusicCoreFacade();
        JsonElement opened = Json(facade.Open("Songbook.pdf", Photos, """{"id": "p_photos", "photos": 3, "now": "2026-11-12T10:00:00Z"}"""));

        Assert.Equal("images", opened.GetProperty("document").GetProperty("kind").GetString());
        Assert.Equal("images", Json(facade.Call(1, "document")).GetProperty("kind").GetString());
        string file = facade.Call(1, "project.save", """{"now": "2026-11-12T10:01:00Z"}""");
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(file));
        PieceOriginal original = Assert.IsType<PieceOriginal>(project.FindOriginal(PieceOriginal.DocumentId));
        Assert.Equal(OriginalKind.Images, original.Kind);
        Assert.Equal(3, original.Pages);
        Assert.Equal(PieceOriginal.Fingerprint(Photos), original.Sha256);

        var again = new MusicCoreFacade();
        Assert.Equal("images", Json(again.Open("Songbook.motiff.json", Encoding.UTF8.GetBytes(file))).GetProperty("document").GetProperty("kind").GetString());
    }

    [Fact]
    public void A_pdf_the_player_added_stays_a_pdf_and_photos_need_a_page()
    {
        var facade = new MusicCoreFacade();
        Assert.Equal("pdf", Json(facade.Open("Nocturne.pdf", Photos)).GetProperty("document").GetProperty("kind").GetString());
        Assert.Equal("badRequest", Json(new MusicCoreFacade().Open("Songbook.pdf", Photos, """{"photos": 0}""")).GetProperty("error").GetString());
    }

    [Fact]
    public void A_page_photographed_again_replaces_the_pdf_keeping_its_marks_until_the_notes_are_read()
    {
        var facade = new MusicCoreFacade();
        Json(facade.Open("Songbook.pdf", Photos, """{"id": "p_photos", "photos": 2, "now": "2026-11-12T10:00:00Z"}"""));
        facade.Call(1, "document.bookmark.add", """{"page": 2, "name": "Trio", "now": "2026-11-12T10:00:30Z"}""");

        JsonElement replaced = Json(facade.Call(1, "document.replace", JsonSerializer.Serialize(new { pdf = Convert.ToBase64String(Again) })));

        Assert.False(replaced.TryGetProperty("error", out _), replaced.ToString());
        JsonElement document = Json(facade.Call(1, "document"));
        Assert.Equal(Again, document.GetProperty("pdf").GetBytesFromBase64());
        Assert.Equal("images", document.GetProperty("kind").GetString());
        Assert.Equal("Trio", document.GetProperty("marks").GetProperty("bookmarks")[0].GetProperty("name").GetString());
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));
        PieceOriginal original = Assert.IsType<PieceOriginal>(project.FindOriginal(PieceOriginal.DocumentId));
        Assert.Equal(PieceOriginal.Fingerprint(Again), original.Sha256);
        Assert.Equal(Again.Length, original.Bytes);
        Assert.Equal(2, original.Pages);

        Assert.Equal("badRequest", Json(facade.Call(1, "document.replace", """{"pdf": "bm90IGEgcGRm"}""")).GetProperty("error").GetString());
        Assert.Equal("badRequest", Json(facade.Call(1, "document.replace", """{"pdf": "not base64!"}""")).GetProperty("error").GetString());
    }

    private static JsonElement Json(string answer) => JsonDocument.Parse(answer).RootElement.Clone();
}