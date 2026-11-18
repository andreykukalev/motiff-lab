using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Tests.Import.Scan;

/// <summary>A score longer than the scan server reads at a time, read in parts and joined back (v0.5, issue #479).</summary>
public class ScanPartsTests
{
    private static ImportedPiece Import(byte[] score) => PieceImporter.Import("scan.musicxml", score).Piece!;

    [Fact]
    public void Parts_joined_are_the_pages_joined_and_each_page_of_the_pdf_still_starts_where_it_did()
    {
        byte[][] pages = [ScanJoinTests.Page(3), ScanJoinTests.Page(2), ScanJoinTests.Page(4), ScanJoinTests.Page(1), ScanJoinTests.Page(2), ScanJoinTests.Page(3)];
        // What the server sends for each part of four pages: its pages already joined, as src/backend/omr/app/merge.py does.
        byte[][] parts = [ScanJoin.Join(pages[0..4]), ScanJoin.Join(pages[4..6])];

        byte[] joined = ScanJoin.Join(parts);

        Assert.Equal(ScanJoin.Join(pages), joined);
        Assert.Equal(6, ScanJoin.Split(joined).Count);
        PageMap map = PageMap.Of(Import(joined), pages.Length);
        Assert.Equal(PageMap.Of(Import(ScanJoin.Join(pages)), pages.Length), map);
        Assert.Equal(6, map.Pages);
        // Page 5 starts at bar 11 (3 + 2 + 4 + 1 bars before it), in 3/4 at 480 ticks a quarter.
        Assert.Equal(10 * 3 * 480, map.StartOf(5).Value);
    }

    [Fact]
    public void The_facade_joins_parts_as_it_joins_pages_with_one_report_per_page_of_the_pdf()
    {
        var facade = new MusicCoreFacade();
        string report = "{\"page\":1,\"sharpness\":1.2,\"contrast\":120,\"barLines\":4,\"measures\":2}";
        string request = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["pages"] = new[]
            {
                Encoding.UTF8.GetString(ScanJoin.Join([ScanJoinTests.Page(2), ScanJoinTests.Page(2), ScanJoinTests.Page(2), ScanJoinTests.Page(2)])),
                Encoding.UTF8.GetString(ScanJoin.Join([ScanJoinTests.Page(2), ScanJoinTests.Page(2)])),
            },
            ["reports"] = new[] { $"[{report},{report},{report},{report}]", $"[{report},{report}]" },
        });

        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "scan.join", request));

        JsonElement[] reported = [.. answer.RootElement.GetProperty("pages").EnumerateArray()];
        Assert.Equal([1, 2, 3, 4, 5, 6], reported.Select(page => page.GetProperty("page").GetInt32()));
        Assert.All(reported, page => Assert.Equal(2, page.GetProperty("bars").GetInt32()));
    }
}