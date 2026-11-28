using System.Text.Json;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Facade;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>The library's playlists through the facade, on piece 0 (version 1.7, contract 17, calls added; docs/playlists-file.md).</summary>
public class PlaylistsFacadeTests
{
    private readonly MusicCoreFacade _facade = new();

    /// <summary>The answer of a call on piece 0, the request built as an object; with <paramref name="error"/>, the error it must answer.</summary>
    private JsonNode Call(string name, JsonObject request, string? error = null)
    {
        JsonNode answer = JsonNode.Parse(_facade.Call(0, name, request.ToJsonString()))!;
        Assert.Equal(error, answer["error"]?.GetValue<string>());
        return answer;
    }

    private static string File(JsonNode answer) => answer["file"]!.GetValue<string>();

    private static List<string> Names(JsonNode answer) => [.. answer["playlists"]!.AsArray().Select(static playlist => playlist!["name"]!.GetValue<string>())];

    private static List<string> Pieces(JsonNode answer, int index) => [.. answer["playlists"]![index]!["pieces"]!.AsArray().Select(static piece => piece!.GetValue<string>())];

    [Fact]
    public void No_file_is_no_playlists_and_every_edit_answers_the_file_to_keep()
    {
        JsonNode none = Call("playlists", []);
        Assert.Empty(Names(none));

        JsonNode made = Call("playlists.create", new() { ["file"] = File(none), ["name"] = "Для рояля" });
        Assert.Equal(("list_0001", null), (made["id"]!.GetValue<string>(), made["taken"]?.GetValue<string>()));
        JsonNode added = Call("playlists.add", new() { ["file"] = File(made), ["id"] = "list_0001", ["pieces"] = new JsonArray("p_a", "p_b", "p_c") });
        JsonNode moved = Call("playlists.move", new() { ["file"] = File(added), ["id"] = "list_0001", ["pieces"] = new JsonArray("p_c"), ["before"] = "p_a" });
        JsonNode removed = Call("playlists.remove", new() { ["file"] = File(moved), ["id"] = "list_0001", ["pieces"] = new JsonArray("p_a") });
        JsonNode renamed = Call("playlists.rename", new() { ["file"] = File(removed), ["id"] = "list_0001", ["name"] = "Рояль" });

        Assert.Equal(["p_c", "p_b"], Pieces(renamed, 0));
        Assert.Equal(["Рояль"], Names(Call("playlists", new() { ["file"] = File(renamed) })));
        Assert.False(renamed["readOnly"]!.GetValue<bool>());
        Assert.Empty(Names(Call("playlists.delete", new() { ["file"] = File(renamed), ["id"] = "list_0001" })));
    }

    [Fact]
    public void A_name_another_playlist_has_answers_it_as_taken_and_changes_nothing()
    {
        JsonNode two = Call("playlists.migrate", new() { ["pieces"] = new JsonArray(new JsonObject { ["id"] = "p_a", ["playlists"] = new JsonArray("Бах", "Шопен") }) });
        string bach = two["playlists"]![0]!["id"]!.GetValue<string>();
        string chopin = two["playlists"]![1]!["id"]!.GetValue<string>();

        JsonNode created = Call("playlists.create", new() { ["file"] = File(two), ["name"] = "БАХ" });
        JsonNode renamed = Call("playlists.rename", new() { ["file"] = File(two), ["id"] = chopin, ["name"] = "бах" });

        Assert.Equal((null, bach), (created["id"]?.GetValue<string>(), created["taken"]!.GetValue<string>()));
        Assert.Equal(bach, renamed["taken"]!.GetValue<string>());
        Assert.Equal(File(two), File(created));
        Assert.Equal(File(two), File(renamed));
    }

    [Fact]
    public void A_deleted_piece_and_a_re_keyed_one_follow_on_every_playlist()
    {
        JsonNode moved = Call("playlists.migrate", new()
        {
            ["pieces"] = new JsonArray(
            new JsonObject { ["id"] = "p_0001", ["playlists"] = new JsonArray("Бах") },
            new JsonObject { ["id"] = "p_b", ["playlists"] = new JsonArray("Бах", "Шопен") })
        });

        JsonNode forgotten = Call("playlists.forget", new() { ["file"] = File(moved), ["pieces"] = new JsonArray("p_b") });
        JsonNode rekeyed = Call("playlists.rekey", new() { ["file"] = File(forgotten), ["pieces"] = new JsonArray(new JsonObject { ["id"] = "p_new", ["formerIds"] = new JsonArray("p_0001") }) });

        Assert.Equal(["p_new"], Pieces(rekeyed, 0));
        Assert.Empty(Pieces(rekeyed, 1));
    }

    [Fact]
    public void A_playlist_not_there_an_empty_name_a_damaged_file_and_a_later_builds_file_are_refused()
    {
        string file = File(Call("playlists.create", new() { ["name"] = "Бах" }));
        string newer = file.Replace("\"version\": 1", "\"version\": 2", StringComparison.Ordinal);

        Call("playlists.delete", new() { ["file"] = file, ["id"] = "list_9" }, "notFound");
        Call("playlists.create", new() { ["file"] = file, ["name"] = " " }, "badRequest");
        Call("playlists.move", new() { ["file"] = file, ["id"] = "list_0001", ["pieces"] = new JsonArray("p_a"), ["before"] = "p_x" }, "badRequest");
        Call("playlists", new() { ["file"] = "{}" }, "badRequest");
        Assert.True(Call("playlists", new() { ["file"] = newer })["readOnly"]!.GetValue<bool>());
        Call("playlists.add", new() { ["file"] = newer, ["id"] = "list_0001", ["pieces"] = new JsonArray("p_a") }, "readOnly");
    }

    [Fact]
    public void Two_copies_resolve_against_their_base_with_what_goes_online_and_the_conflicts()
    {
        string common = File(Call("playlists.create", new() { ["name"] = "Бах", ["pieces"] = new JsonArray("p_a") }));
        string here = File(Call("playlists.rename", new() { ["file"] = common, ["id"] = "list_0001", ["name"] = "Бах I" }));
        string online = File(Call("playlists.rename", new() { ["file"] = common, ["id"] = "list_0001", ["name"] = "Бах II" }));

        JsonNode resolved = Call("playlists.resolve", new() { ["here"] = here, ["savedHere"] = "2026-11-28T10:05:00Z", ["online"] = online, ["savedOnline"] = "2026-11-28T10:00:00Z", ["base"] = common });

        Assert.Equal(["Бах I"], Names(Call("playlists", new() { ["file"] = File(resolved) })));
        Assert.True(resolved["upload"]!.GetValue<bool>());
        Assert.True(resolved["merged"]!.GetValue<bool>());
        Assert.Equal("""[{"in":"playlists.name","id":"list_0001"}]""", resolved["conflicts"]!.ToJsonString());
        Call("playlists.resolve", new() { ["here"] = here, ["savedHere"] = "soon", ["online"] = online, ["savedOnline"] = "2026-11-28T10:00:00Z" }, "badRequest");
    }
}