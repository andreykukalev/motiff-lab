using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// Sync 0.2 (Andrei, 2026-11-11): new pieces and their changes go between the devices by themselves; when both changed one,
/// the last save wins and the previous copy is kept beside, so nothing is lost. A PDF stays on the device (0026 C3).
/// </summary>
public class ProjectSyncTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    private const string CopyTitle = "{0} (previous copy, 11 Nov 20:40)";

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name));

    private static Project FurElise() => Project.Create("p_0123456789abcdef0123456789abcdef", "fur-elise.musicxml", Fixture("fur-elise-original.musicxml"), s_now);

    private static Project Tagged(Project project, params string[] tags) =>
        project with { Metadata = new PieceMetadata(project.Metadata.Title, project.Metadata.Composer, tags, project.Metadata.Playlists) };

    private static SyncStep Next(bool here, bool online, bool known, bool changedHere = false, bool changedOnline = false, bool deletedOnline = false) =>
        ProjectSync.Next(new SyncFacts(here, online, known, changedHere, changedOnline, deletedOnline));

    [Fact]
    public void What_changed_on_one_side_goes_to_the_other_and_a_change_on_both_is_resolved()
    {
        Assert.Equal(SyncStep.Nothing, Next(here: true, online: true, known: true));
        Assert.Equal(SyncStep.Upload, Next(here: true, online: true, known: true, changedHere: true));
        Assert.Equal(SyncStep.Download, Next(here: true, online: true, known: true, changedOnline: true));
        Assert.Equal(SyncStep.Resolve, Next(here: true, online: true, known: true, changedHere: true, changedOnline: true));
        // Two copies that never met (an old download, or one piece saved on two devices before sync) are compared.
        Assert.Equal(SyncStep.Resolve, Next(here: true, online: true, known: false, changedHere: true));
    }

    [Fact]
    public void A_new_piece_goes_online_and_one_new_online_comes_here()
    {
        Assert.Equal(SyncStep.Upload, Next(here: true, online: false, known: false, changedHere: true));
        Assert.Equal(SyncStep.Download, Next(here: false, online: true, known: false));
    }

    [Fact]
    public void A_deletion_is_never_repeated_and_a_change_after_it_brings_the_piece_back()
    {
        // Deleted online on another device: kept here, not sent again unless it changes here.
        Assert.Equal(SyncStep.Forget, Next(here: true, online: false, known: true));
        Assert.Equal(SyncStep.Nothing, Next(here: true, online: false, known: true, deletedOnline: true));
        Assert.Equal(SyncStep.Upload, Next(here: true, online: false, known: true, changedHere: true, deletedOnline: true));
        // Deleted here: left online for the other devices, and fetched again only when it changes there.
        Assert.Equal(SyncStep.Nothing, Next(here: false, online: true, known: true));
        Assert.Equal(SyncStep.Download, Next(here: false, online: true, known: true, changedOnline: true));
        Assert.Equal(SyncStep.Forget, Next(here: false, online: false, known: true));
    }

    [Fact]
    public void The_last_save_wins_and_the_other_copy_is_kept_beside_with_a_new_id()
    {
        Project online = Tagged(FurElise(), "Exam");
        Project here = Tagged(FurElise(), "Recital");

        SyncResolution hereWins = ProjectSync.Resolve(here, s_now.AddMinutes(5), online, s_now, CopyTitle, "p_copy");
        Assert.Same(here, hereWins.Kept);
        Assert.True(hereWins.Upload);
        Assert.Equal("p_copy", hereWins.PreviousCopy!.Id);
        Assert.Equal(["Exam"], hereWins.PreviousCopy.Metadata.Tags);
        Assert.Equal("Fur Elise, opening (previous copy, 11 Nov 20:40)", hereWins.PreviousCopy.Details.Title.Value);
        Assert.Equal(online.Source, hereWins.PreviousCopy.Source);

        SyncResolution onlineWins = ProjectSync.Resolve(here, s_now, online, s_now.AddMinutes(5), CopyTitle, "p_copy");
        Assert.Equal(["Exam"], onlineWins.Kept.Metadata.Tags);
        Assert.Equal(here.Id, onlineWins.Kept.Id);
        Assert.False(onlineWins.Upload);
        Assert.Equal(["Recital"], onlineWins.PreviousCopy!.Metadata.Tags);
        Assert.Equal("p_copy", onlineWins.PreviousCopy.Id);
    }

    [Fact]
    public void Two_copies_that_are_the_same_keep_nothing_beside()
    {
        Project here = Tagged(FurElise(), "Exam");
        SyncResolution same = ProjectSync.Resolve(here, s_now, ProjectFile.Read(ProjectFile.Write(here)), s_now.AddDays(1), CopyTitle, "p_copy");
        Assert.Same(here, same.Kept);
        Assert.False(same.Upload);
        Assert.Null(same.PreviousCopy);
    }

    [Fact]
    public void A_different_piece_with_the_same_old_id_is_kept_beside_as_it_is_and_this_one_goes_online()
    {
        // V8: two devices once numbered their pieces p_0001, p_0002…; another piece's copy is no previous copy of this one.
        Project here = Project.Create("p_0001", "fur-elise.musicxml", Fixture("fur-elise-original.musicxml"), s_now);
        Project other = Project.Create("p_0001", "other.musicxml", Fixture("fur-elise-original.musicxml"), s_now.AddDays(-3));

        SyncResolution resolved = ProjectSync.Resolve(here, s_now, other, s_now.AddDays(1), CopyTitle, "p_beside");
        Assert.Same(here, resolved.Kept);
        Assert.True(resolved.Upload);
        Assert.Equal("p_beside", resolved.PreviousCopy!.Id);
        Assert.Equal(other.Metadata, resolved.PreviousCopy.Metadata);
        Assert.Equal(other.FileName, resolved.PreviousCopy.FileName);
    }

    [Fact]
    public void The_pdf_its_pages_and_marks_stay_here_when_the_online_copy_comes()
    {
        Project notes = FurElise();
        Project withPdf = notes.WithDocument(s_pdf, PageMap.Of(PieceImporter.Import("fur-elise.musicxml", Fixture("fur-elise-original.musicxml")).Piece!, 1)) with { DocumentMarks = DocumentMarks.Empty.AddBookmark(1, "Start", s_now) };
        Project online = Tagged(withPdf.ForOnline()!, "Exam");
        Assert.Null(online.Document);

        Project pulled = ProjectSync.Pulled(online, withPdf);
        Assert.Equal(s_pdf, pulled.Document);
        Assert.Equal(withPdf.DocumentMarks, pulled.DocumentMarks);
        Assert.Equal(withPdf.DocumentPages, pulled.DocumentPages);
        Assert.Equal(["Exam"], pulled.Metadata.Tags);
        Assert.Same(online, ProjectSync.Pulled(online, null));

        // Only the online copy without the PDF differs, so the two are the same piece as it is.
        Assert.Null(ProjectSync.Resolve(withPdf, s_now, withPdf.ForOnline()!, s_now.AddDays(1), CopyTitle, "p_copy").PreviousCopy);
    }

    [Fact]
    public void The_ipad_syncs_through_the_facade_with_project_files_as_text()
    {
        var facade = new MusicCoreFacade();
        string Text(Project project) => Encoding.UTF8.GetString(ProjectFile.Write(project));
        JsonElement Call(string name, object request)
        {
            JsonElement answer = JsonDocument.Parse(facade.Call(0, name, JsonSerializer.Serialize(request))).RootElement;
            Assert.False(answer.TryGetProperty("error", out JsonElement error), $"{name}: {error}");
            return answer;
        }

        Assert.Equal("resolve", Call("project.sync", new { here = true, online = true, known = true, changedHere = true, changedOnline = true }).GetProperty("step").GetString());
        Assert.Equal("forget", Call("project.sync", new { here = true, known = true }).GetProperty("step").GetString());

        Project pdf = Project.CreateDocument("p_pdf", "a.pdf", s_pdf, s_now);
        Assert.Equal(JsonValueKind.Null, Call("project.online", new { file = Text(pdf) }).GetProperty("file").ValueKind);
        Assert.Equal(Text(FurElise()), Call("project.online", new { file = Text(FurElise()) }).GetProperty("file").GetString());

        JsonElement resolved = Call("project.resolve", new
        {
            here = Text(Tagged(FurElise(), "Recital")),
            savedHere = "2026-11-11T09:00:00Z",
            online = Text(Tagged(FurElise(), "Exam")),
            savedOnline = "2026-11-11T09:05:00Z",
            copyTitle = CopyTitle,
        });
        Assert.False(resolved.GetProperty("upload").GetBoolean());
        Project kept = ProjectFile.Read(Encoding.UTF8.GetBytes(resolved.GetProperty("file").GetString()!));
        Project copy = ProjectFile.Read(Encoding.UTF8.GetBytes(resolved.GetProperty("copy").GetString()!));
        Assert.Equal(["Exam"], kept.Metadata.Tags);
        Assert.Equal(["Recital"], copy.Metadata.Tags);
        Assert.Matches("^p_[0-9a-f]{32}$", copy.Id);

        string pulled = Call("project.pulled", new { file = Text(FurElise()), here = (string?)null }).GetProperty("file").GetString()!;
        Assert.Equal(Text(FurElise()), pulled);
    }
}