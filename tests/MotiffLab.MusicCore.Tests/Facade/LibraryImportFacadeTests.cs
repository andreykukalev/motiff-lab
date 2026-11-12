using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>library.plan and library.confirm on piece 0 (contract 12, version 0.5, «Импорт папки»): the iPad plans a folder as the web does.</summary>
public class LibraryImportFacadeTests
{
    private static readonly string s_a = new('a', 64);

    private static JsonElement Call(MusicCoreFacade facade, string name, string request)
    {
        JsonElement answer = FacadeCalls.Json(facade.Call(0, name, request));
        Assert.False(answer.TryGetProperty("error", out JsonElement error), $"{name}: {error} {answer}");
        return answer;
    }

    [Fact]
    public void Library_plan_answers_each_file_with_its_status_suggestions_and_playlists()
    {
        string score = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            """<?xml version="1.0"?><score-partwise version="4.0"><work><work-title>Prelude in C</work-title></work><identification><creator type="composer">J. S. Bach</creator></identification><part-list/></score-partwise>"""));
        JsonElement plan = Call(new MusicCoreFacade(), "library.plan", $$"""
            {"files": [
                {"path": "Бах/Prelude.musicxml", "bytes": 500, "musicXml": "{{score}}"},
                {"path": "Шопен/Chopin - Ballade No. 1.pdf", "bytes": 1000, "sha256": "{{s_a}}", "pages": 24},
                {"path": "Концерт/Ballade.pdf", "bytes": 1000, "sha256": "{{s_a}}"},
                {"path": "readme.txt", "bytes": 3}],
             "library": [], "folders": "playlists"}
            """);

        JsonElement[] files = [.. plan.GetProperty("files").EnumerateArray()];
        Assert.Equal(["readme.txt", "Бах/Prelude.musicxml", "Концерт/Ballade.pdf", "Шопен/Chopin - Ballade No. 1.pdf"], files.Select(file => file.GetProperty("path").GetString()));
        Assert.Equal(["unsupported", "new", "new", "duplicate"], files.Select(file => file.GetProperty("status").GetString()));

        JsonElement prelude = files[1];
        Assert.Equal("Prelude in C", prelude.GetProperty("title").GetProperty("value").GetString());
        Assert.Equal("scoreHeader", prelude.GetProperty("title").GetProperty("from").GetString());
        Assert.Equal("inferred", prelude.GetProperty("title").GetProperty("provenance").GetString());
        Assert.Equal("J. S. Bach", prelude.GetProperty("composer").GetProperty("value").GetString());

        JsonElement ballade = files[2];
        Assert.Equal(["Концерт", "Шопен"], ballade.GetProperty("playlists").EnumerateArray().Select(name => name.GetString()));
        Assert.Equal("Концерт/Ballade.pdf", files[3].GetProperty("sameAs").GetString());
        Assert.True(files[3].GetProperty("collection").GetBoolean());
        Assert.Equal(4, plan.GetProperty("counts").GetProperty("found").GetInt32());
        Assert.Equal(1, plan.GetProperty("counts").GetProperty("unsuitable").GetInt32());
    }

    [Fact]
    public void Library_plan_refuses_a_path_outside_the_folder()
    {
        JsonElement answer = FacadeCalls.Json(new MusicCoreFacade().Call(0, "library.plan", """{"files": [{"path": "../secret.pdf", "bytes": 1}]}"""));

        Assert.Equal("badRequest", answer.GetProperty("error").GetString());
    }

    [Fact]
    public void Library_confirm_keeps_the_players_title_and_adds_the_playlists()
    {
        Project pdf = Project.CreateDocument("p_pdf", "Bach - Prelude.pdf", "%PDF-1.4 x"u8.ToArray(), DateTimeOffset.UnixEpoch) with
        {
            Metadata = new PieceMetadata("Мой прелюд", null, null, null),
        };
        string file = Encoding.UTF8.GetString(ProjectFile.Write(pdf));

        JsonElement confirmed = Call(new MusicCoreFacade(), "library.confirm", $$"""{"file": {{JsonSerializer.Serialize(file)}}, "title": "Prelude", "composer": "Bach", "playlists": ["Бах"]}""");

        JsonElement project = confirmed.GetProperty("project");
        Assert.Equal("Мой прелюд", project.GetProperty("title").GetString());
        Assert.Equal("Bach", project.GetProperty("composer").GetString());
        Assert.Equal(["Бах"], project.GetProperty("playlists").EnumerateArray().Select(name => name.GetString()));
    }

    [Fact]
    public void Library_link_makes_a_piece_whose_pdf_stays_in_the_folder_and_opens_only_with_that_pdf()
    {
        byte[] pdf = "%PDF-1.4 linked"u8.ToArray();
        string sha = PieceOriginal.Fingerprint(pdf);
        var facade = new MusicCoreFacade();
        JsonElement made = Call(facade, "library.link", $$"""{"id": "p_link", "fileName": "Ballade No. 1.pdf", "folder": "Шопен/Баллады", "sha256": "{{sha}}", "bytes": {{pdf.Length}}, "pages": 12, "now": "2026-11-12T21:00:00Z"}""");

        JsonElement original = made.GetProperty("project").GetProperty("originals")[0];
        Assert.Equal("link", original.GetProperty("kept").GetString());
        Assert.Equal("Шопен/Баллады", original.GetProperty("folder").GetString());
        Assert.Equal(["ipad"], original.GetProperty("held").EnumerateArray().Select(mark => mark.GetProperty("device").GetString()));
        string file = made.GetProperty("file").GetString()!;
        Assert.Equal("originalAway", FacadeCalls.Json(facade.Open("Ballade.motiff.json", Encoding.UTF8.GetBytes(file))).GetProperty("error").GetString());

        string request = JsonSerializer.Serialize(new { project = file, language = "ru" });
        Assert.Equal("originalChanged", FacadeCalls.Json(facade.Open("Ballade No. 1.pdf", "%PDF-1.4 other"u8.ToArray(), request)).GetProperty("error").GetString());
        JsonElement opened = FacadeCalls.Json(facade.Open("Ballade No. 1.pdf", pdf, request));
        int piece = opened.GetProperty("piece").GetInt32();
        Assert.Equal("p_link", opened.GetProperty("project").GetProperty("id").GetString());
        Assert.Equal(pdf.Length, opened.GetProperty("document").GetProperty("bytes").GetInt32());

        Project saved = ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(piece, "project.save")));
        Assert.Null(saved.Document);
        Assert.True(saved.LinksDocument);
    }

    [Fact]
    public void Library_link_finds_a_linked_pdf_again_and_keeps_the_piece_as_it_was()
    {
        string first = new('1', 64);
        string second = new('2', 64);
        Project linked = Project.CreateLinkedDocument("p_moved", "Nocturne.pdf", "Шопен", first, 1000, 3, HoldingDevice.Web, DateTimeOffset.UnixEpoch) with
        {
            Metadata = new PieceMetadata("Ноктюрн", "Шопен", null, ["Концерт"]),
        };
        string file = JsonSerializer.Serialize(Encoding.UTF8.GetString(ProjectFile.Write(linked)));

        JsonElement moved = Call(new MusicCoreFacade(), "library.link", $$"""{"file": {{file}}, "fileName": "Nocturne op. 9.pdf", "folder": "Шопен/Ноктюрны", "sha256": "{{second}}", "bytes": 1200, "now": "2026-11-12T21:00:00Z"}""");

        JsonElement project = moved.GetProperty("project");
        Assert.Equal("Ноктюрн", project.GetProperty("title").GetString());
        Assert.Equal(["Концерт"], project.GetProperty("playlists").EnumerateArray().Select(name => name.GetString()));
        JsonElement original = project.GetProperty("originals")[0];
        Assert.Equal("Nocturne op. 9.pdf", original.GetProperty("fileName").GetString());
        Assert.Equal("Шопен/Ноктюрны", original.GetProperty("folder").GetString());
        Assert.Equal(second, original.GetProperty("sha256").GetString());
        Assert.Equal(JsonValueKind.Null, original.GetProperty("pages").ValueKind);
        Assert.Equal(["ipad", "web"], original.GetProperty("held").EnumerateArray().Select(mark => mark.GetProperty("device").GetString()));
    }

    [Fact]
    public void Library_link_refuses_a_piece_whose_pdf_is_in_its_file_and_open_refuses_a_piece_with_notes()
    {
        Project copy = Project.CreateDocument("p_copy", "Prelude.pdf", "%PDF-1.4 x"u8.ToArray(), DateTimeOffset.UnixEpoch);
        string file = Encoding.UTF8.GetString(ProjectFile.Write(copy));
        var facade = new MusicCoreFacade();

        JsonElement refused = FacadeCalls.Json(facade.Call(0, "library.link", $$"""{"file": {{JsonSerializer.Serialize(file)}}, "fileName": "Prelude.pdf", "sha256": "{{s_a}}", "bytes": 10}"""));
        JsonElement opened = FacadeCalls.Json(facade.Open("Prelude.pdf", "%PDF-1.4 x"u8.ToArray(), JsonSerializer.Serialize(new { project = file })));

        Assert.Equal("unavailable", refused.GetProperty("error").GetString());
        Assert.Equal("unavailable", opened.GetProperty("error").GetString());
    }

    [Fact]
    public void Library_link_refuses_a_folder_outside_the_chosen_one()
    {
        JsonElement answer = FacadeCalls.Json(new MusicCoreFacade().Call(0, "library.link", $$"""{"fileName": "x.pdf", "folder": "../up", "sha256": "{{s_a}}", "bytes": 10}"""));

        Assert.Equal("badRequest", answer.GetProperty("error").GetString());
    }
}