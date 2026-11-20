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

    private static string Heard()
    {
        ImportedPiece piece = PieceImporter.Import("fur-elise.musicxml", Fixture("fur-elise-original.musicxml")).Piece!;
        string midi = Convert.ToBase64String(global::MotiffLab.MusicCore.Export.MidiExporter.Export(piece.Notes, [], piece.Meter, "heard"));
        return $$$"""{"original": "src_rec", "engine": "transkun", "milliseconds": 60000, "sentFromMs": 30000, "parts": [{"index": 0, "midi": "{{{midi}}}"}], "now": "2026-11-20T11:00:00Z"}""";
    }

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
}