using System.Text;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// A piece whose only original is a recording (version 1.1, finding 29; project file version 17): added free, on the
/// device, with no notes until the player recognises it. Version 17 lets such a piece be; a file of an earlier version
/// with no notes and no PDF stays damaged.
/// </summary>
public class RecordingPieceFormatTests
{
    private const string Golden = "v18-recording.motiff.json";

    private static readonly DateTimeOffset s_now = new(2026, 11, 19, 23, 30, 0, TimeSpan.Zero);

    private static readonly string s_sha = PieceOriginal.Fingerprint("the sound of a take"u8);

    private static Project Recording() =>
        Project.CreateRecording("p_9c1d2e3f4a5b4c6d8e7f0a1b2c3d4e5f", "Etude in E.m4a", 183_500, s_sha, 2_940_000, HoldingDevice.Ipad, s_now);

    [Fact]
    public void A_recording_added_as_it_is_is_a_piece_with_no_notes_whose_only_original_is_that_recording_on_the_device()
    {
        Project project = Recording();

        Assert.False(project.HasNotes);
        Assert.Null(project.Document);
        Assert.True(project.IsOnlyRecording);
        PieceOriginal original = Assert.Single(project.Originals);
        Assert.Equal((PieceOriginal.RecordingId, OriginalKind.Audio, "Etude in E.m4a", OriginalKept.Device), (original.Id, original.Kind, original.FileName, original.Kept));
        Assert.Equal((183_500L, s_sha, 2_940_000L), (original.DurationMs, original.Sha256, original.Bytes));
        Assert.True(original.IsHeldBy(HoldingDevice.Ipad));
        Assert.Equal("Etude in E", project.Details.Title.Value);
        Assert.Equal(PieceOrigin.Audio, project.Summary.Origin);
        Assert.Equal("the recording, no notes yet", PieceSources.Of(project).BaseName(Words.For("en")));
    }

    [Fact]
    public void The_file_written_is_the_golden_of_version_17_and_reads_back_as_the_same_piece()
    {
        GoldenFiles.AssertIs(Golden, ProjectFile.Write(Recording()), ["v17-recording.motiff.json"]);

        Project read = ProjectFile.Read(GoldenFiles.Kept(Golden));
        Assert.True(read.IsOnlyRecording);
        Assert.Equal(Recording().Originals, read.Originals);
        Assert.Empty(JsonNode.Parse(GoldenFiles.Kept(Golden))!["notes"]!.AsArray());
    }

    /// <summary>The builds before 1.1 knew no piece that is only a recording: a version 16 file of one is damaged, as it was.</summary>
    [Fact]
    public void The_same_piece_in_a_version_16_file_is_damaged()
    {
        JsonObject file = JsonNode.Parse(GoldenFiles.Kept(Golden))!.AsObject();
        file["version"] = 16;

        ProjectFileException refused = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString())));

        Assert.StartsWith("This project file is damaged", refused.Message);
    }

    [Fact]
    public void A_file_of_version_17_with_no_notes_no_pdf_and_no_recording_is_damaged()
    {
        JsonObject file = JsonNode.Parse(GoldenFiles.Kept(Golden))!.AsObject();
        file["originals"] = new JsonArray();

        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString())));
    }

    [Fact]
    public void The_only_recording_of_a_piece_with_no_notes_is_removed_with_the_piece_and_not_on_its_own()
    {
        Project project = Recording();

        Assert.Throws<InvalidOperationException>(() => project.RemoveOriginal(PieceOriginal.RecordingId, s_now));

        Project two = project.AddOriginal(new PieceOriginal(project.NextOriginalId(PieceOriginal.RecordingId, IdSource.Numbered), OriginalKind.Audio, "Take 2.m4a", OriginalKept.Device, s_now), null);
        Project removed = two.RemoveOriginal(PieceOriginal.RecordingId, s_now);
        Assert.True(removed.IsOnlyRecording);
        Assert.Equal(removed.Originals, ProjectFile.Read(ProjectFile.Write(removed)).Originals);
    }
}