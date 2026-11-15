using System.Text;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// The envelope of a project file (0026 S2): what the API checks and lists, read without the musical content, from a file
/// of any version, a later one too.
/// </summary>
public class ProjectEnvelopeTests
{
    [Fact]
    public void The_envelope_gives_the_summary_a_full_read_gives_for_this_version_and_an_earlier_one()
    {
        Project project = ProjectFileTests.Worked();
        ProjectEnvelope envelope = ProjectFile.ReadEnvelope(ProjectFile.Write(project));

        Assert.Equal((project.Id, ProjectFile.Version), (envelope.Id, envelope.Version));
        ProjectSummary full = project.Summary;
        ProjectSummary row = envelope.Summary;
        Assert.Equal((full.Id, full.FileName, full.MotifCount, full.VariationCount, full.LastOpened, full.ResumeCount, full.IdeaCount), (row.Id, row.FileName, row.MotifCount, row.VariationCount, row.LastOpened, row.ResumeCount, row.IdeaCount));
        Assert.Equal((2, 2), (envelope.MotifCount, envelope.VariationCount));

        byte[] earlier = GoldenFiles.Kept("v15-worked.motiff.json");
        ProjectSummary read = ProjectFile.Read(earlier).Summary;
        ProjectEnvelope old = ProjectFile.ReadEnvelope(earlier);
        Assert.Equal(15, old.Version);
        Assert.Equal((read.MotifCount, read.VariationCount, read.ResumeCount, read.IdeaCount), (old.MotifCount, old.VariationCount, old.ResumeCount, old.IdeaCount));
    }

    [Fact]
    public void A_later_version_passes_and_a_damaged_envelope_does_not()
    {
        JsonObject file = JsonNode.Parse(ProjectFile.Write(ProjectFileTests.Worked()))!.AsObject();
        file["version"] = ProjectFile.Version + 1;
        file["workspace"]!["motifs"]![0]!["kind"] = "FutureValue";

        Assert.Equal(ProjectFile.Version + 1, ProjectFile.ReadEnvelope(Encoding.UTF8.GetBytes(file.ToJsonString())).Version);
        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString())));

        file.Remove("id");
        Assert.Throws<ProjectFileException>(() => ProjectFile.ReadEnvelope(Encoding.UTF8.GetBytes(file.ToJsonString())));
        Assert.Throws<ProjectFileException>(() => ProjectFile.ReadEnvelope("[]"u8));
        Assert.Throws<ProjectFileException>(() => ProjectFile.ReadEnvelope("not json"u8));
    }
}