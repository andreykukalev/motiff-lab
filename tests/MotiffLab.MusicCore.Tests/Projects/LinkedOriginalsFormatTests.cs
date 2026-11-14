using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// The format step of «Импорт папки» within version 15 (version 0.5; Andrei, 2026-11-12 17:42 UTC): an original kept
/// "link" (its bytes stay in the player's folder, never in the file) and "folder", where it was inside the folder the
/// player imported. A piece whose PDF is linked has no notes and no PDF in its file, and is still a piece.
/// </summary>
public class LinkedOriginalsFormatTests
{
    private const string LinkedGolden = "v15-linked-pdf.motiff.json";
    private const string FolderGolden = "v15-folder-notes.motiff.json";

    private static readonly DateTimeOffset s_now = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    /// <summary>The PDF in the player's folder, as the app reads it in place.</summary>
    private static readonly byte[] s_pdf = "%PDF-1.4 Prelude in C, three pages"u8.ToArray();

    private static Project Linked() => Project.CreateLinkedDocument(
        "p_7a1b2c3d4e5f4a6b8c9d0e1f2a3b4c5d", "Bach - Prelude in C.pdf", "Бах/ХТК", PieceOriginal.Fingerprint(s_pdf), s_pdf.Length, 3, HoldingDevice.Ipad, s_now) with
    {
        Metadata = new PieceMetadata("Prelude in C", "Bach", ["Бах", "ХТК"], ["ХТК"]),
    };

    /// <summary>A MusicXML score imported from the folder, with the PDF of it beside it in the folder, linked.</summary>
    private static Project FromFolder()
    {
        byte[] score = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", "fur-elise-original.musicxml"));
        Project project = Project.Create("p_8b2c3d4e5f6a4b7c9d0e1f2a3b4c5d6e", "Für Elise.musicxml", score, s_now);
        PieceOriginal source = project.FindOriginal(PieceOriginal.SourceFileId)! with { Folder = "Бетховен", Sha256 = PieceOriginal.Fingerprint(score) };
        var pdf = new PieceOriginal(PieceOriginal.DocumentId, OriginalKind.Pdf, "Für Elise.pdf", OriginalKept.Link, s_now)
        {
            Folder = "Бетховен/PDF",
            Sha256 = PieceOriginal.Fingerprint(s_pdf),
            Bytes = s_pdf.Length,
            Pages = 3,
        };
        return project.WithOriginal(source).WithOriginal(pdf.HeldBy(HoldingDevice.Web, s_now));
    }

