using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// The three-way merge of two copies of one project (#322, 0026 S4, S5, S8, S9, C2, C5; the 1.6 plan's decisions 9, 14, 16):
/// per entity, against the copy both last agreed on, with a record of what of the copy that lost is not held.
/// </summary>
public class ProjectMergeTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);

    private const string CopyTitle = "{0} (previous copy)";

    private static byte[] Music() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", "fur-elise-original.musicxml"));

    private static Project Piece() => Project.Create("p_0123456789abcdef0123456789abcdef", "fur-elise.musicxml", Music(), s_now);

    /// <summary>The project with its file changed as <paramref name="change"/> says, read again.</summary>
    private static Project Edited(Project project, Action<JsonObject> change)
    {
        JsonObject file = JsonNode.Parse(ProjectFile.Write(project))!.AsObject();
        change(file);
        return ProjectFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString()));
    }

    private static JsonArray List(JsonObject file, string name) =>
        file["workspace"]![name] as JsonArray ?? (JsonArray)(file["workspace"]![name] = new JsonArray());

    private static JsonObject Idea(string id, string note, int onset = 0) => new()
    {
        ["id"] = id,
        ["name"] = "Bar 1",
        ["factId"] = "rhythm.anacrusis",
        ["note"] = note,
        ["createdAt"] = "2026-11-27T09:00:00.0000000+00:00",
        ["phrase"] = new JsonObject { ["onset"] = onset, ["duration"] = 1440, ["filter"] = new JsonObject() },
    };

    private static Project WithIdeas(Project project, params JsonObject[] ideas) =>
        Edited(project, file => ideas.ToList().ForEach(idea => List(file, "observations").Add(idea)));

    private static Project WithoutIdea(Project project, string id) =>
        Edited(project, file => List(file, "observations").Remove(List(file, "observations").First(each => each!["id"]!.GetValue<string>() == id)));

    private static Project WithBookmark(Project project, string id, int onset) => Edited(project, file =>
        ((JsonArray)file["workspace"]!["bookmarks"]!["items"]!).Add(new JsonObject
        {
            ["id"] = id,
            ["title"] = "Opening",
            ["category"] = "practise",
            ["onset"] = onset,
            ["duration"] = 960,
            ["fromPhrase"] = false,
            ["createdAt"] = "2026-11-27T09:00:00.0000000+00:00",
        }));

    private static Project WithPhraseStart(Project project, int tick) => Edited(project, file => ((JsonArray)file["workspace"]!["phrases"]!["added"]!).Add(tick));

    private static IReadOnlyList<string> Ideas(Project project) => [.. project.Observations.Select(static idea => $"{idea.Id}:{idea.Note}")];

    private static MergedProject Merge(Project winner, Project loser, Project? @base) => Assert.IsType<MergedProject>(ProjectMerge.Merged(winner, loser, @base));

    [Fact]
    public void Ideas_kept_on_two_devices_since_the_base_are_both_kept_with_no_conflict_and_nothing_beside()
    {
        Project @base = Piece();
        Project here = WithIdeas(@base, Idea("o_here", "Left hand leads."));
        Project online = WithIdeas(@base, Idea("o_online", "Pedal late."));

        SyncResolution resolved = ProjectSync.Resolve(here, s_now.AddMinutes(5), online, s_now, CopyTitle, "p_copy", null, @base);
        Assert.Equal(["o_here:Left hand leads.", "o_online:Pedal late."], Ideas(resolved.Kept));
        Assert.Empty(resolved.Conflicts);
        Assert.True(resolved.Merged);
        Assert.Null(resolved.PreviousCopy);
        Assert.True(resolved.Upload);
    }

    [Fact]
    public void An_idea_removed_on_one_side_goes_with_a_base_and_comes_back_without_one()
    {
        Project @base = WithIdeas(Piece(), Idea("o_1", "Kept."), Idea("o_2", "Removed online."));
        Project online = WithoutIdea(@base, "o_2");
        Project here = WithIdeas(@base, Idea("o_3", "New here."));

        Assert.Equal(["o_1:Kept.", "o_3:New here."], Ideas(Merge(here, online, @base).Project));
        // S4: with no base the merge is a union with no deletions.
        Assert.Equal(["o_1:Kept.", "o_2:Removed online.", "o_3:New here."], Ideas(Merge(here, online, null).Project));
    }

    [Fact]
    public void The_same_idea_changed_on_both_keeps_the_last_save_records_the_conflict_and_keeps_the_other_beside()
    {
        Project @base = WithIdeas(Piece(), Idea("o_1", "First."));
        Project here = Edited(@base, file => List(file, "observations")[0]!["note"] = "Here.");
        Project online = Edited(@base, file => List(file, "observations")[0]!["note"] = "Online.");

        SyncResolution resolved = ProjectSync.Resolve(here, s_now, online, s_now.AddMinutes(1), CopyTitle, "p_copy", null, @base);
        Assert.Equal(["o_1:Online."], Ideas(resolved.Kept));
        Assert.Equal([new MergeConflict("workspace.observations", "o_1")], resolved.Conflicts);
        Assert.Equal(["o_1:Here."], Ideas(resolved.PreviousCopy!));
        Assert.Equal("p_copy", resolved.PreviousCopy!.Id);
    }

    [Fact]
    public void An_idea_removed_on_one_side_and_changed_on_the_other_stays_changed()
    {
        Project @base = WithIdeas(Piece(), Idea("o_1", "First."));
        Project changed = Edited(@base, file => List(file, "observations")[0]!["note"] = "Changed.");
        Project removed = WithoutIdea(@base, "o_1");

        MergedProject merged = Merge(removed, changed, @base);
        Assert.Equal(["o_1:Changed."], Ideas(merged.Project));
        Assert.Empty(merged.Conflicts);
    }

    [Fact]
    public void Bookmarks_resume_points_and_phrase_boundaries_merge_by_id_and_by_value()
    {
        Project @base = Piece();
        Project here = WithPhraseStart(WithBookmark(@base, "b_here", 0), 3840);
        Project online = WithPhraseStart(WithBookmark(@base, "b_online", 1920), 1920);

        Project merged = Merge(here, online, @base).Project;
        Assert.Equal(["b_here", "b_online"], merged.Bookmarks.Select(static bookmark => bookmark.Id).Order(StringComparer.Ordinal));
        Assert.Equal([1920, 3840], merged.Phrases.Added.Select(static tick => tick.Value));

        // A boundary set on both stays once; one taken away on one side since the base goes.
        Project removed = Edited(merged, file => ((JsonArray)file["workspace"]!["phrases"]!["added"]!).RemoveAt(0));
        Assert.Equal([3840], Merge(removed, merged, merged).Project.Phrases.Added.Select(static tick => tick.Value));
    }

    [Fact]
    public void Two_anchors_on_one_span_from_two_devices_become_one_and_the_smaller_id_stays_with_both_ideas()
    {
        // c-14d's rule (the lead, 2026-11-27): one anchor per span, the smaller id kept, what the other carried moved to it.
        Project @base = Piece();
        JsonObject Anchored(string idea, string anchor) => new(Idea(idea, idea).Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone())))
        {
            ["anchor"] = anchor,
        };
        Project With(string idea, string anchor) => Edited(@base, file =>
        {
            List(file, "observations").Add(Anchored(idea, anchor));
            file["workspace"]!["phrases"]!["anchors"] = new JsonArray(new JsonObject { ["id"] = anchor, ["onset"] = 0, ["duration"] = 1440 });
        });

        Project merged = Merge(With("o_a", "ph_bbbb"), With("o_b", "ph_aaaa"), @base).Project;
        Assert.Equal(["ph_aaaa"], merged.Anchors.Select(static anchor => anchor.Id));
        Assert.All(merged.Observations, idea => Assert.Equal("ph_aaaa", idea.Anchor));
    }

    [Fact]
    public void Scan_marks_merge_by_kind_bar_and_staff_and_the_same_place_changed_on_both_is_a_conflict()
    {
        // The 1.6 plan's decision 16: not "scan" taken whole from one copy.
        Project @base = Piece() with { Scan = ScanOrigin.Read("homr 0.7.0") };
        static Project Marked(Project project, params ReadingMark[] marks) => project with { Scan = marks.Aggregate(project.Scan!, static (scan, mark) => scan.WithMark(mark)) };
        var meter = new ReadingMark(ReadingMarkKind.Meter, 3, 3, null, "3/4");
        var clef = new ReadingMark(ReadingMarkKind.Clef, 5, 5, 2, "bass");
        Project here = Marked(@base, meter);
        Project online = Marked(@base, clef);

        MergedProject merged = Merge(here, online, @base);
        Assert.Equal([meter, clef], merged.Project.Scan!.Marks);
        Assert.Empty(merged.Conflicts);

        MergedProject both = Merge(here, Marked(@base, new ReadingMark(ReadingMarkKind.Meter, 3, 3, null, "5/4")), @base);
        Assert.Equal([meter], both.Project.Scan!.Marks);
        Assert.Equal([new MergeConflict("notes.scan.marks", "Meter|3|")], both.Conflicts);

        // Taken away on one side since the base, unchanged on the other: it goes.
        Assert.Equal([clef], Merge(online, Marked(online, meter), Marked(online, meter)).Project.Scan!.Marks);
    }

    [Fact]
    public void The_same_note_corrected_on_both_is_a_conflict_on_that_note()
    {
        Project @base = Piece();
        Note note = PieceImporter.Import(@base.FileName, @base.Source).Piece!.Notes[0];
        NoteValues before = NoteValues.Of(note);
        Project Raised(int semitones) => @base with { Edits = new NoteEditLog([new NoteEdit(note.Id, before, before with { Pitch = new Pitch(before.Pitch.MidiNumber + semitones) })]) };

        MergedProject merged = Merge(Raised(1), Raised(2), @base);
        Assert.Equal([new MergeConflict("notes.edits", note.Id)], merged.Conflicts);
        Assert.Equal(Raised(1).Edits.Edits, merged.Project.Edits.Edits);
    }

    [Fact]
    public void Settings_merge_per_setting_and_the_title_changed_on_both_is_a_conflict()
    {
        Project @base = Piece();
        Project here = Edited(@base, file =>
        {
            file["workspace"]!["metadata"] = new JsonObject { ["title"] = "Here" };
        });
        Project online = Edited(@base, file =>
        {
            file["workspace"]!["metadata"] = new JsonObject { ["title"] = "Online" };
        });

        MergedProject merged = Merge(here, online, @base);
        Assert.Equal("Here", merged.Project.Metadata.Title);
        Assert.Equal([new MergeConflict("workspace.metadata", null)], merged.Conflicts);
    }

    [Fact]
    public void The_same_three_copies_give_the_same_project_and_conflicts()
    {
        Project @base = WithIdeas(Piece(), Idea("o_1", "First."));
        Project here = WithBookmark(Edited(@base, file => List(file, "observations")[0]!["note"] = "Here."), "b_1", 0);
        Project online = WithIdeas(Edited(@base, file => List(file, "observations")[0]!["note"] = "Online."), Idea("o_2", "Two."));

        MergedProject once = Merge(here, online, @base);
        MergedProject again = Merge(ProjectFile.Read(ProjectFile.Write(here)), ProjectFile.Read(ProjectFile.Write(online)), ProjectFile.Read(ProjectFile.Write(@base)));
        Assert.Equal(ProjectFile.Write(once.Project), ProjectFile.Write(again.Project));
        Assert.Equal(once.Conflicts, again.Conflicts);
    }

    [Fact]
    public void A_copy_re_keyed_from_the_online_id_merges_with_it_and_keeps_the_online_id()
    {
        // #317 and the 1.6 plan's decision 9: a project saved online keeps its id; this device's re-keyed one joins formerIds.
        Project online = WithIdeas(Piece() with { Id = "p_0007" }, Idea("o_online", "Online."));
        Project here = WithIdeas(Piece() with { Id = "p_" + new string('a', 32), FormerIds = ["p_0007"] }, Idea("o_here", "Here."));

        SyncResolution resolved = ProjectSync.Resolve(here, s_now.AddMinutes(1), online, s_now, CopyTitle, "p_copy");
        Assert.Equal("p_0007", resolved.Kept.Id);
        Assert.Equal([here.Id], resolved.Kept.FormerIds);
        Assert.Equal(["o_here:Here.", "o_online:Online."], Ideas(resolved.Kept));
        Assert.Null(resolved.PreviousCopy);
    }

    [Fact]
    public void A_piece_removed_here_is_deleted_online_unless_it_changed_there_since()
    {
        Assert.Equal(SyncStep.DeleteOnline, ProjectSync.Next(new SyncFacts(Here: false, Online: true, Known: true, ChangedHere: false, ChangedOnline: false, DeletedHere: true)));
        Assert.Equal(SyncStep.Download, ProjectSync.Next(new SyncFacts(Here: false, Online: true, Known: true, ChangedHere: false, ChangedOnline: true, DeletedHere: true)));
        Assert.Equal(SyncStep.Forget, ProjectSync.Next(new SyncFacts(Here: false, Online: false, Known: true, ChangedHere: false, ChangedOnline: false, DeletedHere: true)));
        Assert.Equal(SyncStep.Nothing, ProjectSync.Next(new SyncFacts(Here: false, Online: true, Known: true, ChangedHere: false, ChangedOnline: false)));
    }
    [Fact]
    public void A_copy_a_later_build_saved_online_is_kept_read_only_merged_with_its_unknown_fields_and_never_sent()
    {
        // The 1.6 plan's decision 14 (c-14a's decision 10): it can be fetched, it is never written over by this build.
        var facade = new MusicCoreFacade();
        JsonElement Call(string name, object request) => JsonDocument.Parse(facade.Call(0, name, JsonSerializer.Serialize(request))).RootElement;
        string future = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "v19-future.motiff.json"));
        JsonObject today = JsonNode.Parse(future)!.AsObject();
        today["version"] = ProjectFile.Version;
        today.Remove("future");
        today["workspace"]!.AsObject().Remove("future");
        Project here = WithIdeas(ProjectFile.Read(Encoding.UTF8.GetBytes(today.ToJsonString())), Idea("o_here", "Here."));
        string hereText = Encoding.UTF8.GetString(ProjectFile.Write(here));

        JsonElement pulled = Call("project.pulled", new { file = future });
        Assert.True(pulled.GetProperty("readOnly").GetBoolean());
        Assert.Contains("A field a later version adds to motifs.", pulled.GetProperty("file").GetString(), StringComparison.Ordinal);
        Assert.Equal("readOnly", Call("project.online", new { file = pulled.GetProperty("file").GetString() }).GetProperty("error").GetString());

        JsonElement resolved = Call("project.resolve", new { here = hereText, savedHere = "2026-11-27T10:00:00Z", online = future, savedOnline = "2026-11-27T09:00:00Z", copyTitle = CopyTitle });
        Assert.True(resolved.GetProperty("readOnly").GetBoolean());
        Assert.False(resolved.GetProperty("upload").GetBoolean());
        Assert.Equal("[]", resolved.GetProperty("conflicts").GetRawText());
        Assert.True(resolved.GetProperty("merged").GetBoolean());
        Project kept = ProjectFile.Read(Encoding.UTF8.GetBytes(resolved.GetProperty("file").GetString()!));
        Assert.True(kept.IsReadOnly);
        Assert.Contains(kept.Observations, idea => idea.Id == "o_here");
        // The workspace field only the later build's copy has is kept.
        Assert.Equal(1, JsonNode.Parse(resolved.GetProperty("file").GetString()!)!["workspace"]!["future"]!.GetValue<int>());

        // project.join leaves it out rather than failing as a whole.
        JsonElement joined = Call("project.join", new { files = new[] { future, hereText } });
        Assert.False(joined.TryGetProperty("error", out _));
        Assert.All(joined.GetProperty("joined").EnumerateArray(), piece => Assert.NotEqual(0, piece.GetProperty("keeper").GetInt32()));
    }

    [Fact]
    public void The_ipad_sends_the_base_and_reads_the_conflicts_through_the_facade()
    {
        var facade = new MusicCoreFacade();
        static string Text(Project project) => Encoding.UTF8.GetString(ProjectFile.Write(project));
        Project @base = WithIdeas(Piece(), Idea("o_1", "First."));
        Project here = Edited(@base, file => List(file, "observations")[0]!["note"] = "Here.");
        Project online = WithIdeas(Edited(@base, file => List(file, "observations")[0]!["note"] = "Online."), Idea("o_2", "Two."));

        JsonElement resolved = JsonDocument.Parse(facade.Call(0, "project.resolve", JsonSerializer.Serialize(new
        {
            here = Text(here),
            savedHere = "2026-11-27T10:00:00Z",
            online = Text(online),
            savedOnline = "2026-11-27T09:00:00Z",
            copyTitle = CopyTitle,
            @base = Text(@base),
        }))).RootElement;
        Assert.True(resolved.GetProperty("merged").GetBoolean());
        Assert.False(resolved.GetProperty("readOnly").GetBoolean());
        Assert.Equal("""[{"in":"workspace.observations","id":"o_1"}]""", resolved.GetProperty("conflicts").GetRawText());
        Assert.Equal(["o_1:Here.", "o_2:Two."], Ideas(ProjectFile.Read(Encoding.UTF8.GetBytes(resolved.GetProperty("file").GetString()!))));
        Assert.Equal("deleteOnline", JsonDocument.Parse(facade.Call(0, "project.sync", """{"online":true,"known":true,"deletedHere":true}""")).RootElement.GetProperty("step").GetString());
    }
}