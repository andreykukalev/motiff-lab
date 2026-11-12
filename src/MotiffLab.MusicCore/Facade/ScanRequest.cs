using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
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