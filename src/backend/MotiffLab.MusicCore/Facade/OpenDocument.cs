using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// A piece the app has open without its notes: a PDF piece (0023, within contract 4), the pages kept as the player added
/// them and the marks made on them, or (version 1.1, finding 29) a piece that is only its recording, kept on the device.
/// It answers only the calls about the document, the sources and its project file; a call about notes is refused with
/// "noNotes", since nothing on a PDF's pages or in a recording is music the engine knows until the notes are read from
/// them.
/// </summary>
internal sealed class OpenDocument
{
    private OpenDocument(Project project) => Project = project;

    public Project Project { get; private set; }

    /// <summary>
    /// A new PDF piece from the file the player added. With <paramref name="photos"/> (v0.4, the iPad's «Фото страниц»
    /// experiment), the PDF is the player's photos of that many pages put together on the device, and its original is
    /// described so (<see cref="OriginalKind.Images"/>, a value of the project file since 0.3).
    /// </summary>
    public static OpenDocument Create(string id, string fileName, byte[] pdf, Words words, DateTimeOffset now, int? photos = null)
    {
        if (PdfDocument.CheckSize(pdf.Length) is { } rejection)
        {
            throw new FacadeException("import", $"{rejection.Text(words)} {rejection.TipIn(words)}".Trim()) { Data = { ["reason"] = JsonWrite.Name(rejection.Reason), ["tip"] = rejection.TipIn(words) } };
        }

        if (photos is < 1)
        {
            throw FacadeException.BadRequest("\"photos\" is the number of pages photographed, from 1.");
        }

        return PdfDocument.IsPdf(pdf)
            ? new OpenDocument(Photographed(Project.CreateDocument(id, Path.ChangeExtension(fileName, PdfDocument.Extension), pdf, now), photos))
            : throw new FacadeException("import", words.T("This file is not a PDF, though its name ends in .pdf.", "Этот файл не PDF, хотя его имя кончается на .pdf.")) { Data = { ["reason"] = "damaged" } };
    }

