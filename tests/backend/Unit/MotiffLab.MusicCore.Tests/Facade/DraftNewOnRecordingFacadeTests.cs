using System.Text.Json;

using MotiffLab.MusicCore.Facade;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// Version 1.5, d-11 (DRAFT-11, the lead's option «а»; spec «Ноты + запись»: the card «Новый черновик» with «место на
/// записи, % совпадения, число приёмов» and «С ассистентом · Pro»): the selection searched on the piece's recordings
/// before any draft is made, by the engine of «Найти на записи», and a new draft made with the stretch found as a variant
/// of its first place.
/// </summary>
public class DraftNewOnRecordingFacadeTests
{
    private const string Bars = """ "bars": {"first": 1, "last": 4} """;
    private const string Ipad = """ "device": "ipad", "here": [], "now": "2026-11-26T10:00:00Z" """;

    /// <summary>The example open as piece 1 in <paramref name="language"/>, with its recording heard as Notes n_2 unless <paramref name="heard"/> is false, and no draft.</summary>
    private static MusicCoreFacade Opened(bool heard = true, string language = "en")
    {
        var facade = new MusicCoreFacade();
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));
        Json(facade.Open("FurElise.musicxml", data, $$"""{"language":"{{language}}","id":"p_new"}"""));
        if (heard)
        {
            Call(facade, "sources.original.add", """{"kind": "audio", "fileName": "Live.m4a", "kept": "device", "now": "2026-11-20T10:30:00Z"}""");
            Call(facade, "sources.notes.hear", Heard());
        }

        return facade;
    }

    private static JsonElement[] Found(MusicCoreFacade facade, int piece = 1, string fields = Bars) =>
        [.. Json(facade.Call(piece, "drafts.findNewOnRecording", $"{{{fields}}}")).GetProperty("found").EnumerateArray()];

    private static int Searched(MusicCoreFacade facade, int piece = 1) =>
        Json(facade.Call(piece, "drafts.findNewOnRecording", $"{{{Bars}}}")).GetProperty("recordings").GetInt32();

    private static int Drafts(MusicCoreFacade facade) => Call(facade, "sources.layers", "{}").GetProperty("drafts").GetArrayLength();

    [Fact]
    public void The_selection_is_found_on_the_recording_before_a_draft_is_made_and_nothing_changes()
    {
        MusicCoreFacade facade = Opened();
        string sheet = facade.Call(1, "drafts.sheet", $"{{{Bars}}}");

        JsonElement found = Assert.Single(Found(facade));

        Assert.Equal("n_2", found.GetProperty("notes").GetString());
        Assert.True(found.GetProperty("percent").GetInt32() >= 50);
        Assert.StartsWith("Recording", found.GetProperty("label").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Object, found.GetProperty("ms").ValueKind);
        Assert.Equal(JsonValueKind.Null, found.GetProperty("variant").ValueKind);
        Assert.True(found.GetProperty("techniques").GetInt32() >= 0);
        Assert.Equal(1, Searched(facade));
        // Read only, and the same each time: no draft, the sheet as it was.
        Assert.Equal(0, Drafts(facade));
        Assert.Equal(sheet, facade.Call(1, "drafts.sheet", $"{{{Bars}}}"));
        Assert.Equal(found.GetRawText(), Assert.Single(Found(facade)).GetRawText());
    }

    [Fact]
    public void The_same_stretch_a_place_s_card_would_find_is_what_the_new_card_finds()
    {
        MusicCoreFacade facade = Opened();
        JsonElement before = Assert.Single(Found(facade));
        Call(facade, "drafts.add", Fields(Bars));

        JsonElement place = Assert.Single(Call(facade, "drafts.findOnRecording", """{"draft": "d_0001", "place": "pl_0001"}""").GetProperty("found").EnumerateArray());

        foreach (string field in new[] { "notes", "label", "percent", "span", "bars", "ms" })
        {
            Assert.Equal(place.GetProperty(field).GetRawText(), before.GetProperty(field).GetRawText());
        }
    }

    [Fact]
    public void A_new_draft_with_the_recording_has_the_stretch_as_a_variant_of_its_first_place_in_one_change()
    {
        MusicCoreFacade facade = Opened();
        JsonElement found = Assert.Single(Found(facade));

        JsonElement added = Call(facade, "drafts.add", Fields($$"""{{Bars}}, "recording": "n_2" """));

        Assert.Equal(("d_0001", "pl_0001", "v_0001", "v_0002"), (added.GetProperty("draft").GetString(), added.GetProperty("place").GetString(), added.GetProperty("variant").GetString(), added.GetProperty("recording").GetString()));
        Assert.Equal(1, Drafts(facade));
        JsonElement view = Call(facade, "drafts.open", """{"draft": "d_0001"}""").GetProperty("draft");
        JsonElement place = Assert.Single(view.GetProperty("places").EnumerateArray());
        Assert.Equal(["v_0001", "v_0002"], place.GetProperty("variants").EnumerateArray().Select(static variant => variant.GetProperty("id").GetString()));
        Assert.True(place.GetProperty("variants")[0].GetProperty("base").GetBoolean());
        Assert.Equal(found.GetProperty("label").GetString(), place.GetProperty("variants")[1].GetProperty("label").GetString());
        // The приёмы the card counted are the place's.
        Assert.Equal(found.GetProperty("techniques").GetInt32(), Call(facade, "drafts.techniques", """{"draft": "d_0001", "place": "pl_0001"}""").GetProperty("total").GetInt32());
        // Found again on the place, it is that variant; and a draft made in one change has nothing to undo.
        Assert.Equal("v_0002", Call(facade, "drafts.findOnRecording", """{"draft": "d_0001", "place": "pl_0001"}""").GetProperty("found")[0].GetProperty("variant").GetString());
        Assert.Equal("unavailable", Error(facade, "drafts.undo", Fields(""" "draft": "d_0001" """)));
    }

    [Fact]
    public void The_card_counts_the_techniques_the_place_would_have_with_the_stretch()
    {
        // Bars 13–16 of the notes are played otherwise on the recording: three приёмы between them.
        const string Otherwise = """ "bars": {"first": 13, "last": 16} """;
        MusicCoreFacade facade = Opened();
        JsonElement found = Assert.Single(Found(facade, fields: Otherwise));

        Assert.Equal(3, found.GetProperty("techniques").GetInt32());
        Call(facade, "drafts.add", Fields($$"""{{Otherwise}}, "recording": "n_2" """));
        Assert.Equal(3, Call(facade, "drafts.techniques", """{"draft": "d_0001", "place": "pl_0001"}""").GetProperty("total").GetInt32());
    }

    [Fact]
    public void Without_the_recording_drafts_add_makes_the_draft_as_before()
    {
        MusicCoreFacade facade = Opened();

        JsonElement added = Call(facade, "drafts.add", Fields(Bars));

        Assert.False(added.TryGetProperty("recording", out _));
        JsonElement place = Assert.Single(Call(facade, "drafts.open", """{"draft": "d_0001"}""").GetProperty("draft").GetProperty("places").EnumerateArray());
        Assert.Equal(1, place.GetProperty("variants").GetArrayLength());
    }

    [Fact]
    public void A_piece_with_no_recording_finds_nothing_and_makes_no_draft_with_one()
    {
        MusicCoreFacade facade = Opened(heard: false, language: "ru");

        Assert.Empty(Found(facade));
        Assert.Equal(0, Searched(facade));
        JsonElement refused = Json(facade.Call(1, "drafts.add", Fields($$"""{{Bars}}, "recording": "n_2" """)));

        Assert.Equal("unavailable", refused.GetProperty("error").GetString());
        Assert.Equal("На записи нет выделенной музыки.", refused.GetProperty("message").GetString());
        Assert.Equal(0, Drafts(facade));
    }

    [Fact]
    public void A_passage_selected_in_the_recording_s_own_notes_is_not_found_in_them()
    {
        MusicCoreFacade facade = Opened();
        int shown = Call(facade, "sources.notes.open", """{"id": "n_2"}""").GetProperty("piece").GetInt32();

        Assert.Empty(Found(facade, shown));
        // Its own recording is not searched for it: none is, and the card has nothing to say (not «no recording has it»).
        Assert.Equal(0, Searched(facade, shown));
        Assert.Equal("unavailable", Json(facade.Call(shown, "drafts.add", Fields($$"""{{Bars}}, "recording": "n_2" """))).GetProperty("error").GetString());
        Assert.Equal(0, Drafts(facade));
    }

    [Fact]
    public void The_recording_is_only_for_a_new_draft_and_the_search_only_before_one()
    {
        MusicCoreFacade facade = Opened();
        Call(facade, "drafts.add", Fields(Bars));

        Assert.Equal("badRequest", Error(facade, "drafts.add", Fields($$"""{{Bars}}, "draft": "d_0001", "drop": "place", "recording": "n_2" """)));
        Assert.Equal("badRequest", Error(facade, "drafts.findNewOnRecording", $$"""{{{Bars}}, "draft": "d_0001"}"""));
        Assert.Equal(1, Call(facade, "drafts.open", """{"draft": "d_0001"}""").GetProperty("draft").GetProperty("places").GetArrayLength());
    }

    [Fact]
    public void With_nothing_selected_the_search_says_so()
    {
        MusicCoreFacade facade = Opened();

        Assert.Equal("noSelection", Error(facade, "drafts.findNewOnRecording", "{}"));
    }

    private static string Fields(string more) => $"{{{Ipad}, {more}}}";
}