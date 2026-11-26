using System.Text.Json;

using MotiffLab.MusicCore.Facade;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// Version 1.4, Andrei's answers of 2026-11-26: a place made longer or shorter (drafts.length, drafts.resize), a приём carried
/// into another place (drafts.carryPlaces, drafts.carry), and a move that drops a bridge; each a change of the draft's
/// structure that drafts.undo takes back.
/// </summary>
public class DraftLengthFacadeTests
{
    private const string Ipad = """ "device": "ipad", "here": [], "now": "2026-11-26T10:00:00Z" """;

    /// <summary>The example open in <paramref name="language"/>, a draft whose places are bars 1–4 and 5–8 of the notes.</summary>
    private static MusicCoreFacade Opened(string language = "en")
    {
        var facade = new MusicCoreFacade();
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));
        Json(facade.Open("FurElise.musicxml", data, $$"""{"language":"{{language}}","id":"p_length"}"""));
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 4}, "now": "2026-11-20T10:00:00Z"}""");
        Call(facade, "drafts.add", """{"bars": {"first": 5, "last": 8}, "draft": "d_0001", "drop": "place", "now": "2026-11-20T10:05:00Z"}""");
        return facade;
    }

    private static string Fields(string more) => $"{{{Ipad}, {more}}}";

    private static JsonElement Place(MusicCoreFacade facade, string id) =>
        Call(facade, "drafts.open", """{"draft": "d_0001"}""").GetProperty("draft").GetProperty("places").EnumerateArray().Single(each => each.GetProperty("id").GetString() == id);

    private static (int, int) Bars(JsonElement bars) => (bars.GetProperty("first").GetInt32(), bars.GetProperty("last").GetInt32());

    [Fact]
    public void A_place_made_longer_has_its_source_s_bars_and_undo_puts_its_length_back()
    {
        MusicCoreFacade facade = Opened();
        JsonElement length = Call(facade, "drafts.length", """{"draft": "d_0001", "place": "pl_0001"}""");
        Assert.Equal((1, 4), Bars(length.GetProperty("bars")));
        Assert.True(length.GetProperty("most").GetProperty("last").GetInt32() >= 8);
        Assert.True(length.GetProperty("resizable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, length.GetProperty("reason").ValueKind);
        int lastBar = Place(facade, "pl_0001").GetProperty("lastBar").GetInt32();

        JsonElement resized = Call(facade, "drafts.resize", Fields(""" "draft": "d_0001", "place": "pl_0001", "bars": {"first": 1, "last": 6} """));

        Assert.Equal(("pl_0001", "v_0001", (1, 6)), (resized.GetProperty("place").GetString(), resized.GetProperty("variant").GetString(), Bars(resized.GetProperty("bars"))));
        Assert.Empty(resized.GetProperty("dropped").EnumerateArray());
        Assert.StartsWith("① Notes 1–6", Place(facade, "pl_0001").GetProperty("label").GetString(), StringComparison.Ordinal);
        Assert.Equal(lastBar + 2, Place(facade, "pl_0001").GetProperty("lastBar").GetInt32());
        Assert.True(Call(facade, "drafts.undo", Fields(""" "draft": "d_0001" """)).GetProperty("structure").GetBoolean());
        Assert.Equal((1, 4), Bars(Call(facade, "drafts.length", """{"draft": "d_0001", "place": "pl_0001"}""").GetProperty("bars")));
    }

    [Theory]
    [InlineData(4, 3)]
    [InlineData(-1, 4)]
    [InlineData(1, 999)]
    [InlineData(1, 4)]
    public void No_bars_bars_outside_the_notes_or_the_length_it_has_are_a_bad_request(int first, int last)
    {
        MusicCoreFacade facade = Opened();

        Assert.Equal("badRequest", Error(facade, "drafts.resize", Fields($$""" "draft": "d_0001", "place": "pl_0001", "bars": {"first": {{first}}, "last": {{last}}} """)));
        Assert.Equal("unavailable", Error(facade, "drafts.undo", Fields(""" "draft": "d_0001" """)));
    }

    [Fact]
    public void A_seam_kept_as_it_is_goes_when_the_place_before_it_ends_elsewhere()
    {
        MusicCoreFacade facade = Opened();
        Call(facade, "drafts.leaveSeam", Fields(""" "draft": "d_0001", "place": "pl_0002" """));

        JsonElement resized = Call(facade, "drafts.resize", Fields(""" "draft": "d_0001", "place": "pl_0001", "bars": {"first": 1, "last": 3} """));

        Assert.Equal([1], resized.GetProperty("dropped").EnumerateArray().Select(static step => step.GetInt32()));
        Call(facade, "drafts.undo", Fields(""" "draft": "d_0001" """));
        Assert.True(Call(facade, "drafts.seams", """{"draft": "d_0001"}""").GetProperty("seams")[0].GetProperty("left").GetBoolean());
    }

    [Fact]
    public void A_приём_is_carried_into_another_place_as_a_variant_the_player_takes_from_and_undo_takes_it_back()
    {
        MusicCoreFacade facade = Opened();
        Call(facade, "drafts.add", """{"bars": {"first": 5, "last": 8}, "draft": "d_0001", "drop": "variant", "place": "pl_0001", "now": "2026-11-20T10:10:00Z"}""");
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 4}, "draft": "d_0001", "drop": "place", "now": "2026-11-20T10:15:00Z"}""");
        JsonElement technique = Call(facade, "drafts.techniques", """{"draft": "d_0001", "place": "pl_0001"}""").GetProperty("techniques")[0];
        int number = technique.GetProperty("number").GetInt32();

        JsonElement places = Call(facade, "drafts.carryPlaces", $$"""{"draft": "d_0001", "place": "pl_0001", "technique": {{number}}}""").GetProperty("places");
        Assert.Equal(["pl_0002", "pl_0003"], places.EnumerateArray().Select(static each => each.GetProperty("place").GetString()));
        JsonElement fits = places.EnumerateArray().First(static each => each.GetProperty("bars").GetArrayLength() > 0);
        string to = fits.GetProperty("place").GetString()!;

        JsonElement carried = Call(facade, "drafts.carry", Fields($$""" "draft": "d_0001", "place": "pl_0001", "technique": {{number}}, "to": "{{to}}" """));

        Assert.Equal((to, "v_0005"), (carried.GetProperty("place").GetString(), carried.GetProperty("variant").GetString()));
        Assert.Equal(fits.GetProperty("bars").EnumerateArray().Select(static bar => bar.GetInt32()), carried.GetProperty("bars").EnumerateArray().Select(static bar => bar.GetInt32()));
        JsonElement variant = Place(facade, to).GetProperty("variants")[1];
        Assert.StartsWith("Carried technique: ", variant.GetProperty("label").GetString(), StringComparison.Ordinal);
        Assert.Contains(Call(facade, "drafts.techniques", $$"""{"draft": "d_0001", "place": "{{to}}"}""").GetProperty("techniques").EnumerateArray(), static each => each.GetProperty("variant").GetString() == "v_0005");
        Assert.Equal("unavailable", Error(facade, "drafts.carry", Fields($$""" "draft": "d_0001", "place": "pl_0001", "technique": {{number}}, "to": "{{to}}" """)));
        Assert.True(Call(facade, "drafts.undo", Fields(""" "draft": "d_0001" """)).GetProperty("structure").GetBoolean());
        Assert.Equal(1, Place(facade, to).GetProperty("variants").GetArrayLength());
    }

    [Fact]
    public void A_carried_variant_made_the_base_cannot_change_its_length_and_says_why()
    {
        MusicCoreFacade facade = Opened("ru");
        Call(facade, "drafts.add", """{"bars": {"first": 5, "last": 8}, "draft": "d_0001", "drop": "variant", "place": "pl_0001", "now": "2026-11-20T10:10:00Z"}""");
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 4}, "draft": "d_0001", "drop": "place", "now": "2026-11-20T10:15:00Z"}""");
        int number = Call(facade, "drafts.techniques", """{"draft": "d_0001", "place": "pl_0001"}""").GetProperty("techniques")[0].GetProperty("number").GetInt32();
        string to = Call(facade, "drafts.carry", Fields($$""" "draft": "d_0001", "place": "pl_0001", "technique": {{number}}, "to": "pl_0003" """)).GetProperty("place").GetString()!;
        Call(facade, "drafts.base", Fields($$""" "draft": "d_0001", "place": "{{to}}", "variant": "v_0005" """));

        JsonElement length = Call(facade, "drafts.length", $$"""{"draft": "d_0001", "place": "{{to}}"}""");

        Assert.False(length.GetProperty("resizable").GetBoolean());
        Assert.StartsWith("Основу сделало приложение", length.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Equal("unavailable", Error(facade, "drafts.resize", Fields($$""" "draft": "d_0001", "place": "{{to}}", "bars": {"first": 1, "last": 2} """)));
    }

    [Fact]
    public void A_move_that_gives_a_bridge_other_neighbours_drops_it_and_undo_puts_it_back()
    {
        MusicCoreFacade facade = Opened();
        Call(facade, "drafts.transpose", Fields(""" "draft": "d_0001", "place": "pl_0002", "semitones": 2 """));
        string bridge = Call(facade, "drafts.bridge", Fields(""" "draft": "d_0001", "place": "pl_0002" """)).GetProperty("place").GetString()!;
        Call(facade, "drafts.tempo", Fields($$""" "draft": "d_0001", "place": "{{bridge}}", "quarterNotesPerMinute": 100 """));

        JsonElement moved = Call(facade, "drafts.movePlace", Fields(""" "draft": "d_0001", "place": "pl_0002", "index": 0 """));

        Assert.Equal([2], moved.GetProperty("dropped").EnumerateArray().Select(static step => step.GetInt32()));
        string[] Order() => [.. Call(facade, "drafts.open", """{"draft": "d_0001"}""").GetProperty("draft").GetProperty("places").EnumerateArray().Select(static place => place.GetProperty("id").GetString()!)];
        Assert.Equal(["pl_0002", "pl_0001"], Order());
        Call(facade, "drafts.undo", Fields(""" "draft": "d_0001" """));
        Assert.Equal(["pl_0001", bridge, "pl_0002"], Order());
    }

    [Fact]
    public void A_bridge_itself_is_not_moved_and_says_why()
    {
        MusicCoreFacade facade = Opened("ru");
        Call(facade, "drafts.transpose", Fields(""" "draft": "d_0001", "place": "pl_0002", "semitones": 2 """));
        string bridge = Call(facade, "drafts.bridge", Fields(""" "draft": "d_0001", "place": "pl_0002" """)).GetProperty("place").GetString()!;

        JsonElement answer = Json(facade.Call(1, "drafts.movePlace", Fields($$""" "draft": "d_0001", "place": "{{bridge}}", "index": 0 """)));

        Assert.Equal("unavailable", answer.GetProperty("error").GetString());
        Assert.Equal("Связку нельзя переставить: она построена для своего стыка.", answer.GetProperty("message").GetString());
    }

    [Fact]
    public void A_resize_that_moves_a_bridge_s_seam_drops_it_and_undo_puts_it_back()
    {
        MusicCoreFacade facade = Opened();
        Call(facade, "drafts.transpose", Fields(""" "draft": "d_0001", "place": "pl_0002", "semitones": 2 """));
        string bridge = Call(facade, "drafts.bridge", Fields(""" "draft": "d_0001", "place": "pl_0002" """)).GetProperty("place").GetString()!;
        Call(facade, "drafts.tempo", Fields($$""" "draft": "d_0001", "place": "{{bridge}}", "quarterNotesPerMinute": 100 """));

        JsonElement resized = Call(facade, "drafts.resize", Fields(""" "draft": "d_0001", "place": "pl_0001", "bars": {"first": 1, "last": 3} """));

        Assert.Equal([2], resized.GetProperty("dropped").EnumerateArray().Select(static step => step.GetInt32()));
        string[] Order() => [.. Call(facade, "drafts.open", """{"draft": "d_0001"}""").GetProperty("draft").GetProperty("places").EnumerateArray().Select(static place => place.GetProperty("id").GetString()!)];
        Assert.Equal(["pl_0001", "pl_0002"], Order());
        Call(facade, "drafts.undo", Fields(""" "draft": "d_0001" """));
        Assert.Equal(["pl_0001", bridge, "pl_0002"], Order());
    }

    [Fact]
    public void A_place_cut_from_a_recording_s_notes_is_cut_again_from_them_with_its_stretch_of_the_recording()
    {
        MusicCoreFacade facade = Opened();
        Call(facade, "sources.original.add", """{"kind": "audio", "fileName": "Live.m4a", "kept": "device", "now": "2026-11-20T10:30:00Z"}""");
        Call(facade, "sources.notes.hear", Heard());
        int shown = Call(facade, "sources.notes.open", """{"id": "n_2"}""").GetProperty("piece").GetInt32();
        Assert.DoesNotContain("error", facade.Call(shown, "drafts.add", """{"bars": {"first": 1, "last": 4}, "draft": "d_0001", "drop": "place", "now": "2026-11-20T10:40:00Z"}"""), StringComparison.Ordinal);
        Assert.True(Call(facade, "drafts.length", """{"draft": "d_0001", "place": "pl_0003"}""").GetProperty("resizable").GetBoolean());

        JsonElement resized = Call(facade, "drafts.resize", Fields(""" "draft": "d_0001", "place": "pl_0003", "bars": {"first": 2, "last": 4} """));

        Assert.Equal((2, 4), Bars(resized.GetProperty("bars")));
        Assert.StartsWith("③ Recording 2–4", Place(facade, "pl_0003").GetProperty("label").GetString(), StringComparison.Ordinal);
    }
}