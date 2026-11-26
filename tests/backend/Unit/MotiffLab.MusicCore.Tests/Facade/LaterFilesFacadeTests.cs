using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;
using static MotiffLab.MusicCore.Tests.Projects.UnknownValueTests;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// Project files a later build wrote, through the facade as both apps open and save them: one of a newer version is refused
/// in the player's language (#363); one of this version with a value this build does not know keeps the player's work and
/// the entity it hides (0026 S6, #364).
/// </summary>
public class LaterFilesFacadeTests
{
    private static byte[] Golden(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", name));

    private static MusicCoreFacade Opened(JsonNode file)
    {
        var facade = new MusicCoreFacade();
        JsonElement opened = Json(facade.Open("piece" + ProjectFile.Extension, Encoding.UTF8.GetBytes(file.ToJsonString())));
        Assert.False(opened.TryGetProperty("error", out JsonElement error), error.ToString());
        return facade;
    }

    private static JsonObject Saved(MusicCoreFacade facade) => JsonNode.Parse(facade.Call(1, "project.save"))!.AsObject();

    [Theory]
    [InlineData("ru", "Этот проект сохранён более новой версией Motiff Lab. Обновите приложение и попробуйте ещё раз.")]
    [InlineData("en", "This project was saved by a newer version of Motiff Lab. Update the app and try again.")]
    public void A_file_of_a_newer_version_is_refused_in_the_players_language(string language, string message)
    {
        JsonElement answer = Json(new MusicCoreFacade().Open("v18-future" + ProjectFile.Extension, Golden("v18-future.motiff.json"), $$"""{"language": "{{language}}"}"""));

        Assert.Equal(("import", message, "damaged"), (answer.GetProperty("error").GetString(), answer.GetProperty("message").GetString(), answer.GetProperty("reason").GetString()));
    }

    [Theory]
    [InlineData("project.summary", "ru", "Этот проект сохранён более новой версией Motiff Lab. Обновите приложение и попробуйте ещё раз.")]
    [InlineData("project.metadata", "ru", "Этот проект сохранён более новой версией Motiff Lab. Обновите приложение и попробуйте ещё раз.")]
    [InlineData("project.drafts", "ru", "Этот проект сохранён более новой версией Motiff Lab. Обновите приложение и попробуйте ещё раз.")]
    [InlineData("project.summary", "en", "This project was saved by a newer version of Motiff Lab. Update the app and try again.")]
    [InlineData("project.metadata", null, "This project was saved by a newer version of Motiff Lab. Update the app and try again.")]
    public void A_file_of_a_newer_version_read_without_opening_it_is_refused_in_the_players_language(string call, string? language, string message)
    {
        string request = JsonSerializer.Serialize(new { file = Encoding.UTF8.GetString(Golden("v18-future.motiff.json")), language });

        JsonElement answer = Json(new MusicCoreFacade().Call(0, call, request));

        Assert.Equal(("badRequest", message), (answer.GetProperty("error").GetString(), answer.GetProperty("message").GetString()));
    }

    [Fact]
    public void The_scan_inserts_refusal_of_a_newer_file_is_in_the_players_language()
    {
        string request = JsonSerializer.Serialize(new { file = Encoding.UTF8.GetString(Golden("v18-future.motiff.json")), page = 1, language = "ru" });

        JsonElement answer = Json(new MusicCoreFacade().Call(0, "scan.insert", request));

        Assert.Equal(("badRequest", "Этот проект сохранён более новой версией Motiff Lab. Обновите приложение и попробуйте ещё раз."), (answer.GetProperty("error").GetString(), answer.GetProperty("message").GetString()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Bookmarks_go_into_a_built_in_category_with_an_unknown_ink_and_stay_visible(int builtIn)
    {
        JsonObject file = With(Worked(), $"bookmarks/categories/{builtIn}/ink", UnknownValue);
        MusicCoreFacade facade = Opened(file);

        Call(facade, "bookmark.add", """{"bars": {"first": 2}, "title": "Mine", "now": "2026-11-26T10:00:00Z"}""");
        Call(facade, "bookmark.form", """{"now": "2026-11-26T10:01:00Z"}""");
        Call(facade, "bookmark.add", """{"bars": {"first": 3}, "title": "Exam piece", "category": "c_0001", "now": "2026-11-26T10:02:00Z"}""");
        string[] shown = [.. Call(facade, "category.remove", """{"id": "c_0001"}""").GetProperty("bookmarks").EnumerateArray().Select(bookmark => bookmark.GetProperty("id").GetString()!)];
        JsonObject saved = Saved(facade);

        Assert.True(JsonNode.DeepEquals(file["workspace"]!["bookmarks"]!["categories"]![builtIn], saved["workspace"]!["bookmarks"]!["categories"]![builtIn]));
        MusicCoreFacade again = Opened(saved);
        JsonElement bookmarks = Call(again, "bookmarks");
        Assert.Equal(shown, bookmarks.GetProperty("bookmarks").EnumerateArray().Select(bookmark => bookmark.GetProperty("id").GetString()!));
        Assert.Contains(bookmarks.GetProperty("bookmarks").EnumerateArray(), bookmark => bookmark.GetProperty("title").GetString() == "Exam piece" && bookmark.GetProperty("category").GetString() == "practise");
        Assert.Equal(["practise", "performance", "structure", "teacher"], bookmarks.GetProperty("categories").EnumerateArray().Select(category => category.GetProperty("id").GetString()!));
        Assert.Equal(saved.ToJsonString(), Saved(again).ToJsonString());
    }

    [Fact]
    public void A_plain_save_keeps_every_variation_of_a_hidden_motif_and_files_the_working_one_apart()
    {
        // QA finding 1 of PR #362: v_0002 of m_0001 holds the unknown value, so m_0001 is hidden and the facade works on m_0002,
        // whose variation it names v_0002 too.
        JsonObject file = With(Worked(), "variations/1/steps/0/parameters/kind", UnknownValue);
        JsonObject saved = Saved(Opened(file));

        JsonArray variations = saved["workspace"]!["variations"]!.AsArray();
        Assert.Equal(["v_0001", "v_0002", "v_0003", "v_0004", "v_0005"], variations.Where(variation => variation!["motifId"]!.GetValue<string>() == "m_0001").Select(variation => variation!["id"]!.GetValue<string>()));
        Assert.True(JsonNode.DeepEquals(file["workspace"]!["variations"]![1], variations[1]));
        string working = variations.Single(variation => variation!["motifId"]!.GetValue<string>() == "m_0002")!["id"]!.GetValue<string>();
        Assert.Single(variations, variation => variation!["id"]!.GetValue<string>() == working);
        Assert.Single(saved["workspace"]!["motifs"]!.AsArray(), motif => motif!["id"]!.GetValue<string>() == "m_0001");
        Assert.Equal(saved.ToJsonString(), Saved(Opened(saved)).ToJsonString());
    }
}