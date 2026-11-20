using System.Text;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// The drafts in the project file (version 17, decision record 0034, section 7; docs/project-file.md, "Drafts"): written
/// and read back the same, a draft with a value this build does not know kept whole as an unknown entity, and merged by
/// id in the sync as Notes are.
/// </summary>
public class DraftsFormatTests
{
    private const string Golden = "v17-drafts.motiff.json";

    private const string CopyTitle = "{0} (previous copy)";

    private static readonly NotesProvenance s_heard = new(NotesProvenance.AudioRecognition)
    {
        From = [new ProvenanceSource("perf_1") { Sha256 = new string('b', 64), Milliseconds = (0, 245_000) }],
        Engine = "transkun 2.0.1",
        At = At,
    };

    private static Project Piece()
    {
        SourceMusic score = Score();
        byte[] midi = MidiExporter.Export(score.Notes, score.Tempos, score.Meter, "Etude");
        return Project.Create("p_7a1b2c3d4e5f40718293a4b5c6d7e8f9", "Etude.mid", midi, At);
    }

    /// <summary>
    /// A draft of every kind of thing the file keeps: a place of the notes with a variant heard in a recording (its stretch,
    /// an anchor), a place of the melody of another piece, a bridge with a pencil note, a left hand on the shelf, and a
    /// journal of each kind of step.
    /// </summary>
    private static Draft Worked()
    {
        DraftVariant heard = DraftFragment.Variant("v_2", At.AddMinutes(1), Score(melody: bar => bar == 1 ? [84, 86, 88, 89] : [72, 74, 76, 77], prefix: "h"), Bars(1, 4), DraftPart.All, "perf_1", s_heard, milliseconds: (72_000, 84_500))
            with
        { Anchors = [new DraftAnchor(new Tick(BarLength), new Tick(BarLength + 60))] };
        DraftVariant other = DraftFragment.Variant("v_3", At.AddMinutes(2), Score(fifths: -2, perMinute: 66, prefix: "o"), Bars(5, 6), DraftPart.Melody, "n_1", Read, project: "p_0f1e2d3c4b5a49687786a5b4c3d2e1f0");
        DraftPlace bridge = new("pl_3", "v_5", [Variant("v_5", Score(prefix: "b"), 1, 1), Variant("v_6", Score(prefix: "c"), 2, 2)])
        {
            Bridge = true,
            Marks = Marks.Empty with { Notes = [new MarginNote("mn_1", new TickSpan(Tick.Zero, BarLength), "легато", At)] },
        };
        Draft draft = Draft.Start("d_1", "Предложение 1", At, "pl_1", Variant("v_1", Score(), 1, 4))
            .With(heard, DraftDrop.Variant, "pl_1")
            .With(other, DraftDrop.Place, "pl_2")
            .With(Variant("v_4", Score(), 7, 8, DraftPart.Left), DraftDrop.Shelf);
        DraftStep[] steps =
        [
            new DraftTake(1, At, "pl_1", "v_2")
            {
                Technique = new DraftTechnique(TechniqueKind.Octave, [2]),
                Part = DraftPart.Right,
                Removed = ["v_1.n1r0", "v_1.n1r1"],
                Added = ["v_2.h1r0", "v_2.h1r1"],
            },
            new DraftTake(2, At, "pl_1", "v_2") { Technique = new DraftTechnique(TechniqueKind.InsertBars, [4]), Insert = new DraftInsert(new Tick(3 * BarLength), new TickSpan(new Tick(2 * BarLength), BarLength)) },
            new DraftLeave(3, At, "pl_2") { Seam = true, Group = "g_1", Origin = "assistant" },
            new DraftTranspose(4, At, "pl_2", -3),
            new DraftTempo(5, At, "pl_2", 92),
        ];
        return new Draft(draft.Id, draft.Name, draft.CreatedAt, [.. draft.Places, bridge], draft.Shelf, steps, 6);
    }

