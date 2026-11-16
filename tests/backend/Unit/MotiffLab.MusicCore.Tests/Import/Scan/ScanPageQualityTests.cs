using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Import.Scan;

/// <summary>The scan server's page report, and the page's quality in how sure a scanned bar is (v0.4, notes from photos of pages).</summary>
public class ScanPageQualityTests
{
    private const string Header =
        "[{\"page\":1,\"rotation\":90,\"sharpness\":1.2,\"contrast\":118,\"staves\":10,\"noteheads\":226,\"barLines\":59,\"spacePx\":13.0,\"measures\":28}]";

    private static PageReport Report(double sharpness = 1.2, int contrast = 118, int barLines = 6, int measures = 3) =>
        new(1, 0, sharpness, contrast, 10, 200, barLines, 13.0, measures);

    private static ImportedPiece Pages(params int[] measures) =>
        PieceImporter.Import("scan.musicxml", ScanJoin.Join([.. measures.Select(count => ScanJoinTests.Page(count))])).Piece!;

    [Fact]
    public void The_header_reads_as_numbers()
    {
        PageReport page = Assert.Single(PageReport.Parse(Header));

        Assert.Equal(new PageReport(1, 90, 1.2, 118, 10, 226, 59, 13.0, 28), page);
        Assert.Equal(PageQuality.Good, page.Quality);
    }

    [Fact]
    public void A_field_left_out_counts_as_zero_and_one_this_build_does_not_know_is_ignored()
    {
        IReadOnlyList<PageReport> pages = PageReport.Parse("[{\"contrast\":100,\"measures\":4,\"later\":7},{\"page\":5}]");

        Assert.Equal([1, 5], pages.Select(page => page.Page));
        Assert.Equal(0, pages[0].Staves);
        Assert.Equal(PageQuality.Poor, pages[1].Quality);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"page\":1}x")]
    [InlineData("3")]
    [InlineData("[3]")]
    [InlineData("[{\"contrast\":\"dark\"}]")]
    public void A_header_that_is_not_a_page_report_is_refused(string header)
    {
        Assert.Throws<FormatException>(() => PageReport.Parse(header));
    }

    [Fact]
    public void A_page_is_poor_when_unread_very_faint_or_very_soft_and_weak_when_less_so()
    {
        Assert.Equal(PageQuality.Good, Report().Quality);
        Assert.Equal(PageQuality.Poor, Report(measures: 0).Quality);
        Assert.Equal(PageQuality.Poor, Report(contrast: 58).Quality);
        Assert.Equal(PageQuality.Poor, Report(sharpness: 0.5).Quality);
        Assert.Equal(PageQuality.Weak, Report(contrast: 90).Quality);
        Assert.Equal(PageQuality.Weak, Report(sharpness: 0.76).Quality);
        Assert.Equal(PageQuality.Weak, Report(barLines: 13, measures: 3).Quality);
        Assert.Equal(PageQuality.Good, Report(barLines: 12, measures: 3).Quality);
    }

    [Fact]
    public void Bars_on_a_weak_page_are_less_sure_and_bars_on_a_poor_page_are_flagged()
    {
        ImportedPiece piece = Pages(2, 2, 2);

        ScanCheck check = ScanCheck.Of(piece, [Report(measures: 2), Report(contrast: 90, measures: 2), Report(contrast: 50, measures: 2)]);

        Assert.Equal([1, 1, 2, 2, 3, 3], check.Bars.Select(bar => bar.Page!.Value));
        Assert.Equal([0.7, 0.7, 0.5, 0.5, 0.4, 0.4], check.Bars.Select(bar => bar.Confidence));
        Assert.Equal([5, 6], check.Flagged.Select(bar => bar.Bar));
        Assert.All(check.Flagged, bar => Assert.Equal([ScanFlag.PageQuality], bar.Flags));

        ImportedPiece marked = check.Mark(piece, confirmedBars: [6]);
        Assert.Equal(
            [Attribution.Detected(0.7), Attribution.Detected(0.7), Attribution.Detected(0.5), Attribution.Detected(0.5), Attribution.Detected(0.4), Attribution.UserConfirmed],
            marked.Notes.OrderBy(note => note.Span.Onset).Select(note => note.Attribution));
    }

    [Fact]
    public void Without_reports_a_scan_is_checked_as_before()
    {
        ImportedPiece piece = Pages(2, 2);

        ScanCheck check = ScanCheck.Of(piece, null);

        Assert.All(check.Bars, bar => Assert.Null(bar.Page));
        Assert.All(check.Bars, bar => Assert.Equal(ScanCheck.CleanConfidence, bar.Confidence));
        Assert.Equal(ScanCheck.Of(piece).Bars.Select(bar => bar.Flags.Count), check.Bars.Select(bar => bar.Flags.Count));
    }

    [Fact]
    public void A_poor_page_raises_a_page_quality_flag_in_the_project_file_since_its_format_takes_one()
    {
        ImportedPiece piece = Pages(2, 2);
        ScanCheck check = ScanCheck.Of(piece, [Report(measures: 2), Report(contrast: 50, measures: 2)]);

        Assert.Equal(2, check.Flagged.Count());
        Assert.Equal(
            [new CheckFlag(3, CheckSource.Scan, CheckReason.PageQuality, false), new CheckFlag(4, CheckSource.Scan, CheckReason.PageQuality, true)],
            CheckFlag.FromScan(check, [4]));
    }
}