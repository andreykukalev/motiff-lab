using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// Where each piece made from a collection is in it (v0.4, «Пьесы в файле», the format's "partOf" within version 15): the
/// collection's pages or the stretch of its recording, and a scan's page reports on those pages.
/// </summary>
public class PiecePartsPlaceTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static PageReport Report(int contrast) => new(1, 0, 1.1, contrast, 4, 6, 2, 15.5, 2);

    [Fact]
    public void Each_part_of_a_scan_names_the_collections_pages_it_takes_and_keeps_their_page_reports()
    {
        Project scan = PiecePartsTests.Scan();
        Project book = scan with { Scan = scan.Scan! with { Pages = PageReport.ForPages([Report(118), Report(50), Report(110)], 3) } };

        PieceSplit split = PieceParts.Split(book, [new("Prelude", PartUnit.Pages, 1, 1), new("Waltz", PartUnit.Pages, 2, 3) { Pdf = PiecePartsTests.Pdf(2) }], PiecePartsTests.Options());

        (Project prelude, Project waltz) = (split.Parts[0].Project, split.Parts[1].Project);
        Assert.Equal((PieceParts.OriginalId, (1, 1)), (prelude.PartOf!.Original, prelude.PartOf.Pages!.Value));
        Assert.Equal((PieceParts.OriginalId, (2, 3)), (waltz.PartOf!.Original, waltz.PartOf.Pages!.Value));
        Assert.Null(waltz.PartOf.Milliseconds);
        Assert.NotNull(waltz.FindOriginal(PieceParts.OriginalId));

        // The waltz keeps the reports of the collection's pages 2 and 3 as its own pages 1 and 2: its first page is poor.
        Assert.Equal([(1, 50), (2, 110)], waltz.Scan!.Pages.Select(static page => (page.Page, page.Contrast)));
        Assert.Equal([118], prelude.Scan!.Pages.Select(static page => page.Contrast));
        ScanCheck check = waltz.Scan.CheckOf(OpenedProject.Open(waltz).Piece);
        Assert.Equal([1, 2], check.Flagged.Where(static bar => bar.Flags.Contains(ScanFlag.PageQuality)).Select(static bar => bar.Bar));

        // Saved and read back, the link and the reports stay.
        Project saved = ProjectFile.Read(ProjectFile.Write(waltz));
        Assert.Equal(waltz.PartOf, saved.PartOf);
        Assert.Equal(waltz.Scan.Pages, saved.Scan!.Pages);

        // Reports that do not match the pages are not given to a part.
        Project unmatched = scan with { Scan = scan.Scan! with { Pages = PageReport.ForPages([Report(118), Report(50)], 2) } };
        Assert.All(PieceParts.Split(unmatched, [new("Prelude", PartUnit.Pages, 1, 1)], PiecePartsTests.Options()).Parts, static part => Assert.Empty(part.Project.Scan!.Pages));
    }

    [Fact]
    public void A_part_of_a_recording_heard_names_the_stretch_of_the_recording_it_takes()
    {
        // A recording heard that describes no original is described as the recording, held by no device, so each part names its stretch.
        Project recording = PiecePartsTests.Recording(2);
        OpenedProject opened = OpenedProject.Open(recording);
        int last = opened.Piece.Meter.BarAt(new Tick(opened.Piece.End.Value - 1)).Number;
        PieceSplit heard = PieceParts.Split(recording, [new("Start", PartUnit.Bars, opened.Piece.Meter.FirstBarNumber, 3), new("End", PartUnit.Bars, 4, last)], PiecePartsTests.Options(device: HoldingDevice.Web));
        Assert.Equal((OriginalKind.Audio, "recording", 0), (heard.Original!.Kind, heard.Original.FileName, heard.Original.Held.Count));
        Assert.All(heard.Parts, part => Assert.Equal(part.Milliseconds, part.Project.PartOf!.Milliseconds));
        Assert.All(heard.Parts, static part => Assert.Null(part.Project.PartOf!.Pages));
        Assert.All(heard.Parts, static part => Assert.Equal(PieceParts.OriginalId, part.Project.Transcription!.From));
        Assert.Equal(heard.Parts[1].Project.PartOf, ProjectFile.Read(ProjectFile.Write(heard.Parts[1].Project)).PartOf);
    }

    [Fact]
    public void A_part_of_a_pdf_names_its_pages_and_one_of_a_score_cut_by_bars_nothing()
    {
        Project pdf = Project.CreateDocument("p_pdf", "Songbook.pdf", PiecePartsTests.Pdf(5), s_now);
        PieceSplit pages = PieceParts.Split(pdf, [new("One", PartUnit.Pages, 1, 2) { Pdf = PiecePartsTests.Pdf(2) }, new("Two", PartUnit.Pages, 3, 5) { Pdf = PiecePartsTests.Pdf(3) }], PiecePartsTests.Options());
        Assert.Equal([(1, 2), (3, 5)], pages.Parts.Select(static part => part.Project.PartOf!.Pages!.Value));

        // A score cut by bars has no pages and no stretch, and the format keeps no place in bars.
        Assert.All(PieceParts.Split(PiecePartsTests.Book(), [new("Für Elise", PartUnit.Bars, 0, 105)], PiecePartsTests.Options()).Parts, static part => Assert.Null(part.Project.PartOf));

        // The facade's summary of each part says it too.
        string request = JsonSerializer.Serialize(new { file = Encoding.UTF8.GetString(ProjectFile.Write(pdf)), parts = new[] { new { title = "Two", pages = new { first = 3, last = 5 }, pdf = Convert.ToBase64String(PiecePartsTests.Pdf(3)) } } });
        using JsonDocument answer = JsonDocument.Parse(new MusicCoreFacade().Call(0, "parts.split", request));
        JsonElement partOf = answer.RootElement.GetProperty("parts")[0].GetProperty("project").GetProperty("partOf");
        Assert.Equal(("src_book", 3, 5), (partOf.GetProperty("original").GetString(), partOf.GetProperty("pages").GetProperty("first").GetInt32(), partOf.GetProperty("pages").GetProperty("last").GetInt32()));
        Assert.Equal(JsonValueKind.Null, partOf.GetProperty("milliseconds").ValueKind);
    }
}