    private static Project WithDrafts() => Piece().WithNewDraft(Worked()).WithNewDraft(Draft.Start("d_2", "Кода", At, "pl_1", Variant("v_1", Score(), 8, 8)));

    private static JsonObject Node(byte[] bytes) => JsonNode.Parse(bytes)!.AsObject();

    private static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());

    [Fact]
    public void The_file_written_is_the_golden_of_version_17_and_reads_back_as_the_same_drafts()
    {
        GoldenFiles.AssertIs(Golden, ProjectFile.Write(WithDrafts()));

        Project read = ProjectFile.Read(GoldenFiles.Kept(Golden));
        Assert.Equal(["d_1", "d_2"], read.Drafts.Select(draft => draft.Id));
        Draft draft = read.Drafts[0];
        Assert.Equal(["pl_1", "pl_2", "pl_3"], draft.Places.Select(place => place.Id));
        Assert.Equal((DraftPart.Melody, "p_0f1e2d3c4b5a49687786a5b4c3d2e1f0"), (draft.Places[1].BaseVariant.From.Part, draft.Places[1].BaseVariant.From.Project));
        Assert.Equal(new string('a', 64), draft.Places[1].BaseVariant.Provenance.From[0].Sha256);
        Assert.Equal((72_000L, 84_500L), draft.Places[0].Find("v_2")!.From.Milliseconds);
        Assert.True(draft.Places[2].Bridge);
        Assert.Equal("легато", Assert.Single(draft.Places[2].Marks.Notes).Text);
        Assert.Equal([1, 2, 3, 4, 5], draft.Steps.Select(step => step.Number));
        Assert.Equal("assistant", draft.Steps[2].Origin);
        Assert.Equal(DraftMusic.Of(Worked()).Music.Notes, DraftMusic.Of(draft).Music.Notes);
    }

    [Fact]
    public void The_same_piece_with_the_same_drafts_always_gives_the_same_bytes()
    {
        Assert.Equal(ProjectFile.Write(WithDrafts()), ProjectFile.Write(WithDrafts()));
        Assert.Equal(ProjectFile.Write(WithDrafts()), ProjectFile.Write(ProjectFile.Read(ProjectFile.Write(WithDrafts()))));
    }

    [Fact]
    public void A_piece_with_no_drafts_writes_none_and_a_version_16_file_reads_with_none()
    {
        JsonObject file = Node(ProjectFile.Write(Piece()));
        Assert.False(file["workspace"]!.AsObject().ContainsKey("drafts"));

        file["version"] = 16;
        Assert.Empty(ProjectFile.Read(Bytes(file)).Drafts);
    }

    [Theory]
    [InlineData("drafts/0/steps/0/technique/kind")]
    [InlineData("drafts/0/steps/1/kind")]
    [InlineData("drafts/0/places/1/variants/0/from/part")]
    [InlineData("drafts/0/places/0/variants/1/music/notes/0/hand")]
    [InlineData("drafts/0/places/0/variants/0/music/notes/0/attribution/provenance")]
    [InlineData("drafts/0/places/0/variants/0/music/keys/0/mode")]
    public void A_value_this_build_does_not_know_keeps_the_whole_draft_unknown_and_written_back_where_it_stood(string path)
    {
        JsonObject file = Node(GoldenFiles.Kept(Golden));
        Set(file, "workspace/" + path, "FutureValue");

        Project project = ProjectFile.Read(Bytes(file));

        Assert.Equal("d_2", Assert.Single(project.Drafts).Id);
        UnknownEntity kept = Assert.Single(project.Unknown);
        Assert.Equal((Project.DraftsList, 0, "d_1"), (kept.List, kept.Index, kept.Id));
        Assert.True(JsonNode.DeepEquals(file, Node(ProjectFile.Write(project))), "the file is written back whole, the unknown draft in its place");
        Assert.Contains("d_1", project.DraftIds);
        Assert.Throws<ArgumentException>(() => project.WithNewDraft(Draft.Start("d_1", "x", At, "pl_1", Variant("v_1", Score(), 1, 1))));
    }

    [Theory]
    [InlineData("drafts/0/steps/0/technique/kind", "12")]
    [InlineData("drafts/1/id", "d_1")]
    [InlineData("drafts/0/places/0/base", "v_9")]
    [InlineData("drafts/0/steps/0/place", "pl_9")]
    public void A_draft_that_does_not_hold_together_is_damage(string path, string value)
    {
        JsonObject file = Node(GoldenFiles.Kept(Golden));
        Set(file, "workspace/" + path, value);

        ProjectFileException refused = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Bytes(file)));

        Assert.StartsWith("This project file is damaged", refused.Message);
    }

    [Fact]
    public void In_the_sync_drafts_only_the_other_copy_has_come_in()
    {
        Project here = Piece().WithNewDraft(Worked());
        Project online = Piece().WithNewDraft(Draft.Start("d_2", "Кода", At, "pl_1", Variant("v_1", Score(), 8, 8)));

        SyncResolution resolved = ProjectSync.Resolve(here, At.AddMinutes(5), online, At, CopyTitle, "p_copy");

        Assert.Equal(["d_1", "d_2"], resolved.Kept.Drafts.Select(draft => draft.Id));
        Assert.Null(resolved.PreviousCopy);
    }

    [Fact]
    public void The_same_draft_changed_on_both_copies_keeps_the_last_saved_and_the_other_in_the_previous_copy()
    {
        Project @base = Piece().WithNewDraft(Worked());
        Project here = @base.ChangeDraft("d_1", draft => draft.Renamed("Здесь"));
        Project online = @base.ChangeDraft("d_1", draft => draft.Renamed("Там"));

        SyncResolution resolved = ProjectSync.Resolve(here, At, online, At.AddMinutes(5), CopyTitle, "p_copy", null, @base);

        Assert.Equal("Там", resolved.Kept.FindDraft("d_1")!.Name);
        Assert.Equal("Здесь", resolved.PreviousCopy!.FindDraft("d_1")!.Name);
    }

    [Fact]
    public void A_draft_changed_on_one_side_only_since_the_base_takes_that_side()
    {
        Project @base = Piece().WithNewDraft(Worked());
        Project here = @base.ChangeDraft("d_1", draft => draft.Renamed("Здесь")).WithNewDraft(Draft.Start("d_3", "Новый", At, "pl_1", Variant("v_1", Score(), 2, 2)));

        SyncResolution resolved = ProjectSync.Resolve(here, At, @base, At.AddMinutes(5), CopyTitle, "p_copy", null, @base);

        Assert.Equal("Здесь", resolved.Kept.FindDraft("d_1")!.Name);
        Assert.NotNull(resolved.Kept.FindDraft("d_3"));
    }

    [Fact]
    public void With_the_base_a_draft_removed_on_one_side_goes_and_without_it_it_comes_back()
    {
        Project @base = Piece().WithNewDraft(Worked());
        Project here = @base.WithoutDraft("d_1");

        Project withBase = ProjectSync.Resolve(here, At, @base, At.AddMinutes(5), CopyTitle, "p_copy", null, @base).Kept;
        Project withoutBase = ProjectSync.Resolve(here, At, @base, At.AddMinutes(5), CopyTitle, "p_copy").Kept;
        Project removedOnline = ProjectSync.Resolve(@base, At.AddMinutes(5), here, At, CopyTitle, "p_copy", null, @base).Kept;

        Assert.Empty(withBase.Drafts);
        Assert.Empty(removedOnline.Drafts);
        Assert.Equal("d_1", Assert.Single(withoutBase.Drafts).Id);
    }

    private static void Set(JsonObject root, string path, string value)
    {
        string[] parts = path.Split('/');
        JsonNode node = root;
        foreach (string part in parts[..^1])
        {
            node = int.TryParse(part, out int index) ? node[index]! : node[part]!;
        }

        node[parts[^1]] = value;
    }
}