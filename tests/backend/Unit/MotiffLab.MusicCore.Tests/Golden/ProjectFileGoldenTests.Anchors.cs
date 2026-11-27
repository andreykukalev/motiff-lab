using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Golden;

/// <summary>
/// The phrase anchors in the file (#318, 0026 C1): written in "phrases.anchors" only while something is attached, an idea's
/// and a resume point's "anchor" naming one; a file without them reads as before.
/// </summary>
public partial class ProjectFileGoldenTests
{
    /// <summary>What today's writer makes of <c>v18-worked.motiff.json</c> with its idea and its resume point in phrase focus attached to one anchor.</summary>
    private const string Anchored = "v18-anchors.motiff.json";

    private static Project WithAnchor(out PhraseAnchor anchor)
    {
        Project worked = ProjectFile.Read(File(Worked));
        anchor = PhraseAnchor.Mint(TickSpan.FromBounds(new Tick(3_360), new Tick(4_800)), []);
        string id = anchor.Id;
        return worked with
        {
            MintedAnchors = [anchor],
            Observations = [.. worked.Observations.Select(observation => observation with { Anchor = id })],
            ResumePoints = [.. worked.ResumePoints.Select(point => point with { Anchor = id })],
        };
    }

    [Fact]
    public void Anchors_are_written_with_what_names_them_and_read_back_the_same()
    {
        Project anchored = WithAnchor(out PhraseAnchor anchor);

        AssertWritten(ProjectFile.Write(anchored), Anchored);
        Project read = ProjectFile.Read(File(Anchored));
        Assert.Equal([anchor], read.Anchors);
        Assert.Equal(anchor.Id, read.Observations[0].Anchor);
        Assert.Equal(anchor.Id, read.ResumePoints[0].Anchor);
        Assert.Equal(Text(File(Anchored)), Text(ProjectFile.Write(read)));
    }

    [Fact]
    public void An_anchor_nothing_names_is_not_written_and_one_a_later_build_s_entity_names_is_kept()
    {
        Project anchored = WithAnchor(out PhraseAnchor anchor);
        Project loose = anchored with { Observations = [], ResumePoints = [] };

        Assert.Empty(loose.Anchors);
        Assert.Null(Node(ProjectFile.Write(loose))["workspace"]!["phrases"]!["anchors"]);
        Project named = loose with { Unknown = [new UnknownEntity("observations", 0, "o_9", null, $$"""{"id": "o_9", "anchor": "{{anchor.Id}}"}""")] };
        Assert.Equal([anchor], named.Anchors);
    }

    [Fact]
    public void An_idea_or_a_resume_point_naming_an_anchor_the_file_lacks_is_attached_to_none()
    {
        JsonObject file = Node(File(Anchored));
        _ = file["workspace"]!["phrases"]!.AsObject().Remove("anchors");

        Project read = ProjectFile.Read(Bytes(file));

        Assert.Empty(read.Anchors);
        Assert.Null(read.Observations[0].Anchor);
        Assert.Null(read.ResumePoints[0].Anchor);
    }

    [Theory]
    [InlineData("""[{"id": "ph_1", "onset": 0, "duration": 480}, {"id": "ph_1", "onset": 480, "duration": 480}]""")]
    [InlineData("""[{"id": "", "onset": 0, "duration": 480}]""")]
    [InlineData("""[{"id": "ph_1", "onset": 0, "duration": 0}]""")]
    [InlineData("""{"id": "ph_1"}""")]
    public void Anchors_with_one_id_twice_a_blank_id_or_no_span_are_damage(string anchors)
    {
        JsonObject file = Node(File(Anchored));
        file["workspace"]!["phrases"]!["anchors"] = JsonNode.Parse(anchors);

        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Bytes(file)));
    }

    [Fact]
    public void An_anchor_is_minted_from_its_bounds_and_passes_by_an_id_one_minted_on_them_took()
    {
        TickSpan span = TickSpan.FromBounds(new Tick(3_360), new Tick(4_800));
        PhraseAnchor first = PhraseAnchor.Mint(span, []);

        Assert.Equal(first, PhraseAnchor.Mint(span, []));
        Assert.Matches("^ph_[0-9a-f]{32}$", first.Id);
        PhraseAnchor second = PhraseAnchor.Mint(span, [first.Id]);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(second, PhraseAnchor.Mint(span, [first.Id]));
        Assert.NotEqual(first.Id, PhraseAnchor.Mint(TickSpan.FromBounds(new Tick(3_360), new Tick(5_280)), []).Id);
    }
}