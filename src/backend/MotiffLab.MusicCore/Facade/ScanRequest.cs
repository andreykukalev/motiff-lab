using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Import.Vector;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// scan.join on piece 0 (contract 12, a call added; v0.4, notes from photos of pages): the MusicXML of pages read one
/// request at a time joined into one score (<see cref="ScanJoin"/>), with each page's bars and how many of them are
/// doubtful once the scan server's page reports count (<see cref="ScanCheck.Of(ImportedPiece, IReadOnlyList{PageReport})"/>).
/// Nothing is kept: the app opens the score as any scan, and the reports are not saved. Within contract 14 (version 1.3,
/// ADR 0035 section 5), a page of "pages" may be its readings by several engines, merged staff by staff (<see cref="ScanMerge"/>),
/// and such a page answers "merge"; a form and a field added. Also within 14 (c-1b): such a page may come with the page's
/// evidence, {"readings", "evidence"}; a form added, the answer as before. Also within 14 (v1.3 i-2): the answer's
/// "fingerprint" (the SHA-256 of the score as its Notes keep it, as `check` answers it) and a bar to check's "variants" and
/// "crop"; fields added.
/// </summary>
internal static partial class ScanRequest
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
    /// A bar to check also answers "variants": [{"engines", "staves": [{"part", "staff", "notes": [{"onset" (in the bar),
    /// "pitch", "length" (ticks), "name"}]}]}], the other readings of its staves to check, and "crop": {"box": [left, top,
    /// right, bottom], "width", "height", "rotated"} in the page image's pixels, or null. "fingerprint" names the score as
    /// the Notes made from it keep it (`check` answers the working notes' own), so the app can keep these beside them.
    /// Within contract 16 (version 1.6, c-14b2, «Как напечатано» step 3; fields added): a page of "replace" may instead be
    /// {"page", "readings", "evidence" (optional), "bad": [bars, as "score" numbers them, the player marked as read badly]},
    /// merged as a page of "pages" is, each bad bar taking another engine's reading than "score" has there where one read it
    /// otherwise, marked read again and to check (<see cref="ScanBefore"/>); with "file" (the piece's project file) and
    /// "original" (the id of the PDF read, its PDF by default), the bars, the check and "fingerprint" are those of the notes
    /// with the working notes' marks put in, as sources.notes.recognise makes them from "score".
    /// </summary>
    public static string Join(Request fields)
    {
        Dictionary<int, ScanMergedPage> merged = [];
        byte[] score = Score(fields, merged);
        IReadOnlyList<byte[]> pages = ScanJoin.Split(score);
        IReadOnlyList<PageReport>? reports = Reports(fields, pages.Count);
        Func<byte[], ReadScore>? marking = Marking(fields);
        (ImportedPiece piece, byte[] notes) = marking?.Invoke(score) is { } marked
            ? (marked.Piece, marked.MusicXml)
            : PieceImporter.Import("scan.musicxml", score) is { Piece: { } read }
                ? (read, ScanRepair.Repair(score))
                : throw FacadeException.BadRequest("The joined pages could not be read as music.");
        ScanCheck check = ScanCheck.Of(piece, reports);
        PageMap map = PageMap.Of(piece, pages.Count);
        if (marking is not null)
        {
            MarkVariants(merged, score, piece, map, marking);
        }

        return Answer.Write(json =>
        {
            json.WriteString("score", Encoding.UTF8.GetString(score));
            json.WriteString("fingerprint", PieceOriginal.Fingerprint(notes));
            json.WriteStartArray("pages");
            for (int page = 1; page <= pages.Count; page++)
            {
                Page(json, page, [.. check.Bars.Where(bar => map.PageOf(bar.Span.Onset) == page)], reports?[page - 1], merged.GetValueOrDefault(page));
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
                JsonWrite.Texts(json, "issues", page.Issues);
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

    /// <summary>One page of the answer: its number, its bars and how many are doubtful, its quality, and its merge when it was merged.</summary>
    private static void Page(Utf8JsonWriter json, int page, ScanBar[] bars, PageReport? report, ScanMergedPage? merged)
    {
        json.WriteStartObject();
        json.WriteNumber("page", page);
        json.WriteNumber("bars", bars.Length);
        json.WriteNumber("doubtful", bars.Count(static bar => bar.Confidence < ScanCheck.CleanConfidence));
        if (report is null)
        {
            json.WriteNull("quality");
        }
        else
        {
            json.WriteString("quality", JsonNamingPolicy.CamelCase.ConvertName(report.Quality.ToString()));
        }

        if (merged is not null)
        {
            Merge(json, merged, bars);
        }

        json.WriteEndObject();
    }

    /// <summary>A merged page's engines and each of its bars' level and the engines that gave each staff.</summary>
    private static void Merge(Utf8JsonWriter json, ScanMergedPage page, ScanBar[] bars)
    {
        json.WriteStartObject("merge");
        JsonWrite.Texts(json, "engines", page.Engines);
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
                JsonWrite.Texts(json, "engines", staff.Engines);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            if (read.Level == ScanLevel.Check)
            {
                Variants(json, read.Variants);
                Crop(json, read.Crop);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    /// <summary>A bar to check's other readings (v1.3 i-2): each with its engines and its notes on the bar's staves to check.</summary>
    private static void Variants(Utf8JsonWriter json, IReadOnlyList<ScanVariant> variants)
    {
        json.WriteStartArray("variants");
        foreach (ScanVariant variant in variants)
        {
            json.WriteStartObject();
            JsonWrite.Texts(json, "engines", variant.Engines);
            json.WriteStartArray("staves");
            foreach (ScanVariantStaff staff in variant.Staves)
            {
                json.WriteStartObject();
                json.WriteNumber("part", staff.Part);
                json.WriteNumber("staff", staff.Staff);
                json.WriteStartArray("notes");
                foreach (ScanVariantNote note in staff.Notes)
                {
                    json.WriteStartObject();
                    json.WriteNumber("onset", note.Onset);
                    json.WriteNumber("pitch", note.Pitch);
                    json.WriteNumber("length", note.Length);
                    json.WriteString("name", new Pitch(note.Pitch).ToString());
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>A bar to check's place on its page image (v1.3 i-2): its box and the image's size and turn; null without them.</summary>
    private static void Crop(Utf8JsonWriter json, ScanCrop? crop)
    {
        if (crop is null)
        {
            json.WriteNull("crop");
            return;
        }

        json.WriteStartObject("crop");
        json.WriteStartArray("box");
        foreach (double value in (double[])[crop.Box.Left, crop.Box.Top, crop.Box.Right, crop.Box.Bottom])
        {
            json.WriteNumberValue(value);
        }

        json.WriteEndArray();
        json.WriteNumber("width", crop.Image.Width);
        json.WriteNumber("height", crop.Image.Height);
        json.WriteNumber("rotated", crop.Image.Rotated);
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
                byte[] score = Encoding.UTF8.GetBytes(fields.Required("score"));
                Dictionary<int, byte[]> replaced = [];
                foreach (JsonElement element in fields.Elements("replace"))
                {
                    Request page = Request.Of(element);
                    int number = page.Int("page") ?? throw FacadeException.BadRequest("Each page to replace needs \"page\".");
                    replaced[number] = page.Has("readings")
                        ? Read(element, number, merged, Before(score, number, page.Ints("bad")))
                        : Encoding.UTF8.GetBytes(page.Required("musicxml"));
                }

                return ScanJoin.Replace(score, replaced);
            }

            IReadOnlyList<JsonElement> pages = fields.Elements("pages");
            return pages.Count > 0
                ? ScanJoin.Join([.. pages.Select((page, index) => Read(page, index + 1, merged))])
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
    private static byte[] Read(JsonElement page, int number, Dictionary<int, ScanMergedPage> merged, ScanBefore? before = null)
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
            evidence is { } found ? ScanEvidence.Parse(found.GetRawText()) : null,
            before);
        merged[number] = merge;
        return merge.MusicXml;
    }

    /// <summary>
    /// Page <paramref name="number"/> of the joined <paramref name="score"/> as read before, with its measures at the
    /// <paramref name="bad"/> bars (as the score numbers them) the player marked as read badly; null when none is marked.
    /// </summary>
    /// <exception cref="FacadeException">A bar is not on the page ("badRequest").</exception>
    private static ScanBefore? Before(byte[] score, int number, IReadOnlyList<int> bad)
    {
        if (bad.Count == 0)
        {
            return null;
        }

        IReadOnlyList<byte[]> pages = ScanJoin.Split(score);
        if (number < 1 || number > pages.Count || PieceImporter.Import("scan.musicxml", score).Piece is not { MusicXml: { } import } piece)
        {
            throw FacadeException.BadRequest($"Page {number} of the score cannot be read again.");
        }

        PageMap map = PageMap.Of(piece, pages.Count);
        MusicXmlMeasure[] measures = [.. import.Measures.Where(measure => map.PageOf(measure.Span.Onset) == number)];
        HashSet<int> indices = [];
        foreach (int bar in bad)
        {
            int index = Array.FindIndex(measures, measure => piece.Meter.ToPosition(measure.Span.Onset).Bar == bar);
            indices.Add(index >= 0 ? index : throw FacadeException.BadRequest($"Bar {bar} is not on page {number}."));
        }

        return new ScanBefore(pages[number - 1], indices);
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