    private static Project Read(JsonNode file) => ProjectFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString()));

    private static string Text(byte[] bytes) => GoldenFiles.Text(bytes);

    private static byte[] Kept(string name) => GoldenFiles.Kept(name);

    [Theory]
    [InlineData(LinkedGolden)]
    [InlineData(FolderGolden)]
    public void The_files_version_0_5_writes_are_the_goldens_and_read_back_unchanged(string golden)
    {
        GoldenFiles.AssertIs(golden, ProjectFile.Write(golden == LinkedGolden ? Linked() : FromFolder()));
    }

    [Fact]
    public void A_linked_pdf_piece_has_no_notes_and_no_pdf_in_its_file_and_is_a_pdf_piece_of_the_library()
    {
        Project project = ProjectFile.Read(Kept(LinkedGolden));

        Assert.False(project.HasNotes);
        Assert.Null(project.Document);
        Assert.True(project.LinksDocument);
        PieceOriginal pdf = Assert.Single(project.Originals);
        Assert.Equal((OriginalKept.Link, "Бах/ХТК", 3, s_pdf.Length), (pdf.Kept, pdf.Folder, pdf.Pages, pdf.Bytes));
        Assert.False(project.HasInFile(pdf));
        Assert.Equal(PieceOrigin.Pdf, project.Summary.Origin);
        Assert.Equal(("Prelude in C", "Bach"), (project.Details.Title.Value, project.Details.Composer!.Value));
        Assert.Equal(OriginalWhere.Here, Assert.Single(project.OriginalSummaries).Where(HoldingDevice.Ipad, heldHere: true));
    }

    [Fact]
    public void The_bytes_of_a_linked_pdf_are_never_written_into_the_file_even_while_the_piece_is_open_with_them()
    {
        Project open = Linked().RelinkDocument(s_pdf, new PageMap([Tick.Zero]))!;
        Assert.NotNull(open.Document);

        JsonObject file = JsonNode.Parse(ProjectFile.Write(open))!.AsObject();

        Assert.False(file.ContainsKey("document"));
        Assert.False(file.ContainsKey("documentPages"));
        Assert.Equal(Text(Kept(LinkedGolden)), Text(ProjectFile.Write(open)));

        JsonObject notes = JsonNode.Parse(ProjectFile.Write(FromFolder() with { Document = s_pdf }))!.AsObject();
        Assert.False(notes.ContainsKey("document"));
    }

    [Fact]
    public void A_file_with_no_notes_and_no_pdf_that_describes_no_pdf_is_still_damaged()
    {
        JsonObject file = JsonNode.Parse(ProjectFile.Write(Linked()))!.AsObject();
        file["originals"]![0]!["id"] = "src_other";

        Assert.Contains("source", Assert.Throws<ProjectFileException>(() => Read(file)).Message, StringComparison.Ordinal);

        file["originals"]![0]!["id"] = PieceOriginal.DocumentId;
        file["originals"]![0]!["kept"] = "cloud";
        Project future = Read(file);
        Assert.True(future.DescribesDocument);
        Assert.Contains("\"cloud\"", Encoding.UTF8.GetString(ProjectFile.Write(future)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_folder_is_kept_on_any_original_and_one_that_is_not_a_path_inside_the_folder_is_damage()
    {
        Project project = ProjectFile.Read(Kept(FolderGolden));
        Assert.Equal("Бетховен", project.FindOriginal(PieceOriginal.SourceFileId)!.Folder);
        Assert.Equal("Бетховен/PDF", project.FindOriginal(PieceOriginal.DocumentId)!.Folder);
        Assert.Equal(string.Empty, ProjectFile.Read(ProjectFile.Write(project.WithOriginal(project.FindOriginal(PieceOriginal.SourceFileId)! with { Folder = string.Empty }))).FindOriginal(PieceOriginal.SourceFileId)!.Folder);

        JsonObject file = JsonNode.Parse(Kept(FolderGolden))!.AsObject();
        foreach (JsonNode? broken in new JsonNode?[] { "/Бах", "Бах/", "Бах//ХТК", "../Бах", "Бах\\ХТК", " ", 3, null })
        {
            file["originals"]![0]!["folder"] = broken;
            Assert.Throws<ProjectFileException>(() => Read(file));
        }

        Assert.Throws<ArgumentException>(() => project.FindOriginal(PieceOriginal.SourceFileId)! with { Folder = "a/./b" });
    }

    [Fact]
    public void In_the_sync_the_folder_is_the_players_data_kept_from_either_copy()
    {
        PieceOriginal here = FromFolder().FindOriginal(PieceOriginal.DocumentId)!;
        PieceOriginal online = here with { Folder = null };

        PieceOriginal merged = Assert.Single(PieceOriginals.Merge([online], [here], HoldingDevice.Ipad));

        Assert.Equal("Бетховен/PDF", merged.Folder);
    }

    /// <summary>What a build before 0.5 does with the file, short of running it: without the new fields the notes are the same.</summary>
    [Fact]
    public void Without_the_folders_the_piece_of_notes_is_the_same_and_the_linked_pdf_is_described_whole()
    {
        Project project = FromFolder();
        JsonObject file = JsonNode.Parse(ProjectFile.Write(project))!.AsObject();
        foreach (JsonNode? original in file["originals"]!.AsArray())
        {
            _ = original!.AsObject().Remove("folder");
        }

        Project old = Read(file);

        Assert.Null(old.FindOriginal(PieceOriginal.SourceFileId)!.Folder);
        Assert.Equal(OpenedProject.Open(project).Notes, OpenedProject.Open(old).Notes);
        Assert.Equal(OriginalKept.Link, old.FindOriginal(PieceOriginal.DocumentId)!.Kept);
    }

    [Fact]
    public void The_facade_lists_a_linked_piece_and_opens_nothing_until_its_file_is_linked_again()
    {
        var facade = new MusicCoreFacade();
        string file = Encoding.UTF8.GetString(Kept(LinkedGolden));

        JsonElement summary = Tests.Facade.FacadeCalls.Json(facade.Call(0, "project.summary", $$"""{"file": {{JsonSerializer.Serialize(file)}}}""")).GetProperty("project");
        JsonElement opened = Tests.Facade.FacadeCalls.Json(facade.Open("Prelude.motiff.json", Kept(LinkedGolden)));

        Assert.Equal("Prelude in C", summary.GetProperty("title").GetString());
        JsonElement original = summary.GetProperty("originals")[0];
        Assert.Equal(("link", "Бах/ХТК", false), (original.GetProperty("kept").GetString(), original.GetProperty("folder").GetString(), original.GetProperty("inFile").GetBoolean()));
        Assert.Equal("originalAway", opened.GetProperty("error").GetString());
        Assert.Equal(0, facade.Count);
    }
}