using System.Text;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// Local-first originals (version 0.3, Andrei 2026-11-11 21:20 UTC): the descriptions of a piece's originals in the
/// project file, where each one's bytes are held, linking a picked file again by its fingerprint, and the sync's union.
/// Within version 15, so version 0.2 of both apps still opens the file.
/// </summary>
public class PieceOriginalTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 12, 1, 0, 0, TimeSpan.Zero);
    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");
    private static readonly string s_pdfSha = PieceOriginal.Fingerprint(s_pdf);
    private static readonly string s_audioSha = new('a', 64);

    private static byte[] Scanned() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"));

    private static byte[] Midi() => MidiExporter.Export(
        [new Note { Id = "n_1", Pitch = new Pitch(60), Span = new TickSpan(Tick.Zero, 480), Velocity = 80, Track = 0, Attribution = Attribution.Imported }],
        [],
        MeterMap.Default,
        "heard");

    /// <summary>A PDF read by the scan server, with a recording added as a version, as the web makes it.</summary>
    private static Project Recognised()
    {
        Project pdf = Project.CreateDocument("p_9f2c4e1a0b7d4c3e8f6a5b4c3d2e1f00", "Nocturne.pdf", s_pdf, s_now);
        byte[] musicXml = Scanned();
        PageMap pages = PageMap.Of(PieceImporter.Import("a.musicxml", musicXml).Piece!, 2);
        Project read = pdf.Recognised(musicXml, "homr 0.7.0", pages, s_now.AddMinutes(1));
        SavedPerformance recording = new("perf_1", "Evening take", PerformanceSource.Recording, Midi(), 0, [])
        {
            Engine = "transkun 2.0.1",
            From = "perf_1",
            At = s_now.AddMinutes(2),
        };
        return read.AddPerformance(recording)
            .WithOriginal(new PieceOriginal("perf_1", OriginalKind.Audio, "evening.m4a", OriginalKept.Device, s_now.AddMinutes(2)) { Sha256 = s_audioSha, Bytes = 1_234_567, DurationMs = 61_000 })
            .MarkHeld(PieceOriginal.DocumentId, HoldingDevice.Web, held: true, s_now.AddMinutes(3))
            .MarkHeld("perf_1", HoldingDevice.Web, held: true, s_now.AddMinutes(3));
    }

    private static Project RoundTrip(Project project) => ProjectFile.Read(ProjectFile.Write(project));

    [Fact]
    public void A_file_written_before_originals_shows_them_from_the_piece_and_is_written_back_without_them()
    {
        byte[] golden = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", "v15-worked.motiff.json"));

        Project project = ProjectFile.Read(golden);

        PieceOriginal file = Assert.Single(project.Originals, original => original.Id == PieceOriginal.SourceFileId);
        Assert.Equal(OriginalKind.MusicXml, file.Kind);
        Assert.Equal(OriginalKept.File, file.Kept);
        Assert.Equal(project.Source.Length, file.Bytes);
        Assert.Null(file.Sha256);
        Assert.Empty(project.KeptOriginals);
        Assert.DoesNotContain("\"originals\"", Encoding.UTF8.GetString(ProjectFile.Write(project)), StringComparison.Ordinal);
    }

    [Fact]
    public void An_old_pdf_piece_and_recording_versions_get_originals_with_the_ids_the_web_keeps_their_sound_under()
    {
        Project old = Project.CreateDocument("p_1", "Etude.pdf", s_pdf, s_now) with { KeptOriginals = [] };
        old = old.Recognised(Scanned(), "homr 0.7.0", PageMap.Of(PieceImporter.Import("a.musicxml", Scanned()).Piece!, 1))
            .AddPerformance(new SavedPerformance("perf_3", "Take", PerformanceSource.Recording, Midi(), 0, []))
            .AddPerformance(new SavedPerformance("perf_4", "Played in", PerformanceSource.Midi, Midi(), 0, []))
            .AddPerformance(new SavedPerformance("perf_5", "Edition", PerformanceSource.Score, Midi(), 0, []));

        IReadOnlyList<PieceOriginal> originals = old.Originals;

        Assert.Equal([PieceOriginal.DocumentId, "perf_3", "perf_4"], originals.Select(original => original.Id));
        Assert.Equal((OriginalKind.Pdf, OriginalKept.File), (originals[0].Kind, originals[0].Kept));
        Assert.Equal((OriginalKind.Audio, OriginalKept.Device), (originals[1].Kind, originals[1].Kept));
        Assert.Equal((OriginalKind.Midi, OriginalKept.File), (originals[2].Kind, originals[2].Kept));
        Assert.Equal("Etude.pdf", originals[0].FileName);
        Assert.Null(originals[0].Sha256);
        Assert.Equal(s_pdfSha, old.Fingerprinted().FindOriginal(PieceOriginal.DocumentId)!.Sha256);
        Project fingerprinted = old.Fingerprinted();
        Assert.Same(fingerprinted, fingerprinted.Fingerprinted());
    }

    [Fact]
    public void Descriptions_links_and_marks_round_trip_and_the_file_stays_version_15()
    {
        Project project = Recognised();

        byte[] written = ProjectFile.Write(project);
        Project reread = ProjectFile.Read(written);

        Assert.Equal(15, JsonNode.Parse(written)!["version"]!.GetValue<int>());
        Assert.Equal(project.Originals, reread.Originals);
        Assert.Equal(written, ProjectFile.Write(reread));
        PieceOriginal pdf = reread.FindOriginal(PieceOriginal.DocumentId)!;
        Assert.Equal(s_pdfSha, pdf.Sha256);
        Assert.Equal(s_pdf.Length, pdf.Bytes);
        Assert.Equal([new OriginalHeld(HoldingDevice.Web, s_now.AddMinutes(3))], pdf.Held);
        Assert.Equal(PieceOriginal.DocumentId, reread.Scan!.From);
        Assert.Equal(s_now.AddMinutes(1), reread.Scan.At);
        Assert.Equal(("perf_1", s_now.AddMinutes(2)), (reread.Performances[0].From, reread.Performances[0].At));
        PieceOriginal audio = reread.FindOriginal("perf_1")!;
        Assert.Equal((s_audioSha, 61_000L, OriginalKept.Device), (audio.Sha256, audio.DurationMs!.Value, audio.Kept));
    }

    [Fact]
    public void Confirming_a_scanned_bar_keeps_where_the_notes_came_from()
    {
        ScanOrigin scan = Recognised().Scan!;

        Assert.Equal(PieceOriginal.DocumentId, scan.Confirm([2]).Unconfirm(2).From);
        Assert.Equal(scan.At, scan.Confirm([2]).At);
    }

    [Fact]
    public void The_online_copy_keeps_the_pdf_description_so_another_device_shows_where_it_is()
    {
        Project online = RoundTrip(Recognised().ForOnline()!);

        Assert.Null(online.Document);
        PieceOriginal pdf = Assert.Single(online.Originals, original => original.Kind == OriginalKind.Pdf);
        Assert.Equal((s_pdfSha, OriginalKept.File), (pdf.Sha256, pdf.Kept));
        Assert.True(pdf.IsHeldBy(HoldingDevice.Web));
        Assert.False(pdf.IsHeldBy(HoldingDevice.Ipad));
    }

    [Fact]
    public void The_pdf_is_linked_again_only_from_the_same_file()
    {
        Project online = RoundTrip(Recognised().ForOnline()!);
        PageMap pages = new([Tick.Zero]);

        Assert.Null(online.RelinkDocument(Encoding.ASCII.GetBytes("%PDF-1.4\nanother\n%%EOF\n"), pages));
        Project linked = online.RelinkDocument(s_pdf, pages)!;
        Assert.Equal(s_pdf, linked.Document);
        Assert.Equal(pages, linked.DocumentPages);
        Assert.Equal(PieceOriginal.DocumentId, linked.OriginalWithFingerprint(s_pdfSha)!.Id);
        Assert.Throws<InvalidOperationException>(() => linked.RelinkDocument(s_pdf, pages));
    }

    [Fact]
    public void A_device_mark_is_set_and_taken_back_and_an_unchanged_mark_changes_nothing()
    {
        Project project = Recognised();

        Assert.Same(project, project.MarkHeld("perf_1", HoldingDevice.Web, held: true, s_now.AddDays(1)));
        Project gone = project.MarkHeld("perf_1", HoldingDevice.Web, held: false, s_now);
        Assert.Empty(gone.FindOriginal("perf_1")!.Held);
        Assert.Throws<KeyNotFoundException>(() => project.MarkHeld("src_none", HoldingDevice.Web, held: true, s_now));
    }

    [Theory]
    [InlineData(true, false, new HoldingDevice[0], HoldingDevice.Web, OriginalWhere.InFile)]
    [InlineData(false, true, new HoldingDevice[0], HoldingDevice.Web, OriginalWhere.Here)]
    [InlineData(false, false, new[] { HoldingDevice.Ipad }, HoldingDevice.Web, OriginalWhere.OnIpad)]
    [InlineData(false, false, new[] { HoldingDevice.Ipad, HoldingDevice.Web }, HoldingDevice.Web, OriginalWhere.OnIpad)]
    [InlineData(false, false, new[] { HoldingDevice.Web }, HoldingDevice.Web, OriginalWhere.InBrowser)]
    [InlineData(false, false, new[] { HoldingDevice.Ipad, HoldingDevice.Web }, HoldingDevice.Ipad, OriginalWhere.InBrowser)]
    [InlineData(false, false, new[] { HoldingDevice.Ipad }, HoldingDevice.Ipad, OriginalWhere.OnIpad)]
    [InlineData(false, false, new HoldingDevice[0], HoldingDevice.Ipad, OriginalWhere.Nowhere)]
    public void The_badge_says_where_the_bytes_are_as_this_device_sees_them(bool inFile, bool heldHere, HoldingDevice[] held, HoldingDevice self, OriginalWhere where)
    {
        OriginalSummary original = new("src_pdf", OriginalKind.Pdf, OriginalKept.File, inFile, held);

        Assert.Equal(where, original.Where(self, heldHere));
    }

    [Fact]
    public void A_library_row_keeps_each_original_and_whether_its_bytes_are_in_the_file()
    {
        Project online = RoundTrip(Recognised().ForOnline()!);

        Assert.Equal(
            [new OriginalSummary(PieceOriginal.DocumentId, OriginalKind.Pdf, OriginalKept.File, InFile: false, [HoldingDevice.Web]),
             new OriginalSummary("perf_1", OriginalKind.Audio, OriginalKept.Device, InFile: false, [HoldingDevice.Web])],
            online.Summary.Originals);
        Assert.True(Recognised().Summary.Originals[0].InFile);
    }

    [Fact]
    public void Removing_a_version_removes_its_recording_description()
    {
        Project removed = Recognised().RemovePerformance("perf_1");

        Assert.Null(removed.FindOriginal("perf_1"));
        Assert.DoesNotContain("perf_1", Encoding.UTF8.GetString(ProjectFile.Write(removed)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_pull_unites_the_originals_and_each_device_keeps_its_own_mark()
    {
        // The iPad pulls the web's copy: the web holds the PDF and the recording; the iPad holds the PDF too and once had a
        // web mark it saw before the web took it back.
        Project web = RoundTrip(Recognised().MarkHeld("perf_1", HoldingDevice.Web, held: false, s_now).ForOnline()!);
        Project ipad = Recognised()
            .MarkHeld(PieceOriginal.DocumentId, HoldingDevice.Ipad, held: true, s_now)
            .WithOriginal(new PieceOriginal("src_extra", OriginalKind.Images, "page.jpg", OriginalKept.Device, s_now));

        Project pulled = ProjectSync.Pulled(web, ipad, HoldingDevice.Ipad);

        Assert.Equal(s_pdf, pulled.Document);
        PieceOriginal pdf = pulled.FindOriginal(PieceOriginal.DocumentId)!;
        Assert.True(pdf.IsHeldBy(HoldingDevice.Ipad));
        Assert.True(pdf.IsHeldBy(HoldingDevice.Web));
        Assert.False(pulled.FindOriginal("perf_1")!.IsHeldBy(HoldingDevice.Web));
        Assert.NotNull(pulled.FindOriginal("src_extra"));
        Assert.Equal(pulled.Originals, RoundTrip(pulled).Originals);
    }

    [Fact]
    public void Originals_with_one_fingerprint_are_one_and_a_different_one_with_a_taken_id_gets_its_own()
    {
        PieceOriginal online = new("src_a", OriginalKind.Audio, "a.wav", OriginalKept.Device, s_now) { Sha256 = s_audioSha };
        PieceOriginal same = new("src_b", OriginalKind.Audio, "a copy.wav", OriginalKept.Device, s_now) { Sha256 = s_audioSha, DurationMs = 5 };
        PieceOriginal other = new("src_a", OriginalKind.Audio, "b.wav", OriginalKept.Device, s_now) { Sha256 = new string('b', 64) };

        IReadOnlyList<PieceOriginal> merged = PieceOriginals.Merge([online], [same, other], device: null);

        Assert.Equal(["src_a", "src_a_2"], merged.Select(original => original.Id));
        Assert.Equal(5, merged[0].DurationMs);
        Assert.Equal("b.wav", merged[1].FileName);
    }

    /// <summary>
    /// A later build adds a value: a kind, or another way of keeping the bytes (as "link" was added in version 0.5, a file
    /// left in the player's own folder). This build keeps the description whole and writes it back.
    /// </summary>
    [Theory]
    [InlineData("kind", "hologram", nameof(OriginalKind))]
    [InlineData("kept", "cloud", nameof(OriginalKept))]
    public void An_original_with_a_value_a_later_build_adds_is_kept_and_written_back(string field, string value, string type)
    {
        JsonObject file = JsonNode.Parse(ProjectFile.Write(Recognised()))!.AsObject();
        file["originals"]![0]![field] = value;
        byte[] bytes = Encoding.UTF8.GetBytes(file.ToJsonString());

        Project project = ProjectFile.Read(bytes);

        Assert.Contains(project.Unknown, entity => entity.List == "originals" && entity.Enum == type);
        Assert.Contains($"\"{value}\"", Encoding.UTF8.GetString(ProjectFile.Write(project)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sha256", "\"ABC\"")]
    [InlineData("bytes", "-1")]
    [InlineData("addedAt", "\"yesterday\"")]
    [InlineData("held", "{}")]
    public void A_damaged_description_makes_the_file_damaged(string field, string value)
    {
        JsonObject file = JsonNode.Parse(ProjectFile.Write(Recognised()))!.AsObject();
        file["originals"]![0]![field] = JsonNode.Parse(value);

        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString())));
    }

    /// <summary>
    /// The files version 0.3 writes for a recognised PDF with a recording version, on the device and online, kept in
    /// Golden (README.md): version 0.2's reader, built from the tag v0.2, opened both (see the README). Run with
    /// MOTIFF_UPDATE_SNAPSHOTS=1 to write them again when the format changes on purpose.
    /// </summary>
    [Theory]
    [InlineData("v15-originals.motiff.json", false)]
    [InlineData("v15-originals-online.motiff.json", true)]
    public void The_files_version_0_3_writes_are_the_goldens_and_read_back_unchanged(string golden, bool online)
    {
        Project project = online ? Recognised().ForOnline()! : Recognised();
        byte[] written = ProjectFile.Write(project);
        string path = Path.Combine(AppContext.BaseDirectory, "Golden", golden);
        if (Environment.GetEnvironmentVariable("MOTIFF_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllBytes(Path.Combine(GoldenFolder(), golden), written);
            return;
        }

        byte[] kept = File.ReadAllBytes(path);
        Assert.Equal(Encoding.UTF8.GetString(kept).ReplaceLineEndings("\n"), Encoding.UTF8.GetString(written).ReplaceLineEndings("\n"));
        Assert.Equal(Encoding.UTF8.GetString(kept).ReplaceLineEndings("\n"), Encoding.UTF8.GetString(ProjectFile.Write(ProjectFile.Read(kept))).ReplaceLineEndings("\n"));
    }

    private static string GoldenFolder([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(source))!, "Golden");

    /// <summary>
    /// What version 0.2 does with a file version 0.3 writes, short of running it (the PR runs the v0.2 build itself): it is
    /// version 15, and without the fields 0.2 does not know it reads as the same piece, so 0.2 opens it and loses only them.
    /// </summary>
    [Fact]
    public void Without_the_new_fields_the_file_is_the_same_piece_for_a_reader_that_does_not_know_them()
    {
        Project project = Recognised();
        JsonObject file = JsonNode.Parse(ProjectFile.Write(project))!.AsObject();
        _ = file.Remove("originals");
        foreach (JsonObject made in new[] { file["scan"]!.AsObject(), file["performances"]![0]!.AsObject() })
        {
            _ = made.Remove("from");
            _ = made.Remove("at");
        }

        Project old = ProjectFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString()));

        Assert.Equal(OpenedProject.Open(project).Notes, OpenedProject.Open(old).Notes);
        Assert.Equal(project.Document, old.Document);
        Assert.Equal(project.Performances[0].Midi, old.Performances[0].Midi);
        Assert.Equal(project.Originals.Select(original => original.Id), old.Originals.Select(original => original.Id));

        // Through the sync: a v0.2 device writes the online copy without the new fields; the v0.3 device that pulls it
        // keeps every description it had, with its fingerprints and its own marks.
        Project pulled = ProjectSync.Pulled(old with { Document = null, DocumentPages = null }, project, HoldingDevice.Web);
        Assert.Equal(project.Originals, pulled.Originals);
    }
}