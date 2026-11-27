using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The Sources sheet's calls that need no notes open (version 0.6; 0030, section 4), the same on a piece with notes and
/// on a piece that is only its PDF: the layers, hiding a view, adding and removing an original, removing Notes. Each
/// answers the layers (<see cref="Answer.SourceLayers"/>). Since version 1.1 (finding 29) also the sources answer
/// (<see cref="Sources"/>) and «Распознать» on a recording (<see cref="Hear"/>), on a piece with notes and on one without
/// (a PDF or a recording).
/// </summary>
internal static class SourceRequest
{
    /// <summary>
    /// The sources' calls that need no notes open, on a piece with notes and on one without (<see cref="CallRegistry"/>), each
    /// on the project the <see cref="SourceOwner"/> holds: the sources answer, «Распознать» on a recording, the layers, hiding a
    /// view, removing Notes, removing and adding an original.
    /// </summary>
    public static readonly FacadeCall<SourceOwner>[] Calls =
    [
        new("sources", static (on, _) => Sources(on.Get(), on.Words), CallFlags.OpenFirst),
        new("sources.notes.hear", static (on, request) => Hear(request, on)),
        new("sources.layers", static (on, request) => Layers(request, on.Get(), on.Words)),
        new("sources.view.hide", static (on, request) => Changed(request, on.Get, on.Set, on.Words, project => project.ViewHidden(request.Required("id"), request.Bool("hidden") ?? throw FacadeException.BadRequest("Send \"hidden\", true or false.")))),
        new("sources.notes.remove", static (on, request) => Changed(request, on.Get, on.Set, on.Words, project => project.DeleteNotes(request.Required("id")))),
        new("sources.original.remove", static (on, request) => Changed(request, on.Get, on.Set, on.Words, project => project.RemoveOriginal(request.Required("id"), request.Now(), request.Bool("withNotes") ?? false))),
        new("sources.original.add", static (on, request) => AddOriginal(request, on)),
    ];

    /// <summary>
    /// The base and its name, the summary line for the Sources sheet («Основа — ноты из файла · взято 12 тактов · 9 правок»),
    /// the PDF kept in the piece with its pages, the other versions, the bars taken and the corrections, and the originals
    /// with where their bytes are and the project's id (version 0.3), on a piece with notes and, since version 1.1, on one
    /// without (a PDF or a recording, finding 29); <paramref name="more"/> writes what a call adds first.
    /// </summary>
    public static string Sources(Project project, Words words, Action<Utf8JsonWriter>? more = null)
    {
        PieceSources sources = PieceSources.Of(project);
        return Answer.Write(json =>
        {
            more?.Invoke(json);
            json.WriteString("base", sources.Base.ToString().ToLowerInvariant());
            json.WriteString("baseName", sources.BaseName(words));
            json.WriteString("summary", sources.Summary(words));
            if (project.Document is not null)
            {
                json.WriteStartObject("document");
                json.WriteString("fileName", project.DocumentFileName);
                json.WriteNumber("pages", project.DocumentPages?.Pages ?? 0);
                json.WriteBoolean("readFrom", sources.ReadFromDocument);
                JsonWrite.Numbers(json, "starts", (project.DocumentPages?.Starts ?? []).Select(static start => start.Value));
                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("document");
            }

            Answer.Performances(json, project);
            json.WriteNumber("taken", sources.TakenBars);
            json.WriteNumber("edits", sources.Edits);
            // Version 0.3: the originals, and the project's id, which the iPad keys its links to files in Files by.
            json.WriteString("projectId", project.Id);
            Answer.Originals(json, project);
        });
    }

