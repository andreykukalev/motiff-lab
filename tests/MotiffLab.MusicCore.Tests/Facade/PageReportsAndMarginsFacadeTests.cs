using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Tests.Import.Scan;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// Two of the fields version 0.4 adds within version 15, written by the calls the apps already make (v0.4 design, PR 11):
/// the scan server's page reports kept with the notes read from a PDF, raising PageQuality, and the margin of a stroke.
/// </summary>
public sealed class PageReportsAndMarginsFacadeTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    private static readonly byte[] Photos = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    /// <summary>Two photographed pages read one at a time and joined, two bars of 3/4 each.</summary>
    private static readonly byte[] Score = ScanJoin.Join([ScanJoinTests.Page(2), ScanJoinTests.Page(2)]);

    /// <summary>A page read on its own, as its X-Page-Report header says it: always page 1 of its own upload.</summary>
    private static string Header(int contrast) =>
        $"[{{\"page\":1,\"rotation\":90,\"sharpness\":1.1,\"contrast\":{contrast},\"staves\":4,\"noteheads\":2,\"barLines\":2,\"spacePx\":15.5,\"measures\":2}}]";

    /// <summary>Page 1 weak (its print faint), page 2 poor (fainter still).</summary>
    private static readonly string[] Headers = [Header(90), Header(50)];

    [Fact]
    public void Notes_read_from_a_pdf_keep_each_pages_report_and_open_with_the_bars_of_a_weak_page_less_sure()
    {
        ImportedPiece piece = PieceImporter.Import("scan.musicxml", Score).Piece!;
        Project pdf = Project.CreateDocument("p_photos", "Photos.pdf", Photos, s_now);
        IReadOnlyList<PageReport> reports = [.. Headers.SelectMany(PageReport.Parse)];

        Project read = pdf.Recognised(Score, "homr 0.7.0", PageMap.Of(piece, 2), s_now, reports);
        ScanOrigin scan = read.Scan!;
        read = read with { Check = CheckFlag.FromScan(scan.CheckOf(piece), []) };

        // Numbered by their place in the notes, not as each upload numbered its one page.
        Assert.Equal([1, 2], scan.Pages.Select(static page => page.Page));
        Assert.Equal([(3, CheckReason.PageQuality), (4, CheckReason.PageQuality)], read.Check!.Select(static flag => (flag.Bar, flag.Reason)));
        Project saved = ProjectFile.Read(ProjectFile.Write(read));
        Assert.Equal(scan.Pages, saved.Scan!.Pages);
        Assert.Equal(
            [Attribution.Detected(ScanCheck.WeakPageConfidence), Attribution.Detected(ScanCheck.WeakPageConfidence), Attribution.Detected(ScanCheck.FlaggedConfidence), Attribution.Detected(ScanCheck.FlaggedConfidence)],
            OpenedProject.Open(saved).Notes.OrderBy(static note => note.Span.Onset).Select(static note => note.Attribution));

        // Not one report per page of the notes is refused; without reports nothing changes.
        Assert.Throws<ArgumentException>(() => pdf.Recognised(Score, "homr 0.7.0", PageMap.Of(piece, 2), s_now, [reports[0]]));
        Assert.Empty(pdf.Recognised(Score, "homr 0.7.0", PageMap.Of(piece, 2), s_now).Scan!.Pages);
    }

    [Fact]
    public void Document_read_takes_the_reports_as_scan_join_does_and_check_answers_each_page()
    {
        var facade = new MusicCoreFacade();
        Json(facade.Open("Photos.pdf", Photos, """{"id": "p_photos", "photos": 2, "now": "2026-11-12T12:00:00Z"}"""));
        string request = JsonSerializer.Serialize(new { musicXml = Convert.ToBase64String(Score), engine = "homr 0.7.0", pages = 2, reports = Headers, now = "2026-11-12T12:01:00Z" });

        JsonElement read = Json(facade.Call(1, "document.read", request));

        Assert.False(read.TryGetProperty("error", out _), read.ToString());
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(read.GetProperty("file").GetString()!));
        Assert.Equal([90, 50], project.Scan!.Pages.Select(static page => page.Contrast));
        Assert.Equal([3, 4], project.Check!.Where(static flag => flag.Reason == CheckReason.PageQuality).Select(static flag => flag.Bar));
        Assert.Equal(2, read.GetProperty("project").GetProperty("openChecks").GetInt32());

        var again = new MusicCoreFacade();
        Json(again.Open("Photos.motiff.json", Encoding.UTF8.GetBytes(read.GetProperty("file").GetString()!)));
        JsonElement check = Json(again.Call(1, "check"));
        Assert.Equal(["pageQuality", "pageQuality"], check.GetProperty("flags").EnumerateArray().Select(static flag => flag.GetProperty("reason").GetString()));
        JsonElement[] pages = [.. check.GetProperty("pages").EnumerateArray()];
        Assert.Equal(
            [(1, "weak", 90, 2, 2, 1, 2), (2, "poor", 90, 2, 2, 3, 4)],
            pages.Select(static page => (page.GetProperty("page").GetInt32(), page.GetProperty("quality").GetString(), page.GetProperty("rotation").GetInt32(), page.GetProperty("bars").GetInt32(),
                page.GetProperty("doubtful").GetInt32(), page.GetProperty("firstBar").GetInt32(), page.GetProperty("lastBar").GetInt32())));

        // One report for two pages, or a header that is not one, is refused and leaves the PDF piece as it was.
        var wrong = new MusicCoreFacade();
        Json(wrong.Open("Photos.pdf", Photos));
        Assert.Equal("badRequest", Error(wrong.Call(1, "document.read", request.Replace(JsonSerializer.Serialize(Headers), JsonSerializer.Serialize(Headers[..1]), StringComparison.Ordinal))));
        Assert.Equal("badRequest", Error(wrong.Call(1, "document.read", request.Replace(JsonSerializer.Serialize(Headers), "[\"not json\", \"[]\"]", StringComparison.Ordinal))));

        // Without reports, as before: no pages kept and none answered.
        var plain = new MusicCoreFacade();
        Json(plain.Open("Photos.pdf", Photos));
        JsonElement unreported = Json(plain.Call(1, "document.read", JsonSerializer.Serialize(new { musicXml = Convert.ToBase64String(Score), engine = "homr 0.7.0", pages = 2 })));
        Assert.Empty(ProjectFile.Read(Encoding.UTF8.GetBytes(unreported.GetProperty("file").GetString()!)).Scan!.Pages);
        var reopened = new MusicCoreFacade();
        Json(reopened.Open("Photos.motiff.json", Encoding.UTF8.GetBytes(unreported.GetProperty("file").GetString()!)));
        Assert.Equal(0, Json(reopened.Call(1, "check")).GetProperty("pages").GetArrayLength());
    }

    [Fact]
    public void A_stroke_sent_with_its_margin_keeps_it_in_the_marks_answer_and_the_file()
    {
        var facade = new MusicCoreFacade();
        Json(facade.Open("Scan.musicxml", Score, """{"id": "p_margin"}"""));
        const string Stroke = """{"id": "m1", "tool": "pen", "colour": "carmine", "size": 0.16, "bar": 0, "u": -0.2, "v": 1.5, "points": [0, 0, 0.5, 0.4, 0.1, 0.5], "pressure": 0.5, "margin": "left"}""";
        const string InBar = """{"id": "m2", "tool": "pen", "colour": "graphite", "size": 0.16, "bar": 0, "u": 0.4, "v": 1, "points": [0, 0, 0.5], "pressure": 0.5}""";

        JsonElement ink = Json(facade.Call(1, "ink.add", $$"""{"strokes": [{{Stroke}}, {{InBar}}]}""")).GetProperty("ink");

        Assert.Equal("left", ink[0].GetProperty("margin").GetString());
        Assert.False(ink[1].TryGetProperty("margin", out _), "a stroke in its bar has no margin");
        Assert.Equal("left", Json(facade.Call(1, "marks")).GetProperty("ink")[0].GetProperty("margin").GetString());
        Project saved = ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));
        Assert.Equal([StrokeMargin.Left, null], saved.Marks.Ink.Select(static stroke => stroke.Margin));
        Assert.Equal("badRequest", Error(facade.Call(1, "ink.add", $$"""{"strokes": [{{Stroke.Replace("\"left\"", "\"middle\"", StringComparison.Ordinal)}}]}""")));

        // The web's pencil layer saves through ProjectFile.ReadInk and Marks.WithInk, which keep it too.
        IReadOnlyList<InkStroke> layer = ProjectFile.ReadInk(ProjectFile.WriteInk(saved.Marks.Ink));
        Assert.Equal(StrokeMargin.Left, saved.Marks.WithInk(layer).Ink[0].Margin);
    }

    [Fact]
    public void A_stroke_on_a_pdf_page_sent_with_its_margin_keeps_it()
    {
        var facade = new MusicCoreFacade();
        Json(facade.Open("Photos.pdf", Photos, """{"id": "p_pdf"}"""));
        const string Stroke = """{"id": "s1", "tool": "pen", "colour": "carmine", "size": 2, "page": 1, "u": 1.1, "v": 4, "points": [0, 0, 0.5], "pressure": 0.5, "margin": "right"}""";

        JsonElement marks = Json(facade.Call(1, "document.ink.add", $$"""{"strokes": [{{Stroke}}]}""")).GetProperty("marks");

        Assert.Equal("right", marks.GetProperty("ink")[0].GetProperty("margin").GetString());
        var again = new MusicCoreFacade();
        Json(again.Open("Photos.motiff.json", Encoding.UTF8.GetBytes(facade.Call(1, "project.save"))));
        Assert.Equal("right", Json(again.Call(1, "document.marks")).GetProperty("marks").GetProperty("ink")[0].GetProperty("margin").GetString());
    }

    private static string Error(string answer) => Json(answer).GetProperty("error").GetString()!;

    private static JsonElement Json(string answer) => JsonDocument.Parse(answer).RootElement.Clone();
}