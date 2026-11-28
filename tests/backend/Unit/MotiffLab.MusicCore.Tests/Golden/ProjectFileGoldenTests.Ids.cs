using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Golden;

/// <summary>A numbered project id re-keyed (0026 C4, #317): the encoding fixed by a golden, the old id kept in "formerIds".</summary>
public partial class ProjectFileGoldenTests
{
    /// <summary>What today's writer makes of <c>v19-edits-meters.motiff.json</c> with the numbered id "p_0003", re-keyed.</summary>
    private const string Rekeyed = "v19-rekeyed.motiff.json";

    /// <summary>The C4 id of that file: "p_" and the first 32 hex digits of sha256("p_0003|{createdAt}|{sha256 of its source}").</summary>
    private const string RekeyedId = "p_83a08b1319e2925df3c0cdd6aa0fff43";

    private static Project Numbered(string id = "p_0003")
    {
        JsonObject file = Node(File("v19-edits-meters.motiff.json"));
        file["id"] = id;
        return ProjectFile.Read(Bytes(file));
    }

    [Fact]
    public void A_numbered_id_is_rekeyed_to_the_hash_of_the_id_its_creation_and_its_source_the_old_one_kept()
    {
        Project rekeyed = Numbered().Rekeyed();

        Assert.Equal(RekeyedId, rekeyed.Id);
        Assert.Equal(["p_0003"], rekeyed.FormerIds);
        AssertWritten(ProjectFile.Write(rekeyed), Rekeyed);
        Project read = ProjectFile.Read(File(Rekeyed));
        Assert.Equal(RekeyedId, read.Id);
        Assert.Equal(["p_0003"], read.FormerIds);
        Assert.Same(read, read.Rekeyed());
        // What version 1.6 wrote of the same project, in version 18, reads and is written as this golden.
        Assert.Equal(Text(File(Rekeyed)), Text(ProjectFile.Write(ProjectFile.Read(File("v18-rekeyed.motiff.json")))));
    }

    [Fact]
    public void The_same_file_rekeyed_twice_gets_one_id_and_another_piece_with_its_numbered_id_another()
    {
        Assert.Equal(Numbered().Rekeyed().Id, Numbered().Rekeyed().Id);
        DateTimeOffset now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
        Project one = Project.Create("p_0003", "a.mid", [1, 2, 3], now), other = Project.Create("p_0003", "a.mid", [1, 2, 4], now);
        Assert.NotEqual(one.Rekeyed().Id, other.Rekeyed().Id);
        Assert.NotEqual(one.Rekeyed().Id, (one with { CreatedAt = now.AddSeconds(1) }).Rekeyed().Id);
        Assert.NotEqual(one.Rekeyed().Id, Numbered("p_0004").Rekeyed().Id);
    }

    [Fact]
    public void A_pdf_piece_is_rekeyed_by_its_pdf_alike_where_the_file_holds_it_and_where_it_only_describes_it()
    {
        byte[] pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF\n");
        Project held = Project.CreateDocument("p_0005", "a.pdf", pdf, new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero));
        Project described = held.ForOnline()!;

        Assert.Null(described.Document);
        Assert.Equal(held.Rekeyed().Id, described.Rekeyed().Id);
    }

    [Theory]
    [InlineData("p_9c3d4e5f6a7b4c8d9e0f1a2b3c4d5e6f")]
    [InlineData("p_pdf")]
    [InlineData("p_")]
    [InlineData("p_12a")]
    [InlineData("0003")]
    public void An_id_that_is_not_numbered_stays(string id)
    {
        Project project = Numbered(id);
        Assert.False(ProjectIds.IsNumbered(project.Id));
        Assert.Same(project, project.Rekeyed());
    }

    [Theory]
    [InlineData("""["p_0001", "p_0001"]""")]
    [InlineData("""[""]""")]
    [InlineData("""["p_0001", 2]""")]
    [InlineData("\"p_0001\"")]
    public void Former_ids_that_repeat_or_are_not_texts_are_damage(string former)
    {
        JsonObject file = Node(File("v19-edits-meters.motiff.json"));
        file["id"] = "p_0003";
        file["formerIds"] = JsonNode.Parse(former);

        Assert.Contains("formerIds", Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Bytes(file))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_former_id_that_names_the_piece_itself_is_dropped()
    {
        JsonObject file = Node(File("v19-edits-meters.motiff.json"));
        file["id"] = "p_0003";
        file["formerIds"] = JsonNode.Parse("""["p_0001", "p_0003", "p_0002"]""");

        Assert.Equal(["p_0001", "p_0002"], ProjectFile.Read(Bytes(file)).FormerIds);
    }
}