using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The collage through the facade (version 1.2, the plan's answer 5; decision record 0034, section 3): in piece B, open,
/// a fragment goes into a draft of piece A, whose project file the app sends and saves again; B and its undo stay as they
/// were, and A's draft keeps a copy that names B.
/// </summary>
public class DraftCollageFacadeTests
{
    private const string PieceB = "p_5a1c4e1a0b7d4c3e8f6a5b4c3d2e1f00";

    private const string Bars = """ "bars": {"first": 1, "last": 4}, "now": "2026-11-20T10:00:00Z" """;

    /// <summary>Piece B, a PDF read by the scan server, open as piece 1 to work on, with a draft of its own and a change of its structure that drafts.undo takes back.</summary>
    private static MusicCoreFacade OpenedB()
    {
        MusicCoreFacade facade = DraftsFacadeTests.Opened();
        Call(facade, "drafts.add", $$"""{{{Bars}}}""");
        Call(facade, "drafts.add", """{"bars": {"first": 5, "last": 8}, "draft": "d_0001", "drop": "shelf", "now": "2026-11-20T10:05:00Z"}""");
        Call(facade, "drafts.place", """{"draft": "d_0001", "variant": "v_0002", "drop": "place", "now": "2026-11-20T10:06:00Z"}""");
        return facade;
    }

    /// <summary>Piece A's project file: Ode to Joy with one draft of one place, saved in a facade of its own.</summary>
    private static string FileOfA()
    {
        var facade = new MusicCoreFacade();
        Json(facade.Open("Ode.musicxml", Fixture("ode-to-joy.musicxml"), """{"language": "en", "id": "p_a", "now": "2026-11-20T08:00:00Z"}"""));
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 2}, "name": "Ode", "now": "2026-11-20T08:30:00Z"}""");
        return facade.Call(1, "project.save", """{"now": "2026-11-20T08:45:00Z"}""");
    }

    private static string Into(string file, string more) =>
        $$"""{{{Bars}}, "file": {{JsonSerializer.Serialize(file)}}, {{more}} }""";

    private static string SavedB(MusicCoreFacade facade) => facade.Call(1, "project.save", """{"now": "2026-11-20T11:00:00Z"}""");

    private static Project FileIn(JsonElement answer) => ProjectFile.Read(Encoding.UTF8.GetBytes(answer.GetProperty("file").GetString()!));

    [Fact]
    public void The_fragment_lands_at_the_end_of_the_other_pieces_draft_naming_this_piece_in_its_provenance()
    {
        MusicCoreFacade facade = OpenedB();
        string fileOfA = FileOfA();

        JsonElement added = Call(facade, "drafts.add", Into(fileOfA, """ "draft": "d_0001", "drop": "place" """));

        Assert.Equal(("d_0001", "pl_0002", "v_0002"), (added.GetProperty("draft").GetString(), added.GetProperty("place").GetString(), added.GetProperty("variant").GetString()));
        Assert.Equal("p_a", added.GetProperty("project").GetProperty("id").GetString());
        Draft draft = FileIn(added).FindDraft("d_0001")!;
        DraftVariant variant = draft.Places[1].BaseVariant;
        Assert.Equal((PieceB, "n_1", (1, 4), DraftPart.All), (variant.From.Project, variant.From.Notes, variant.From.Bars, variant.From.Part));
        ProvenanceSource source = Assert.Single(variant.Provenance.From);
        Assert.Equal(("src_pdf", PieceB), (source.Original, source.Project));
        Assert.NotNull(source.Sha256);
        Assert.Equal(ProjectFile.Read(Encoding.UTF8.GetBytes(SavedB(facade))).WorkingProvenance.From[0].Sha256, source.Sha256);
        Assert.Equal("② Another piece 1–4", DraftLabels.Place(draft.Places[1], 1, Words.English));
    }

    [Fact]
    public void This_piece_and_its_undo_stay_as_they_were()
    {
        MusicCoreFacade untouched = OpenedB();
        MusicCoreFacade collaged = OpenedB();

        Call(collaged, "drafts.add", Into(FileOfA(), """ "draft": "d_0001", "drop": "shelf" """));

        Assert.Equal(SavedB(untouched), SavedB(collaged));
        Assert.Equal(untouched.Call(1, "drafts.undo", """{"draft": "d_0001"}"""), collaged.Call(1, "drafts.undo", """{"draft": "d_0001"}"""));
        Assert.Equal(SavedB(untouched), SavedB(collaged));
    }

    [Fact]
    public void On_the_shelf_the_copy_stays_whole_when_this_piece_is_gone()
    {
        MusicCoreFacade facade = OpenedB();
        JsonElement added = Call(facade, "drafts.add", Into(FileOfA(), """ "draft": "d_0001", "drop": "shelf", "part": "right" """));
        Assert.Equal(JsonValueKind.Null, added.GetProperty("place").ValueKind);

        var elsewhere = new MusicCoreFacade();
        JsonElement opened = Json(elsewhere.Open("Ode.motiff.json", Encoding.UTF8.GetBytes(added.GetProperty("file").GetString()!), """{"language": "en"}"""));
        int a = opened.GetProperty("piece").GetInt32();
        JsonElement map = Json(elsewhere.Call(a, "drafts.open", """{"draft": "d_0001"}""")).GetProperty("draft");

        JsonElement shelved = Assert.Single(map.GetProperty("shelf").EnumerateArray());
        Assert.Equal("v_0002", shelved.GetProperty("id").GetString());
        DraftVariant copy = FileIn(added).FindDraft("d_0001")!.Shelf.Single();
        Assert.NotEmpty(copy.Music.Notes);
        Assert.All(copy.Music.Notes, note => Assert.Equal(Hand.Right, note.Hand?.Value));
    }

    [Fact]
    public void The_other_pieces_drafts_are_listed_from_its_file_which_stays_at_version_16_until_one_is_written()
    {
        string sixteen = FileOfA().Replace($"\"version\": {ProjectFile.Version}", "\"version\": 16", StringComparison.Ordinal);
        Assert.Equal(16, Json(sixteen).GetProperty("version").GetInt32());
        MusicCoreFacade facade = OpenedB();

        JsonElement listed = Json(facade.Call(0, "project.drafts", $$"""{"file": {{JsonSerializer.Serialize(sixteen)}}, "language": "en"}"""));

        Assert.Equal("p_a", listed.GetProperty("project").GetProperty("id").GetString());
        JsonElement draft = Assert.Single(listed.GetProperty("drafts").EnumerateArray());
        Assert.Equal(("d_0001", "Ode", 1, 0), (draft.GetProperty("id").GetString(), draft.GetProperty("name").GetString(), draft.GetProperty("places").GetInt32(), draft.GetProperty("shelf").GetInt32()));
        Assert.False(listed.TryGetProperty("file", out _));

        JsonElement added = Call(facade, "drafts.add", Into(sixteen, """ "draft": "d_0001", "drop": "place" """));
        Assert.Equal(ProjectFile.Version, Json(added.GetProperty("file").GetString()!).GetProperty("version").GetInt32());
        Assert.Equal(2, FileIn(added).FindDraft("d_0001")!.Places.Count);
    }

    [Fact]
    public void A_missing_draft_a_damaged_file_and_this_pieces_own_file_are_refused()
    {
        MusicCoreFacade facade = OpenedB();
        string fileOfA = FileOfA();
        string before = SavedB(facade);

        Assert.Equal("badRequest", Error(facade, "drafts.add", Into(fileOfA, """ "draft": "d_0009", "drop": "place" """)));
        Assert.Equal("badRequest", Error(facade, "drafts.add", Into(fileOfA, """ "drop": "place" """)));
        Assert.Equal("badRequest", Error(facade, "drafts.add", Into(fileOfA, """ "draft": "d_0001", "drop": "variant", "place": "pl_0009" """)));
        Assert.Equal("badRequest", Error(facade, "drafts.add", Into("{\"format\": \"motifflab-project\", \"version\": 17", """ "draft": "d_0001", "drop": "place" """)));
        Assert.Equal("badRequest", Error(facade, "drafts.add", Into(before, """ "draft": "d_0001", "drop": "place" """)));
        Assert.Equal("badRequest", Json(facade.Call(0, "project.drafts", """{"file": "not a project"}""")).GetProperty("error").GetString());
        Assert.Equal(before, SavedB(facade));
    }

    [Fact]
    public void A_file_and_into_together_are_refused_and_nothing_changes()
    {
        MusicCoreFacade facade = OpenedB();
        string before = SavedB(facade);

        Assert.Equal("badRequest", Error(facade, "drafts.add", Into(FileOfA(), """ "draft": "d_0001", "drop": "place", "into": 1 """)));
        Assert.Equal(before, SavedB(facade));
    }

    [Fact]
    public void The_same_calls_give_the_same_file()
    {
        string fileOfA = FileOfA();
        string request = Into(fileOfA, """ "draft": "d_0001", "drop": "place" """);

        Assert.Equal(OpenedB().Call(1, "drafts.add", request), OpenedB().Call(1, "drafts.add", request));
    }
}