using System.Text;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// The «Ноты крупнее» step in the project file (version 1.7, project file 19, the score-reading spec 4.3;
/// docs/project-file.md, "Workspace"): "workspace"."largerNotes", left out at 0, brought into 0 to 10 on reading.
/// </summary>
public class LargerNotesFormatTests
{
    private static JsonObject Worked() => JsonNode.Parse(GoldenFiles.Kept("v19-worked.motiff.json"))!.AsObject();

    private static Project Read(JsonObject file) => ProjectFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString()));

    private static JsonObject Written(Project project) => JsonNode.Parse(ProjectFile.Write(project))!.AsObject();

    [Fact]
    public void The_step_is_written_in_the_workspace_before_the_motifs_and_reads_back_the_same()
    {
        Project project = Read(Worked()) with { LargerNotes = 7 };

        JsonObject workspace = Written(project)["workspace"]!.AsObject();

        Assert.Equal(7, workspace["largerNotes"]!.GetValue<int>());
        List<string> fields = [.. workspace.Select(static field => field.Key)];
        Assert.Equal(fields.IndexOf("motifs") - 1, fields.IndexOf("largerNotes"));
        Assert.Equal(7, ProjectFile.Read(ProjectFile.Write(project)).LargerNotes);
        Assert.Equal(19, Written(project)["version"]!.GetValue<int>());
    }

    [Fact]
    public void At_step_0_the_file_has_no_step_and_the_piece_shows_the_a4_layout()
    {
        Project project = Read(Worked());

        Assert.Equal(0, project.LargerNotes);
        Assert.False(Written(project)["workspace"]!.AsObject().ContainsKey("largerNotes"));
        Assert.False(Written(project with { LargerNotes = 3 } with { LargerNotes = 0 })["workspace"]!.AsObject().ContainsKey("largerNotes"));
    }

    /// <summary>A whole number outside 0 to 10 is brought into it on reading: a later build's larger step reads as the largest here.</summary>
    [Theory]
    [InlineData(-3, 0)]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    [InlineData(25, 10)]
    [InlineData(1e20, 10)]
    public void A_step_out_of_range_is_clamped_on_reading(double step, int read)
    {
        JsonObject file = Worked();
        file["workspace"]!["largerNotes"] = step;

        Assert.Equal(read, Read(file).LargerNotes);
    }

    [Theory]
    [InlineData("2.5")]
    [InlineData("\"big\"")]
    [InlineData("null")]
    [InlineData("true")]
    public void A_step_that_is_not_a_whole_number_makes_the_file_damaged(string value)
    {
        JsonObject file = Worked();
        file["workspace"]!["largerNotes"] = JsonNode.Parse(value);

        ProjectFileException refused = Assert.Throws<ProjectFileException>(() => Read(file));

        Assert.Equal("This project file is damaged: its largerNotes cannot be read.", refused.Message);
    }

    /// <summary>A file of version 18 or earlier has no step: it opens at 100 %, as the build that wrote it showed it.</summary>
    [Theory]
    [InlineData("v18-worked.motiff.json")]
    [InlineData("v17-worked.motiff.json")]
    [InlineData("v15-worked.motiff.json")]
    public void A_file_before_version_19_reads_with_no_step(string golden) =>
        Assert.Equal(0, ProjectFile.Read(GoldenFiles.Kept(golden)).LargerNotes);

    /// <summary>A choice of how the piece is shown, as whether the corrections are off: in a merge the copy saved last says it, never a conflict.</summary>
    [Fact]
    public void The_step_is_bookkeeping_for_the_sync() =>
        Assert.Equal(FieldClass.Bookkeeping, MergeRules.Class("workspace.largerNotes"));

    [Fact]
    public void The_steps_go_by_ten_per_cent_from_the_a4_layout_to_twice_its_size()
    {
        Assert.Equal((100, 170, 200), (LargerNotes.Percent(0), LargerNotes.Percent(7), LargerNotes.Percent(LargerNotes.MaxStep)));
        Assert.True(LargerNotes.IsValid(0) && LargerNotes.IsValid(10));
        Assert.False(LargerNotes.IsValid(-1) || LargerNotes.IsValid(11));
    }
}