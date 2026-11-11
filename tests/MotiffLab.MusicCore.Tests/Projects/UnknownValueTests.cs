using System.Text;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// The tolerant reader for enum values (decision record 0026, S6; #313): what is kept as unknown, what goes with it, what
/// is still damage, and how an app's changes meet the entities it never sees. The file is the version 15 golden.
/// </summary>
public class UnknownValueTests
{
    private const string UnknownValue = "FutureValue";

    private static readonly DateTimeOffset s_now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static JsonObject Worked() =>
        JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", "v15-worked.motiff.json")))!.AsObject();

    private static byte[] Bytes(JsonNode file) => Encoding.UTF8.GetBytes(file.ToJsonString());

    private static JsonObject Node(byte[] bytes) => JsonNode.Parse(bytes)!.AsObject();

    private static JsonObject With(JsonObject file, string path, JsonNode? value)
    {
        string[] parts = path.Split('/');
        JsonNode node = file;
        foreach (string part in parts[..^1])
        {
            node = int.TryParse(part, out int index) ? node[index]! : node[part]!;
        }

        node[parts[^1]] = value;
        return file;
    }

    private static string[] Ids(JsonNode file, string list) => [.. file[list]!.AsArray().Select(entity => entity!["id"]!.GetValue<string>())];

    [Fact]
    public void In_a_file_of_an_older_version_an_unknown_value_is_still_damage()
    {
        // The builds that wrote version 14 knew no value this one does not: an unknown one there is not from a later build.
        JsonObject file = With(With(Worked(), "version", 14), "motifs/0/mode", UnknownValue);

        ProjectFileException refused = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Bytes(file)));

        Assert.Equal("This project file is damaged: its mode cannot be read.", refused.Message);
    }

    [Theory]
    [InlineData("Future value")]
    [InlineData("9")]
    [InlineData("_Future")]
    [InlineData("Ünknown")]
    public void A_value_that_is_not_a_name_is_still_damage(string value)
    {
        JsonObject file = With(Worked(), "motifs/0/mode", value);

        ProjectFileException refused = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Bytes(file)));

        Assert.Equal("This project file is damaged: its mode cannot be read.", refused.Message);
    }

    [Fact]
    public void An_unknown_setting_or_variation_operation_is_still_refused_as_before()
    {
        // Criterion 4 of #313: settings and operations are the next task.
        JsonObject value = With(Worked(), "settings/phrases.granularity", UnknownValue);
        JsonObject setting = With(Worked(), "settings/future.setting", "on");
        JsonObject operation = With(Worked(), "variations/1/steps/0/operation", "V99");

        Assert.Equal("This project file is damaged: its settings cannot be read.", Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Bytes(value))).Message);
        Assert.Equal("This project file is damaged: its settings cannot be read.", Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Bytes(setting))).Message);
        Assert.Equal("A variation uses an operation (V99) this version of Motiff Lab does not have.", Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Bytes(operation))).Message);
    }

    [Fact]
    public void A_motif_and_its_variations_are_left_unknown_together()
    {
        // Variation v_0004 holds an unknown accent pattern; its motif m_0001 has four other variations.
        JsonObject file = With(Worked(), "variations/3/steps/0/parameters/pattern", UnknownValue);

        Project project = ProjectFile.Read(Bytes(file));
        OpenedProject opened = OpenedProject.Open(project);

        Assert.Equal(["m_0002", "m_0003"], opened.Motifs.Select(motif => motif.Id));
        Assert.Equal(6, opened.Variations.Count);
        Assert.DoesNotContain(project.Variations, variation => variation.MotifId == "m_0001");
        UnknownEntity motif = Assert.Single(project.Unknown, entity => entity.List == "motifs");
        Assert.Equal(("m_0001", 0, (string?)null), (motif.Id, motif.Index, motif.Enum));
        Assert.Equal(
            [("v_0001", (string?)null), ("v_0002", null), ("v_0003", null), ("v_0004", "AccentPattern"), ("v_0005", null)],
            project.Unknown.Where(entity => entity.List == "variations").Select(entity => (entity.Id!, entity.Enum)));
        Assert.All(project.Unknown.Where(entity => entity.List == "variations"), variation => Assert.Equal("m_0001", variation.Owner));
        Assert.Equal((2, 6), (project.Motifs.Count, project.Variations.Count));
        Assert.True(JsonNode.DeepEquals(file, Node(ProjectFile.Write(project))));
    }

    [Fact]
    public void A_motif_an_app_makes_in_the_place_of_an_unknown_one_replaces_it_with_its_variations()
    {
        // The facade keeps its working motif as m_0001 when no motif opens; saving it takes the unknown one's place.
        JsonObject file = With(Worked(), "motifs/0/mode", UnknownValue);
        Project project = ProjectFile.Read(Bytes(file));
        SavedMotif working = project.Motifs[0] with { Id = "m_0001", Name = "New working motif" };

        JsonObject written = Node(ProjectFile.Write(project with { Motifs = [working, .. project.Motifs] }));

        Assert.Equal(["m_0001", "m_0002", "m_0003"], Ids(written, "motifs"));
        Assert.Equal("New working motif", written["motifs"]![0]!["name"]!.GetValue<string>());
        Assert.Equal(["v_0006", "v_0007", "v_0008", "v_0009", "v_0010", "v_0011"], Ids(written, "variations"));
        Assert.DoesNotContain(UnknownValue, written.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_entity_keeps_its_place_among_the_ones_an_app_adds()
    {
        JsonObject file = With(Worked(), "marks/ink/0/tool", UnknownValue);
        Project project = ProjectFile.Read(Bytes(file));
        Assert.Equal(["k2"], project.Marks.Ink.Select(stroke => stroke.Id));

        InkStroke two = project.Marks.Ink[0];
        Project drawn = project.ChangeMarks(marks => marks.AddInk([new InkStroke("k3", two.Tool, two.Colour, two.Size, two.Bar, two.U, two.V, two.Points, two.Pressure)]));
        JsonObject written = Node(ProjectFile.Write(drawn));

        Assert.Equal(["k1", "k2", "k3"], written["marks"]!["ink"]!.AsArray().Select(stroke => stroke!["id"]!.GetValue<string>()));
        Assert.Equal(UnknownValue, written["marks"]!["ink"]![0]!["tool"]!.GetValue<string>());
        Assert.Equal(0, Assert.Single(drawn.Marks.Unknown).Index);
    }

    [Fact]
    public void A_bookmark_in_an_unknown_category_is_kept_with_it_and_new_ids_pass_both_by()
    {
        JsonObject file = With(With(Worked(), "bookmarks/categories/4/ink", UnknownValue), "bookmarks/items/0/category", "c_0001");

        Project project = ProjectFile.Read(Bytes(file));

        Assert.DoesNotContain(project.Categories, category => category.Id == "c_0001");
        Assert.Empty(project.Bookmarks);
        UnknownEntity bookmark = Assert.Single(project.Unknown, entity => entity.List == "bookmarks.items");
        Assert.Equal(("b_0001", "c_0001", (string?)null), (bookmark.Id, bookmark.Owner, bookmark.Enum));
        Assert.Equal("c_0002", project.NextCategoryId());
        Assert.Equal("b_0002", project.NextBookmarkId());
        Assert.True(JsonNode.DeepEquals(file, Node(ProjectFile.Write(project))));
    }

    [Fact]
    public void A_built_in_category_with_an_unknown_ink_is_written_back_and_not_taken_by_the_built_in()
    {
        JsonObject file = With(Worked(), "bookmarks/categories/0/ink", UnknownValue);

        Project project = ProjectFile.Read(Bytes(file));
        JsonObject written = Node(ProjectFile.Write(project));

        Assert.DoesNotContain(project.Categories, category => category.Id == BookmarkCategory.PractiseId);
        Assert.Equal(["practise", "performance", "structure", "teacher", "c_0001"], Ids(written["bookmarks"]!, "categories"));
        Assert.True(JsonNode.DeepEquals(file, written));
    }

    [Fact]
    public void New_resume_point_idea_and_highlight_ids_pass_the_unknown_ones_by()
    {
        JsonObject file = With(With(With(Worked(), "resumePoints/0/hands", UnknownValue), "observations/0/phrase/filter/hand", UnknownValue), "marks/highlights/0/colour", UnknownValue);

        Project project = ProjectFile.Read(Bytes(file));

        Assert.Empty(project.ResumePoints);
        Assert.Empty(project.Observations);
        Assert.Empty(project.Marks.Highlights);
        Assert.Equal("r_0002", project.NextResumePointId());
        Assert.Equal("o_0002", project.NextObservationId());
        Assert.Equal("h_0002", project.Marks.NextHighlightId());
        Assert.Equal(["Hand", "Hand"], project.Unknown.Select(entity => entity.Enum));
        Assert.Equal("InkColour", Assert.Single(project.Marks.Unknown).Enum);
    }

    [Fact]
    public void A_pdf_pieces_unknown_stroke_is_written_back_though_its_marks_are_otherwise_empty()
    {
        byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");
        var stroke = new InkStroke("k1", InkTool.Pen, InkColour.Graphite, 0.16, new Tick(1), 0.5, 1, [], 0.5);
        Project piece = Project.CreateDocument("p_pdf", "a.pdf", pdf, s_now) with { DocumentMarks = DocumentMarks.Empty.WithInk([stroke]) };
        JsonObject file = With(Node(ProjectFile.Write(piece)), "documentMarks/ink/0/tool", "eraser");

        Project project = ProjectFile.Read(Bytes(file));

        Assert.True(project.DocumentMarks.IsEmpty);
        Assert.Equal(("documentMarks.ink", "InkTool"), (Assert.Single(project.DocumentMarks.Unknown).List, project.DocumentMarks.Unknown[0].Enum));
        Assert.True(JsonNode.DeepEquals(file, Node(ProjectFile.Write(project))));
    }

    [Fact]
    public void A_transcription_with_unknown_entities_keeps_its_bars()
    {
        // The unknown entities are kept by their place in the bars, like the player's own work (Project.CanChangeBars).
        Project heard = ProjectFile.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", "v12-paterlini-all-my-joy.motiff.json"))) with { Motifs = [], Variations = [] };
        Assert.True(heard.CanChangeBars);

        Assert.False((heard with { Unknown = [new UnknownEntity("check", 0, null, null, "{}")] }).CanChangeBars);
        Assert.False(heard.ChangeMarks(marks => marks with { Unknown = [new UnknownEntity("marks.ink", 0, "k1", null, "{}")] }).CanChangeBars);
    }
}