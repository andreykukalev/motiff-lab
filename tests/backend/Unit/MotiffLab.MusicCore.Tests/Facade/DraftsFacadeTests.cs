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
        Assert.Equal("unavailable", Error(facade, "sources.view.hide", """{"id": "d_0001", "hidden": true}"""));
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
        Assert.Equal("badRequest", Error(facade, "drafts.add", $$"""{{{Bars}}, "ms": [5, 1] }"""));
        Assert.Equal("v_0002", Call(facade, "drafts.add", $$"""{{{Bars}}, "draft": "d_0001", "drop": "shelf", "ms": [0, 4000] }""").GetProperty("variant").GetString());
        Assert.Equal((0L, 4000L), Saved(facade).FindDraft("d_0001")!.Shelf.Single().From.Milliseconds);
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
}