using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;
using static MotiffLab.MusicCore.Tests.Projects.UnknownValueTests;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// Ids two devices never clash on, through the facade (0026 S3 and C4, #317): with the iPad's random source every entity a
/// piece makes gets a random id the file keeps, and a project file with a numbered id opens re-keyed unless the app says
/// it is saved online.
/// </summary>
public partial class ProjectIdsFacadeTests
{
    private const string Now = """ "now": "2026-11-27T10:00:00Z" """;

    private static readonly byte[] s_elise = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));

    [GeneratedRegex("^(?<prefix>[a-z_]+_)[0-9a-f]{32}$")]
    private static partial Regex RandomId();

    private static MusicCoreFacade Random(string? file = null)
    {
        var facade = new MusicCoreFacade(IdSource.Random);
        JsonElement opened = file is null
            ? Json(facade.Open("FurElise.musicxml", s_elise, """{"language": "en", "id": "p_random"}"""))
            : Json(facade.Open("piece" + ProjectFile.Extension, Encoding.UTF8.GetBytes(file)));
        Assert.False(opened.TryGetProperty("error", out JsonElement error), error.ToString());
        return facade;
    }

    private static JsonObject Saved(MusicCoreFacade facade) => JsonNode.Parse(facade.Call(1, "project.save"))!.AsObject();

    private static string Id(JsonNode? entity) => entity!["id"]!.GetValue<string>();

    private static void AssertRandom(string prefix, string id) =>
        Assert.True(RandomId().Match(id) is { Success: true } match && match.Groups["prefix"].Value == prefix, $"{id} is not {prefix} and 32 random hex digits");

    [Fact]
    public void Everything_a_piece_makes_with_the_random_source_gets_a_random_id_the_file_keeps()
    {
        MusicCoreFacade facade = Random();
        Call(facade, "note.add", $$"""{"bars": {"first": 1, "last": 2}, "text": "Lean into the E", {{Now}}}""");
        Call(facade, "highlight.add", """{"bars": {"first": 1, "last": 1}, "notes": []}""");
        string category = Id(JsonNode.Parse(facade.Call(1, "category.add", """{"name": "Exam", "ink": "ochre"}"""))!["categories"]!.AsArray()[^1]);
        Call(facade, "bookmark.add", $$"""{"bars": {"first": 1, "last": 2}, "title": "A", "category": "{{category}}", {{Now}}}""");
        Call(facade, "resume.save", $$"""{"name": "Coda", {{Now}}}""");
        Call(facade, "select", """{"bars": {"first": 1, "last": 4}}""");
        Call(facade, "try", """{"chip": "v11.passing"}""");
        Call(facade, "idea.keep", $$"""{"name": "Half time", {{Now}}}""");
        Call(facade, "drafts.add", $$"""{"bars": {"first": 1, "last": 4}, {{Now}}}""");

        JsonObject saved = Saved(facade);
        JsonNode work = saved["workspace"]!;
        AssertRandom("n_", Id(work["marks"]!["notes"]![0]));
        AssertRandom("h_", Id(work["marks"]!["highlights"]![0]));
        AssertRandom("c_", category);
        AssertRandom("b_", Id(work["bookmarks"]!["items"]![0]));
        AssertRandom("r_", Id(work["resumePoints"]![0]));
        AssertRandom("o_", Id(work["observations"]![0]));
        string motif = Id(work["motifs"]![0]);
        AssertRandom("m_", motif);
        // The working motif's variation is named after it, as v_0001 after m_0001.
        Assert.Equal("v_" + motif[2..], Id(work["variations"]![0]));
        JsonNode draft = work["drafts"]![0]!;
        AssertRandom("d_", Id(draft));
        AssertRandom("pl_", Id(draft["places"]![0]));
        AssertRandom("v_", Id(draft["places"]![0]!["variants"]![0]));
        Assert.Equal(saved.ToJsonString(), Saved(Random(saved.ToJsonString())).ToJsonString());
    }

    [Fact]
    public void Notes_a_version_and_every_original_but_the_first_pdf_get_random_ids_the_answers_name()
    {
        MusicCoreFacade facade = Random();
        string pdf = Call(facade, "sources.original.add", """{"kind": "pdf", "fileName": "Henle.pdf", "kept": "link", "now": "2026-11-27T10:00:00Z"}""").GetProperty("id").GetString()!;
        string recording = Call(facade, "sources.original.add", """{"kind": "audio", "fileName": "Live.m4a", "kept": "device", "now": "2026-11-27T10:01:00Z"}""").GetProperty("id").GetString()!;
        string heard = Call(facade, "sources.notes.hear", RecordingPieceFacadeTests.Heard(recording)).GetProperty("id").GetString()!;
        var piece = PieceImporter.Import("FurElise.musicxml", s_elise).Piece!;
        string midi = Convert.ToBase64String(MidiExporter.Export(piece.Notes, [], piece.Meter, "performance"));
        string version = Id(JsonNode.Parse(facade.Call(1, "performance.add", $$$"""{"name": "Live", "midi": "{{{midi}}}"}"""))!["performance"]);

        // The first PDF is the piece's PDF, as on every device; a first recording added is kept and could clash.
        Assert.Equal(PieceOriginal.DocumentId, pdf);
        AssertRandom("src_rec_", recording);
        AssertRandom("n_", heard);
        AssertRandom("perf_", version);
        Project saved = ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));
        Assert.Equal(Project.FirstNotesId, saved.NotesId);
        Assert.Equal(heard, Assert.Single(saved.Sources.Notes).Id);
        Assert.Equal(version, Assert.Single(saved.Performances).Id);
        Assert.Contains(saved.Originals, original => original.Id == recording);
    }

    [Fact]
    public void A_bookmark_made_after_one_removed_never_takes_the_fields_a_later_build_kept_with_that_one()
    {
        // c-14a's risk: a later build's field is kept by the bookmark's id, and a numbered id is made again once the highest goes.
        MusicCoreFacade facade = Random(With(Worked(), "bookmarks/items/0/later", "kept").ToJsonString());
        Call(facade, "bookmark.remove", """{"id": "b_0001"}""");
        Call(facade, "bookmark.add", $$"""{"bars": {"first": 1, "last": 2}, "title": "New", {{Now}}}""");

        JsonNode item = Assert.Single(Saved(facade)["workspace"]!["bookmarks"]!["items"]!.AsArray())!;
        AssertRandom("b_", Id(item));
        Assert.Null(item["later"]);
    }

    /// <summary>Für Elise with bars 1–4 and bars 5–8 as two places of a draft, the second a tone higher: their ids.</summary>
    private static (MusicCoreFacade Facade, string Draft, string First, string Second) Collage()
    {
        MusicCoreFacade facade = Random();
        JsonElement first = Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 4}, "now": "2026-11-20T10:00:00Z"}""");
        string draft = first.GetProperty("draft").GetString()!;
        JsonElement second = Call(facade, "drafts.add", $$"""{"bars": {"first": 5, "last": 8}, "draft": "{{draft}}", "drop": "place", "now": "2026-11-20T10:05:00Z"}""");
        return (facade, draft, first.GetProperty("place").GetString()!, second.GetProperty("place").GetString()!);
    }

    private static JsonNode Draft(MusicCoreFacade facade) => Saved(facade)["workspace"]!["drafts"]![0]!;

    [Fact]
    public void A_bridge_put_with_the_random_source_answers_the_place_and_the_base_it_made()
    {
        (MusicCoreFacade facade, string draft, _, string second) = Collage();
        Call(facade, "drafts.transpose", $$"""{"draft": "{{draft}}", "place": "{{second}}", "semitones": 2, "now": "2026-11-20T10:10:00Z"}""");

        JsonElement put = Call(facade, "drafts.bridge", $$"""{"draft": "{{draft}}", "place": "{{second}}", "now": "2026-11-20T10:20:00Z"}""");

        JsonNode bridge = Draft(facade)["places"]![1]!;
        Assert.Equal((Id(bridge), bridge["base"]!.GetValue<string>()), (put.GetProperty("place").GetString(), put.GetProperty("variant").GetString()));
        Assert.Equal(bridge["variants"]!.AsArray().Select(Id), put.GetProperty("candidates").EnumerateArray().Select(static each => each.GetProperty("variant").GetString()));
    }

    [Fact]
    public void A_technique_carried_with_the_random_source_answers_the_variant_it_made()
    {
        (MusicCoreFacade facade, string draft, string first, _) = Collage();
        Call(facade, "drafts.add", $$"""{"bars": {"first": 5, "last": 8}, "draft": "{{draft}}", "drop": "variant", "place": "{{first}}", "now": "2026-11-20T10:06:00Z"}""");
        string third = Call(facade, "drafts.add", $$"""{"bars": {"first": 1, "last": 4}, "draft": "{{draft}}", "drop": "place", "now": "2026-11-20T10:07:00Z"}""").GetProperty("place").GetString()!;

        JsonElement carried = Call(facade, "drafts.carry", $$"""{"draft": "{{draft}}", "place": "{{first}}", "technique": 1, "to": "{{third}}", "now": "2026-11-20T10:15:00Z"}""");

        JsonNode place = Draft(facade)["places"]!.AsArray().Single(each => Id(each) == third)!;
        Assert.Equal(Id(place["variants"]!.AsArray()[^1]), carried.GetProperty("variant").GetString());
    }

    private static string Numbered(string id = "p_0003")
    {
        JsonObject file = JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", "v18-edits-meters.motiff.json")))!.AsObject();
        file["id"] = id;
        return file.ToJsonString();
    }

    private static JsonElement Opened(MusicCoreFacade facade, string file, string request)
    {
        JsonElement opened = Json(facade.Open("piece" + ProjectFile.Extension, Encoding.UTF8.GetBytes(file), request));
        Assert.False(opened.TryGetProperty("error", out JsonElement error), error.ToString());
        return opened.GetProperty("project");
    }

    [Fact]
    public void A_numbered_piece_not_saved_online_opens_rekeyed_and_is_saved_so()
    {
        var facade = new MusicCoreFacade();
        JsonElement project = Opened(facade, Numbered(), """{"onlineIds": ["p_0001", "p_9f2c4e1a0b7d4c3e8f6a5b4c3d2e1f00"]}""");

        // The id of the golden v18-rekeyed.motiff.json, which is this file re-keyed.
        const string id = "p_83a08b1319e2925df3c0cdd6aa0fff43";
        Assert.Equal(id, project.GetProperty("id").GetString());
        Assert.Equal(["p_0003"], project.GetProperty("formerIds").EnumerateArray().Select(static former => former.GetString()));
        JsonObject saved = Saved(facade);
        Assert.Equal((id, "p_0003"), (saved["id"]!.GetValue<string>(), saved["formerIds"]![0]!.GetValue<string>()));
        // Opened again, the piece keeps the id it was re-keyed to.
        Assert.Equal(id, Opened(new MusicCoreFacade(), saved.ToJsonString(), """{"onlineIds": []}""").GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("""{"onlineIds": ["p_0003"]}""")]
    [InlineData("""{"language": "en"}""")]
    public void A_numbered_piece_saved_online_or_opened_without_the_ids_online_keeps_its_id(string request)
    {
        JsonElement project = Opened(new MusicCoreFacade(), Numbered(), request);

        Assert.Equal("p_0003", project.GetProperty("id").GetString());
        Assert.False(project.TryGetProperty("formerIds", out _));
    }

    [Fact]
    public void A_later_versions_numbered_piece_opened_read_only_keeps_its_id()
    {
        JsonObject file = JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", "v19-future.motiff.json")))!.AsObject();
        file["id"] = "p_0003";

        JsonElement project = Opened(new MusicCoreFacade(), file.ToJsonString(), """{"onlineIds": []}""");

        Assert.Equal(("p_0003", true), (project.GetProperty("id").GetString(), project.GetProperty("readOnly").GetBoolean()));
    }
}