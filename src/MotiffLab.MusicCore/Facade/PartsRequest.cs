using System.Globalization;
using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// parts.split on piece 0 (contract 12, a call added; v0.4, the «Пьесы в файле» experiment): a collection's project file
/// cut into the pieces the player confirmed (<see cref="PieceParts"/>), each answered as a project file to save as a new
/// project beside the collection, which is not changed. Nothing is kept: the app saves the files and adds nothing else.
/// </summary>
internal static class PartsRequest
{
    /// <summary>
    /// {"file": the collection's project file, "parts": [{"title", "bars": {"first", "last"} or "pages": {"first", "last"},
    /// "pdf" (optional, base64: the part's pages copied), "id" (optional)}], "playlist" (optional; the collection's title),
    /// "device" ("ipad" or "web", optional: it holds the collection's original), "pages" (optional: how many pages the
    /// collection's PDF has, as the app's viewer reads it), "now", "language"} →
    /// {"original": {"id", "kind", "fileName", "sha256"} or null, "playlist", "parts": [{"number", "title", "bars", "pages",
    /// "milliseconds": {"from", "to"} or null, "span", "notes", "project", "file"}]}.
    /// </summary>
    public static string Split(Request fields)
    {
        Project collection = ProjectFile.Read(Encoding.UTF8.GetBytes(fields.Required("file")));
        var options = new PieceSplitOptions(Now(fields), MusicCoreFacade.NewProjectId)
        {
            Playlist = fields.Text("playlist"),
            Device = fields.Enum<HoldingDevice>("device"),
            Words = Words.For(fields.Text("language")),
            Pages = fields.Int("pages") is { } pages ? pages > 0 ? pages : throw FacadeException.BadRequest("\"pages\" is how many pages the collection's PDF has, from 1.") : null,
        };
        PieceSplit split;
        try
        {
            split = PieceParts.Split(collection, Ranges(fields), options);
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
        catch (NotSupportedException exception)
        {
            throw new FacadeException("notSupported", exception.Message);
        }

        return Answer.Write(json =>
        {
            Original(json, split.Original);
            json.WriteString("playlist", split.Playlist);
            json.WriteStartArray("parts");
            foreach (PiecePart part in split.Parts)
            {
                Part(json, part);
            }

            json.WriteEndArray();
        });
    }

    private static void Part(Utf8JsonWriter json, PiecePart part)
    {
        json.WriteStartObject();
        json.WriteNumber("number", part.Number);
        json.WriteString("title", part.Project.Metadata.Title);
        Range(json, "bars", part.Bars);
        Range(json, "pages", part.Pages);
        if (part.Milliseconds is { } stretch)
        {
            json.WriteStartObject("milliseconds");
            json.WriteNumber("from", stretch.From);
            json.WriteNumber("to", stretch.To);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("milliseconds");
        }

        if (part.Span is { } span)
        {
            Answer.Span(json, "span", span);
        }
        else
        {
            json.WriteNull("span");
        }

        json.WriteNumber("notes", part.Notes.Count);
        Answer.Project(json, "project", part.Project);
        json.WriteString("file", Encoding.UTF8.GetString(ProjectFile.Write(part.Project)));
        json.WriteEndObject();
    }

    private static void Original(Utf8JsonWriter json, PieceOriginal? original)
    {
        if (original is null)
        {
            json.WriteNull("original");
            return;
        }

        json.WriteStartObject("original");
        json.WriteString("id", original.Id);
        json.WriteString("kind", original.Kind.ToString().ToLowerInvariant());
        json.WriteString("fileName", original.FileName);
        json.WriteString("sha256", original.Sha256);
        json.WriteEndObject();
    }

    private static void Range(Utf8JsonWriter json, string name, (int First, int Last)? range)
    {
        if (range is not { } both)
        {
            json.WriteNull(name);
            return;
        }

        json.WriteStartObject(name);
        json.WriteNumber("first", both.First);
        json.WriteNumber("last", both.Last);
        json.WriteEndObject();
    }

    private static List<PartRange> Ranges(Request fields)
    {
        List<PartRange> ranges = [];
        foreach (JsonElement element in fields.Elements("parts"))
        {
            Request part = Request.Of(element);
            PartUnit unit = part.Has("pages") ? PartUnit.Pages : part.Has("bars") ? PartUnit.Bars
                : throw FacadeException.BadRequest("Each part needs \"bars\" or \"pages\": {\"first\", \"last\"}.");
            Request range = part.Object(unit == PartUnit.Pages ? "pages" : "bars");
            int first = range.Int("first") ?? throw FacadeException.BadRequest("A part's range needs \"first\".");
            ranges.Add(new PartRange(part.Text("title"), unit, first, range.Int("last") ?? first)
            {
                Pdf = part.Text("pdf") is { } pdf ? Base64(pdf) : null,
                Id = part.Text("id"),
            });
        }

        return ranges.Count > 0 ? ranges : throw FacadeException.BadRequest("Send \"parts\", the pieces to make, each with its \"bars\" or \"pages\".");
    }

    private static byte[] Base64(string text)
    {
        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            throw FacadeException.BadRequest("A part's \"pdf\" is not base64.");
        }
    }

    private static DateTimeOffset Now(Request request) =>
        request.Text("now") is { } text
            ? DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : throw FacadeException.BadRequest("\"now\" must be an ISO 8601 time.")
            : DateTimeOffset.UtcNow;
}