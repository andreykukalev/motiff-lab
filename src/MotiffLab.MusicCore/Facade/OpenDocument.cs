using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// A PDF piece the app has open (0023, within contract 4): the pages kept as the player added them and the marks made on
/// them, with no notes. It answers only the calls about the document and its project file; a call about notes is refused
/// with "noNotes", since nothing on a PDF's pages is music the engine knows until the notes are read from them.
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
            throw new FacadeException("import", $"{rejection.Text(words)} {rejection.TipIn(words)}".Trim()) { Data = { ["reason"] = Answer.Name(rejection.Reason), ["tip"] = rejection.TipIn(words) } };
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
    /// A saved piece's PDF: a PDF piece with no notes, or one with notes read from its PDF or added beside it (testing item
    /// 16), opened in its "PDF" view. Either way the calls here are about the PDF and its marks.
    /// </summary>
    public static OpenDocument Open(Project project) =>
        project is { Document: not null } ? new OpenDocument(project) : throw new ArgumentException("The project has no PDF.", nameof(project));

    /// <summary>The open answer: the project's summary and what the document is.</summary>
    public void Summary(Utf8JsonWriter json)
    {
        Answer.Project(json, "project", Project);
        json.WriteStartObject("document");
        json.WriteString("fileName", Project.DocumentFileName);
        json.WriteNumber("bytes", Project.Document!.Length);
        json.WriteString("kind", Kind);
        json.WriteEndObject();
    }

    /// <summary>"images" for a PDF made of photos of pages, else "pdf" (within contract 12, v0.4).</summary>
    private string Kind => Project.FindOriginal(PieceOriginal.DocumentId)?.Kind == OriginalKind.Images ? "images" : "pdf";

    private static Project Photographed(Project project, int? photos) =>
        photos is { } pages && project.FindOriginal(PieceOriginal.DocumentId) is { } original
            ? project.WithOriginal(original with { Kind = OriginalKind.Images, Pages = pages })
            : project;

    public string Call(string name, Request request) => name switch
    {
        "project" => Answer.Write(json => Answer.Project(json, "project", Project)),
        "project.save" => Save(request),
        "project.metadata" => Metadata(request),
        "document" => Answer.Write(json =>
        {
            json.WriteString("fileName", Project.DocumentFileName);
            json.WriteString("kind", Kind);
            json.WriteBase64String("pdf", Project.Document);
            Marks(json);
        }),
        "document.replace" => Replace(request),
        "document.marks" => Answer.Write(Marks),
        "document.ink.add" => Change(marks => marks.WithInk([.. marks.Ink.Where(stroke => !Ids(request.Elements("strokes")).Contains(stroke.Id)), .. request.Elements("strokes").Select(ReadStroke)])),
        "document.ink.remove" => Change(marks => marks.WithInk([.. marks.Ink.Where(stroke => !request.Texts("ids").Contains(stroke.Id))])),
        "document.ink.clear" => Change(marks => marks.WithInk(request.Int("page") is { } page ? [.. marks.Ink.Where(stroke => stroke.Bar.Value != page)] : [])),
        "document.bookmark.add" => Change(marks => marks.AddBookmark(request.Int("page") ?? throw FacadeException.BadRequest("Send \"page\", from 1."), request.Text("name"), Now(request))),
        "document.bookmark.rename" => Change(marks => marks.RenameBookmark(request.Required("id"), request.Text("name"))),
        "document.bookmark.remove" => Change(marks => marks.RemoveBookmark(request.Required("id"))),
        "document.read" => Read(request),
        _ => throw new FacadeException("noNotes", $"This piece is a PDF with no notes yet, so there is no call \"{name}\" on it. Read the notes from it first."),
    };

    /// <summary>The PDF piece with the title, composer, tags and playlists of the request (contract 12), answered as its summary.</summary>
    private string Metadata(Request request)
    {
        Project = MetadataRequest.Apply(Project, request);
        return Answer.Write(json => Answer.Project(json, "project", Project));
    }

    private void Marks(Utf8JsonWriter json)
    {
        json.WriteStartObject("marks");
        json.WriteStartArray("ink");
        foreach (InkStroke stroke in Project.DocumentMarks.Ink)
        {
            json.WriteStartObject();
            json.WriteString("id", stroke.Id);
            json.WriteString("tool", Answer.Name(stroke.Tool));
            json.WriteString("colour", Answer.Name(stroke.Colour));
            json.WriteNumber("size", stroke.Size);
            json.WriteNumber("page", stroke.Bar.Value);
            json.WriteNumber("u", stroke.U);
            json.WriteNumber("v", stroke.V);
            json.WriteStartArray("points");
            foreach (double value in stroke.Points)
            {
                json.WriteNumberValue(value);
            }

            json.WriteEndArray();
            json.WriteNumber("pressure", stroke.Pressure);
            Answer.Margin(json, stroke.Margin);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("bookmarks");
        foreach (PageBookmark bookmark in Project.DocumentMarks.Bookmarks)
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

    private string Change(Func<DocumentMarks, DocumentMarks> change)
    {
        try
        {
            Project = Project with { DocumentMarks = change(Project.DocumentMarks) };
        }
        catch (Exception exception) when (exception is ArgumentException or ProjectFileException)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        return Answer.Write(Marks);
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
    /// is then opened as notes; until it is closed, saving it here writes the notes too.
    /// </summary>
    private string Read(Request request)
    {
        byte[] musicXml;
        try
        {
            musicXml = Convert.FromBase64String(request.Required("musicXml"));
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