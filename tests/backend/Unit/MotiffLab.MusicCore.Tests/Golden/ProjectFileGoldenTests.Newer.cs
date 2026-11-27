using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Golden;

/// <summary>What today's reader does with a file of a later version (#314, #315): it opens read-only, or is refused.</summary>
public partial class ProjectFileGoldenTests
{
    /// <summary>
    /// Today's future file (#314, #315): the version 18 file with version 19 and "minReader" 18, an unknown field at the top
    /// level, in the workspace and in an entity of each workspace list a note piece keeps, an unknown per-piece setting and
    /// a variation with a step of an unknown operation.
    /// </summary>
    [Fact]
    public void The_future_file_is_the_version_18_file_with_version_19_unknown_fields_an_unknown_setting_and_an_unknown_operation() =>
        Assert.True(JsonNode.DeepEquals(Futured19(Node(File(Worked))), Node(File(Future))), "the future file is the version 18 file with exactly these changes");

    /// <summary>The changes <see cref="Future"/> makes to the version 18 file.</summary>
    internal static JsonObject Futured19(JsonObject file)
    {
        file["version"] = 19;
        file["minReader"] = 18;
        file["future"] = new JsonObject { ["note"] = "A field a later version adds." };
        JsonObject work = file["workspace"]!.AsObject();
        work["future"] = 1;
        foreach (string list in (string[])["motifs", "variations", "observations", "resumePoints"])
        {
            work[list]![0]!["future"] = $"A field a later version adds to {list}.";
        }

        work["bookmarks"]!["categories"]![0]!["future"] = 2;
        work["settings"]!["future.setting"] = "on";
        work["variations"]![1]!["steps"]![0]!["operation"] = "V99";
        return file;
    }

    /// <summary>
    /// Read-only opening (S6, #315), flipping council fact V3: a file of a later version whose "minReader" is this build's
    /// opens, every unknown field, setting and operation kept, and is written again as it was, its version and "minReader"
    /// the larger of its own and this build's; it is never saved (<see cref="ProjectFile.Save"/>).
    /// </summary>
    [Fact]
    public void The_future_file_opens_read_only_and_is_written_back_whole_but_never_saved()
    {
        Project project = ProjectFile.Read(File(Future));
        OpenedProject opened = OpenedProject.Open(project);

        Assert.True(project.IsReadOnly);
        Assert.Equal(new NewerFile(19, 18), project.Newer);
        Assert.True(JsonNode.DeepEquals(Node(File(Future)), Node(ProjectFile.Write(project))), "the future file is written back whole");
        Assert.True(ProjectFile.Read(ProjectFile.Write(project)).IsReadOnly);
        ProjectFileException refused = Assert.Throws<ProjectFileException>(() => ProjectFile.Save(project));
        Assert.True(refused.ReadOnly);
        Assert.StartsWith("Этот проект сохранён более новой версией Motiff Lab", refused.RussianMessage, StringComparison.Ordinal);
        // The variation of the unknown operation is kept whole with its motif, made by a later build; the others open.
        UnknownEntity newer = Assert.Single(project.Unknown, entity => entity.Enum == UnknownEntity.NewerOperation);
        Assert.Equal(("variations", 1), (newer.List, newer.Index));
        Assert.Equal(2, opened.Motifs.Count);
        Assert.False(project.SettingOverrides.ContainsKey("future.setting"));
    }

    /// <summary>A file whose "minReader" is above this build, or of a later version without one, is refused before any other field is read (#315 criterion 4).</summary>
    [Theory]
    [InlineData(19, 19)]
    [InlineData(19, null)]
    [InlineData(18, 19)]
    public void A_file_this_build_may_not_read_is_refused_as_saved_by_a_newer_version(int version, int? minReader)
    {
        JsonObject file = Node(File(Future));
        file["version"] = version;
        file["minReader"] = minReader;
        if (minReader is null)
        {
            file.Remove("minReader");
        }

        ProjectFileException refused = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Bytes(file)));

        Assert.Equal("This project was saved by a newer version of Motiff Lab. Update the app and try again.", refused.Message);
    }

    /// <summary>The future file made while version 17 was today's now has this version without "minReader", which every file of version 18 has: it is damaged.</summary>
    [Fact]
    public void The_future_file_of_version_17_now_has_this_version_without_min_reader_and_is_damaged()
    {
        ProjectFileException refused = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(File(Future17)));

        Assert.StartsWith("This project file is damaged", refused.Message);
    }
}