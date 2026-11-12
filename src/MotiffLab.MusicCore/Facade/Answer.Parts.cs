using System.Text.Json;

using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// What the facade answers of the fields version 0.4 adds within version 15 (within contract 12, fields added): where a
/// piece is in its collection, the scan server's page reports as the check counts them, and which margin a stroke is in.
/// </summary>
internal static partial class Answer
{
    /// <summary>
    /// "partOf": {"original" (an id in the piece's "originals"), "pages": {"first", "last"} or null, "milliseconds":
    /// {"from", "to"} or null}, or null for a piece of its own (<see cref="Projects.PartOf"/>).
    /// </summary>
    public static void PartOf(Utf8JsonWriter json, PartOf? partOf)
    {
        if (partOf is null)
        {
            json.WriteNull("partOf");
            return;
        }

        json.WriteStartObject("partOf");
        json.WriteString("original", partOf.Original);
        if (partOf.Pages is var (first, last))
        {
            json.WriteStartObject("pages");
            json.WriteNumber("first", first);
            json.WriteNumber("last", last);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("pages");
        }

        if (partOf.Milliseconds is var (from, to))
        {
            json.WriteStartObject("milliseconds");
            json.WriteNumber("from", from);
            json.WriteNumber("to", to);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("milliseconds");
        }

        json.WriteEndObject();
    }

    /// <summary>"margin": "left" or "right" on a stroke begun in a line's margin; left out for a stroke in its bar, as the file does.</summary>
    public static void Margin(Utf8JsonWriter json, StrokeMargin? margin)
    {
        if (margin is { } side)
        {
            json.WriteString("margin", Name(side));
        }
    }

    /// <summary>
    /// "pages": the pages of a scan the server reported on (<see cref="ScanCheck.Pages"/>), each {"page" (from 1, in the
    /// notes' order), "quality" ("good", "weak" or "poor"), "rotation" (degrees the server turned it), "bars" (how many were
    /// read from it), "doubtful" (those below the 0.6 a hint needs and not confirmed), "firstBar", "lastBar" (null without
    /// bars)}; empty for a piece with no reports.
    /// </summary>
    public static void ScanPages(Utf8JsonWriter json, IReadOnlyList<ScanPage> pages, IReadOnlyCollection<int> confirmedBars)
    {
        json.WriteStartArray("pages");
        foreach (ScanPage page in pages)
        {
            json.WriteStartObject();
            json.WriteNumber("page", page.Page);
            json.WriteString("quality", Name(page.Quality));
            json.WriteNumber("rotation", page.Report.Rotation);
            json.WriteNumber("bars", page.Bars.Count);
            json.WriteNumber("doubtful", page.DoubtfulOf(confirmedBars));
            OptionalNumber(json, "firstBar", page.Bars.Count > 0 ? page.Bars[0].Bar : null);
            OptionalNumber(json, "lastBar", page.Bars.Count > 0 ? page.Bars[^1].Bar : null);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }
}