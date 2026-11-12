using System.Text;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// The format step of version 0.4 within version 15 (v0.4 design, Ф1; Andrei, 2026-11-12): "scan"."pages", the Check reason
/// PageQuality, "partOf" and "margin" on a stroke. Optional fields, so versions 0.2 and 0.3 open the file and drop them on
/// writing; a pull keeps this device's when the online copy has none.
/// </summary>
public class PhotosAndPartsFormatTests
{
    private const string Golden = "v15-photos-parts.motiff.json";

    private static readonly DateTimeOffset s_now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    private static byte[] Scanned() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"));

    /// <summary>Two photographed pages, the second dim, as the server reports them.</summary>
    private static readonly PageReport[] s_pages =
    [
        new(1, 0, 1.12, 118, 6, 214, 38, 15.5, 9),
        new(2, 90, 0.55, 58, 6, 120, 36, 12, 0),
    ];

    /// <summary>
    /// A waltz made from pages 6 to 7 of a photographed songbook («Пьесы в файле»): its own two photos as a PDF, read by the
    /// scan server page by page with a report on each, the dim second page's bars flagged, and a stroke in a margin.
    /// </summary>
    private static Project Waltz()
    {
        byte[] musicXml = Scanned();
        ImportedPiece piece = PieceImporter.Import("a.musicxml", musicXml).Piece!;
        Project read = Project.CreateDocument("p_5b0c1d2e3f4a4b5c8d9e0f1a2b3c4d5e", "Waltz.pdf", s_pdf, s_now)
            .Recognised(musicXml, "homr 0.7.0", PageMap.Of(piece, 2), s_now.AddMinutes(1));
        PieceOriginal photos = read.FindOriginal(PieceOriginal.DocumentId)! with { Kind = OriginalKind.Images, Pages = 2 };
        var book = new PieceOriginal(PieceParts.OriginalId, OriginalKind.Images, "Songbook.pdf", OriginalKept.Device, s_now)
        {
            Sha256 = new string('b', 64),
            Bytes = 9_876_543,
            Pages = 24,
            Held = [new OriginalHeld(HoldingDevice.Ipad, s_now)],
        };
        var margin = new InkStroke("m1", InkTool.Pen, InkColour.Carmine, 0.16, Tick.Zero, -0.2, 1.5, [0.5, 0.5, 0.5], 0.5) { Margin = StrokeMargin.Left };
        return read.WithOriginal(photos).WithOriginal(book) with
        {
            Scan = read.Scan! with { Pages = s_pages },
            Check = CheckFlag.FromScan(ScanCheck.Of(piece, s_pages), []),
            PartOf = PartOf.OnPages(PieceParts.OriginalId, 6, 7),
            Marks = Marks.Empty.AddInk([margin]),
            Metadata = new PieceMetadata("Waltz", null, [], ["Songbook"]),
        };
    }

