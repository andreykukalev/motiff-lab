using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// A PDF that stays in the player's folder (version 0.5, «Импорт папки»), for the iPad's side: library.linked makes its
/// piece, library.link moves its original to where it was found again, and open takes the PDF read from the folder.
/// </summary>
public class LinkedPdfFacadeTests
{
    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");
    private static readonly byte[] s_otherPdf = Encoding.ASCII.GetBytes("%PDF-1.4\nanother score\n%%EOF\n");

    private static JsonElement Call(MusicCoreFacade facade, string name, string request)
    {
        JsonElement answer = FacadeCalls.Json(facade.Call(0, name, request));
        Assert.False(answer.TryGetProperty("error", out JsonElement error), $"{name}: {error} {answer}");
        return answer;
    }

    private static string LinkedFile(MusicCoreFacade facade) => Call(facade, "library.linked", $$"""
        {"id": "p_linked", "fileName": "Prelude.pdf", "folder": "Бах/ХТК", "sha256": "{{PieceOriginal.Fingerprint(s_pdf)}}", "bytes": {{s_pdf.Length}},
         "pages": 2, "device": "ipad", "now": "2026-11-12T20:00:00Z"}
        """).GetProperty("file").GetString()!;

    [Fact]
    public void Library_linked_makes_a_piece_whose_pdf_stays_in_the_folder()
    {
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(LinkedFile(new MusicCoreFacade())));

        PieceOriginal pdf = project.FindOriginal(PieceOriginal.DocumentId)!;
        Assert.Equal(OriginalKept.Link, pdf.Kept);
        Assert.Equal("Бах/ХТК", pdf.Folder);
        Assert.Equal(2, pdf.Pages);
        Assert.True(pdf.IsHeldBy(HoldingDevice.Ipad));
        Assert.Null(project.Document);
    }

    [Fact]
    public void Open_takes_the_pdf_from_the_folder_and_never_writes_it_into_the_file()
    {
        var facade = new MusicCoreFacade();
        byte[] file = Encoding.UTF8.GetBytes(LinkedFile(facade));

        Assert.Equal("originalAway", FacadeCalls.Json(facade.Open("Prelude.motiff.json", file)).GetProperty("error").GetString());
        JsonElement opened = FacadeCalls.Json(facade.Open("Prelude.motiff.json", file, $$"""{"pdf": "{{Convert.ToBase64String(s_pdf)}}"}"""));
        int piece = opened.GetProperty("piece").GetInt32();
        Assert.Equal(s_pdf.Length, opened.GetProperty("document").GetProperty("bytes").GetInt32());

        Assert.Null(ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(piece, "project.save"))).Document);
    }

    [Fact]
    public void Open_refuses_another_file_by_its_fingerprint()
    {
        var facade = new MusicCoreFacade();
        byte[] file = Encoding.UTF8.GetBytes(LinkedFile(facade));

        JsonElement opened = FacadeCalls.Json(facade.Open("Prelude.motiff.json", file, $$"""{"pdf": "{{Convert.ToBase64String(s_otherPdf)}}"}"""));

        Assert.Equal("otherFile", opened.GetProperty("error").GetString());
    }

    [Fact]
    public void Library_link_moves_the_original_and_takes_a_changed_file_the_player_confirmed()
    {
        var facade = new MusicCoreFacade();
        string file = LinkedFile(facade);

        string moved = Call(facade, "library.link", $$"""{"file": {{JsonSerializer.Serialize(file)}}, "folder": "Бах", "fileName": "Prelude in C.pdf"}""").GetProperty("file").GetString()!;
        PieceOriginal there = ProjectFile.Read(Encoding.UTF8.GetBytes(moved)).FindOriginal(PieceOriginal.DocumentId)!;
        Assert.Equal(("Бах", "Prelude in C.pdf", PieceOriginal.Fingerprint(s_pdf)), (there.Folder, there.FileName, there.Sha256));
        Assert.Equal(OriginalKept.Link, there.Kept);

        string changed = Call(facade, "library.link", $$"""
            {"file": {{JsonSerializer.Serialize(moved)}}, "folder": "Бах", "fileName": "Prelude in C.pdf", "sha256": "{{PieceOriginal.Fingerprint(s_otherPdf)}}", "bytes": {{s_otherPdf.Length}}, "pages": 3}
            """).GetProperty("file").GetString()!;
        PieceOriginal now = ProjectFile.Read(Encoding.UTF8.GetBytes(changed)).FindOriginal(PieceOriginal.DocumentId)!;
        Assert.Equal((PieceOriginal.Fingerprint(s_otherPdf), (long?)s_otherPdf.Length, (int?)3), (now.Sha256, now.Bytes, now.Pages));
    }

    [Fact]
    public void Library_link_refuses_a_place_outside_the_folder()
    {
        var facade = new MusicCoreFacade();
        string file = LinkedFile(facade);

        JsonElement answer = FacadeCalls.Json(facade.Call(0, "library.link", $$"""{"file": {{JsonSerializer.Serialize(file)}}, "folder": "../x", "fileName": "a.pdf"}"""));

        Assert.Equal("badRequest", answer.GetProperty("error").GetString());
    }
}