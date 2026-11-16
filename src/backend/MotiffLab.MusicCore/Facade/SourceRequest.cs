using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The Sources sheet's calls that need no notes open (version 0.6; 0030, section 4), the same on a piece with notes and
/// on a piece that is only its PDF: the layers, hiding a view, adding and removing an original, removing Notes. Each
/// answers the layers (<see cref="Answer.SourceLayers"/>).
/// </summary>
internal static class SourceRequest
{
    /// <summary>The answer of a call of the sheet on the piece <paramref name="get"/> gives; null when the call is not one of them.</summary>
    /// <param name="evenPages">The page map of a PDF added to a piece with notes, its pages shared over the bars; null for a piece with none.</param>
    public static string? Call(string name, Request request, Func<Project> get, Action<Project> set, Func<int, PageMap>? evenPages) => name switch
    {
        "sources.layers" => Layers(request, get()),
        "sources.view.hide" => Changed(request, get, set, project => project.ViewHidden(request.Required("id"), request.Bool("hidden") ?? throw FacadeException.BadRequest("Send \"hidden\", true or false."))),
        "sources.notes.remove" => Changed(request, get, set, project => project.DeleteNotes(request.Required("id"))),
        "sources.original.remove" => Changed(request, get, set, project => project.RemoveOriginal(request.Required("id"), Now(request), request.Bool("withNotes") ?? false)),
        "sources.original.add" => AddOriginal(request, get, set, evenPages),
        _ => null,
    };

    /// <summary>
    /// The sources (<see cref="Answer.SourceLayers"/>) as "device" ("ipad", the default, or "web") sees them, with "here",
    /// the ids of the originals whose bytes the app has on this device beside the file (a recording's sound).
    /// </summary>
    public static string Layers(Request request, Project project, Action<Utf8JsonWriter>? more = null)
    {
        PieceViews views = PieceViews.Of(project, DeviceOf(request), request.Texts("here"));
        return Answer.Write(json =>
        {
            more?.Invoke(json);
            Answer.SourceLayers(json, project, views);
        });
    }

    /// <summary>The device the app runs on, "device": "ipad" (the default) or "web".</summary>
    public static HoldingDevice DeviceOf(Request request) => request.Text("device") switch
    {
        null or "ipad" => HoldingDevice.Ipad,
        "web" => HoldingDevice.Web,
        _ => throw FacadeException.BadRequest("\"device\" is \"ipad\" or \"web\"."),
    };

    /// <summary>The piece changed by one of the sheet's operations, answered as the sources; what the operation refuses is a bad request.</summary>
    public static string Changed(Request request, Func<Project> get, Action<Project> set, Func<Project, Project> change, Action<Utf8JsonWriter>? more = null)
    {
        Project changed;
        try
        {
            changed = change(get());
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw FacadeException.Unavailable(exception.Message);
        }

        set(changed);
        return Layers(request, changed, more);
    }

    public static DateTimeOffset Now(Request request) =>
        request.Text("now") is { } text
            ? DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : throw FacadeException.BadRequest("\"now\" must be an ISO 8601 time.")
            : DateTimeOffset.UtcNow;

    /// <summary>
    /// «+ Добавить оригинал»: "kind" ("pdf", "images", "audio", "midi", "musicxml"), "fileName", "kept" ("file", the
    /// default, "device" or "link"), "data" (base64, with "file"), "folder", "pages", "durationMs" and "now". The same file
    /// as a removed original revives it. Answers the sources with the original's "id".
    /// </summary>
    private static string AddOriginal(Request request, Func<Project> get, Action<Project> set, Func<int, PageMap>? evenPages)
    {
        OriginalKind kind = request.Text("kind") switch
        {
            "pdf" => OriginalKind.Pdf,
            "images" => OriginalKind.Images,
            "audio" => OriginalKind.Audio,
            "midi" => OriginalKind.Midi,
            "musicxml" => OriginalKind.MusicXml,
            _ => throw FacadeException.BadRequest("Send \"kind\": pdf, images, audio, midi or musicxml."),
        };
        OriginalKept kept = request.Text("kept") switch
        {
            null or "file" => OriginalKept.File,
            "device" => OriginalKept.Device,
            "link" => OriginalKept.Link,
            _ => throw FacadeException.BadRequest("\"kept\" is file, device or link."),
        };
        byte[]? data = request.Text("data") is { } text ? Base64(text, "data") : null;
        string stem = kind switch
        {
            OriginalKind.Pdf => PieceOriginal.DocumentId,
            OriginalKind.Images => "src_photos",
            OriginalKind.Audio => "src_rec",
            OriginalKind.Midi => "src_midi",
            _ => "src_score",
        };
        Project project = get();
        var original = new PieceOriginal(project.NextOriginalId(stem), kind, request.Required("fileName"), kept, Now(request))
        {
            Folder = request.Text("folder"),
            Pages = request.Int("pages"),
            DurationMs = request.Int("durationMs"),
        };
        PageMap? pages = data is not null && kind == OriginalKind.Pdf && PdfDocument.IsPdf(data) && evenPages is not null
            ? evenPages(Math.Max(1, request.Int("pages") ?? PdfDocument.PageCount(data)))
            : null;
        // The same file as a removed original takes its id back.
        string? sha = data is null ? null : PieceOriginal.Fingerprint(data);
        string id = project.Sources.Removed.FirstOrDefault(removed => sha is not null && removed.Original.Matches(sha))?.Original.Id ?? original.Id;
        return Changed(request, get, set, piece => piece.AddOriginal(original, data, pages), json => json.WriteString("id", id));
    }

    public static byte[] Base64(string text, string name)
    {
        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            throw FacadeException.BadRequest($"\"{name}\" must be base64.");
        }
    }
}