    private static Project RoundTrip(Project project) => ProjectFile.Read(ProjectFile.Write(project));

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes).ReplaceLineEndings("\n");

    private static string GoldenFolder([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(source))!, "Golden");

    [Fact]
    public void The_file_version_0_4_writes_is_the_golden_and_reads_back_unchanged()
    {
        byte[] written = ProjectFile.Write(Waltz());
        if (Environment.GetEnvironmentVariable("MOTIFF_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllBytes(Path.Combine(GoldenFolder(), Golden), written);
            return;
        }

        byte[] kept = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", Golden));
        Assert.Equal(Text(kept), Text(written));
        Assert.Equal(Text(kept), Text(ProjectFile.Write(ProjectFile.Read(kept))));
    }

    [Fact]
    public void The_golden_holds_each_new_field()
    {
        Project project = ProjectFile.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", Golden)));

        Assert.Equal(s_pages, project.Scan!.Pages);
        Assert.Equal(PageQuality.Poor, project.Scan.Pages[1].Quality);
        Assert.Contains(project.Check!, flag => flag.Reason == CheckReason.PageQuality && !flag.Resolved);
        Assert.Equal(PieceParts.OriginalId, project.PartOf!.Original);
        Assert.Equal((6, 7), project.PartOf.Pages);
        Assert.Null(project.PartOf.Milliseconds);
        Assert.NotNull(project.FindOriginal(project.PartOf.Original));
        Assert.Equal(StrokeMargin.Left, Assert.Single(project.Marks.Ink).Margin);
        Assert.Equal(Waltz().Scan, project.Scan);
    }

    /// <summary>
    /// What versions 0.2 and 0.3 do with the file, short of running them (the PR runs both builds): it is version 15, and
    /// without the new fields it is the same piece, so they open it and lose only those fields; the PageQuality flag they
    /// keep whole as an unknown flag (S6).
    /// </summary>
    [Fact]
    public void Without_the_new_fields_the_file_is_the_same_piece()
    {
        Project project = Waltz();
        JsonObject file = JsonNode.Parse(ProjectFile.Write(project))!.AsObject();
        _ = file.Remove("partOf");
        _ = file["scan"]!.AsObject().Remove("pages");
        _ = file["marks"]!["ink"]![0]!.AsObject().Remove("margin");

        Project old = ProjectFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString()));

        Assert.Equal(OpenedProject.Open(project).Notes, OpenedProject.Open(old).Notes);
        Assert.Null(old.PartOf);
        Assert.Empty(old.Scan!.Pages);
        Assert.Null(Assert.Single(old.Marks.Ink).Margin);
        Assert.Equal(project.Marks.Ink[0].U, old.Marks.Ink[0].U);
        Assert.Equal(project.Check, old.Check);
    }

    [Fact]
    public void A_part_of_a_recording_keeps_its_stretch_and_a_place_of_another_kind_reads_as_none()
    {
        Project heard = Waltz() with { PartOf = PartOf.InRecording("src_concert", 750_000, 1_180_000) };
        Assert.Equal((750_000L, 1_180_000L), RoundTrip(heard).PartOf!.Milliseconds);
        Assert.Null(RoundTrip(heard).PartOf!.Pages);

        JsonObject file = JsonNode.Parse(ProjectFile.Write(heard))!.AsObject();
        file["partOf"] = new JsonObject { ["original"] = "src_concert", ["bars"] = new JsonArray(3, 40) };
        Assert.Null(ProjectFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString())).PartOf);

        foreach (JsonNode broken in new JsonNode[]
        {
            new JsonObject { ["original"] = "src_book", ["pages"] = new JsonArray(7, 6) },
            new JsonObject { ["original"] = "src_book", ["pages"] = new JsonArray(0, 6) },
            new JsonObject { ["original"] = "src_book", ["pages"] = new JsonArray(6, 7), ["ms"] = new JsonArray(0, 10) },
            new JsonObject { ["original"] = "src_book", ["ms"] = new JsonArray(10, 10) },
            new JsonObject { ["original"] = "src_book", ["ms"] = new JsonArray(10) },
            new JsonObject { ["pages"] = new JsonArray(6, 7) },
        })
        {
            file["partOf"] = broken;
            Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString())));
        }
    }

    [Fact]
    public void A_margin_this_build_does_not_know_keeps_its_stroke_whole_and_thinning_keeps_the_margin()
    {
        JsonObject file = JsonNode.Parse(ProjectFile.Write(Waltz()))!.AsObject();
        file["marks"]!["ink"]![0]!["margin"] = "top";
        byte[] future = Encoding.UTF8.GetBytes(file.ToJsonString());

        Project project = ProjectFile.Read(future);
        Assert.Empty(project.Marks.Ink);
        Assert.Equal("marks.ink", Assert.Single(project.Marks.Unknown).List);
        Assert.Equal("top", JsonNode.Parse(ProjectFile.Write(project))!["marks"]!["ink"]![0]!["margin"]!.GetValue<string>());

        var stroke = new InkStroke("m2", InkTool.Pen, InkColour.Graphite, 0.16, Tick.Zero, 1.2, 0, [0.5, 0, 0.5, 1, 0, 0.5, 1.5, 0, 0.5], 0.5) { Margin = StrokeMargin.Right };
        Assert.Equal(StrokeMargin.Right, stroke.Thinned().Margin);
        Assert.NotEqual(stroke, stroke with { Margin = null });
    }

    [Fact]
    public void A_pull_keeps_this_devices_new_fields_when_the_online_copy_has_none()
    {
        Project here = Waltz();
        Project fromOldBuild = RoundTrip(here with
        {
            PartOf = null,
            Scan = here.Scan! with { Pages = [] },
            Metadata = new PieceMetadata("Waltz (edited)", null, [], ["Songbook"]),
        });

        Project pulled = ProjectSync.Pulled(fromOldBuild, here, HoldingDevice.Ipad);
        Assert.Equal("Waltz (edited)", pulled.Metadata.Title);
        Assert.Equal(here.PartOf, pulled.PartOf);
        Assert.Equal(s_pages, pulled.Scan!.Pages);

        // The online copy's own place wins, and page reports of other notes are not kept.
        Project moved = RoundTrip(here with { PartOf = PartOf.OnPages(PieceParts.OriginalId, 8, 9) });
        Assert.Equal((8, 9), ProjectSync.Pulled(moved, here).PartOf!.Pages);
        Project readAgain = RoundTrip(fromOldBuild with { Source = [.. here.Source, (byte)' '] });
        Assert.Empty(ProjectSync.Pulled(readAgain, here).Scan!.Pages);
    }

    [Fact]
    public void A_bar_on_a_poor_page_is_a_check_flag_with_its_own_words()
    {
        ImportedPiece piece = PieceImporter.Import("a.musicxml", Scanned()).Piece!;
        IReadOnlyList<CheckFlag> flags = CheckFlag.FromScan(ScanCheck.Of(piece, s_pages), []);
        Assert.Contains(flags, flag => flag.Reason == CheckReason.PageQuality);
        Assert.DoesNotContain(CheckFlag.FromScan(ScanCheck.Of(piece), []), flag => flag.Reason == CheckReason.PageQuality);
        Assert.Equal("Плохая страница", CheckFlag.NameOf(CheckReason.PageQuality, Words.Russian));
        Assert.Equal(ScanBar.Describe(ScanFlag.PageQuality, Words.English), CheckFlag.Explain(CheckReason.PageQuality, Words.English));
    }
}