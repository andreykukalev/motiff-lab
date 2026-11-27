using System.Text;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// Version 16 of the project file (decision record 0030; docs/project-file.md): the originals, the Notes with their
/// provenance and the workspace, the corrections' group and origin, an idea's Notes and provenance, the tombstone of a
/// removed original, and what the reader keeps and refuses in each layer.
/// </summary>
public class SourcesFormatTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name));

    private static byte[] Scanned() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"));

    private static Project FurElise() => Project.Create("p_fur", "fur-elise.musicxml", Fixture("fur-elise-original.musicxml"), s_now);

    private static Project ScannedPdf()
    {
        byte[] musicXml = Scanned();
        return Project.CreateDocument("p_pdf", "Nocturne.pdf", s_pdf, s_now)
            .Recognised(musicXml, "homr 0.7.0", PageMap.Of(PieceImporter.Import("a.musicxml", musicXml).Piece!, 2), s_now.AddMinutes(1));
    }

    private static JsonObject Worked() => JsonNode.Parse(GoldenFiles.Kept("v18-worked.motiff.json"))!.AsObject();

    private static JsonObject Node(byte[] bytes) => JsonNode.Parse(bytes)!.AsObject();

    private static byte[] Bytes(JsonNode file) => Encoding.UTF8.GetBytes(file.ToJsonString());

    private static Project Read(JsonNode file) => ProjectFile.Read(Bytes(file));

    private static Project RoundTrip(Project project) => ProjectFile.Read(ProjectFile.Write(project));

    private static string Damage(JsonNode file) => Assert.Throws<ProjectFileException>(() => Read(file)).Message;

    [Fact]
    public void A_piece_is_written_as_its_originals_its_notes_and_its_workspace_and_the_same_project_gives_the_same_bytes()
    {
        Project project = FurElise();

        byte[] written = ProjectFile.Write(project);
        JsonObject file = Node(written);

        Assert.Equal(["format", "version", "minReader", "id", "fileName", "createdAt", "lastOpened", "originals", "notes", "workspace"], file.Select(field => field.Key));
        Assert.Equal(18, file["version"]!.GetValue<int>());
        Assert.Equal(PieceOriginal.SourceFileId, file["originals"]![0]!["id"]!.GetValue<string>());
        JsonObject notes = file["notes"]![0]!.AsObject();
        Assert.Equal(Project.FirstNotesId, notes["id"]!.GetValue<string>());
        Assert.Equal("""{"op":"import","from":[{"original":"src_file"}]}""", notes["provenance"]!.ToJsonString());
        Assert.Equal(Project.FirstNotesId, file["workspace"]!["working"]!.GetValue<string>());
        Assert.Equal(written, ProjectFile.Write(ProjectFile.Read(written)));
        Assert.Equal(written, ProjectFile.Write(project));
    }

    [Fact]
    public void A_scanned_pdf_keeps_its_bytes_in_its_original_and_its_notes_name_it_by_fingerprint_engine_and_time()
    {
        JsonObject file = Node(ProjectFile.Write(ScannedPdf()));

        JsonObject pdf = file["originals"]![0]!.AsObject();
        Assert.Equal(PieceOriginal.DocumentId, pdf["id"]!.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(s_pdf), pdf["data"]!.GetValue<string>());
        JsonObject provenance = file["notes"]![0]!["provenance"]!.AsObject();
        Assert.Equal(("pdfRecognition", "homr 0.7.0"), (provenance["op"]!.GetValue<string>(), provenance["engine"]!.GetValue<string>()));
        Assert.Equal(PieceOriginal.Fingerprint(s_pdf), provenance["from"]![0]!["sha256"]!.GetValue<string>());
        Assert.Equal(s_now.AddMinutes(1), RoundTrip(ScannedPdf()).WorkingProvenance.At);
        Assert.Equal("src_pdf", file["notes"]![0]!["pageMap"]!["original"]!.GetValue<string>());
    }

    [Fact]
    public void A_piece_with_no_notes_names_no_working_notes()
    {
        JsonObject file = Node(ProjectFile.Write(Project.CreateDocument("p_pdf", "a.pdf", s_pdf, s_now)));

        Assert.Empty(file["notes"]!.AsArray());
        Assert.False(file["workspace"]!.AsObject().ContainsKey("working"));
    }

    /// <summary>
    /// A provenance read from the file stays while it still tells of the notes, so a PDF replaced under the same id after
    /// the notes were read shows the fingerprint they were read from; a scan read again gets the provenance it shows.
    /// </summary>
    [Fact]
    public void The_provenance_read_stays_while_it_tells_of_the_notes_and_follows_a_new_reading()
    {
        Project read = RoundTrip(ScannedPdf());
        byte[] other = Encoding.ASCII.GetBytes("%PDF-1.4\nanother printing\n%%EOF\n");
        Project replaced = read with { Document = other, KeptOriginals = [read.FindOriginal(PieceOriginal.DocumentId)! with { Sha256 = PieceOriginal.Fingerprint(other) }] };

        Assert.Equal(PieceOriginal.Fingerprint(s_pdf), replaced.WorkingProvenance.Original!.Sha256);
        Assert.Equal(PieceOriginal.Fingerprint(other), replaced.DerivedProvenance.Original!.Sha256);

        Project again = replaced with { Scan = ScanOrigin.Read("homr 0.8.0") with { From = PieceOriginal.DocumentId, At = s_now.AddDays(1) } };
        Assert.Equal(("homr 0.8.0", PieceOriginal.Fingerprint(other)), (again.WorkingProvenance.Engine, again.WorkingProvenance.Original!.Sha256));
    }

    [Fact]
    public void An_operation_this_build_does_not_know_keeps_its_provenance_whole()
    {
        JsonObject file = Worked();
        file["notes"]![0]!["provenance"] = JsonNode.Parse("""{"op": "split", "from": [{"original": "src_file", "pages": [2, 3]}], "inputs": ["n_0"], "params": {"at": 1920}, "at": "2026-11-14T10:00:00.0000000+00:00", "model": "later"}""");

        Project project = Read(file);

        Assert.Equal(("split", (2, 3), "n_0"), (project.WorkingProvenance.Op, project.WorkingProvenance.Original!.Pages!.Value, project.WorkingProvenance.Inputs[0]));
        Assert.True(JsonNode.DeepEquals(file, Node(ProjectFile.Write(project))), "the provenance is written back as the file held it");
    }

    [Fact]
    public void A_correction_keeps_its_group_and_who_proposed_it_and_the_players_own_says_neither()
    {
        JsonObject file = Worked();
        JsonObject edit = file["notes"]![0]!["edits"]![0]!.AsObject();
        edit["group"] = "g_1";
        edit["origin"] = "repair";

        Project project = Read(file);

        NoteEdit read = Assert.Single(project.Edits.Edits);
        Assert.Equal(("g_1", NoteEditOrigin.Repair), (read.Group, read.Origin));
        Assert.True(JsonNode.DeepEquals(file, Node(ProjectFile.Write(project))));
        Assert.Equal((null, null), (ProjectFile.Read(GoldenFiles.Kept("v15-worked.motiff.json")).Edits.Edits[0].Group, ProjectFile.Read(GoldenFiles.Kept("v15-worked.motiff.json")).Edits.Edits[0].Origin));

        edit["origin"] = "player";
        Assert.Null(Assert.Single(Read(file).Edits.Edits).Origin);
        Assert.False(Node(ProjectFile.Write(Read(file)))["notes"]![0]!["edits"]![0]!.AsObject().ContainsKey("origin"));
        edit["origin"] = "a later build";
        Assert.StartsWith("This project file is damaged", Damage(file), StringComparison.Ordinal);
        edit["origin"] = "repair";
        edit["group"] = " ";
        Assert.StartsWith("This project file is damaged", Damage(file), StringComparison.Ordinal);
    }

    [Fact]
    public void Views_hidden_from_the_switch_are_kept_in_the_order_of_their_ids_and_never_the_working_notes()
    {
        Project project = ProjectFile.Read(GoldenFiles.Kept("v18-worked.motiff.json"));
        Assert.False(Node(ProjectFile.Write(project))["workspace"]!.AsObject().ContainsKey("hiddenViews"));

        Project hidden = project.ViewHidden("src_file", hidden: true).ViewHidden("perf_1", hidden: true).ViewHidden("perf_1", hidden: true);
        JsonObject file = Node(ProjectFile.Write(hidden));

        Assert.Equal("""["perf_1","src_file"]""", file["workspace"]!["hiddenViews"]!.ToJsonString());
        Assert.Equal(["perf_1", "src_file"], RoundTrip(hidden).HiddenViews);
        Assert.True(hidden.IsHidden("perf_1"));
        Assert.Throws<InvalidOperationException>(() => hidden.ViewHidden(Project.FirstNotesId, hidden: true));
        Assert.Equal(["src_file"], hidden.ViewHidden("perf_1", hidden: false).HiddenViews);
        Assert.Same(hidden, hidden.ViewHidden("n_9", hidden: false));

        file["workspace"]!["hiddenViews"] = JsonNode.Parse("""["n_1", "later_view"]""");
        Project kept = Read(file);
        Assert.Equal(["later_view", "n_1"], kept.HiddenViews);
        Assert.False(kept.IsHidden(Project.FirstNotesId));
        foreach (string broken in new[] { "\"perf_1\"", "[\"perf_1\", \"perf_1\"]", "[\" \"]", "[3]" })
        {
            file["workspace"]!["hiddenViews"] = JsonNode.Parse(broken);
            Assert.Contains("hiddenViews", Damage(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_removed_original_is_written_as_its_tombstone_with_its_marks_and_read_back_apart_from_the_piece()
    {
        DocumentMarks marks = DocumentMarks.Empty.AddBookmark(2, "Reprise", s_now, IdSource.Numbered);
        var book = new PieceOriginal("src_book", OriginalKind.Pdf, "Songbook.pdf", OriginalKept.Device, s_now) { Sha256 = new string('b', 64), Pages = 24 };
        Project project = FurElise() with { Sources = new SourcesLayer { Removed = [new RemovedOriginal(book, s_now.AddHours(1)) { Marks = marks }] } };

        JsonObject file = Node(ProjectFile.Write(project));
        Project read = RoundTrip(project);

        JsonObject tombstone = file["originals"]![1]!.AsObject();
        Assert.Equal(("src_book", "2026-11-14T11:00:00.0000000+00:00"), (tombstone["id"]!.GetValue<string>(), tombstone["removed"]!["at"]!.GetValue<string>()));
        Assert.False(tombstone.ContainsKey("data"));
        Assert.Equal(project.Sources.Removed, read.Sources.Removed);
        Assert.Null(read.FindOriginal("src_book"));
        Assert.True(read.DocumentMarks.IsEmpty);
        Assert.Equal(ProjectFile.Write(project), ProjectFile.Write(read));
    }

    [Fact]
    public void A_notes_this_build_cannot_open_is_kept_whole_in_its_place()
    {
        JsonObject file = Worked();
        JsonObject later = JsonNode.Parse("""{"id": "n_9", "provenance": {"op": "assembly", "from": [], "inputs": ["n_1", "perf_1"]}, "music": "AAAA", "edits": [], "check": null}""")!.AsObject();
        file["notes"]!.AsArray().Insert(1, later);

        Project project = Read(file);

        UnknownEntity kept = Assert.Single(project.Unknown, entity => entity.List == "notes");
        Assert.Equal(("n_9", 1), (kept.Id, kept.Index));
        Assert.Single(project.Performances);
        Assert.True(JsonNode.DeepEquals(file, Node(ProjectFile.Write(project))), "the Notes is written back where it stood");
    }

    [Fact]
    public void A_file_whose_notes_do_not_add_up_is_damaged()
    {
        JsonObject twice = Worked();
        twice["notes"]![1]!["id"] = Project.FirstNotesId;
        Assert.Contains("notes", Damage(twice), StringComparison.Ordinal);

        JsonObject lost = Worked();
        lost["workspace"]!["working"] = "n_lost";
        Assert.Contains("working notes", Damage(lost), StringComparison.Ordinal);

        JsonObject empty = Worked();
        empty["notes"]![0]!["music"] = "";
        Assert.Contains("music", Damage(empty), StringComparison.Ordinal);

        JsonObject both = Node(ProjectFile.Write(ScannedPdf()));
        both["notes"]![0]!["transcription"] = JsonNode.Parse("""{"bars": null}""");
        Assert.Contains("origin", Damage(both), StringComparison.Ordinal);

        JsonObject without = Worked();
        _ = without["notes"]![0]!.AsObject().Remove("provenance");
        Assert.Contains("provenance", Damage(without), StringComparison.Ordinal);

        JsonObject flat = Worked();
        _ = flat.Remove("workspace");
        Assert.Contains("workspace", Damage(flat), StringComparison.Ordinal);
    }

    [Fact]
    public void A_compared_notes_is_its_performance_with_its_provenance_and_reads_as_before()
    {
        JsonObject file = Worked();
        JsonObject compared = file["notes"]![1]!.AsObject();

        Assert.Equal(["id", "name", "provenance", "music", "performance"], compared.Select(field => field.Key));
        SavedPerformance performance = Assert.Single(Read(file).Performances);
        Assert.Equal(ProjectFile.Read(GoldenFiles.Kept("v15-worked.motiff.json")).Performances[0], performance);
        Assert.Equal(compared["id"]!.GetValue<string>(), performance.Id);
    }

    [Fact]
    public void Ticks_of_the_working_notes_stay_where_they_were_on_the_way_through()
    {
        Project earlier = ProjectFile.Read(GoldenFiles.Kept("v15-worked.motiff.json"));
        Project read = ProjectFile.Read(ProjectFile.Write(earlier));

        Assert.Equal(OpenedProject.Open(earlier).Notes, OpenedProject.Open(read).Notes);
        Assert.Equal(earlier.ResumePoints, read.ResumePoints);
        Assert.Equal(earlier.Marks, read.Marks);
        Assert.Equal(new Tick(3360), read.ResumePoints[0].At);
    }
}