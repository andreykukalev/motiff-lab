using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The drafts through the facade (decision record 0034, version 1.1; spec «Экран (iPad)»): the «В черновик» sheet in any
/// view of notes, «Добавить», the Sources group «Черновики», the draft's tab in the switch, the draft view with its place
/// labels, renaming and removing.
/// </summary>
public class DraftsFacadeTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    private static byte[] Scanned() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"));

    /// <summary>A PDF read by the scan server, open as piece 1, in <paramref name="language"/>.</summary>
    private static MusicCoreFacade Opened(string language = "en")
    {
        byte[] musicXml = Scanned();
        Project read = Project.CreateDocument("p_5a1c4e1a0b7d4c3e8f6a5b4c3d2e1f00", "Nocturne.pdf", s_pdf, s_now)
            .Recognised(musicXml, "homr 0.7.0", PageMap.Of(PieceImporter.Import("a.musicxml", musicXml).Piece!, 1), s_now);
        var facade = new MusicCoreFacade();
        Json(facade.Open("Nocturne.motiff.json", ProjectFile.Write(read), $$"""{"language": "{{language}}"}"""));
        return facade;
    }

    private const string Bars = """ "bars": {"first": 1, "last": 4}, "now": "2026-11-20T10:00:00Z" """;

    private static JsonElement New(MusicCoreFacade facade, int piece = 1) => Json(facade.Call(piece, "drafts.add", $$"""{{{Bars}}}"""));

    private static Project Saved(MusicCoreFacade facade) => ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));

    private static JsonElement Item(JsonElement answer, string list, string id) =>
        answer.GetProperty(list).EnumerateArray().Single(each => each.GetProperty("id").GetString() == id);

    [Fact]
    public void The_sheet_offers_what_can_be_taken_and_a_new_draft_when_the_piece_has_none()
    {
        MusicCoreFacade facade = Opened();

        JsonElement sheet = Call(facade, "drafts.sheet", $$"""{{{Bars}}}""");

        Assert.Contains("all", sheet.GetProperty("parts").EnumerateArray().Select(part => part.GetString()));
        Assert.Equal("all", sheet.GetProperty("part").GetString());
        Assert.True(sheet.GetProperty("notes").GetInt32() > 0);
        Assert.Equal("Draft 1", sheet.GetProperty("newName").GetString());
        Assert.Empty(sheet.GetProperty("drafts").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, sheet.GetProperty("suggested").ValueKind);
        Assert.Equal("noSelection", Error(facade, "drafts.sheet"));
    }

    [Fact]
    public void Added_with_no_draft_the_fragment_starts_a_new_one_listed_in_sources_and_last_in_the_switch()
    {
        MusicCoreFacade facade = Opened();

        JsonElement added = Call(facade, "drafts.add", $$"""{{{Bars}}}""");

        Assert.Equal(("d_0001", "pl_0001", "v_0001"), (added.GetProperty("draft").GetString(), added.GetProperty("place").GetString(), added.GetProperty("variant").GetString()));
        JsonElement draft = Item(added, "drafts", "d_0001");
        Assert.Equal(("Draft 1", "from the PDF's notes", 1, 0), (draft.GetProperty("name").GetString(), draft.GetProperty("caption").GetString(), draft.GetProperty("places").GetInt32(), draft.GetProperty("shelf").GetInt32()));
        JsonElement view = added.GetProperty("views").EnumerateArray().Last();
        Assert.Equal(("d_0001", "draft", "draft"), (view.GetProperty("id").GetString(), view.GetProperty("kind").GetString(), view.GetProperty("op").GetString()));
        DraftVariant variant = Saved(facade).FindDraft("d_0001")!.Places[0].BaseVariant;
        Assert.Equal(("n_1", null, (1, 4)), (variant.From.Notes, variant.From.Project, variant.From.Bars));
    }

    [Fact]
    public void The_same_music_is_offered_first_as_a_variant_of_its_place_with_the_percentage()
    {
        MusicCoreFacade facade = Opened();
        New(facade);

        JsonElement sheet = Call(facade, "drafts.sheet", $$"""{{{Bars}}}""");

        JsonElement suggested = sheet.GetProperty("suggested");
        Assert.Equal(("d_0001", "pl_0001", 100), (suggested.GetProperty("draft").GetString(), suggested.GetProperty("place").GetString(), suggested.GetProperty("percent").GetInt32()));
        JsonElement place = Item(sheet.GetProperty("drafts").EnumerateArray().Single(), "places", "pl_0001");
        Assert.Equal(("① Notes 1–4", 100, true), (place.GetProperty("label").GetString(), place.GetProperty("percent").GetInt32(), place.GetProperty("sameMusic").GetBoolean()));
        Assert.Equal("Draft 2", sheet.GetProperty("newName").GetString());
    }

    [Fact]
    public void A_fragment_of_notes_open_only_to_be_shown_goes_into_a_draft_of_the_piece_they_were_opened_from()
    {
        MusicCoreFacade facade = Opened("ru");
        New(facade);
        Call(facade, "sources.notes.recognise", JsonSerializer.Serialize(new { original = "src_pdf", musicXml = Convert.ToBase64String(Scanned()), engine = "homr 0.8.0", pages = 1, now = "2026-11-20T10:30:00Z" }));
        int shown = Call(facade, "sources.notes.open", """{"id": "n_2"}""").GetProperty("piece").GetInt32();

        JsonElement added = Json(facade.Call(shown, "drafts.add", $$"""{{{Bars}}, "draft": "d_0001", "drop": "variant", "place": "pl_0001" }"""));

        Assert.Equal("v_0002", added.GetProperty("variant").GetString());
        DraftPlace place = Saved(facade).FindDraft("d_0001")!.Places.Single();
        Assert.Equal(["n_1", "n_2"], place.Variants.Select(variant => variant.From.Notes));
        Assert.Equal("homr 0.8.0", place.Variants[1].Provenance.Engine);
        Assert.Equal("① Ноты 1–4 · 2 варианта", DraftLabels.Place(place, 0, Words.Russian));
        Assert.Equal("readOnly", Json(facade.Call(shown, "project.save")).GetProperty("error").GetString());
    }

    [Fact]
    public void A_passage_is_selected_on_notes_and_on_a_draft_open_only_to_be_shown_and_the_piece_saves_nothing_of_it()
    {
        MusicCoreFacade facade = Opened();
        New(facade);
        Call(facade, "sources.notes.recognise", JsonSerializer.Serialize(new { original = "src_pdf", musicXml = Convert.ToBase64String(Scanned()), engine = "homr 0.8.0", pages = 1, now = "2026-11-20T10:30:00Z" }));
        string before = facade.Call(1, "project.save");
        int shown = Call(facade, "sources.notes.open", """{"id": "n_2"}""").GetProperty("piece").GetInt32();
        int view = Call(facade, "drafts.open", """{"draft": "d_0001"}""").GetProperty("piece").GetInt32();

        JsonElement onNotes = Json(facade.Call(shown, "select", """{"bars": {"first": 1, "last": 2}}"""));
        JsonElement onDraft = Json(facade.Call(view, "select", """{"bars": {"first": 1, "last": 4}}"""));

        Assert.Equal(1, onNotes.GetProperty("selection").GetProperty("firstBar").GetInt32());
        Assert.Equal(4, onDraft.GetProperty("selection").GetProperty("lastBar").GetInt32());
        Assert.Equal(before, facade.Call(1, "project.save"));
        Assert.Equal("noSelection", Error(facade, "drafts.sheet", "{}"));
        JsonElement added = Json(facade.Call(shown, "drafts.add", """{"draft": "d_0001", "drop": "variant", "place": "pl_0001", "now": "2026-11-20T10:40:00Z"}"""));
        Assert.Equal("v_0002", added.GetProperty("variant").GetString());
        Assert.Equal("n_2", Saved(facade).FindDraft("d_0001")!.Places.Single().Variants[1].From.Notes);
    }

    [Fact]
    public void A_fragment_of_another_piece_names_that_piece_and_its_label_says_so()
    {
        MusicCoreFacade facade = Opened();
        New(facade);
        int other = Json(facade.Open("Ode.musicxml", Fixture("ode-to-joy.musicxml"), """{"language": "en"}""")).GetProperty("piece").GetInt32();
        string otherId = ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(other, "project.save"))).Id;

        JsonElement added = Json(facade.Call(other, "drafts.add", $$"""{{{Bars}}, "into": 1, "draft": "d_0001", "drop": "place" }"""));

        Assert.Equal("pl_0002", added.GetProperty("place").GetString());
        Draft draft = Saved(facade).FindDraft("d_0001")!;
        Assert.Equal(otherId, draft.Places[1].BaseVariant.From.Project);
        Assert.Equal("② Another piece 1–4", DraftLabels.Place(draft.Places[1], 1, Words.English));
        Assert.Equal("from the PDF's notes and another piece", Item(added, "drafts", "d_0001").GetProperty("caption").GetString());
    }

    [Fact]
    public void A_draft_opens_as_one_score_only_to_be_shown_with_its_place_labels_and_plays()
    {
        MusicCoreFacade facade = Opened();
        New(facade);
        Call(facade, "drafts.add", $$"""{{{Bars}}, "draft": "d_0001", "drop": "place" }""");

        JsonElement opened = Call(facade, "drafts.open", """{"draft": "d_0001"}""");

        int view = opened.GetProperty("piece").GetInt32();
        Assert.True(opened.GetProperty("readOnly").GetBoolean());
        JsonElement[] places = [.. opened.GetProperty("draft").GetProperty("places").EnumerateArray()];
        Assert.Equal(["① Notes 1–4", "② Notes 1–4"], places.Select(place => place.GetProperty("label").GetString()));
        Assert.Equal((1, 4, 5, 8), (places[0].GetProperty("firstBar").GetInt32(), places[0].GetProperty("lastBar").GetInt32(), places[1].GetProperty("firstBar").GetInt32(), places[1].GetProperty("lastBar").GetInt32()));
        Assert.Equal(places[0].GetProperty("end").GetInt64(), places[1].GetProperty("onset").GetInt64());
        Assert.False(Json(facade.Call(view, "playback")).TryGetProperty("error", out _));
        Assert.False(Json(facade.Call(view, "notation")).TryGetProperty("error", out _));
        Assert.Equal("readOnly", Json(facade.Call(view, "drafts.add", $$"""{{{Bars}}}""")).GetProperty("error").GetString());
        Assert.Equal("badRequest", Error(facade, "drafts.open", """{"draft": "d_0009"}"""));
    }

    [Fact]
    public void A_draft_is_hidden_from_the_switch_and_shown_again_and_deleting_it_forgets_it_was_hidden()
    {
        MusicCoreFacade facade = Opened();
        New(facade);

        JsonElement hidden = Call(facade, "sources.view.hide", """{"id": "d_0001", "hidden": true}""");

        Assert.True(Item(hidden, "views", "d_0001").GetProperty("hidden").GetBoolean());
        Assert.Equal(1, hidden.GetProperty("hidden").GetInt32());
        Assert.Equal(["d_0001"], Saved(facade).HiddenViews);
        Assert.False(Item(Call(facade, "sources.view.hide", """{"id": "d_0001", "hidden": false}"""), "views", "d_0001").GetProperty("hidden").GetBoolean());
        Call(facade, "sources.view.hide", """{"id": "d_0001", "hidden": true}""");
        Assert.Equal(0, Call(facade, "drafts.remove", """{"draft": "d_0001"}""").GetProperty("hidden").GetInt32());
        Assert.Empty(Saved(facade).HiddenViews);
    }

    [Fact]
    public void A_draft_is_renamed_and_removed_and_only_it_goes()
    {
        MusicCoreFacade facade = Opened();
        New(facade);

        Assert.Equal("Coda", Item(Call(facade, "drafts.rename", """{"draft": "d_0001", "name": "Coda"}"""), "drafts", "d_0001").GetProperty("name").GetString());
        JsonElement removed = Call(facade, "drafts.remove", """{"draft": "d_0001"}""");

        Assert.Equal("d_0001", removed.GetProperty("removed").GetString());
        Assert.Empty(removed.GetProperty("drafts").EnumerateArray());
        Assert.Equal("n_1", removed.GetProperty("working").GetString());
        Assert.Equal("badRequest", Error(facade, "drafts.remove", """{"draft": "d_0001"}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.rename", """{"draft": "d_0001", "name": "x"}"""));
    }

    [Fact]
    public void Adding_asks_where_in_a_draft_and_what_can_be_taken()
    {
        MusicCoreFacade facade = Opened();
        New(facade);

        Assert.Equal("badRequest", Error(facade, "drafts.add", $$"""{{{Bars}}, "draft": "d_0001" }"""));
        Assert.Equal("badRequest", Error(facade, "drafts.add", $$"""{{{Bars}}, "draft": "d_0001", "drop": "variant", "place": "pl_0009" }"""));
        Assert.Equal("badRequest", Error(facade, "drafts.add", $$"""{{{Bars}}, "drop": "shelf" }"""));
        foreach (string part in new[] { "all", "melody" })
        {
            string nowhere = $$"""{"span": {"onset": 9600000, "end": 9601920}, "part": "{{part}}"}""";
            Assert.Equal(("unavailable", "unavailable"), (Error(facade, "drafts.sheet", nowhere), Error(facade, "drafts.add", nowhere)));
        }

        Assert.Equal("v_0002", Call(facade, "drafts.add", $$"""{{{Bars}}, "draft": "d_0001", "drop": "shelf", "ms": [0, 4000] }""").GetProperty("variant").GetString());
        Assert.Null(Saved(facade).FindDraft("d_0001")!.Shelf.Single().From.Milliseconds);
    }

    [Fact]
    public void The_same_calls_give_the_same_answers()
    {
        static string Run()
        {
            MusicCoreFacade facade = Opened();
            New(facade);
            return facade.Call(1, "drafts.sheet", $$"""{{{Bars}}}""") + facade.Call(1, "drafts.open", """{"draft": "d_0001"}""");
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Notes_heard_in_a_recording_keep_the_stretch_music_core_works_out_and_are_labelled_the_recording()
    {
        MusicCoreFacade facade = Opened();
        New(facade);
        Call(facade, "sources.original.add", """{"kind": "audio", "fileName": "Live.m4a", "kept": "device", "now": "2026-11-20T10:30:00Z"}""");
        Call(facade, "sources.notes.hear", Heard());
        int shown = Call(facade, "sources.notes.open", """{"id": "n_2"}""").GetProperty("piece").GetInt32();

        JsonElement added = Json(facade.Call(shown, "drafts.add", $$"""{{{Bars}}, "draft": "d_0001", "drop": "variant", "place": "pl_0001" }"""));

        DraftPlace place = Saved(facade).FindDraft("d_0001")!.Places.Single();
        Assert.Null(place.BaseVariant.From.Milliseconds);
        // Bars 1–4 as heard, from bar line to bar line on the heard notes' clock, moved by the 30 s where the stretch sent started.
        Assert.Equal((31_500L, 37_500L), place.Find("v_0002")!.From.Milliseconds);
        Assert.Equal("① Notes 1–4 + Recording · 2 variants", DraftLabels.Place(place, 0, Words.English));
        Assert.Equal("from the PDF's notes and the recording", Item(added, "drafts", "d_0001").GetProperty("caption").GetString());
        Assert.Equal("из нот с PDF и записи", DraftLabels.Caption(Saved(facade).FindDraft("d_0001")!, Words.Russian));
    }

    /// <summary>
    /// A recording added to piece 1 and heard as Notes n_2, opened to be shown, its bars 1–4 added to the draft
    /// <paramref name="into"/>'s place as a variant (that piece's first draft and place).
    /// </summary>
    private static void HeardVariant(MusicCoreFacade facade, int into = 1)
    {
        Call(facade, "sources.original.add", """{"kind": "audio", "fileName": "Live.m4a", "kept": "device", "now": "2026-11-20T10:30:00Z"}""");
        Call(facade, "sources.notes.hear", Heard());
        int shown = Call(facade, "sources.notes.open", """{"id": "n_2"}""").GetProperty("piece").GetInt32();
        JsonElement added = Json(facade.Call(shown, "drafts.add", $$"""{{{Bars}}, "into": {{into}}, "draft": "d_0001", "drop": "variant", "place": "pl_0001" }"""));
        Assert.False(added.TryGetProperty("error", out _), added.ToString());
    }

    /// <summary>The "recording" of the variant's first occurrence of a simplification in piece <paramref name="piece"/>'s first draft and place.</summary>
    private static JsonElement Recording(MusicCoreFacade facade, int piece = 1) =>
        Json(facade.Call(piece, "drafts.techniques", """{"draft": "d_0001", "place": "pl_0001"}""")).GetProperty("techniques").EnumerateArray()
            .Single(each => each.GetProperty("kind").GetString() == "simplify").GetProperty("occurrences")[0].GetProperty("recording");

    [Fact]
    public void An_occurrence_of_a_variant_heard_in_a_recording_names_the_recording_and_the_stretch_it_was_played_in()
    {
        MusicCoreFacade facade = Opened();
        New(facade);
        HeardVariant(facade);

        JsonElement techniques = Call(facade, "drafts.techniques", """{"draft": "d_0001", "place": "pl_0001"}""").GetProperty("techniques");

        JsonElement simplified = techniques.EnumerateArray().Single(each => each.GetProperty("kind").GetString() == "simplify").GetProperty("occurrences")[0];
        JsonElement inVariant = simplified.GetProperty("inVariant");
        Assert.Equal((4320, 5040), (inVariant.GetProperty("onset").GetInt32(), inVariant.GetProperty("end").GetInt32()));
        // That bar of the variant as heard, inside the variant's own stretch (31.5–37.5 s), on the heard notes' clock.
        JsonElement recording = simplified.GetProperty("recording");
        Assert.Equal(("p_5a1c4e1a0b7d4c3e8f6a5b4c3d2e1f00", "src_rec", 36_000L, 36_750L), (recording.GetProperty("project").GetString(), recording.GetProperty("original").GetString(), recording.GetProperty("from").GetInt64(), recording.GetProperty("to").GetInt64()));
        // Played as the variant has it: the variant's own music alone, opened only to be shown.
        JsonElement variant = Call(facade, "drafts.open", """{"draft": "d_0001", "variant": "v_0002"}""");
        Assert.Equal(("v_0002", true, false), (variant.GetProperty("variant").GetString(), variant.GetProperty("readOnly").GetBoolean(), variant.TryGetProperty("draft", out _)));
        Assert.Equal(Saved(facade).FindDraft("d_0001")!.Places[0].Find("v_0002")!.Music.Notes.Count, Json(facade.Call(variant.GetProperty("piece").GetInt32(), "playback")).GetProperty("notes").GetArrayLength());
        Assert.Equal("badRequest", Error(facade, "drafts.open", """{"draft": "d_0001", "variant": "v_0009"}"""));
    }

    [Fact]
    public void Once_the_heard_notes_are_removed_an_occurrence_has_no_recording()
    {
        MusicCoreFacade facade = Opened();
        New(facade);
        HeardVariant(facade);
        Assert.Equal(JsonValueKind.Object, Recording(facade).ValueKind);

        Call(facade, "sources.notes.remove", """{"id": "n_2"}""");

        Assert.Equal(JsonValueKind.Null, Recording(facade).ValueKind);
    }

    [Fact]
    public void A_variant_heard_in_another_piece_has_its_recording_only_while_that_piece_is_open()
    {
        MusicCoreFacade facade = Opened();
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));
        int other = Json(facade.Open("FurElise.musicxml", data, """{"language": "en", "id": "p_other"}""")).GetProperty("piece").GetInt32();
        Json(facade.Call(other, "drafts.add", $$"""{{{Bars}}}"""));
        HeardVariant(facade, into: other);
        Assert.Equal("p_5a1c4e1a0b7d4c3e8f6a5b4c3d2e1f00", Recording(facade, other).GetProperty("project").GetString());

        Assert.True(facade.Close(1));

        Assert.Equal(JsonValueKind.Null, Recording(facade, other).ValueKind);
    }

    [Fact]
    public void The_melody_is_taken_where_the_voices_are_cleanly_split_marked_inferred()
    {
        MusicCoreFacade facade = Opened();
        JsonElement sheet = Call(facade, "drafts.sheet", $$"""{{{Bars}}}""");
        Assert.Contains("melody", sheet.GetProperty("parts").EnumerateArray().Select(part => part.GetString()));

        Call(facade, "drafts.add", $$"""{{{Bars}}, "part": "melody" }""");

        DraftVariant variant = Saved(facade).FindDraft("d_0001")!.Places[0].BaseVariant;
        Assert.Equal(DraftPart.Melody, variant.From.Part);
        Assert.All(variant.Music.Notes, note => Assert.Equal(MusicCore.Events.Provenance.Inferred, note.Attribution.Provenance));
    }

    [Fact]
    public void Two_drafts_with_the_same_music_offer_the_earlier_first()
    {
        MusicCoreFacade facade = Opened();
        New(facade);
        New(facade);

        JsonElement sheet = Call(facade, "drafts.sheet", $$"""{{{Bars}}}""");

        Assert.Equal(["pl_0001", "pl_0001"], sheet.GetProperty("drafts").EnumerateArray().Select(draft => draft.GetProperty("suggested").GetString()));
        Assert.Equal("d_0001", sheet.GetProperty("suggested").GetProperty("draft").GetString());
    }

    [Fact]
    public void An_empty_draft_in_the_file_is_listed_as_empty_and_has_nothing_to_open()
    {
        byte[] musicXml = Scanned();
        Project read = Project.CreateDocument("p_5a1c4e1a0b7d4c3e8f6a5b4c3d2e1f00", "Nocturne.pdf", s_pdf, s_now)
            .Recognised(musicXml, "homr 0.7.0", PageMap.Of(PieceImporter.Import("a.musicxml", musicXml).Piece!, 1), s_now)
            .WithNewDraft(new Draft("d_0001", "Draft 1", s_now, [], [], [], 1));
        var facade = new MusicCoreFacade();
        Json(facade.Open("Nocturne.motiff.json", ProjectFile.Write(read), """{"language": "en"}"""));

        JsonElement layers = Call(facade, "sources.layers");

        Assert.Equal("empty so far", Item(layers, "drafts", "d_0001").GetProperty("caption").GetString());
        Assert.Equal("unavailable", Error(facade, "drafts.open", """{"draft": "d_0001"}"""));
        Assert.Equal("пока пусто", DraftLabels.Caption(read.Drafts[0], Words.Russian));
        Assert.Equal("v_0001", Call(facade, "drafts.add", $$"""{{{Bars}}, "draft": "d_0001", "drop": "place" }""").GetProperty("variant").GetString());
    }

    private const string Named = """ "draft": "d_0001" """;

    // A draft's map (the iPad's step 7): each place's variants with its base, the shelf, «Поставить…», a variant moved, and
    // another base chosen; the structure is the draft's state, not a step, undone while the piece is open.

    /// <summary>The example open in Russian with bars 1–4 and 5–8 as two places of a draft and the right hand of bars 1–4 on its shelf.</summary>
    private static MusicCoreFacade Shelved()
    {
        var facade = new MusicCoreFacade();
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));
        Json(facade.Open("FurElise.musicxml", data, """{"language": "ru", "id": "p_map"}"""));
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 4}, "now": "2026-11-20T10:00:00Z"}""");
        Call(facade, "drafts.add", """{"bars": {"first": 5, "last": 8}, "draft": "d_0001", "drop": "place", "now": "2026-11-20T10:05:00Z"}""");
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 4}, "part": "right", "draft": "d_0001", "drop": "shelf", "now": "2026-11-20T10:10:00Z"}""");
        return facade;
    }

    private static JsonElement Map(MusicCoreFacade facade) => Call(facade, "drafts.open", $$"""{{{Named}}}""").GetProperty("draft");

    private static Draft SavedDraft(MusicCoreFacade facade) => Saved(facade).Drafts[0];

    private static string PlaceRequest(string variant, string drop, string? place = null) =>
        $$"""{{{Named}}, "variant": "{{variant}}", "drop": "{{drop}}"{{(place is null ? "" : $", \"place\": \"{place}\"")}}, "now": "2026-11-20T10:20:00Z"}""";

    [Fact]
    public void The_map_lists_each_place_with_its_variants_and_base_and_the_shelf_in_the_language_of_the_piece()
    {
        JsonElement map = Map(Shelved());

        JsonElement first = map.GetProperty("places")[0];
        JsonElement variant = Assert.Single(first.GetProperty("variants").EnumerateArray());
        Assert.Equal(("v_0001", "Ноты 1–4", true, 0), (variant.GetProperty("id").GetString(), variant.GetProperty("label").GetString(), variant.GetProperty("base").GetBoolean(), variant.GetProperty("steps").GetInt32()));
        Assert.Equal(0, first.GetProperty("steps").GetInt32());
        JsonElement shelved = Assert.Single(map.GetProperty("shelf").EnumerateArray());
        Assert.Equal(("v_0003", "Ноты 1–4 · правая рука"), (shelved.GetProperty("id").GetString(), shelved.GetProperty("label").GetString()));
        Assert.False(shelved.TryGetProperty("base", out _));
    }

    [Fact]
    public void A_variant_of_the_shelf_is_put_into_a_place_or_as_a_new_place_and_saved_as_the_drafts_state_not_a_step()
    {
        MusicCoreFacade into = Shelved();
        MusicCoreFacade asNew = Shelved();

        JsonElement put = Call(into, "drafts.place", PlaceRequest("v_0003", "variant", "pl_0001"));
        JsonElement made = Call(asNew, "drafts.place", PlaceRequest("v_0003", "place"));

        Assert.Equal(("pl_0001", "v_0003"), (put.GetProperty("place").GetString(), put.GetProperty("variant").GetString()));
        Assert.Empty(put.GetProperty("dropped").EnumerateArray());
        Assert.Equal(0, put.GetProperty("drafts")[0].GetProperty("shelf").GetInt32());
        Assert.Equal(["v_0001", "v_0003"], SavedDraft(into).Places[0].Variants.Select(variant => variant.Id));
        Assert.Empty(SavedDraft(into).Steps);
        Assert.Equal(1, SavedDraft(into).Next);
        Assert.Equal("pl_0003", made.GetProperty("place").GetString());
        Assert.Equal(["pl_0001", "pl_0002", "pl_0003"], SavedDraft(asNew).Places.Select(place => place.Id));
    }

    [Fact]
    public void A_variant_moved_out_of_its_place_takes_the_steps_that_name_it_and_says_which()
    {
        MusicCoreFacade facade = Shelved();
        Call(facade, "drafts.place", PlaceRequest("v_0003", "variant", "pl_0001"));
        Call(facade, "drafts.takeFrom", $$"""{{{Named}}, "place": "pl_0001", "variant": "v_0003", "now": "2026-11-20T10:25:00Z"}""");
        Call(facade, "drafts.transpose", $$"""{{{Named}}, "place": "pl_0001", "semitones": 2, "now": "2026-11-20T10:30:00Z"}""");
        Assert.Equal(1, Map(facade).GetProperty("places")[0].GetProperty("variants")[1].GetProperty("steps").GetInt32());

        JsonElement moved = Call(facade, "drafts.place", PlaceRequest("v_0003", "shelf"));

        Assert.Equal(JsonValueKind.Null, moved.GetProperty("place").ValueKind);
        Assert.Equal([1], moved.GetProperty("dropped").EnumerateArray().Select(step => step.GetInt32()));
        Assert.Equal([2], SavedDraft(facade).Steps.Select(step => step.Number));
        Assert.Equal("v_0003", Assert.Single(SavedDraft(facade).Shelf).Id);
    }

    [Fact]
    public void Another_base_drops_the_places_steps_and_says_which_and_the_map_marks_it()
    {
        MusicCoreFacade facade = Shelved();
        Call(facade, "drafts.place", PlaceRequest("v_0003", "variant", "pl_0001"));
        Call(facade, "drafts.transpose", $$"""{{{Named}}, "place": "pl_0001", "semitones": 2, "now": "2026-11-20T10:25:00Z"}""");
        Call(facade, "drafts.transpose", $$"""{{{Named}}, "place": "pl_0002", "semitones": 2, "now": "2026-11-20T10:26:00Z"}""");
        Assert.Equal(1, Map(facade).GetProperty("places")[0].GetProperty("steps").GetInt32());

        JsonElement rebased = Call(facade, "drafts.base", $$"""{{{Named}}, "place": "pl_0001", "variant": "v_0003", "now": "2026-11-20T10:30:00Z"}""");

        Assert.Equal([1], rebased.GetProperty("dropped").EnumerateArray().Select(step => step.GetInt32()));
        Assert.Equal("v_0003", SavedDraft(facade).Places[0].Base);
        Assert.Equal([2], SavedDraft(facade).Steps.Select(step => step.Number));
        Assert.Equal([false, true], Map(facade).GetProperty("places")[0].GetProperty("variants").EnumerateArray().Select(variant => variant.GetProperty("base").GetBoolean()));
        JsonElement again = Call(facade, "drafts.base", $$"""{{{Named}}, "place": "pl_0001", "variant": "v_0003", "now": "2026-11-20T10:35:00Z"}""");
        Assert.Empty(again.GetProperty("dropped").EnumerateArray());
    }

    [Fact]
    public void A_base_is_not_moved_a_variant_is_not_put_where_it_is_and_unknown_ones_are_refused()
    {
        MusicCoreFacade facade = Shelved();

        Assert.Equal("badRequest", Error(facade, "drafts.place", PlaceRequest("v_0001", "shelf")));
        Assert.Equal("badRequest", Error(facade, "drafts.place", PlaceRequest("v_0003", "shelf")));
        Assert.Equal("badRequest", Error(facade, "drafts.place", PlaceRequest("v_0009", "place")));
        Assert.Equal("badRequest", Error(facade, "drafts.place", PlaceRequest("v_0003", "variant", "pl_0009")));
        Assert.Equal("badRequest", Error(facade, "drafts.place", $$"""{{{Named}}, "variant": "v_0003"}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.base", $$"""{{{Named}}, "place": "pl_0001", "variant": "v_0003"}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.base", $$"""{{{Named}}, "place": "pl_0009", "variant": "v_0001"}"""));
        Assert.Equal("v_0003", Assert.Single(SavedDraft(facade).Shelf).Id);
    }

    [Fact]
    public void Undo_takes_back_a_change_of_structure_with_the_steps_it_dropped_and_the_steps_in_their_order()
    {
        MusicCoreFacade facade = Shelved();
        Call(facade, "drafts.place", PlaceRequest("v_0003", "variant", "pl_0001"));
        Call(facade, "drafts.transpose", $$"""{{{Named}}, "place": "pl_0001", "semitones": 2, "now": "2026-11-20T10:25:00Z"}""");
        Call(facade, "drafts.base", $$"""{{{Named}}, "place": "pl_0001", "variant": "v_0003", "now": "2026-11-20T10:30:00Z"}""");

        JsonElement rebased = Call(facade, "drafts.undo", $$"""{{{Named}}}""");
        Assert.True(rebased.GetProperty("structure").GetBoolean());
        Assert.Equal(("v_0001", 1, 2), (SavedDraft(facade).Places[0].Base, Assert.Single(SavedDraft(facade).Steps).Number, SavedDraft(facade).Next));
        JsonElement step = Call(facade, "drafts.undo", $$"""{{{Named}}}""");
        Assert.False(step.GetProperty("structure").GetBoolean());
        Assert.Equal([1], step.GetProperty("undone").EnumerateArray().Select(each => each.GetInt32()));
        Assert.True(Call(facade, "drafts.undo", $$"""{{{Named}}}""").GetProperty("structure").GetBoolean());
        Assert.Equal(("v_0003", 2), (Assert.Single(SavedDraft(facade).Shelf).Id, SavedDraft(facade).Next));
    }
}