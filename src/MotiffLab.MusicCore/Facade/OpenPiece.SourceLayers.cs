using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The piece's sources by the layers of 0030 (version 0.6, step 5): the Originals and Notes with their states and the
/// views of the switch in one call, and the Sources sheet's operations. The calls of version 0.5 ("sources",
/// "sources.document", …) stay as they were. «Сделать рабочими» changes the notes the piece is open on, so the facade
/// opens it again (<c>sources.makeWorking</c>, <see cref="MusicCoreFacade"/>).
/// </summary>
internal sealed partial class OpenPiece
{
    private string? CallOnSourceLayers(string name, Request request) => name switch
    {
        "sources.layers" => LayersAnswer(request),
        "sources.view.hide" => Changed(request, project => project.ViewHidden(request.Required("id"), request.Bool("hidden") ?? throw FacadeException.BadRequest("Send \"hidden\", true or false."))),
        "sources.notes.remove" => Changed(request, project => project.DeleteNotes(request.Required("id"))),
        "sources.original.remove" => Changed(request, project => project.RemoveOriginal(request.Required("id"), Now(request), request.Bool("withNotes") ?? false)),
        "sources.original.add" => AddOriginal(request),
        "sources.notes.recognise" => RecogniseAgain(request),
        _ => null,
    };

    /// <summary>
    /// The sources (<see cref="Answer.SourceLayers"/>) as "device" ("ipad", the default, or "web") sees them, with "here",
    /// the ids of the originals whose bytes the app has on this device beside the file (a recording's sound).
    /// </summary>
    private string LayersAnswer(Request request, Action<Utf8JsonWriter>? more = null)
    {
        PieceViews views = PieceViews.Of(Project, DeviceOf(request), request.Texts("here"));
        return Answer.Write(json =>
        {
            more?.Invoke(json);
            Answer.SourceLayers(json, Project, views);
        });
    }

    /// <summary>The device the app runs on, "device": "ipad" (the default) or "web".</summary>
    internal static HoldingDevice DeviceOf(Request request) => request.Text("device") switch
    {
        null or "ipad" => HoldingDevice.Ipad,
        "web" => HoldingDevice.Web,
        _ => throw FacadeException.BadRequest("\"device\" is \"ipad\" or \"web\"."),
    };

    /// <summary>The piece changed by one of the sheet's operations, answered as the sources; what the operation refuses is a bad request.</summary>
    private string Changed(Request request, Func<Project, Project> change, Action<Utf8JsonWriter>? more = null)
    {
        try
        {
            Project = change(Project);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw FacadeException.Unavailable(exception.Message);
        }

        return LayersAnswer(request, more);
    }

    /// <summary>
    /// «+ Добавить оригинал»: "kind" ("pdf", "images", "audio", "midi", "musicxml"), "fileName", "kept" ("file", the
    /// default, "device" or "link"), "data" (base64, with "file"), "folder", "pages", "durationMs" and "now". The same file
    /// as a removed original revives it. Answers the sources with the original's "id".
    /// </summary>
    private string AddOriginal(Request request)
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
        var original = new PieceOriginal(Project.NextOriginalId(stem), kind, request.Required("fileName"), kept, Now(request))
        {
            Folder = request.Text("folder"),
            Pages = request.Int("pages"),
            DurationMs = request.Int("durationMs"),
        };
        PageMap? pages = data is not null && kind == OriginalKind.Pdf && PdfDocument.IsPdf(data)
            ? PageMap.Even(Piece.Meter, Piece.End, Math.Max(1, request.Int("pages") ?? PdfDocument.PageCount(data)))
            : null;
        // The same file as a removed original takes its id back.
        string? sha = data is null ? null : PieceOriginal.Fingerprint(data);
        string id = Project.Sources.Removed.FirstOrDefault(removed => sha is not null && removed.Original.Matches(sha))?.Original.Id ?? original.Id;
        return Changed(request, project => project.AddOriginal(original, data, pages), json => json.WriteString("id", id));
    }

    /// <summary>
    /// «Распознать ещё раз»: "original" (the PDF's id), "musicXml" (base64) the scan server read from it, "engine",
    /// "pages", "reports" (the scan server's page reports) and "now": other Notes beside the working ones, which keep their
    /// corrections. The engine's mistakes are put right first (<see cref="ScanRepair"/>). Answers the sources with their "id".
    /// </summary>
    private string RecogniseAgain(Request request)
    {
        byte[] musicXml = ScanRepair.Repair(Base64(request.Required("musicXml"), "musicXml"));
        PieceImportResult imported = PieceImporter.Import("notes.musicxml", musicXml);
        if (imported.Piece is not { } read)
        {
            throw new FacadeException("import", imported.Rejection?.Text(Words) ?? Words.T("No notes could be read from these pages.", "На этих страницах не удалось прочитать нот."));
        }

        string id = Project.NextNotesId();
        PageMap pages = PageMap.Of(read, Math.Max(1, request.Int("pages") ?? 1));
        List<PageReport>? reports = ScanRequest.ReportsIn(request);
        return Changed(request, project => project.RecognisedAgain(request.Required("original"), musicXml, request.Required("engine"), pages, Now(request), reports), json => json.WriteString("id", id));
    }

    private static byte[] Base64(string text, string name)
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