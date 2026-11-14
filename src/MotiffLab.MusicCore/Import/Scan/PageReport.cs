using System.Text.Json;

namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>How well a photographed or scanned page can be trusted, from its <see cref="PageReport"/>.</summary>
public enum PageQuality
{
    Good,

    /// <summary>Readable, but its bars are less sure: they count as doubtful without a flag.</summary>
    Weak,

    /// <summary>Too dark, too soft or not read: every bar on it is flagged.</summary>
    Poor,
}

/// <summary>
/// What the scan server saw of one page (services/omr, header <c>X-Page-Report</c>): numbers only, never the music.
/// The page's quality is worked out here from the numbers each time, so the thresholds can change without touching
/// anything saved.
/// </summary>
/// <param name="Page">The page's number in the upload, from 1.</param>
/// <param name="Rotation">Degrees the server turned the page clockwise before reading it: 0, 90, 180 or 270.</param>
/// <param name="Sharpness">How steep the edges of the print are against its contrast; about 1 is crisp.</param>
/// <param name="Contrast">How much darker the darkest print is than the paper around it, in grey levels (0-255).</param>
/// <param name="Staves">Staves the engine found.</param>
/// <param name="Noteheads">Noteheads the engine found.</param>
/// <param name="BarLines">Bar lines the engine found, staff by staff.</param>
/// <param name="SpacePx">The median notehead height, about one staff space, in the engine's working copy of the page.</param>
/// <param name="Measures">Measures in the music that came out of the page; 0 when the page could not be read.</param>
public sealed record PageReport(
    int Page,
    int Rotation,
    double Sharpness,
    int Contrast,
    int Staves,
    int Noteheads,
    int BarLines,
    double SpacePx,
    int Measures)
{
    /// <summary>Print fainter than this makes a poor page: on the 14 test photos the dim one had 58, the others 105–127.</summary>
    public const int PoorContrast = 70;

    /// <summary>Print fainter than this makes a weak page.</summary>
    public const int WeakContrast = 95;

    /// <summary>Edges softer than this make a poor page.</summary>
    public const double PoorSharpness = 0.6;

    /// <summary>Edges softer than this make a weak page: on the test photos the readable pages had 0.86–1.28.</summary>
    public const double WeakSharpness = 0.8;

    /// <summary>A page whose measures are fewer than its bar lines over this many lost bars on the way.</summary>
    public const int BarLinesPerMeasure = 4;

    /// <summary>The page's quality: poor when it was not read or its print is very faint or soft, weak when less so.</summary>
    public PageQuality Quality =>
        Measures == 0 || Contrast < PoorContrast || Sharpness < PoorSharpness ? PageQuality.Poor
        : Contrast < WeakContrast || Sharpness < WeakSharpness || Measures * BarLinesPerMeasure < BarLines ? PageQuality.Weak
        : PageQuality.Good;

    /// <summary>
    /// The pages of an <c>X-Page-Report</c> header: a JSON list of objects, in page order. A field the server does not
    /// send counts as 0 and a page without "page" takes its place in the list; fields this build does not know are left out.
    /// </summary>
    /// <exception cref="FormatException">The header is not a JSON list of objects with numbers.</exception>
    public static IReadOnlyList<PageReport> Parse(string header)
    {
        ArgumentNullException.ThrowIfNull(header);
        try
        {
            using JsonDocument document = JsonDocument.Parse(header);
            return Of(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new FormatException("The page report is not JSON.", exception);
        }
    }

    /// <summary>
    /// The reports as the pages of a score of <paramref name="pages"/> pages, one each in order, numbered by their place from
    /// 1: a page read on its own is page 1 of its own report, and becomes the page it is in the joined score.
    /// </summary>
    /// <exception cref="ArgumentException">Not one report per page.</exception>
    public static IReadOnlyList<PageReport> ForPages(IReadOnlyList<PageReport> reports, int pages)
    {
        ArgumentNullException.ThrowIfNull(reports);
        return reports.Count == pages
            ? [.. reports.Select((report, index) => report with { Page = index + 1 })]
            : throw new ArgumentException($"Send one page report per page: {reports.Count} for {pages} pages.", nameof(reports));
    }

    /// <summary>
    /// The reports as the pages of a scan's MusicXML (<see cref="ForPages"/>), its pages told by the <c>new-page</c> marks the
    /// scan server and <see cref="ScanJoin"/> put on the first measure of each page after the first.
    /// </summary>
    /// <exception cref="ArgumentException">Not one report per page, or the MusicXML is not a partwise score.</exception>
    public static IReadOnlyList<PageReport> ForScore(IReadOnlyList<PageReport> reports, byte[] musicXml) =>
        ForPages(reports, ScanJoin.Split(musicXml).Count);

    /// <summary>The pages of a report already read as JSON: a list of objects, or one object for one page.</summary>
    /// <exception cref="FormatException">The JSON is not a page report.</exception>
    public static IReadOnlyList<PageReport> Of(JsonElement report)
    {
        if (report.ValueKind == JsonValueKind.Object)
        {
            return [One(report, 1)];
        }

        if (report.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("The page report is a list of pages.");
        }

        return [.. report.EnumerateArray().Select((page, index) => One(page, index + 1))];
    }

    private static PageReport One(JsonElement page, int place)
    {
        if (page.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Each page of the report is an object.");
        }

        return new PageReport(
            Page: (int)Number(page, "page", place),
            Rotation: (int)Number(page, "rotation", 0),
            Sharpness: Number(page, "sharpness", 0),
            Contrast: (int)Number(page, "contrast", 0),
            Staves: (int)Number(page, "staves", 0),
            Noteheads: (int)Number(page, "noteheads", 0),
            BarLines: (int)Number(page, "barLines", 0),
            SpacePx: Number(page, "spacePx", 0),
            Measures: (int)Number(page, "measures", 0));
    }

    private static double Number(JsonElement page, string name, double missing)
    {
        if (!page.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return missing;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number) && Math.Abs(number) < int.MaxValue
            ? number
            : throw new FormatException($"\"{name}\" in the page report is not a number.");
    }
}