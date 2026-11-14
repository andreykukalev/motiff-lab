using System.Text;
using System.Xml.Linq;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;

namespace MotiffLab.MusicCore.Tests.Import.Scan;

/// <summary>
/// The time signature of a score read a page a request (v0.5.6, #533 section 1) and joined (issue #543): the engine reads
/// each page on its own and writes, where it sees a signature printed, the median length of that page's bars, so a page
/// that cannot settle its meter alone takes the meter of the pages it continues, and a real change of meter, at the top of
/// a page or inside one, keeps its own.
/// </summary>
public class ScanMeterPagesTests
{
    private const int Quarter = 480;

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", name));

    private static ImportedPiece Import(byte[] score) => PieceImporter.Import("scan.musicxml", score).Piece!;

    /// <summary>Each time signature as "beats/beat-type@bar", bars from 1.</summary>
    private static string[] Meters(ImportedPiece piece) =>
        [.. piece.MusicXml!.TimeSignatures.Select(time => $"{time.Numerator}/{time.Denominator}@{time.MeasureIndex + 1}")];

    /// <summary>Each bar's length in half quarters.</summary>
    private static long[] Halves(ImportedPiece piece) => [.. piece.MusicXml!.Measures.Select(measure => measure.Span.Duration * 2 / Quarter)];

    /// <summary>
    /// The 4/4 page the engine read as 5/4 cut in two pages as the engine answers them a page a request: each a whole
    /// score, its measures numbered from 1, the first with the attributes the engine writes at the top of a page, the
    /// second page's signature written too (as where one is printed there), with the median of its own bars.
    /// </summary>
    private static byte[][] GracePages(int firstPageBars, int secondPageBeats)
    {
        XDocument first = XDocument.Parse(Encoding.UTF8.GetString(Fixture("homr-grace-eighths-4-4.musicxml")));
        XDocument second = new(first);
        List<XElement> attributes = [.. first.Root!.Element("part")!.Element("measure")!.Elements("attributes").Select(element => new XElement(element))];
        attributes.Single(element => element.Element("time") is not null).Element("time")!.Element("beats")!.Value = secondPageBeats.ToString(System.Globalization.CultureInfo.InvariantCulture);

        first.Root!.Element("part")!.Elements("measure").Skip(firstPageBars).Remove();
        second.Root!.Element("part")!.Elements("measure").Take(firstPageBars).Remove();
        List<XElement> measures = [.. second.Root!.Element("part")!.Elements("measure")];
        measures[0].AddFirst(attributes);
        for (int i = 0; i < measures.Count; i++)
        {
            measures[i].SetAttributeValue("number", (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return [Encoding.UTF8.GetBytes(first.ToString(SaveOptions.DisableFormatting)), Encoding.UTF8.GetBytes(second.ToString(SaveOptions.DisableFormatting))];
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void A_page_of_few_bars_with_the_engines_5_4_takes_the_4_4_of_the_page_before_it(int secondPageBeats)
    {
        byte[][] pages = GracePages(firstPageBars: 5, secondPageBeats);
        ImportedPiece alone = Import(ScanRepair.Repair(pages[1]));

        ImportedPiece joined = Import(ScanJoin.Join(pages));

        // Alone, bars 6-8 settle no length (six, four and a half, six quarters), so the page keeps what the engine wrote.
        Assert.Equal([$"{secondPageBeats}/4@1"], Meters(alone));
        // Joined, they are 4/4 like the page before: bar 8 comes back to four quarters, bars 6 and 7 stay flagged as read.
        Assert.Equal(["4/4@1"], Meters(joined));
        Assert.Equal([8, 8, 8, 8, 8, 12, 9, 8], Halves(joined));
        Assert.Equal(
            ["6", "7"],
            ScanCheck.Of(joined).Bars.Where(bar => bar.Flags.Contains(ScanFlag.BarTooLong)).Select(bar => bar.Measure));
    }

    [Fact]
    public void Read_a_page_a_request_or_in_one_request_the_score_is_the_same()
    {
        byte[][] pages = GracePages(firstPageBars: 5, secondPageBeats: 5);
        byte[] joined = ScanJoin.Join(pages);

        Assert.Same(joined, ScanRepair.Repair(joined));
        Assert.Equal(joined, ScanJoin.Join(ScanJoin.Split(joined)));
        Assert.Equal(Halves(Import(ScanRepair.Repair(Fixture("homr-grace-eighths-4-4.musicxml")))), Halves(Import(joined)));

        // Page 2 read again, as the engine wrote it, is repaired with page 1 as when it was first joined.
        ImportedPiece reread = Import(ScanJoin.Replace(joined, new Dictionary<int, byte[]> { [2] = pages[1] }));
        Assert.Equal(Meters(Import(joined)), Meters(reread));
        Assert.Equal(Halves(Import(joined)), Halves(reread));
    }

    [Fact]
    public void A_change_to_3_4_at_the_top_of_a_page_and_back_to_4_4_at_the_next_is_kept()
    {
        byte[][] pages = [.. Enumerable.Range(1, 3).Select(page => Fixture($"homr-meter-change-page-{page}.musicxml"))];

        ImportedPiece joined = Import(ScanJoin.Join(pages));

        // Page 3 alone (two split bars and a misread one) cannot settle its 4/4; with page 1 it does, and page 2's own
        // five bars of three quarters keep its 3/4.
        Assert.Equal(["5/4@1"], Meters(Import(ScanRepair.Repair(pages[2]))));
        Assert.Equal(["4/4@1", "3/4@7", "4/4@12"], Meters(joined));
        Assert.Equal([8, 8, 8, 8, 8, 8, 6, 6, 6, 6, 6, 8, 8, 9], Halves(joined));
        Assert.Contains("inferred from the bar contents", Encoding.UTF8.GetString(ScanJoin.Join(pages)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_change_to_6_8_inside_a_page_is_kept_with_its_upper_number_from_its_bars()
    {
        byte[] page = Fixture("homr-meter-change-mid-page.musicxml");

        ImportedPiece read = Import(page);
        ImportedPiece repaired = Import(ScanJoin.Join([page, page]));

        Assert.Equal(["4/4@1", "8/8@5", "4/4@9"], Meters(read));
        Assert.Equal(["4/4@1", "6/8@5", "4/4@9", "6/8@15", "4/4@19"], Meters(repaired));
        Assert.Equal([8, 8, 8, 8, 6, 6, 6, 6, 8, 8, 8, 8, 8, 8, 6, 6, 6, 6, 8, 8], Halves(repaired));
    }

    [Fact]
    public void A_page_without_a_signature_of_its_own_goes_on_in_the_meter_before_it()
    {
        byte[][] pages = GracePages(firstPageBars: 5, secondPageBeats: 5);
        XDocument second = XDocument.Parse(Encoding.UTF8.GetString(pages[1]));
        second.Descendants("time").Remove();

        ImportedPiece joined = Import(ScanJoin.Join([pages[0], Encoding.UTF8.GetBytes(second.ToString(SaveOptions.DisableFormatting))]));

        Assert.Equal(["4/4@1"], Meters(joined));
        Assert.Equal([8, 8, 8, 8, 8, 12, 9, 8], Halves(joined));
    }
}