    /// <summary>
    /// «Распознать» on a recording already added: "original" (the recording's id) and the notes the transcription heard in
    /// the stretch sent («Что распознать»), as for transcribe ("milliseconds", "parts", "levels", "engine", "sentFromMs",
    /// "tempo"), with "now". Other Notes beside the working ones, their provenance naming the stretch
    /// ("sentFromMs" to "sentFromMs" + "milliseconds"); on a piece with no notes (version 1.1, a PDF or a recording,
    /// finding 29), the working notes, so the app opens the piece again as notes from the file it saves. Answers the
    /// sources with their "id".
    /// </summary>
    private static string Hear(Request request, SourceOwner on)
    {
        Project project = on.Get();
        (byte[] midi, TranscriptionOrigin origin, IReadOnlyList<CheckFlag> check) = OpenPiece.Heard(Path.ChangeExtension(project.FileName, ".mid"), request, on.Words);
        long from = request.Long("sentFromMs") ?? 0;
        (long, long) stretch = (from, from + request.Long("milliseconds")!.Value);
        string id = string.Empty;
        return Changed(
            request,
            on.Get,
            on.Set,
            on.Words,
            piece =>
            {
                Project heard = piece.HeardFrom(request.Required("original"), midi, origin, check, request.Now(), on.Ids, stretch);
                id = piece.HasNotes ? heard.Sources.Notes[^1].Id : heard.NotesId;
                return heard;
            },
            json => json.WriteString("id", id));
    }

    /// <summary>
    /// The sources (<see cref="Answer.SourceLayers"/>) as "device" ("ipad", the default, or "web") sees them, with "here",
    /// the ids of the originals whose bytes the app has on this device beside the file (a recording's sound).
    /// </summary>
    public static string Layers(Request request, Project project, Words words, Action<Utf8JsonWriter>? more = null)
    {
        PieceViews views = PieceViews.Of(project, DeviceOf(request), request.Texts("here"));
        return Answer.Write(json =>
        {
            more?.Invoke(json);
            Answer.SourceLayers(json, project, views, words);
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
    public static string Changed(Request request, Func<Project> get, Action<Project> set, Words words, Func<Project, Project> change, Action<Utf8JsonWriter>? more = null)
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
        return Layers(request, changed, words, more);
    }

    /// <summary>
    /// «+ Добавить оригинал»: "kind" ("pdf", "images", "audio", "midi", "musicxml"), "fileName", "kept" ("file", the
    /// default, "device" or "link"), "data" (base64, with "file"), "folder", "pages", "durationMs" and "now". The same file
    /// as a removed original revives it. Answers the sources with the original's "id".
    /// </summary>
    private static string AddOriginal(Request request, SourceOwner on)
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
            OriginalKind.Audio => PieceOriginal.RecordingId,
            OriginalKind.Midi => "src_midi",
            _ => "src_score",
        };
        Project project = on.Get();
        var original = new PieceOriginal(project.NextOriginalId(stem, on.Ids), kind, request.Required("fileName"), kept, request.Now())
        {
            Folder = request.Text("folder"),
            Pages = request.Int("pages"),
            DurationMs = request.Int("durationMs"),
        };
        PageMap? pages = data is not null && kind == OriginalKind.Pdf && PdfDocument.IsPdf(data) && on.EvenPages is not null
            ? on.EvenPages(Math.Max(1, request.Int("pages") ?? PdfDocument.PageCount(data)))
            : null;
        // The same file as a removed original takes its id back.
        string? sha = data is null ? null : PieceOriginal.Fingerprint(data);
        string id = project.Sources.Removed.FirstOrDefault(removed => sha is not null && removed.Original.Matches(sha))?.Original.Id ?? original.Id;
        return Changed(request, on.Get, on.Set, on.Words, piece => piece.AddOriginal(original, data, pages), json => json.WriteString("id", id));
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

/// <summary>
/// The project a call of <see cref="SourceRequest.Calls"/> reads and changes, of a piece with notes or without: how to get
/// and set it, the page map of a PDF added to a piece with notes (its pages shared over the bars; null for a piece with
/// none), the words of the answer and where the ids of new Notes and originals come from.
/// </summary>
internal sealed record SourceOwner(Func<Project> Get, Action<Project> Set, Func<int, PageMap>? EvenPages, Words Words, IdSource Ids);