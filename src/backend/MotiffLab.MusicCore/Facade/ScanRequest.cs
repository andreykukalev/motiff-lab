using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Import.Vector;
using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// scan.join on piece 0 (contract 12, a call added; v0.4, notes from photos of pages): the MusicXML of pages read one
/// request at a time joined into one score (<see cref="ScanJoin"/>), with each page's bars and how many of them are
/// doubtful once the scan server's page reports count (<see cref="ScanCheck.Of(ImportedPiece, IReadOnlyList{PageReport})"/>).
/// Nothing is kept: the app opens the score as any scan, and the reports are not saved. Within contract 14 (version 1.3,
/// ADR 0035 section 5), a page of "pages" may be its readings by several engines, merged staff by staff (<see cref="ScanMerge"/>),
/// and such a page answers "merge"; a form and a field added. Also within 14 (c-1b): such a page may come with the page's
/// evidence, {"readings", "evidence"}; a form added, the answer as before.
/// </summary>
internal static class ScanRequest
{
    /// <summary>
    /// {"pages": [MusicXML text, one per page]} or {"score": a joined score, "replace": [{"page", "musicxml"}]} for pages read
    /// again, and "reports" (optional): one per page in order, each the X-Page-Report header as text, its list or one of
    /// its objects → {"score": the joined MusicXML, "pages": [{"page", "bars", "doubtful", "quality": "good", "weak",
    /// "poor" or null without reports}]}. "doubtful" counts the bars below the 0.6 a hint needs: flagged, or on a weak page.
    /// A page of "pages" may instead be a list of its readings, [{"engine", "musicxml"}], or {"readings": that list, "evidence":
    /// the page's evidence from the scan server}: they are merged (<see cref="ScanMerge"/>, the evidence settling the staves
    /// no two engines agree on) and the page answers "merge": {"engines": [as sent, in the merge's order], "bars": [{"bar",
    /// "level": "sure", "probable" or "check", "staves": [{"part", "staff", "engines"}]}]}, one per bar of the page in order.
    /// </summary>
    public static string Join(Request fields)
    {
        Dictionary<int, ScanMergedPage> merged = [];
        byte[] score = Score(fields, merged);
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

                if (merged.TryGetValue(page, out ScanMergedPage? readings))
                {
                    Merge(json, readings, bars);
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

    /// <summary>A merged page's engines and each of its bars' level and the engines that gave each staff.</summary>
    private static void Merge(Utf8JsonWriter json, ScanMergedPage page, ScanBar[] bars)
    {
        json.WriteStartObject("merge");
        json.WriteStartArray("engines");
        foreach (string engine in page.Engines)
        {
            json.WriteStringValue(engine);
        }

        json.WriteEndArray();
        json.WriteStartArray("bars");
        foreach ((ScanBar bar, ScanMergedBar read) in bars.Zip(page.Bars))
        {
            json.WriteStartObject();
            json.WriteNumber("bar", bar.Bar);
            json.WriteString("level", JsonNamingPolicy.CamelCase.ConvertName(read.Level.ToString()));
            json.WriteStartArray("staves");
            foreach (ScanStaffSource staff in read.Staves)
            {
                json.WriteStartObject();
                json.WriteNumber("part", staff.Part);
                json.WriteNumber("staff", staff.Staff);
                json.WriteStartArray("engines");
                foreach (string engine in staff.Engines)
                {
                    json.WriteStringValue(engine);
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
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

    /// <summary>The joined score; the pages sent as readings are merged first, and their merges kept in <paramref name="merged"/> by page.</summary>
    private static byte[] Score(Request fields, Dictionary<int, ScanMergedPage> merged)
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

            IReadOnlyList<JsonElement> pages = fields.Elements("pages");
            return pages.Count > 0
                ? ScanJoin.Join([.. pages.Select((page, index) => Page(page, index + 1, merged))])
                : throw FacadeException.BadRequest("Send \"pages\", the MusicXML read from each page, or \"score\".");
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }

    /// <summary>
    /// One page of "pages": its MusicXML, or its readings [{"engine", "musicxml"}] merged, or {"readings": those, "evidence":
    /// the page's evidence as the scan server gives it (optional)} merged with the evidence (<see cref="ScanEvidence"/>).
    /// </summary>
    private static byte[] Page(JsonElement page, int number, Dictionary<int, ScanMergedPage> merged)
    {
        if (page.ValueKind == JsonValueKind.String)
        {
            return Encoding.UTF8.GetBytes(page.GetString()!);
        }

        Request? sent = page.ValueKind == JsonValueKind.Object ? Request.Of(page) : null;
        JsonElement? evidence = sent?.Element("evidence");
        JsonElement listed = sent?.Element("readings") ?? page;
        if (listed.ValueKind != JsonValueKind.Array)
        {
            throw FacadeException.BadRequest("Each of \"pages\" is a page's MusicXML, a list of its readings, {\"engine\", \"musicxml\"}, or {\"readings\", \"evidence\"}.");
        }

        List<ScanReading> readings = [.. listed.EnumerateArray().Select(Request.Of)
            .Select(reading => new ScanReading(reading.Required("engine"), Encoding.UTF8.GetBytes(reading.Required("musicxml"))))];
        ScanMergedPage merge = ScanMerge.Merge(
            readings.Count > 0 ? readings : throw FacadeException.BadRequest($"Page {number} has no reading."),
            evidence is { } found ? ScanEvidence.Parse(found.GetRawText()) : null);
        merged[number] = merge;
        return merge.MusicXml;
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