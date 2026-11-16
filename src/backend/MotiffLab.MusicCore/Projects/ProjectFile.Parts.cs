using System.Text.Json;

using MotiffLab.MusicCore.Import.Scan;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The fields version 0.4 adds within version 15 (v0.4 design, step Ф1; Andrei, 2026-11-12): where a piece is in its
/// collection ("partOf"), what the scan server saw of each page ("scan"."pages") and which margin a stroke is in
/// ("margin"). Optional, like "originals" and "metadata": versions 0.2 and 0.3 open the file and drop them when they
/// write it again.
/// </summary>
public static partial class ProjectFile
{
    /// <summary>"partOf": {"original", "pages": [first, last]} or {"original", "ms": [from, to]}; left out for a piece of its own.</summary>
    private static void WritePartOf(Utf8JsonWriter json, PartOf? partOf)
    {
        if (partOf is null)
        {
            return;
        }

        json.WriteStartObject("partOf");
        json.WriteString("original", partOf.Original);
        if (partOf.Pages is var (first, last))
        {
            json.WriteStartArray("pages");
            json.WriteNumberValue(first);
            json.WriteNumberValue(last);
            json.WriteEndArray();
        }

        if (partOf.Milliseconds is var (from, to))
        {
            json.WriteStartArray("ms");
            json.WriteNumberValue(from);
            json.WriteNumberValue(to);
            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    /// <summary>The project with the place in its collection its file keeps (<see cref="ReadPartOf"/>).</summary>
    private static Project WithPartOfIn(this Project project, JsonElement root) => project with { PartOf = ReadPartOf(root) };

    /// <summary>
    /// The piece's place in its collection; none when the file has no "partOf", and none for one with neither "pages" nor
    /// "ms" (a later build's other kind of place), so the piece opens as a piece of its own.
    /// </summary>
    private static PartOf? ReadPartOf(JsonElement root)
    {
        if (!root.TryGetProperty("partOf", out JsonElement partOf))
        {
            return null;
        }

        Expect(partOf, JsonValueKind.Object, "partOf");
        string original = Text(partOf, "original");
        bool pages = partOf.TryGetProperty("pages", out JsonElement pageRange);
        bool ms = partOf.TryGetProperty("ms", out JsonElement stretch);
        try
        {
            return (pages, ms) switch
            {
                (true, false) => PartOf.OnPages(original, checked((int)Pair(pageRange).First), checked((int)Pair(pageRange).Last)),
                (false, true) => PartOf.InRecording(original, Pair(stretch).First, Pair(stretch).Last),
                (false, false) => null,
                _ => throw Damaged("partOf"),
            };
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw Damaged("partOf");
        }
    }

    /// <summary>Two whole numbers, as "pages" and "ms" hold them.</summary>
    private static (long First, long Last) Pair(JsonElement pair) =>
        pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() == 2
        && pair[0].ValueKind == JsonValueKind.Number && pair[0].TryGetInt64(out long first)
        && pair[1].ValueKind == JsonValueKind.Number && pair[1].TryGetInt64(out long last)
            ? (first, last)
            : throw Damaged("partOf");

    /// <summary>"pages" in "scan": each page's report as the scan server sent it, numbers only; left out when there are none.</summary>
    private static void WriteScanPages(Utf8JsonWriter json, IReadOnlyList<PageReport> pages)
    {
        if (pages.Count == 0)
        {
            return;
        }

        json.WriteStartArray("pages");
        foreach (PageReport page in pages)
        {
            json.WriteStartObject();
            json.WriteNumber("page", page.Page);
            json.WriteNumber("rotation", page.Rotation);
            json.WriteNumber("sharpness", page.Sharpness);
            json.WriteNumber("contrast", page.Contrast);
            json.WriteNumber("staves", page.Staves);
            json.WriteNumber("noteheads", page.Noteheads);
            json.WriteNumber("barLines", page.BarLines);
            json.WriteNumber("spacePx", page.SpacePx);
            json.WriteNumber("measures", page.Measures);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>The scan's page reports, read as the scan server's header is (<see cref="PageReport.Of"/>); none without "pages".</summary>
    private static IReadOnlyList<PageReport> ReadScanPages(JsonElement scan)
    {
        if (!scan.TryGetProperty("pages", out JsonElement pages))
        {
            return [];
        }

        try
        {
            return pages.ValueKind == JsonValueKind.Array ? PageReport.Of(pages) : throw Damaged("scan pages");
        }
        catch (FormatException)
        {
            throw Damaged("scan pages");
        }
    }

    /// <summary>"margin" on a stroke: "left" or "right", only for a stroke begun in a line's margin.</summary>
    private static void WriteMargin(Utf8JsonWriter json, StrokeMargin? margin)
    {
        if (margin is { } side)
        {
            json.WriteString("margin", Lower(side));
        }
    }

    private static StrokeMargin? ReadMargin(JsonElement stroke) =>
        stroke.TryGetProperty("margin", out _) ? LowerName<StrokeMargin>(stroke, "margin") : null;
}