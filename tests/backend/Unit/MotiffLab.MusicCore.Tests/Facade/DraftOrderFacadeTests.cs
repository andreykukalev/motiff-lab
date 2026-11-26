using System.Text.Json;

using MotiffLab.MusicCore.Facade;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// Version 1.4 on the draft's map (spec «Коллаж», «Карта черновика: порядок мест»; «Ноты + запись»): a place moved among
/// the places (F1) and «Найти на записи» in a place's card (F2), each a change of the draft's structure that drafts.undo takes back.
/// </summary>
public class DraftOrderFacadeTests
{
    private const string Ipad = """ "device": "ipad", "here": [], "now": "2026-11-26T10:00:00Z" """;

    /// <summary>The example open as piece 1 with its recording heard as Notes n_2, and a draft whose places are bars 1–4 and 5–8 of the notes.</summary>
    private static MusicCoreFacade Opened()
    {
        var facade = new MusicCoreFacade();
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));
        Json(facade.Open("FurElise.musicxml", data, """{"language":"en","id":"p_order"}"""));
        Call(facade, "sources.original.add", """{"kind": "audio", "fileName": "Live.m4a", "kept": "device", "now": "2026-11-20T10:30:00Z"}""");
        Call(facade, "sources.notes.hear", Heard());
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 4}, "now": "2026-11-20T10:00:00Z"}""");
        Call(facade, "drafts.add", """{"bars": {"first": 5, "last": 8}, "draft": "d_0001", "drop": "place", "now": "2026-11-20T10:05:00Z"}""");
        return facade;
    }

    /// <summary>A request of a change from the iPad at a fixed time with <paramref name="more"/> fields.</summary>
    private static string Fields(string more) => $"{{{Ipad}, {more}}}";

    private static string[] Places(MusicCoreFacade facade) =>
        [.. Call(facade, "drafts.open", """{"draft": "d_0001"}""").GetProperty("draft").GetProperty("places").EnumerateArray().Select(static place => place.GetProperty("id").GetString()!)];

    private static string[] Variants(MusicCoreFacade facade, string place) =>
        [.. Call(facade, "drafts.open", """{"draft": "d_0001"}""").GetProperty("draft").GetProperty("places").EnumerateArray()
            .Single(each => each.GetProperty("id").GetString() == place).GetProperty("variants").EnumerateArray().Select(static variant => variant.GetProperty("id").GetString()!)];

    [Fact]
    public void A_place_moved_on_the_map_changes_the_order_and_undo_puts_it_back()
    {
        MusicCoreFacade facade = Opened();

        JsonElement moved = Call(facade, "drafts.movePlace", Fields(""" "draft": "d_0001", "place": "pl_0002", "index": 0 """));

        Assert.Equal(("d_0001", "pl_0002"), (moved.GetProperty("draft").GetString(), moved.GetProperty("place").GetString()));
        Assert.Empty(moved.GetProperty("dropped").EnumerateArray());
        Assert.True(moved.TryGetProperty("drafts", out _));
        Assert.Equal(["pl_0002", "pl_0001"], Places(facade));
        Assert.True(Call(facade, "drafts.undo", Fields(""" "draft": "d_0001" """)).GetProperty("structure").GetBoolean());
        Assert.Equal(["pl_0001", "pl_0002"], Places(facade));
    }

    [Fact]
    public void A_seam_kept_before_a_moved_place_is_dropped_and_comes_back_with_undo()
    {
        MusicCoreFacade facade = Opened();
        Call(facade, "drafts.leaveSeam", Fields(""" "draft": "d_0001", "place": "pl_0002" """));

        JsonElement moved = Call(facade, "drafts.movePlace", Fields(""" "draft": "d_0001", "place": "pl_0001", "index": 1 """));

        Assert.Equal([1], moved.GetProperty("dropped").EnumerateArray().Select(static step => step.GetInt32()));
        Assert.All(Call(facade, "drafts.seams", """{"draft": "d_0001"}""").GetProperty("seams").EnumerateArray(), static seam => Assert.False(seam.GetProperty("left").GetBoolean()));
        Call(facade, "drafts.undo", Fields(""" "draft": "d_0001" """));
        Assert.True(Call(facade, "drafts.seams", """{"draft": "d_0001"}""").GetProperty("seams")[0].GetProperty("left").GetBoolean());
    }

    [Theory]
    [InlineData("pl_0001", 0)]
    [InlineData("pl_0001", 2)]
    [InlineData("pl_0001", -1)]
    [InlineData("pl_0009", 0)]
    public void A_move_where_the_place_is_or_outside_the_places_is_a_bad_request_and_changes_nothing(string place, int index)
    {
        MusicCoreFacade facade = Opened();

        Assert.Equal("badRequest", Error(facade, "drafts.movePlace", Fields($$""" "draft": "d_0001", "place": "{{place}}", "index": {{index}} """)));
        Assert.Equal(["pl_0001", "pl_0002"], Places(facade));
        // Nothing was kept for undo: the draft has no step and no change of structure to take back.
        Assert.Equal("unavailable", Error(facade, "drafts.undo", Fields(""" "draft": "d_0001" """)));
    }

    [Fact]
    public void The_recording_s_stretch_of_a_place_is_found_put_in_as_its_variant_and_undone()
    {
        MusicCoreFacade facade = Opened();

        JsonElement found = Assert.Single(Call(facade, "drafts.findOnRecording", """{"draft": "d_0001", "place": "pl_0001"}""").GetProperty("found").EnumerateArray());
        Assert.Equal("n_2", found.GetProperty("notes").GetString());
        Assert.True(found.GetProperty("percent").GetInt32() >= 50);
        Assert.StartsWith("Recording", found.GetProperty("label").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Object, found.GetProperty("ms").ValueKind);
        Assert.Equal(JsonValueKind.Null, found.GetProperty("variant").ValueKind);

        JsonElement added = Call(facade, "drafts.addFromRecording", Fields(""" "draft": "d_0001", "place": "pl_0001" """));

        Assert.Equal("v_0003", added.GetProperty("variant").GetString());
        Assert.Equal(found.GetProperty("percent").GetInt32(), added.GetProperty("percent").GetInt32());
        Assert.Equal(["v_0001", "v_0003"], Variants(facade, "pl_0001"));
        // Found again, it names the variant it is now, and is not put in twice.
        Assert.Equal("v_0003", Call(facade, "drafts.findOnRecording", """{"draft": "d_0001", "place": "pl_0001"}""").GetProperty("found")[0].GetProperty("variant").GetString());
        Assert.Equal("unavailable", Error(facade, "drafts.addFromRecording", Fields(""" "draft": "d_0001", "place": "pl_0001" """)));
        Assert.True(Call(facade, "drafts.undo", Fields(""" "draft": "d_0001" """)).GetProperty("structure").GetBoolean());
        Assert.Equal(["v_0001"], Variants(facade, "pl_0001"));
    }

    [Fact]
    public void A_piece_with_no_recording_finds_nothing_and_adds_nothing()
    {
        var facade = new MusicCoreFacade();
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));
        Json(facade.Open("FurElise.musicxml", data, """{"language":"ru","id":"p_order"}"""));
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 4}, "now": "2026-11-20T10:00:00Z"}""");

        Assert.Empty(Call(facade, "drafts.findOnRecording", """{"draft": "d_0001", "place": "pl_0001"}""").GetProperty("found").EnumerateArray());
        JsonElement refused = Json(facade.Call(1, "drafts.addFromRecording", Fields(""" "draft": "d_0001", "place": "pl_0001" """)));
        Assert.Equal("unavailable", refused.GetProperty("error").GetString());
        Assert.Equal("На записи нет музыки этого места.", refused.GetProperty("message").GetString());
        Assert.Equal("badRequest", Error(facade, "drafts.findOnRecording", """{"draft": "d_0001", "place": "pl_0009"}"""));
    }
}