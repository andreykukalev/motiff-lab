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

    internal const string CopyTitle = "{0} (previous copy)";

    private static byte[] Music() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", "fur-elise-original.musicxml"));

    internal static Project Piece() => Project.Create("p_0123456789abcdef0123456789abcdef", "fur-elise.musicxml", Music(), s_now);

    /// <summary>The project with its file changed as <paramref name="change"/> says, read again.</summary>
    private static Project Edited(Project project, Action<JsonObject> change)
    {
        JsonObject file = JsonNode.Parse(ProjectFile.Write(project))!.AsObject();
        change(file);
        return ProjectFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString()));
    }

    private static JsonArray List(JsonObject file, string name) =>
        file["workspace"]![name] as JsonArray ?? (JsonArray)(file["workspace"]![name] = new JsonArray());

    internal static JsonObject Idea(string id, string note, int onset = 0) => new()
    {
        ["id"] = id,
        ["name"] = "Bar 1",
        ["factId"] = "rhythm.anacrusis",
        ["note"] = note,
        ["createdAt"] = "2026-11-27T09:00:00.0000000+00:00",
        ["phrase"] = new JsonObject { ["onset"] = onset, ["duration"] = 1440, ["filter"] = new JsonObject() },
    };

    internal static Project WithIdeas(Project project, params JsonObject[] ideas) =>
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

    internal static IReadOnlyList<string> Ideas(Project project) => [.. project.Observations.Select(static idea => $"{idea.Id}:{idea.Note}")];

    private static MergedProject Merge(Project winner, Project loser, Project? @base) => Assert.IsType<MergedProject>(ProjectMerge.Merged(winner, loser, @base));

    private static SyncResolution Resolve(Project here, Project online, Project? @base, bool hereWins = true) =>
        ProjectSync.Resolve(here, hereWins ? s_now.AddMinutes(5) : s_now, online, hereWins ? s_now : s_now.AddMinutes(5), CopyTitle, "p_copy", null, @base);

    /// <summary>The piece with other Notes "n_2", the same music as the working ones.</summary>
    internal static Project WithOtherNotes(Project project) => project.AddNotes(new OtherNotes("n_2", "urtext.musicxml", Music(), new NotesProvenance(NotesProvenance.Import)));

    internal static Project Renamed(Project project, string? name) =>
        project with { Sources = project.Sources with { Notes = [.. project.Sources.Notes.Select(notes => notes.Id == "n_2" ? notes with { Name = name } : notes)] } };

    internal static string? NameOfOther(Project project) => project.Sources.Notes.Single(static notes => notes.Id == "n_2").Name;

    /// <summary>The project as a later build saves it: read-only here (0026 S6).</summary>
    internal static Project Later(Project project) => ProjectFile.Read(ProjectFile.Write(project with { Newer = new NewerFile(ProjectFile.Version + 1, ProjectFile.MinReader) }));

    private static JsonObject Motif(string id) => new()
    {
        ["id"] = id,
        ["name"] = "Opening",
        ["kind"] = "Range",
        ["mode"] = "Discovery",
        ["onset"] = 0,
        ["duration"] = 1920,
        ["filter"] = new JsonObject(),
        ["noteIds"] = new JsonArray("n_0001", "n_0002"),
    };

    private static JsonObject Variation(string id, string motif) => new()
    {
        ["id"] = id,
        ["motifId"] = motif,
        ["steps"] = new JsonArray(new JsonObject { ["operation"] = "V1", ["parameters"] = new JsonObject { ["kind"] = "HalfTime", ["eighths"] = 1 } }),
        ["keep"] = new JsonArray(),
    };

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
        Assert.Equal([new MergeConflict("notes.scan.marks", "meter|3|")], both.Conflicts);

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
    public void Every_field_of_the_players_own_is_merged_or_checked_for_a_conflict()
    {
        // The reviewers of b6ee5f00 and e47921c7: a field of the player's no rule names is lost when its copy loses, as the
        // marks on a PDF's pages were. Version 16's layout is the originals, the Notes and the workspace; a top-level field of
        // an older file is read into it, where it is merged.
        Dictionary<string, string> readInto = new(StringComparer.Ordinal)
        {
            ["documentMarks"] = "originals.marks",
            ["tempo"] = "workspace.tempo",
            ["motifs"] = "workspace.motifs",
            ["variations"] = "workspace.variations",
            ["observations"] = "workspace.observations",
            ["edits"] = "notes.edits",
            ["phrases"] = "workspace.phrases",
            ["marks"] = "workspace.marks",
            ["bookmarks"] = "workspace.bookmarks",
            ["resumePoints"] = "workspace.resumePoints",
            ["settings"] = "workspace.settings",
            ["performances"] = "notes.performance",
            ["assembly"] = "workspace.assembly",
            ["metadata"] = "workspace.metadata",
        };
        string[] players = [.. ProjectSync.Fields.Where(static field => field.Value == FieldClass.Player).Select(static field => field.Key)];
        Assert.Contains("originals.marks", players);
        foreach (string field in players.Where(static field => !field.Contains('.', StringComparison.Ordinal) && !ProjectFile.RootFields.Contains(field)))
        {
            Assert.True(readInto.TryGetValue(field, out string? at), $"\"{field}\" names no field of version 16's layout it is read into.");
            Assert.NotNull(MergeRules.MergedBy(at));
        }

        Assert.All(players.Where(static field => field.Contains('.', StringComparison.Ordinal) || ProjectFile.RootFields.Contains(field)), field => Assert.NotNull(MergeRules.MergedBy(field)));
    }

    [Fact]
    public void A_later_builds_field_of_the_player_comes_in_from_the_copy_that_lost()
    {
        // The second review's finding 4: a field this build does not know in a Notes was given the Notes' class, an import's,
        // so the merge dropped it and its self-check let it go. It is the player's, as a later build's field anywhere is.
        Project @base = Piece();
        Project winner = WithIdeas(@base, Idea("o_here", "Here."));
        Project loser = Edited(@base, file => file["notes"]![0]!["future"] = 7);
        Project merged = Merge(winner, loser, @base).Project;
        Assert.Equal(7, JsonNode.Parse(ProjectFile.Write(merged))!["notes"]![0]!["future"]!.GetValue<int>());
        Assert.Equal(["o_here:Here."], Ideas(merged));

        Assert.Equal(FieldClass.Player, MergeRules.Class("notes.future"));
        Assert.Equal(FieldClass.Player, MergeRules.Class("originals.future"));
        Assert.Equal(FieldClass.Import, MergeRules.Class("notes.scan.confirmedBars"));
        Assert.Equal(FieldClass.Player, MergeRules.Class("workspace.settings.layers.analysis"));
        Assert.Equal(FieldClass.Import, MergeRules.Class("notes.transcription.bars"));
    }

    [Fact]
    public void A_recording_added_online_stays_when_this_device_changed_a_setting_and_wins()
    {
        // The reviewer's repro: the originals were the winner's, so the recording only online was lost with no copy beside.
        Project @base = Piece();
        Project online = @base.AddOriginal(new PieceOriginal(PieceOriginal.RecordingId, OriginalKind.Audio, "take.m4a", OriginalKept.Device, s_now), [1, 2, 3]);
        Project here = @base.OverrideSetting("layers.analysis", "off");

        SyncResolution resolved = Resolve(here, online, @base);
        Assert.Contains(resolved.Kept.Originals, original => original.Id == PieceOriginal.RecordingId);
        Assert.Equal("off", resolved.Kept.SettingOverrides["layers.analysis"]);
        Assert.Empty(resolved.Conflicts);
        Assert.Null(resolved.PreviousCopy);
        Assert.True(resolved.Upload);
    }

    [Fact]
    public void An_app_that_sends_its_own_copy_keeps_the_online_one_beside_and_one_that_sends_the_file_needs_none()
    {
        // The lead's decision C: the iPad before c-17b sends "here" on "upload", not "file", so what only the online copy
        // has would be lost unless it is kept beside; and it sends only when its copy was saved last.
        var facade = new MusicCoreFacade();
        static string Text(Project project) => Encoding.UTF8.GetString(ProjectFile.Write(project));
        Project @base = Piece();
        Project online = @base.AddOriginal(new PieceOriginal(PieceOriginal.RecordingId, OriginalKind.Audio, "take.m4a", OriginalKept.Device, s_now), [1, 2, 3]);
        Project here = WithIdeas(@base, Idea("o_here", "Here."));
        JsonElement Resolved(bool? sendsFile, string savedHere) => JsonDocument.Parse(facade.Call(0, "project.resolve", JsonSerializer.Serialize(new
        {
            here = Text(here),
            savedHere,
            online = Text(online),
            savedOnline = "2026-11-27T10:00:00Z",
            copyTitle = CopyTitle,
            @base = Text(@base),
            sendsFile,
        }))).RootElement;

        JsonElement shipped = Resolved(null, "2026-11-27T11:00:00Z");
        Assert.True(shipped.GetProperty("upload").GetBoolean());
        Project beside = ProjectFile.Read(Encoding.UTF8.GetBytes(shipped.GetProperty("copy").GetString()!));
        Assert.Contains(beside.Originals, original => original.Id == PieceOriginal.RecordingId);

        // Saved last online, nothing is sent and the file it writes never is, so its own copy is kept beside (SyncAnswerTests).
        JsonElement older = Resolved(null, "2026-11-27T09:00:00Z");
        Assert.False(older.GetProperty("upload").GetBoolean());
        Assert.Equal(["o_here:Here."], Ideas(ProjectFile.Read(Encoding.UTF8.GetBytes(older.GetProperty("copy").GetString()!))));

        JsonElement sends = Resolved(true, "2026-11-27T09:00:00Z");
        Assert.True(sends.GetProperty("upload").GetBoolean());
        Assert.Equal(JsonValueKind.Null, sends.GetProperty("copy").ValueKind);
        Assert.Equal(JsonValueKind.Null, Resolved(true, "2026-11-27T11:00:00Z").GetProperty("copy").ValueKind);
    }

    [Fact]
    public void A_merge_that_fails_its_self_check_is_not_sent_and_keeps_the_other_copy_beside()
    {
        // 0026 C5 and the lead's decision B: Notes renamed on one copy and removed on the other cannot be merged entity by
        // entity, so nothing merged is sent; this device keeps its own with the other's corrections, the other beside.
        Project @base = WithOtherNotes(Piece());
        Project here = @base.DeleteNotes("n_2");
        Project online = Renamed(@base, "Urtext");
        Assert.Null(ProjectMerge.Merged(here, online, @base));

        SyncResolution resolved = Resolve(here, online, @base);
        Assert.False(resolved.Merged);
        Assert.False(resolved.Upload);
        Assert.DoesNotContain(resolved.Kept.Sources.Notes, notes => notes.Id == "n_2");
        Assert.Equal("Urtext", NameOfOther(resolved.PreviousCopy!));

        // A later build's copy here is not merged: it goes beside, read-only as it came, and the online one takes its place.
        SyncResolution later = Resolve(Later(here), online, @base);
        Assert.False(later.Merged);
        Assert.False(later.Upload);
        Assert.Equal("Urtext", NameOfOther(later.Kept));
        Assert.True(later.PreviousCopy!.IsReadOnly);
        Assert.DoesNotContain(later.PreviousCopy.Sources.Notes, notes => notes.Id == "n_2");
    }

    [Fact]
    public void A_name_of_other_notes_changed_on_one_side_comes_in_and_changed_on_both_is_a_conflict_kept_beside()
    {
        Project @base = WithOtherNotes(Piece());
        Assert.Equal("Urtext", NameOfOther(Resolve(@base, Renamed(@base, "Urtext"), @base).Kept));

        SyncResolution both = Resolve(Renamed(@base, "Here"), Renamed(@base, "Online"), @base);
        Assert.Equal("Here", NameOfOther(both.Kept));
        Assert.Equal([new MergeConflict("notes.name", "n_2")], both.Conflicts);
        Assert.Equal("Online", NameOfOther(both.PreviousCopy!));
    }

    [Fact]
    public void The_same_id_added_on_both_differently_is_a_conflict_and_the_same_change_on_both_is_none()
    {
        Project @base = Piece();
        SyncResolution different = Resolve(WithIdeas(@base, Idea("o_x", "Here.")), WithIdeas(@base, Idea("o_x", "Online.")), @base);
        Assert.Equal(["o_x:Here."], Ideas(different.Kept));
        Assert.Equal([new MergeConflict("workspace.observations", "o_x")], different.Conflicts);
        Assert.Equal(["o_x:Online."], Ideas(different.PreviousCopy!));

        static Project Same(Project project) => WithPhraseStart(WithIdeas(project, Idea("o_x", "Same.")), 1920).OverrideSetting("layers.analysis", "off");
        SyncResolution same = Resolve(Same(@base), Same(@base) with { LastOpened = s_now.AddHours(1) }, @base);
        Assert.Empty(same.Conflicts);
        Assert.Null(same.PreviousCopy);
        Assert.Equal(["o_x:Same."], Ideas(same.Kept));
    }

    [Fact]
    public void Whether_the_corrections_are_off_is_the_last_saved_copys_and_never_a_conflict()
    {
        // V21: "workspace.editsOff" is bookkeeping, so the copy saved last says it.
        Project @base = Piece();
        Project here = @base with { EditsOff = true };
        Project online = WithIdeas(@base, Idea("o_1", "Online.")) with { EditsOff = false };

        SyncResolution resolved = Resolve(here, online, @base, hereWins: false);
        Assert.False(resolved.Kept.EditsOff);
        Assert.Empty(resolved.Conflicts);
        Assert.Null(resolved.PreviousCopy);
        Assert.True(Resolve(here, online, @base).Kept.EditsOff);
    }

    [Fact]
    public void The_larger_notes_step_is_the_last_saved_copys_and_never_a_conflict()
    {
        // Version 19: "workspace.largerNotes" is bookkeeping, as "editsOff": a choice of how the piece is shown.
        Project @base = Piece();
        Project here = @base with { LargerNotes = 3 };
        Project online = WithIdeas(@base, Idea("o_1", "Online.")) with { LargerNotes = 7 };

        SyncResolution resolved = Resolve(here, online, @base, hereWins: false);
        Assert.Equal(7, resolved.Kept.LargerNotes);
        Assert.Empty(resolved.Conflicts);
        Assert.Null(resolved.PreviousCopy);
        Assert.Equal(3, Resolve(here, online, @base).Kept.LargerNotes);
    }

    [Fact]
    public void A_variation_whose_motif_went_and_an_idea_whose_anchor_went_take_them_back()
    {
        static JsonObject Anchored(string idea) => new(Idea(idea, idea).Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone()))) { ["anchor"] = "ph_aaaa" };
        Project @base = Edited(Piece(), file =>
        {
            List(file, "motifs").Add(Motif("m_1"));
            List(file, "observations").Add(Anchored("o_a"));
            file["workspace"]!["phrases"]!["anchors"] = new JsonArray(new JsonObject { ["id"] = "ph_aaaa", ["onset"] = 0, ["duration"] = 1440 });
        });
        // The winner took away the motif, the idea and its anchor; the other copy made a variation of it and an idea there.
        Project winner = Edited(@base, file =>
        {
            List(file, "motifs").Clear();
            List(file, "observations").Clear();
            ((JsonObject)file["workspace"]!["phrases"]!).Remove("anchors");
        });
        Project loser = Edited(@base, file =>
        {
            List(file, "variations").Add(Variation("v_1", "m_1"));
            List(file, "observations").Add(Anchored("o_b"));
        });

        MergedProject merged = Merge(winner, loser, @base);
        Assert.Equal(["m_1"], merged.Project.Motifs.Select(static motif => motif.Id));
        Assert.Equal(["v_1"], merged.Project.Variations.Select(static variation => variation.Id));
        Assert.Equal(["o_b"], merged.Project.Observations.Select(static idea => idea.Id));
        Assert.Equal(["ph_aaaa"], merged.Project.Anchors.Select(static anchor => anchor.Id));
        Assert.Equal("ph_aaaa", merged.Project.Observations[0].Anchor);
    }

    [Fact]
    public void Another_piece_under_the_same_id_keeps_a_later_builds_copy_as_fetched_and_never_sends_a_read_only_one()
    {
        // V8: a piece made at another time is not this one; the 1.6 plan's decision 14: a later build's copy is never written over.
        Project here = WithIdeas(Piece(), Idea("o_here", "Here."));
        Project other = WithIdeas(Project.Create(here.Id, "other.musicxml", Music(), s_now.AddDays(-1)), Idea("o_other", "Other."));

        SyncResolution online = Resolve(here, Later(other), null);
        Assert.Equal(ProjectFile.Write(Later(other)), ProjectFile.Write(online.Kept));
        Assert.True(online.Kept.IsReadOnly);
        Assert.Equal(["o_other:Other."], Ideas(online.Kept));
        Assert.False(online.Upload);
        Assert.Equal(["o_here:Here."], Ideas(online.PreviousCopy!));
        Assert.Equal("p_copy", online.PreviousCopy!.Id);

        // A later build's copy here goes beside, so the next download, of the online piece, does not write over it.
        SyncResolution mine = Resolve(Later(here), other, null);
        Assert.False(mine.Upload);
        Assert.Equal(["o_other:Other."], Ideas(mine.Kept));
        Assert.True(mine.PreviousCopy!.IsReadOnly);
        Assert.Equal(["o_here:Here."], Ideas(mine.PreviousCopy));
        Assert.Equal("p_copy", mine.PreviousCopy.Id);
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
    public void A_copy_a_later_build_saved_online_is_fetched_read_only_and_never_sent()
    {
        // The 1.6 plan's decision 14 (c-14a's decision 10): it can be fetched, it is never written over by this build;
        // resolved with this device's copy, that one is kept beside (SyncAnswerTests).
        var facade = new MusicCoreFacade();
        JsonElement Call(string name, object request) => JsonDocument.Parse(facade.Call(0, name, JsonSerializer.Serialize(request))).RootElement;
        (string future, Project here) = SyncAnswerTests.LaterBuildOnline();
        string hereText = Encoding.UTF8.GetString(ProjectFile.Write(here));

        JsonElement pulled = Call("project.pulled", new { file = future });
        Assert.True(pulled.GetProperty("readOnly").GetBoolean());
        Assert.Contains("A field a later version adds to motifs.", pulled.GetProperty("file").GetString(), StringComparison.Ordinal);
        Assert.Equal("readOnly", Call("project.online", new { file = pulled.GetProperty("file").GetString() }).GetProperty("error").GetString());

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