    /// <summary>
    /// A new piece for a recording the player added as it is (version 1.1, finding 29), with no notes: "recording" holds
    /// its "durationMs" and, for a sound whose bytes are not sent (<paramref name="sound"/> empty), its "sha256"; the bytes,
    /// when sent, give its fingerprint and size, as <c>sources.original.add</c> takes them. "device" is the device that keeps
    /// the sound ("ipad", the default, or "web").
    /// </summary>
    public static OpenDocument CreateRecording(string id, string fileName, ReadOnlySpan<byte> sound, Request request, DateTimeOffset now)
    {
        Request recording = request.Object("recording");
        try
        {
            return new OpenDocument(Project.CreateRecording(
                id,
                fileName,
                recording.Long("durationMs"),
                sound.IsEmpty ? recording.Text("sha256") : PieceOriginal.Fingerprint(sound),
                sound.IsEmpty ? null : sound.Length,
                SourceRequest.DeviceOf(request),
                now));
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }

    /// <summary>
    /// A saved piece without its notes: a PDF piece with no notes, or one with notes read from its PDF or added beside it
    /// (testing item 16), opened in its "PDF" view, the calls here being about the PDF and its marks; or (version 17) a
    /// piece that is only its recording.
    /// </summary>
    public static OpenDocument Open(Project project) =>
        project is { Document: not null } or { IsOnlyRecording: true } ? new OpenDocument(project) : throw new ArgumentException("The project has no PDF or recording of its own.", nameof(project));

    /// <summary>
    /// The open answer: the project's summary and what the document is, or for a piece that is only its recording (version
    /// 1.1), "recording" in place of "document": its original's "id", "fileName" and "durationMs" (null when not known).
    /// </summary>
    public void Summary(Utf8JsonWriter json)
    {
        Answer.Project(json, "project", Project);
        if (Project.Document is not { } pdf)
        {
            PieceOriginal recording = Project.Originals.First(static original => original.Kind == OriginalKind.Audio);
            json.WriteStartObject("recording");
            json.WriteString("id", recording.Id);
            json.WriteString("fileName", recording.FileName);
            Answer.OptionalNumber(json, "durationMs", recording.DurationMs);
            json.WriteEndObject();
            return;
        }

        json.WriteStartObject("document");
        json.WriteString("fileName", Project.DocumentFileName);
        json.WriteNumber("bytes", pdf.Length);
        json.WriteString("kind", Kind);
        json.WriteEndObject();
    }

    /// <summary>"images" for a PDF made of photos of pages, else "pdf" (within contract 12, v0.4).</summary>
    private string Kind => Project.FindOriginal(PieceOriginal.DocumentId)?.Kind == OriginalKind.Images ? "images" : "pdf";

    private static Project Photographed(Project project, int? photos) =>
        photos is { } pages && project.FindOriginal(PieceOriginal.DocumentId) is { } original
            ? project.WithOriginal(original with { Kind = OriginalKind.Images, Pages = pages })
            : project;

    /// <summary>
    /// The registry's call named <paramref name="name"/> on this piece (<see cref="CallRegistry.OnDocument"/>); "noNotes" for
    /// any other, and for a call flagged <see cref="CallFlags.NeedsPdf"/> on a piece that is only its recording.
    /// </summary>
    public string Call(string name, Request request) =>
        CallRegistry.OnDocument.TryGetValue(name, out FacadeCall<OpenDocument>? call) && (Project.Document is not null || !call.Flags.HasFlag(CallFlags.NeedsPdf)) ? call.Run(this, request)
        : Project.Document is null ? throw new FacadeException("noNotes", $"This piece is a recording with no notes yet, so there is no call \"{name}\" on it. Recognise its notes first.")
        : throw new FacadeException("noNotes", $"This piece is a PDF with no notes yet, so there is no call \"{name}\" on it. Read the notes from it first.");

    /// <summary>The calls on a piece without notes (<see cref="CallRegistry.OnDocument"/>), the sources' apart (<see cref="SourceRequest.Calls"/>).</summary>
    internal static readonly FacadeCall<OpenDocument>[] Calls =
    [
        new("project", static (document, _) => Answer.Write(json => Answer.Project(json, "project", document.Project))),
        new("project.save", static (document, request) => document.Save(request)),
        new("project.metadata", static (document, request) => document.Metadata(request)),
        new("document", static (document, request) => document.Pages(request), CallFlags.NeedsPdf),
        new("document.replace", static (document, request) => document.Replace(request), CallFlags.NeedsPdf),
        new("document.marks", static (document, request) => Answer.Write(json => Marks(json, document.Project.PageMarksOf(document.OriginalOf(request)))), CallFlags.NeedsPdf),
        new("document.ink.add", static (document, request) => document.Change(request, marks => marks.WithInk([.. marks.Ink.Where(stroke => !Ids(request.Elements("strokes")).Contains(stroke.Id)), .. request.Elements("strokes").Select(ReadStroke)])), CallFlags.NeedsPdf),
        new("document.ink.remove", static (document, request) => document.Change(request, marks => marks.WithInk([.. marks.Ink.Where(stroke => !request.Texts("ids").Contains(stroke.Id))])), CallFlags.NeedsPdf),
        new("document.ink.clear", static (document, request) => document.Change(request, marks => marks.WithInk(request.Int("page") is { } page ? [.. marks.Ink.Where(stroke => stroke.Bar.Value != page)] : [])), CallFlags.NeedsPdf),
        new("document.bookmark.add", static (document, request) => document.Change(request, marks => marks.AddBookmark(request.Int("page") ?? throw FacadeException.BadRequest("Send \"page\", from 1."), request.Text("name"), Now(request))), CallFlags.NeedsPdf),
        new("document.bookmark.rename", static (document, request) => document.Change(request, marks => marks.RenameBookmark(request.Required("id"), request.Text("name"))), CallFlags.NeedsPdf),
        new("document.bookmark.remove", static (document, request) => document.Change(request, marks => marks.RemoveBookmark(request.Required("id"))), CallFlags.NeedsPdf),
        new("document.read", static (document, request) => document.Read(request), CallFlags.NeedsPdf),
    ];

    /// <summary>This piece's project as the sources' calls change it (<see cref="SourceRequest.Calls"/>), in the request's "language".</summary>
    internal SourceOwner HeldSources(Request request) => new(() => Project, project => Project = project, null, Words.For(request.Text("language")));

    /// <summary>The PDF piece with the title, composer, tags and playlists of the request (contract 12), answered as its summary.</summary>
    private string Metadata(Request request)
    {
        Project = MetadataRequest.Apply(Project, request);
        return Answer.Write(json => Answer.Project(json, "project", Project));
    }

    /// <summary>
    /// The PDF shown, with the marks on its pages: the piece's first PDF, or with "original" (version 0.6) another PDF or
    /// photos kept in the file, so a second PDF takes the pen too.
    /// </summary>
    private string Pages(Request request)
    {
        string id = OriginalOf(request);
        byte[] pdf = id == PieceOriginal.DocumentId ? Project.Document!
            : Project.Sources.Data.GetValueOrDefault(id) ?? throw FacadeException.Unavailable($"The piece keeps no bytes of {id} in its file.");
        return Answer.Write(json =>
        {
            json.WriteString("fileName", id == PieceOriginal.DocumentId ? Project.DocumentFileName : Project.FindOriginal(id)!.FileName);
            json.WriteString("kind", id == PieceOriginal.DocumentId ? Kind : Project.FindOriginal(id)!.Kind.ToString().ToLowerInvariant());
            json.WriteBase64String("pdf", pdf);
            Marks(json, Project.PageMarksOf(id));
        });
    }

    /// <summary>The original whose pages a call is about: "original", the piece's first PDF by default; one with pages of a live PDF or photos.</summary>
    private string OriginalOf(Request request)
    {
        string id = request.Text("original") ?? PieceOriginal.DocumentId;
        return id == PieceOriginal.DocumentId || Project.FindOriginal(id) is { Kind: OriginalKind.Pdf or OriginalKind.Images }
            ? id
            : throw FacadeException.BadRequest($"There is no PDF or photos {id} in this piece.");
    }

    private static void Marks(Utf8JsonWriter json, DocumentMarks marks)
    {
        json.WriteStartObject("marks");
        json.WriteStartArray("ink");
        foreach (InkStroke stroke in marks.Ink)
        {
            ProjectFile.WriteStroke(json, stroke, static (writer, ink) => writer.WriteNumber("page", ink.Bar.Value));
        }

        json.WriteEndArray();
        json.WriteStartArray("bookmarks");
        foreach (PageBookmark bookmark in marks.Bookmarks)
        {
            json.WriteStartObject();
            json.WriteString("id", bookmark.Id);
            json.WriteNumber("page", bookmark.Page);
            if (bookmark.Name is { } named)
            {
                json.WriteString("name", named);
            }
            else
            {
                json.WriteNull("name");
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private string Change(Request request, Func<DocumentMarks, DocumentMarks> change)
    {
        string id = OriginalOf(request);
        try
        {
            Project = Project.WithPageMarks(id, change(Project.PageMarksOf(id)));
        }
        catch (Exception exception) when (exception is ArgumentException or ProjectFileException)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        return Answer.Write(json => Marks(json, Project.PageMarksOf(id)));
    }

    private string Save(Request request)
    {
        if (request.Text("now") is { } text)
        {
            Project = DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset now)
                ? Project with { LastOpened = now }
                : throw FacadeException.BadRequest("\"now\" must be an ISO 8601 time.");
        }

        return System.Text.Encoding.UTF8.GetString(ProjectFile.Write(Project));
    }

    /// <summary>
    /// The notes a scan server read from the PDF, into this same piece (0011, 0023; testing item 16, Andrei, 2026-11-06:
    /// "the PDF is available right inside the piece"): its project file with the notes, the PDF, its marks and bookmarks as
    /// they were, where each page starts in the notes (<c>documentPages</c>), the scan's Check flags open and a resume point
    /// at the start, as the web app's PDF view makes it. With "reports" (version 0.4), as scan.join takes them, one per page
    /// of the notes, the scan keeps the server's page reports and the bars on a poor page are flagged PageQuality. The piece
    /// is then opened as notes; until it is closed, saving it here writes the notes too. The engine's mistakes are put right
    /// first (<see cref="ScanRepair"/>), as scan.join does, so a PDF read in one request is repaired as one read in parts.
    /// </summary>
    private string Read(Request request)
    {
        byte[] musicXml;
        try
        {
            musicXml = ScanRepair.Repair(Convert.FromBase64String(request.Required("musicXml")));
        }
        catch (FormatException)
        {
            throw FacadeException.BadRequest("\"musicXml\" must be base64.");
        }

        if (Project.HasNotes)
        {
            throw FacadeException.Unavailable("The notes of this PDF are read already.");
        }

        Words words = Words.For(request.Text("language"));
        string name = Path.ChangeExtension(Project.FileName, ".musicxml");
        PieceImportResult imported = PieceImporter.Import(name, musicXml);
        if (imported.Piece is not { } piece)
        {
            throw new FacadeException("import", imported.Rejection?.Text(words) ?? words.T("No notes could be read from these pages.", "На этих страницах не удалось прочитать нот."));
        }

        DateTimeOffset now = Now(request);
        int pages = Math.Max(1, request.Int("pages") ?? 1);
        Project read;
        try
        {
            read = Project.Recognised(musicXml, request.Text("engine") ?? "unknown engine", PageMap.Of(piece, pages), now, ScanRequest.ReportsIn(request));
            read = read with
            {
                Check = CheckFlag.FromScan(read.Scan!.CheckOf(piece), []),
                LastOpened = now,
            };
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        Project = read.AddResumePoint(ResumePoint.Start(read.NextResumePointId(), now));
        return Answer.Write(json =>
        {
            Answer.Project(json, "project", Project);
            json.WriteString("file", System.Text.Encoding.UTF8.GetString(ProjectFile.Write(Project)));
        });
    }

    /// <summary>
    /// The pages again, as one new PDF of as many pages, while no notes are read from them (v0.4, «Фото страниц»: a page
    /// photographed again): the marks and bookmarks stay on their pages, and the original keeps what it is, with the new
    /// bytes' fingerprint and size. Answers the project's summary.
    /// </summary>
    private string Replace(Request request)
    {
        byte[] pdf;
        try
        {
            pdf = Convert.FromBase64String(request.Required("pdf"));
        }
        catch (FormatException)
        {
            throw FacadeException.BadRequest("\"pdf\" must be base64.");
        }

        if (Project.HasNotes)
        {
            throw FacadeException.Unavailable("The notes of this PDF are read already, so its pages stay as they are.");
        }

        if (!PdfDocument.IsPdf(pdf) || PdfDocument.CheckSize(pdf.Length) is not null)
        {
            throw FacadeException.BadRequest("\"pdf\" must be a PDF of at most 50 MB.");
        }

        Project replaced = Project with { Document = pdf };
        if (replaced.FindOriginal(PieceOriginal.DocumentId) is { } original)
        {
            replaced = replaced.WithOriginal(original with { Sha256 = null, Bytes = pdf.Length });
        }

        Project = replaced.Fingerprinted();
        return Answer.Write(json => Answer.Project(json, "project", Project));
    }

    private static HashSet<string> Ids(IReadOnlyList<JsonElement> strokes) =>
        [.. strokes.Select(static stroke => stroke.TryGetProperty("id", out JsonElement id) ? id.GetString() ?? "" : "")];

    /// <summary>A stroke as the app sends it: the pencil layer's shape, with "page" for the page it starts on (from 1).</summary>
    private static InkStroke ReadStroke(JsonElement stroke)
    {
        try
        {
            if (stroke.ValueKind == JsonValueKind.Object && stroke.TryGetProperty("page", out JsonElement page) && !stroke.TryGetProperty("bar", out _))
            {
                using var buffer = new MemoryStream();
                using (var json = new Utf8JsonWriter(buffer))
                {
                    json.WriteStartObject();
                    foreach (JsonProperty property in stroke.EnumerateObject())
                    {
                        if (property.Name == "page")
                        {
                            json.WritePropertyName("bar");
                            page.WriteTo(json);
                        }
                        else
                        {
                            property.WriteTo(json);
                        }
                    }

                    json.WriteEndObject();
                }

                using JsonDocument document = JsonDocument.Parse(buffer.ToArray());
                return ProjectFile.ReadStroke(document.RootElement);
            }

            return ProjectFile.ReadStroke(stroke);
        }
        catch (ProjectFileException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }

    private static DateTimeOffset Now(Request request) =>
        request.Text("now") is { } text
            ? DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : throw FacadeException.BadRequest("\"now\" must be an ISO 8601 time.")
            : DateTimeOffset.UtcNow;
}