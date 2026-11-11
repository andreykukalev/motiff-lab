using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>The library's title, composer and tags through the facade (contract 12): the summary, project.metadata and project.save.</summary>
public class ProjectMetadataFacadeTests
{
    private const string FurElise = "fur-elise-original.musicxml";

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name));

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    private static JsonElement Answer(MusicCoreFacade facade, int piece, string name, string? request = null)
    {
        JsonElement answer = Json(facade.Call(piece, name, request));
        Assert.False(answer.TryGetProperty("error", out JsonElement error), $"{name}: {error} {answer}");
        return answer;
    }

    private static string[] Tags(JsonElement project) => [.. project.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()!)];

    private static (string? Title, string? TitleFrom, string? Composer, string? ComposerFrom) Shown(JsonElement project) => (
        project.GetProperty("title").GetString(), project.GetProperty("titleFrom").GetString(),
        project.GetProperty("composer").GetString(), project.GetProperty("composerFrom").GetString());

    private static string Request(string file, string fields) => $"{{\"file\": {JsonSerializer.Serialize(file)}, {fields}}}";

    [Fact]
    public void The_summary_shows_the_score_title_and_composer_from_the_file_and_no_tags()
    {
        var facade = new MusicCoreFacade();
        JsonElement project = Json(facade.Open(FurElise, Fixture(FurElise), """{"id": "p_fur", "now": "2026-11-11T09:00:00Z"}""")).GetProperty("project");

        Assert.Equal(("Fur Elise, opening", "file", "Ludwig van Beethoven", "file"), Shown(project));
        Assert.Empty(Tags(project));

        JsonElement midi = Json(facade.Open("Scale.mid", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Midi", "Fixtures", "two-hands-type1.mid")))).GetProperty("project");
        Assert.Equal(("Scale", "fileName", null, null), Shown(midi));
    }

    [Fact]
    public void Project_metadata_sets_and_clears_the_title_composer_and_tags_and_project_save_keeps_them()
    {
        var facade = new MusicCoreFacade();
        Assert.False(Json(facade.Open(FurElise, Fixture(FurElise), """{"id": "p_fur"}""")).TryGetProperty("error", out _));

        JsonElement set = Answer(facade, 1, "project.metadata", """{"title": " Für  Elise ", "composer": "Бетховен", "tags": ["Экзамен", "экзамен", " Romantic ", ""]}""").GetProperty("project");
        Assert.Equal(("Für Elise", "player", "Бетховен", "player"), Shown(set));
        Assert.Equal(["Экзамен", "Romantic"], Tags(set));

        // A field left out stays; null clears it, and the file's own title shows again.
        JsonElement cleared = Answer(facade, 1, "project.metadata", """{"title": null}""").GetProperty("project");
        Assert.Equal(("Fur Elise, opening", "file", "Бетховен", "player"), Shown(cleared));
        Assert.Equal(["Экзамен", "Romantic"], Tags(cleared));

        string saved = facade.Call(1, "project.save");
        JsonElement file = Json(saved);
        Assert.Equal(15, file.GetProperty("version").GetInt32());
        Assert.Equal("""{"composer":"Бетховен","tags":["Экзамен","Romantic"]}""", JsonSerializer.Serialize(file.GetProperty("metadata"), new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

        var again = new MusicCoreFacade();
        JsonElement reopened = Json(again.Open("fur.motiff.json", Encoding.UTF8.GetBytes(saved))).GetProperty("project");
        Assert.Equal(Shown(cleared), Shown(reopened));
        Assert.Equal(Tags(cleared), Tags(reopened));
        Assert.Equal(saved, again.Call(1, "project.save"));

        JsonElement none = Answer(facade, 1, "project.metadata", """{"composer": "", "tags": null}""").GetProperty("project");
        Assert.Equal(("Fur Elise, opening", "file", "Ludwig van Beethoven", "file"), Shown(none));
        Assert.False(Json(facade.Call(1, "project.save")).TryGetProperty("metadata", out _));
    }

    [Fact]
    public void A_project_file_gets_its_metadata_without_opening_the_piece()
    {
        var facade = new MusicCoreFacade();
        Assert.False(Json(facade.Open(FurElise, Fixture(FurElise), """{"id": "p_fur"}""")).TryGetProperty("error", out _));
        string saved = facade.Call(1, "project.save");

        var library = new MusicCoreFacade();
        JsonElement answer = Answer(library, 0, "project.metadata", Request(saved, """ "tags": ["Exam"], "composer": "L. van Beethoven" """));
        Assert.Equal(("Fur Elise, opening", "file", "L. van Beethoven", "player"), Shown(answer.GetProperty("project")));
        Assert.Equal(0, library.Count);

        string changed = answer.GetProperty("file").GetString()!;
        JsonElement summary = Answer(library, 0, "project.summary", Request(changed, "\"language\": \"en\"")).GetProperty("project");
        Assert.Equal(Shown(answer.GetProperty("project")), Shown(summary));
        Assert.Equal(["Exam"], Tags(summary));
        Assert.Equal("p_fur", summary.GetProperty("id").GetString());
    }

    [Fact]
    public void A_pdf_piece_keeps_its_metadata_too()
    {
        var facade = new MusicCoreFacade();
        byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");
        JsonElement opened = Json(facade.Open("Nocturne Op. 9.pdf", pdf, """{"id": "p_pdf"}""")).GetProperty("project");
        Assert.Equal(("Nocturne Op. 9", "fileName", null, null), Shown(opened));

        JsonElement set = Answer(facade, 1, "project.metadata", """{"composer": "Chopin", "tags": ["Nocturnes"]}""").GetProperty("project");
        Assert.Equal(("Nocturne Op. 9", "fileName", "Chopin", "player"), Shown(set));

        JsonElement reopened = Json(new MusicCoreFacade().Open("p_pdf.motiff.json", Encoding.UTF8.GetBytes(facade.Call(1, "project.save")))).GetProperty("project");
        Assert.Equal(Shown(set), Shown(reopened));
        Assert.Equal(["Nocturnes"], Tags(reopened));
    }

    [Theory]
    [InlineData("""{"tags": "Exam"}""")]
    [InlineData("""{"tags": [1]}""")]
    [InlineData("""{"title": 3}""")]
    public void Metadata_of_the_wrong_shape_is_a_bad_request_and_changes_nothing(string request)
    {
        var facade = new MusicCoreFacade();
        Assert.False(Json(facade.Open(FurElise, Fixture(FurElise))).TryGetProperty("error", out _));
        string before = facade.Call(1, "project.save");

        Assert.Equal("badRequest", Json(facade.Call(1, "project.metadata", request)).GetProperty("error").GetString());
        Assert.Equal(before, facade.Call(1, "project.save"));
        Assert.Equal("badRequest", Json(facade.Call(0, "project.metadata", """{"title": "No file"}""")).GetProperty("error").GetString());
    }
}