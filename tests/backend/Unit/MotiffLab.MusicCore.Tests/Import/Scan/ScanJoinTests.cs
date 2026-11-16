using System.Text;
using System.Text.Json;
using System.Xml.Linq;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Import.Scan;

/// <summary>Pages read one at a time joined into one score and split back (v0.4, notes from photos of pages).</summary>
public class ScanJoinTests
{
    /// <summary>One page as the engine writes it: one part, its own attributes, measures numbered from 1, in 3/4.</summary>
    internal static byte[] Page(int measures, int divisions = 1, string firstMeasureExtra = "")
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><score-partwise version=\"4.0\"><work><work-title>Page</work-title></work>")
            .Append("<part-list><score-part id=\"P1\"><part-name>Piano</part-name></score-part></part-list><part id=\"P1\">");
        for (int i = 1; i <= measures; i++)
        {
            string attributes = i == 1 ? $"{firstMeasureExtra}<attributes><divisions>{divisions}</divisions><time><beats>3</beats><beat-type>4</beat-type></time></attributes>" : "";
            xml.Append($"<measure number=\"{i}\">{attributes}<note><pitch><step>C</step><octave>5</octave></pitch><duration>{3 * divisions}</duration></note></measure>");
        }

        return Encoding.UTF8.GetBytes(xml.Append("</part></score-partwise>").ToString());
    }

    private static XElement Part(byte[] score) => XDocument.Parse(Encoding.UTF8.GetString(score)).Root!.Element("part")!;

    private static string[] Numbers(byte[] score) => [.. Part(score).Elements("measure").Select(measure => (string)measure.Attribute("number")!)];

    private static ImportedPiece Import(byte[] score) => PieceImporter.Import("scan.musicxml", score).Piece!;

    [Fact]
    public void Pages_follow_each_other_and_measure_numbers_run_on()
    {
        byte[] score = ScanJoin.Join([Page(3), Page(2), Page(1)]);

        Assert.Equal(["1", "2", "3", "4", "5", "6"], Numbers(score));
        Assert.Single(XDocument.Parse(Encoding.UTF8.GetString(score)).Root!.Elements("part-list"));
    }

    [Fact]
    public void The_first_measure_of_each_later_page_starts_a_page_so_the_pages_are_known()
    {
        byte[] score = ScanJoin.Join([Page(3), Page(2), Page(1)]);

        Assert.Equal([3, 5], Part(score).Elements("measure").Select((measure, index) => (measure, index))
            .Where(pair => pair.measure.Elements("print").Any(print => (string?)print.Attribute("new-page") == "yes"))
            .Select(pair => pair.index));
        PageMap map = PageMap.Of(Import(score), 3);
        Assert.Equal(3, map.Pages);
        Assert.Equal(map.StartOf(2), Import(score).Meter.GetBar(4).Span.Onset);
    }

    [Fact]
    public void A_page_keeps_its_own_divisions_so_durations_stay_right()
    {
        ImportedPiece piece = Import(ScanJoin.Join([Page(2, divisions: 1), Page(2, divisions: 8)]));

        Assert.Equal(4, piece.Notes.Count);
        Assert.All(piece.Notes, note => Assert.Equal(piece.Notes[0].Span.Duration, note.Span.Duration));
    }

    [Fact]
    public void Layout_the_engine_gave_a_page_s_first_measure_is_kept()
    {
        byte[] score = ScanJoin.Join([Page(1), Page(1, firstMeasureExtra: "<print new-system=\"yes\"/>")]);

        XElement print = Part(score).Elements("measure").Last().Element("print")!;
        Assert.Equal("yes", (string?)print.Attribute("new-system"));
        Assert.Equal("yes", (string?)print.Attribute("new-page"));
    }

    [Fact]
    public void One_page_comes_back_as_it_is()
    {
        byte[] page = Page(2);

        Assert.Same(page, ScanJoin.Join([page]));
    }

    [Fact]
    public void Joining_is_deterministic()
    {
        Assert.Equal(ScanJoin.Join([Page(3), Page(2)]), ScanJoin.Join([Page(3), Page(2)]));
    }

    [Fact]
    public void Pages_that_cannot_be_joined_say_why()
    {
        byte[] twoParts = Encoding.UTF8.GetBytes("<score-partwise><part-list/><part id=\"P1\"><measure number=\"1\"/></part><part id=\"P2\"><measure number=\"1\"/></part></score-partwise>");

        Assert.Contains("Page 2 has 2 parts where page 1 has 1", Assert.Throws<ArgumentException>(() => ScanJoin.Join([Page(1), twoParts])).Message, StringComparison.Ordinal);
        Assert.Contains("page 2 is damaged", Assert.Throws<ArgumentException>(() => ScanJoin.Join([Page(1), "<score"u8.ToArray()])).Message, StringComparison.Ordinal);
        Assert.Contains("not a partwise", Assert.Throws<ArgumentException>(() => ScanJoin.Join(["<score-timewise/>"u8.ToArray()])).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ScanJoin.Join([]));
    }

    [Fact]
    public void A_joined_score_splits_back_into_its_pages_and_joins_again_unchanged()
    {
        byte[] score = ScanJoin.Join([Page(3), Page(2, firstMeasureExtra: "<print new-system=\"yes\"/>"), Page(1)]);

        IReadOnlyList<byte[]> pages = ScanJoin.Split(score);

        Assert.Equal(3, pages.Count);
        Assert.Equal([["1", "2", "3"], ["4", "5"], ["6"]], pages.Select(Numbers));
        Assert.All(pages, page => Assert.DoesNotContain("new-page", Encoding.UTF8.GetString(page), StringComparison.Ordinal));
        Assert.All(pages, page => Assert.Equal("Page", (string?)XDocument.Parse(Encoding.UTF8.GetString(page)).Root!.Element("work")!.Element("work-title")));
        Assert.Equal("yes", (string?)Part(pages[1]).Element("measure")!.Element("print")!.Attribute("new-system"));
        Assert.Equal(score, ScanJoin.Join(pages));
    }

    [Fact]
    public void A_score_without_page_marks_is_one_page()
    {
        Assert.Single(ScanJoin.Split(Page(4)));
    }

    [Fact]
    public void A_page_read_again_takes_the_place_of_its_measures()
    {
        byte[] score = ScanJoin.Join([Page(3), Page(2), Page(1)]);

        byte[] replaced = ScanJoin.Replace(score, new Dictionary<int, byte[]> { [2] = Page(4) });

        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8"], Numbers(replaced));
        Assert.Equal(3, ScanJoin.Split(replaced).Count);
        Assert.Throws<ArgumentException>(() => ScanJoin.Replace(score, new Dictionary<int, byte[]> { [4] = Page(1) }));
    }

    [Fact]
    public void The_engine_s_reading_of_a_real_page_joins_with_itself()
    {
        byte[] page = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"));

        ImportedPiece once = Import(page);
        ImportedPiece twice = Import(ScanJoin.Join([page, page]));

        Assert.Equal(once.Notes.Count * 2, twice.Notes.Count);
        Assert.Equal(2, PageMap.Of(twice, 2).Pages);
    }

    [Fact]
    public void The_facade_joins_pages_and_counts_the_doubtful_bars_of_each()
    {
        var facade = new MusicCoreFacade();
        string[] pages = [Encoding.UTF8.GetString(Page(3)), Encoding.UTF8.GetString(Page(2))];
        string request = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["pages"] = pages,
            ["reports"] = new object[]
            {
                "[{\"page\":1,\"rotation\":0,\"sharpness\":1.2,\"contrast\":120,\"staves\":10,\"noteheads\":170,\"barLines\":6,\"spacePx\":13.0,\"measures\":3}]",
                new Dictionary<string, object> { ["page"] = 1, ["sharpness"] = 0.7, ["contrast"] = 110, ["barLines"] = 4, ["measures"] = 2 },
            },
        });

        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "scan.join", request));

        JsonElement root = answer.RootElement;
        Assert.Equal(["1", "2", "3", "4", "5"], Numbers(Encoding.UTF8.GetBytes(root.GetProperty("score").GetString()!)));
        JsonElement[] reported = [.. root.GetProperty("pages").EnumerateArray()];
        Assert.Equal([(1, 3, 0, "good"), (2, 2, 2, "weak")], reported.Select(page =>
            (page.GetProperty("page").GetInt32(), page.GetProperty("bars").GetInt32(), page.GetProperty("doubtful").GetInt32(), page.GetProperty("quality").GetString())));
    }

    [Fact]
    public void The_facade_replaces_a_page_read_again_and_says_what_is_wrong()
    {
        var facade = new MusicCoreFacade();
        string score = Encoding.UTF8.GetString(ScanJoin.Join([Page(3), Page(2)]));
        string request = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["score"] = score,
            ["replace"] = new[] { new Dictionary<string, object> { ["page"] = 2, ["musicxml"] = Encoding.UTF8.GetString(Page(1)) } },
        });

        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "scan.join", request));
        Assert.Equal(["1", "2", "3", "4"], Numbers(Encoding.UTF8.GetBytes(answer.RootElement.GetProperty("score").GetString()!)));
        Assert.All(answer.RootElement.GetProperty("pages").EnumerateArray(), page => Assert.Equal(JsonValueKind.Null, page.GetProperty("quality").ValueKind));

        using JsonDocument wrongReports = JsonDocument.Parse(facade.Call(0, "scan.join", "{\"pages\": [\"<score-partwise><part-list/><part id=\\\"P1\\\"><measure number=\\\"1\\\"/></part></score-partwise>\"], \"reports\": [[], []]}"));
        Assert.Equal("badRequest", wrongReports.RootElement.GetProperty("error").GetString());
        using JsonDocument damaged = JsonDocument.Parse(facade.Call(0, "scan.join", "{\"pages\": [\"<score\"]}"));
        Assert.Equal("badRequest", damaged.RootElement.GetProperty("error").GetString());
        Assert.Contains("page 1 is damaged", damaged.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
    }
}