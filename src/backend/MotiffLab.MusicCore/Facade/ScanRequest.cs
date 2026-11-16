using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Import.Vector;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// scan.join on piece 0 (contract 12, a call added; v0.4, notes from photos of pages): the MusicXML of pages read one
/// request at a time joined into one score (<see cref="ScanJoin"/>), with each page's bars and how many of them are
/// doubtful once the scan server's page reports count (<see cref="ScanCheck.Of(ImportedPiece, IReadOnlyList{PageReport})"/>).
/// Nothing is kept: the app opens the score as any scan, and the reports are not saved.
/// </summary>
internal static class ScanRequest
{
    /// <summary>
    /// {"pages": [MusicXML text, one per page]} or {"score": a joined score, "replace": [{"page", "musicxml"}]} for pages read
    /// again, and "reports" (optional): one per page in order, each the X-Page-Report header as text, its list or one of
    /// its objects → {"score": the joined MusicXML, "pages": [{"page", "bars", "doubtful", "quality": "good", "weak",
    /// "poor" or null without reports}]}. "doubtful" counts the bars below the 0.6 a hint needs: flagged, or on a weak page.
    /// </summary>
    public static string Join(Request fields)
    {
        byte[] score = Score(fields);
        IReadOnlyList<byte[]> pages = ScanJoin.Split(score);
        IReadOnlyList<PageReport>? reports = Reports(fields, pages.Count);
        ImportedPiece piece = PieceImporter.Import("scan.musicxml", score) is { Piece: { } read }
            ? read
            : throw FacadeException.BadRequest("The joined pages could not be read as music.");
        ScanCheck check = ScanCheck.Of(piece, reports);
        PageMap map = PageMap.Of(piece, pages.Count);
        return Answer.Write(json =>
        {
            json.WriteString("score", Encoding.UTF8.GetString(score));
            json.WriteStartArray("pages");
            for (int page = 1; page <= pages.Count; page++)
            {
                ScanBar[] bars = [.. check.Bars.Where(bar => map.PageOf(bar.Span.Onset) == page)];
                json.WriteStartObject();
                json.WriteNumber("page", page);
                json.WriteNumber("bars", bars.Length);
                json.WriteNumber("doubtful", bars.Count(static bar => bar.Confidence < ScanCheck.CleanConfidence));
                if (reports is null)
                {
                    json.WriteNull("quality");
                }
                else
                {
                    json.WriteString("quality", JsonNamingPolicy.CamelCase.ConvertName(reports[page - 1].Quality.ToString()));
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
        });
    }

    /// <summary>
    /// scan.vector on piece 0 (within contract 12, a call added; decision record 0029, «Авто»): {"pdf": base64} → {"engine":
    /// "vector 1", "pages": [{"page" (from 1), "kind": "read", "scan" or "blank", "notes", "bars", "issues": [English],
    /// "musicxml" (a page read here, the scan server's shape, for scan.join), "report" (its page report as the server writes
    /// one)}]}. A page of kind "scan" goes to the scan server; a blank one has no music. Pure: nothing is kept or sent.
    /// </summary>
    public static string Vector(Request fields)
    {
        byte[] pdf;
        IReadOnlyList<VectorPageReading> pages;
        try
        {
            pdf = Convert.FromBase64String(fields.Required("pdf"));
            pages = VectorReader.Read(pdf);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw FacadeException.BadRequest(exception is FormatException ? "\"pdf\" is not base64." : exception.Message);
        }

        return Answer.Write(json =>
        {
            json.WriteString("engine", VectorReader.EngineAndVersion);
            json.WriteStartArray("pages");
            foreach (VectorPageReading page in pages)
            {
                json.WriteStartObject();
                json.WriteNumber("page", page.Number);
                json.WriteString("kind", JsonNamingPolicy.CamelCase.ConvertName(page.Kind.ToString()));
                json.WriteNumber("notes", page.Notes);
                json.WriteNumber("bars", page.Bars);
                json.WriteStartArray("issues");
                foreach (string issue in page.Issues)
                {
                    json.WriteStringValue(issue);
                }

                json.WriteEndArray();
                if (page.MusicXml is { } musicXml)
                {
                    json.WriteString("musicxml", Encoding.UTF8.GetString(musicXml));
                    Report(json, page.Report);
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
        });
    }

    /// <summary>A page report with the keys of the scan server's X-Page-Report header.</summary>
    private static void Report(Utf8JsonWriter json, PageReport report)
    {
        json.WriteStartObject("report");
        json.WriteNumber("page", report.Page);
        json.WriteNumber("rotation", report.Rotation);
        json.WriteNumber("sharpness", report.Sharpness);
        json.WriteNumber("contrast", report.Contrast);
        json.WriteNumber("staves", report.Staves);
        json.WriteNumber("noteheads", report.Noteheads);
        json.WriteNumber("barLines", report.BarLines);
        json.WriteNumber("spacePx", report.SpacePx);
        json.WriteNumber("measures", report.Measures);
        json.WriteEndObject();
    }

    private static byte[] Score(Request fields)
    {
        try
        {
            if (fields.Has("score"))
            {
                Dictionary<int, byte[]> replaced = [];
                foreach (JsonElement element in fields.Elements("replace"))
                {
                    Request page = Request.Of(element);
                    replaced[page.Int("page") ?? throw FacadeException.BadRequest("Each page to replace needs \"page\".")] = Encoding.UTF8.GetBytes(page.Required("musicxml"));
                }

                return ScanJoin.Replace(Encoding.UTF8.GetBytes(fields.Required("score")), replaced);
            }

            IReadOnlyList<string> pages = fields.Texts("pages");
            return pages.Count > 0
                ? ScanJoin.Join([.. pages.Select(Encoding.UTF8.GetBytes)])
                : throw FacadeException.BadRequest("Send \"pages\", the MusicXML read from each page, or \"score\".");
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }

    private static IReadOnlyList<PageReport>? Reports(Request fields, int pages)
    {
        if (ReportsIn(fields) is not { } reports)
        {
            return null;
        }

        try
        {
            return PageReport.ForPages(reports, pages);
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }

    /// <summary>
    /// "reports" as scan.join and document.read take them: each the X-Page-Report header as text (a list of pages), its list or
    /// one of its objects, all the pages in order; null when the request has none.
    /// </summary>
    internal static List<PageReport>? ReportsIn(Request fields)
    {
        if (!fields.Has("reports"))
        {
            return null;
        }

        List<PageReport> reports = [];
        try
        {
            foreach (JsonElement report in fields.Elements("reports"))
            {
                reports.AddRange(report.ValueKind == JsonValueKind.String ? PageReport.Parse(report.GetString()!) : PageReport.Of(report));
            }
        }
        catch (FormatException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        return reports;
    }
}