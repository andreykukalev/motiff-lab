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
    public void A_plain_save_keeps_every_variation_of_the_working_motif_and_of_a_hidden_one()
    {
        // QA finding 1 of PR #362 and #821: v_0002 of m_0001 holds the unknown value, so m_0001 is hidden and the facade works
        // on m_0002, opened on its first variation v_0006; that one keeps its own id and the others of m_0002 stay as they were.
        JsonObject file = With(Worked(), "variations/1/steps/0/parameters/kind", UnknownValue);
        JsonObject saved = Saved(Opened(file));

        Assert.Equal(["v_0001", "v_0002", "v_0003", "v_0004", "v_0005"], VariationsOf(saved, "m_0001"));
        Assert.Equal(["v_0006", "v_0007", "v_0008", "v_0009", "v_0010"], VariationsOf(saved, "m_0002"));
        Assert.True(JsonNode.DeepEquals(file["workspace"]!["variations"], saved["workspace"]!["variations"]));
        Assert.Single(saved["workspace"]!["motifs"]!.AsArray(), motif => motif!["id"]!.GetValue<string>() == "m_0001");
        Assert.Equal(saved.ToJsonString(), Saved(Opened(saved)).ToJsonString());
    }

    [Fact]
    public void A_variation_tried_on_the_working_motif_is_saved_beside_the_one_it_was_opened_on()
    {
        // #821 and review round 1: v_0006, the variation the piece opened on, stays as it was with every other one; the tried
        // chain is saved under an id no variation has, known to this build or not.
        JsonObject file = With(Worked(), "variations/1/steps/0/parameters/kind", UnknownValue);
        MusicCoreFacade facade = Opened(file);
        Call(facade, "try", """{"chip": "v1.halfTime"}""");
        JsonObject saved = Saved(facade);

        JsonArray after = saved["workspace"]!["variations"]!.AsArray();
        Assert.Equal(["v_0012", "v_0006", "v_0007", "v_0008", "v_0009", "v_0010"], VariationsOf(saved, "m_0002"));
        Assert.True(JsonNode.DeepEquals(file["workspace"]!["variations"], new JsonArray([.. after.Where(variation => variation!["id"]!.GetValue<string>() != "v_0012").Select(variation => variation!.DeepClone())])));
        Assert.Equal(saved.ToJsonString(), Saved(Opened(saved)).ToJsonString());
    }

    [Fact]
    public void Another_passage_is_saved_beside_the_working_motif_which_keeps_its_variations()
    {
        // #821, the lead's decision: m_0002 and v_0006–v_0010 stay as they were; the new passage takes an id no motif has
        // (m_0001 is the hidden one's).
        JsonObject file = With(Worked(), "variations/1/steps/0/parameters/kind", UnknownValue);
        MusicCoreFacade facade = Opened(file);
        Call(facade, "select", """{"bars": {"first": 3, "last": 3}}""");
        JsonObject saved = Saved(facade);

        JsonArray motifs = saved["workspace"]!["motifs"]!.AsArray();
        Assert.Equal(["m_0001", "m_0004", "m_0002", "m_0003"], motifs.Select(motif => motif!["id"]!.GetValue<string>()));
        Assert.True(JsonNode.DeepEquals(file["workspace"]!["motifs"]![1], motifs[2]));
        Assert.True(JsonNode.DeepEquals(file["workspace"]!["variations"], saved["workspace"]!["variations"]));
        Assert.Equal(saved.ToJsonString(), Saved(Opened(saved)).ToJsonString());
    }

    [Fact]
    public void A_passage_saved_when_no_motif_opens_never_takes_the_place_of_a_motif_this_build_does_not_know()
    {
        // #821: with m_0001 hidden and no other motif, the working motif is m_0002, so m_0001 and its variations stay.
        JsonObject file = With(Worked(), "motifs/0/mode", UnknownValue);
        JsonObject workspace = file["workspace"]!.AsObject();
        workspace["motifs"] = new JsonArray(workspace["motifs"]![0]!.DeepClone());
        workspace["variations"] = new JsonArray([.. workspace["variations"]!.AsArray().Take(5).Select(variation => variation!.DeepClone())]);
        MusicCoreFacade facade = Opened(file);
        Call(facade, "select", """{"bars": {"first": 1, "last": 2}}""");
        JsonObject saved = Saved(facade);

        JsonArray motifs = saved["workspace"]!["motifs"]!.AsArray();
        Assert.Equal(["m_0001", "m_0002"], motifs.Select(motif => motif!["id"]!.GetValue<string>()));
        Assert.True(JsonNode.DeepEquals(workspace["motifs"]![0], motifs[0]));
        Assert.True(JsonNode.DeepEquals(workspace["variations"], saved["workspace"]!["variations"]));
        Assert.Equal(saved.ToJsonString(), Saved(Opened(saved)).ToJsonString());
    }

    private static string[] VariationsOf(JsonObject file, string motif) =>
        [.. file["workspace"]!["variations"]!.AsArray().Where(variation => variation!["motifId"]!.GetValue<string>() == motif).Select(variation => variation!["id"]!.GetValue<string>())];
}