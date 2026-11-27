using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// One piece, one entry in the library across the web and the iPad (version 0.6.1, #582 item 1). The cause of the duplicates:
/// a piece is matched between devices by its project id only, and the id is new on each device that adds the file, so the
/// same PDF added on the web and on the iPad synced into two pieces on both; a piece opened on both between two passes also
/// came back with a «previous copy» beside it. The rule: the same files by fingerprint are one piece, and a copy that carries
/// nothing of the player's own joins the other.
/// </summary>
public class SamePieceTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\nNocturne\n");

    private const string CopyTitle = "{0} (previous copy, 15 Nov 10:00)";

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name));

    /// <summary>The same PDF added on the web, and on the iPad a minute later: two new ids.</summary>
    private static Project OnWeb() => Project.CreateDocument("p_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Nocturne.pdf", s_pdf, s_now);

    private static Project OnIpad() => Project.CreateDocument("p_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "Nocturne.pdf", s_pdf, s_now.AddMinutes(1));

    /// <summary>A copy as the other device fetches it: without the PDF, as it went online.</summary>
    private static Project Fetched(Project project) => ProjectFile.Read(ProjectFile.Write(project.ForOnline()!));

    private static Project Bookmarked(Project project) => project with { DocumentMarks = DocumentMarks.Empty.AddBookmark(1, "Reprise", s_now, IdSource.Numbered) };

    private static SyncStep Next(bool here, bool online, bool known) => ProjectSync.Next(new SyncFacts(here, online, known, ChangedHere: !known, ChangedOnline: false));

    [Fact]
    public void The_cause_the_same_pdf_added_on_both_devices_syncs_into_two_pieces_matched_by_id_only()
    {
        Project web = OnWeb();
        Project ipad = OnIpad();

        // Each device sends its own and fetches the other's as a new piece: ids never meet, so both libraries list two.
        Assert.NotEqual(web.Id, ipad.Id);
        Assert.Equal(SyncStep.Upload, Next(here: true, online: false, known: false));
        Assert.Equal(SyncStep.Download, Next(here: false, online: true, known: false));
        // Yet they are the same file: what the piece is known by on every device is the same.
        Assert.Equal(SamePiece.Keys(web), SamePiece.Keys(Fetched(ipad)));
        Assert.Equal([PieceOriginal.Fingerprint(s_pdf)], SamePiece.Keys(web));
    }

    [Fact]
    public void Two_untouched_copies_become_the_one_made_first_on_every_device_alike()
    {
        // On the web: its own copy with the PDF and the iPad's fetched; on the iPad the other way round.
        IReadOnlyList<JoinedPiece> web = SamePiece.Plan([OnWeb(), Fetched(OnIpad())], device: HoldingDevice.Web);
        IReadOnlyList<JoinedPiece> ipad = SamePiece.Plan([OnIpad(), Fetched(OnWeb())], device: HoldingDevice.Ipad);

        JoinedPiece onWeb = Assert.Single(web);
        Assert.Equal((0, "1"), (onWeb.Keeper, string.Join(",", onWeb.Joined)));
        Assert.Equal(OnWeb().Id, onWeb.Project.Id);
        JoinedPiece onIpad = Assert.Single(ipad);
        Assert.Equal((1, "0"), (onIpad.Keeper, string.Join(",", onIpad.Joined)));
        // The iPad keeps the web's piece with its own PDF: the bytes move from the copy that goes.
        Assert.Equal(OnWeb().Id, onIpad.Project.Id);
        Assert.Equal(s_pdf, onIpad.Project.Document);
    }

    [Fact]
    public void The_copy_with_the_players_work_is_kept_and_the_untouched_one_brings_its_pdf_and_details()
    {
        Project ipad = Bookmarked(OnIpad()) with { Metadata = new PieceMetadata("Nocturne", null, ["Шопен"], []) };
        Project web = OnWeb() with { Metadata = new PieceMetadata(null, "Chopin", ["шопен", "Exam"], ["May"]) };

        JoinedPiece joined = Assert.Single(SamePiece.Plan([web, Fetched(ipad)], device: HoldingDevice.Web));

        Assert.Equal((1, "0"), (joined.Keeper, string.Join(",", joined.Joined)));
        Assert.Equal(ipad.Id, joined.Project.Id);
        Assert.Equal(s_pdf, joined.Project.Document);
        Assert.Equal(ipad.DocumentMarks, joined.Project.DocumentMarks);
        Assert.Equal(new PieceMetadata("Nocturne", "Chopin", ["Шопен", "Exam"], ["May"]), joined.Project.Metadata);
        // Saved and read back, it is an ordinary project file.
        Assert.Equal(ipad.Id, ProjectFile.Read(ProjectFile.Write(joined.Project)).Id);
    }

    [Fact]
    public void Two_copies_that_both_carry_work_stay_two_pieces()
    {
        Project web = OnWeb() with { PlayerTempo = 72 };
        Project ipad = Bookmarked(OnIpad());

        Assert.Empty(SamePiece.Plan([web, Fetched(ipad)]));
        Assert.False(SamePiece.AddsNothingTo(web, ipad));
        Assert.False(SamePiece.AddsNothingTo(ipad, web));
    }

    [Fact]
    public void A_copy_with_the_same_work_joins_and_one_with_any_work_of_its_own_does_not()
    {
        Project keeper = Bookmarked(OnIpad()) with { PlayerTempo = 72 };
        Assert.True(SamePiece.AddsNothingTo(Bookmarked(OnWeb()), keeper));
        Assert.True(SamePiece.AddsNothingTo(OnWeb() with { PlayerTempo = 72 }, keeper));

        Project[] own =
        [
            OnWeb() with { DocumentMarks = DocumentMarks.Empty.AddBookmark(2, "Coda", s_now, IdSource.Numbered) },
            OnWeb() with { PlayerTempo = 60 },
            OnWeb() with { SettingOverrides = new Dictionary<string, string> { ["playback.metronome"] = "on" } },
            OnWeb() with { HiddenViews = ["src_pdf"] },
            OnWeb() with { Bookmarks = [new Bookmark("b_1", "Start", BookmarkCategory.StructureId, new TickSpan(Tick.Zero, 480), FromPhrase: false, s_now)] },
        ];
        Assert.All(own, copy => Assert.False(SamePiece.AddsNothingTo(copy, keeper)));
    }

    [Fact]
    public void Every_field_of_the_players_own_is_compared()
    {
        // A field added to the file later is classed (ProjectSync.Fields); one of the player's must be compared too.
        string[] players = [.. ProjectSync.Fields.Where(static field => field.Value == FieldClass.Player).Select(static field => field.Key)];
        Assert.All(players, field => Assert.Contains(field, SamePiece.Compared));
    }

    [Fact]
    public void Different_notes_and_different_parts_of_one_collection_are_never_one_piece()
    {
        Project notes = OnWeb().Recognised(Fixture("fur-elise-original.musicxml"), "homr 0.7.0", new PageMap([Tick.Zero]));
        Assert.False(SamePiece.AddsNothingTo(notes, OnIpad()));
        Assert.True(SamePiece.AddsNothingTo(OnIpad(), notes));

        Project first = OnWeb() with { PartOf = PartOf.OnPages(PieceOriginal.DocumentId, 1, 2) };
        Project second = OnIpad() with { PartOf = PartOf.OnPages(PieceOriginal.DocumentId, 3, 4) };
        Assert.NotEqual(SamePiece.Keys(first), SamePiece.Keys(second));
        Assert.Empty(SamePiece.Plan([first, second]));
        Assert.Single(SamePiece.Plan([first, OnIpad() with { PartOf = PartOf.OnPages(PieceOriginal.DocumentId, 1, 2) }]));
    }

    [Fact]
    public void Untouched_notes_with_their_pdf_elsewhere_get_the_pdf_of_the_copy_that_joins_spread_over_their_pages()
    {
        Project recognised = OnIpad().Recognised(Fixture("fur-elise-original.musicxml"), "homr 0.7.0", new PageMap([Tick.Zero]));

        JoinedPiece joined = Assert.Single(SamePiece.Plan([OnWeb(), Fetched(recognised)], device: HoldingDevice.Web));

        Assert.Equal(recognised.Id, joined.Project.Id);
        Assert.True(joined.Project.HasNotes);
        Assert.Equal(s_pdf, joined.Project.Document);
        Assert.NotNull(joined.Project.DocumentPages);
        Assert.Equal(recognised.Id, ProjectFile.Read(ProjectFile.Write(joined.Project)).Id);
    }

    [Fact]
    public void A_copy_linked_on_this_device_never_goes_though_another_may_join_it()
    {
        Assert.DoesNotContain(SamePiece.Plan([OnWeb(), Fetched(OnIpad())], stay: new HashSet<int> { 1 }), piece => piece.Joined.Contains(1));

        // The web's copy (made first) would be kept, but the iPad's is linked here: it stays, and the web's joins it.
        JoinedPiece joined = Assert.Single(SamePiece.Plan([OnIpad(), Fetched(OnWeb())], stay: new HashSet<int> { 0 }, device: HoldingDevice.Ipad));
        Assert.Equal((0, "1"), (joined.Keeper, string.Join(",", joined.Joined)));
        Assert.Equal(OnIpad().Id, joined.Project.Id);
    }

    [Fact]
    public void Projects_known_by_a_shared_key_are_candidates_and_others_are_not()
    {
        IReadOnlyList<IReadOnlyList<string>> groups = SamePiece.Candidates(
        [
            ("a", ["x"]), ("b", ["y"]), ("c", ["x", "y"]), ("d", []), ("e", ["z"]),
        ]);

        Assert.Equal([["a", "b", "c"]], groups.Select(static group => group.ToArray()).ToArray());
    }

    [Fact]
    public void A_piece_only_opened_on_both_devices_keeps_no_previous_copy_and_real_work_on_both_still_does()
    {
        Project here = OnWeb() with { LastOpened = s_now.AddHours(2) };
        Project online = Fetched(OnWeb() with { LastOpened = s_now.AddHours(1) });

        Assert.Null(ProjectSync.Resolve(here, s_now.AddHours(2), online, s_now.AddHours(1), CopyTitle, "p_copy").PreviousCopy);
        Assert.Null(ProjectSync.Resolve(here, s_now, online, s_now.AddHours(1), CopyTitle, "p_copy").PreviousCopy);

        SyncResolution both = ProjectSync.Resolve(here with { PlayerTempo = 60 }, s_now.AddHours(2), Fetched(OnWeb() with { PlayerTempo = 90 }), s_now.AddHours(1), CopyTitle, "p_copy");
        Assert.Equal(90, both.PreviousCopy!.PlayerTempo);
    }

    [Fact]
    public void A_previous_copy_kept_beside_in_a_conflict_stays_a_piece_of_its_own()
    {
        // Andrei, 2026-11-11: when both changed a piece, the previous copy is kept beside as a piece of its own.
        Project here = OnWeb() with { Metadata = new PieceMetadata("Nocturne", null, null, null) };
        Project online = Fetched(OnWeb() with { Metadata = new PieceMetadata(null, null, ["From the iPad"], null) });
        Project copy = ProjectSync.Resolve(here, s_now.AddHours(1), online, s_now, CopyTitle, "p_copy").PreviousCopy!;

        Assert.Equal(SamePiece.Keys(here), SamePiece.Keys(copy));
        Assert.Empty(SamePiece.Plan([here, copy]));
    }

    [Fact]
    public void The_ipad_finds_and_joins_one_piece_through_the_facade()
    {
        var facade = new MusicCoreFacade();
        string Text(Project project) => Encoding.UTF8.GetString(ProjectFile.Write(project));
        JsonElement Call(string name, object request)
        {
            JsonElement answer = JsonDocument.Parse(facade.Call(0, name, JsonSerializer.Serialize(request))).RootElement;
            Assert.False(answer.TryGetProperty("error", out JsonElement error), $"{name}: {error}");
            return answer;
        }

        JsonElement keys = Call("project.summary", new { file = Text(OnIpad()) }).GetProperty("project").GetProperty("pieceKeys");
        Assert.Equal([PieceOriginal.Fingerprint(s_pdf)], keys.EnumerateArray().Select(static key => key.GetString()!).ToArray());

        JsonElement groups = Call("project.same", new { pieces = new object[] { new { id = "a", keys = new[] { "x" } }, new { id = "b", keys = new[] { "y" } }, new { id = "c", keys = new[] { "x" } } } }).GetProperty("groups");
        Assert.Equal(["a", "c"], Assert.Single(groups.EnumerateArray()).EnumerateArray().Select(static id => id.GetString()!).ToArray());

        JsonElement joined = Call("project.join", new { files = new[] { Text(OnIpad()), Text(Fetched(OnWeb())) } }).GetProperty("joined");
        JsonElement piece = Assert.Single(joined.EnumerateArray());
        Assert.Equal(1, piece.GetProperty("keeper").GetInt32());
        Assert.Equal([0], piece.GetProperty("from").EnumerateArray().Select(static index => index.GetInt32()).ToArray());
        Project kept = ProjectFile.Read(Encoding.UTF8.GetBytes(piece.GetProperty("file").GetString()!));
        Assert.Equal(OnWeb().Id, kept.Id);
        Assert.Equal(s_pdf, kept.Document);

        JsonElement none = Call("project.join", new { files = new[] { Text(OnIpad()), Text(Fetched(OnWeb())) }, stay = new[] { 0 } }).GetProperty("joined");
        Assert.Single(none.EnumerateArray());
        Assert.Equal(0, none[0].GetProperty("keeper").GetInt32());
    }

    [Fact]
    public void Two_files_saved_by_the_ipad_from_the_same_score_on_two_days_join_with_both_tags()
    {
        // As CloudSyncTests.testTheSamePieceAddedHereAndOnTheWebIsOnePiece makes them on the iPad.
        var facade = new MusicCoreFacade();
        byte[] elise = Fixture("fur-elise-original.musicxml");
        string Saved(string id, string tag, string now)
        {
            int piece = JsonDocument.Parse(facade.Open("elise.musicxml", elise, JsonSerializer.Serialize(new { language = "en", id, now }))).RootElement.GetProperty("piece").GetInt32();
            facade.Call(piece, "project.metadata", JsonSerializer.Serialize(new { tags = new[] { tag } }));
            string file = facade.Call(piece, "project.save", JsonSerializer.Serialize(new { now }));
            facade.Close(piece);
            return file;
        }

        string mine = Saved("p_11111111111111111111111111111111", "Here", "2026-11-15T09:00:00Z");
        string web = Saved("p_22222222222222222222222222222222", "Web", "2026-11-15T08:00:00Z");
        JsonElement piece = Assert.Single(JsonDocument.Parse(facade.Call(0, "project.join", JsonSerializer.Serialize(new { files = new[] { mine, web } }))).RootElement.GetProperty("joined").EnumerateArray());

        Assert.Equal(1, piece.GetProperty("keeper").GetInt32());
        Project kept = ProjectFile.Read(Encoding.UTF8.GetBytes(piece.GetProperty("file").GetString()!));
        Assert.Equal(["Web", "Here"], kept.Metadata.Tags);
    }
}