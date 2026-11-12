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
}