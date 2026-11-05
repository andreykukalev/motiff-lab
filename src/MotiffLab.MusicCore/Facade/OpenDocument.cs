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

    /// <summary>A new PDF piece from the file the player added.</summary>
    public static OpenDocument Create(string id, string fileName, byte[] pdf, Words words, DateTimeOffset now)
    {
        if (PdfDocument.CheckSize(pdf.Length) is { } rejection)
        {
            throw new FacadeException("import", $"{rejection.Text(words)} {rejection.TipIn(words)}".Trim()) { Data = { ["reason"] = Answer.Name(rejection.Reason), ["tip"] = rejection.TipIn(words) } };
        }

        return PdfDocument.IsPdf(pdf)
            ? new OpenDocument(Project.CreateDocument(id, Path.ChangeExtension(fileName, PdfDocument.Extension), pdf, now))
            : throw new FacadeException("import", words.T("This file is not a PDF, though its name ends in .pdf.", "Этот файл не PDF, хотя его имя кончается на .pdf.")) { Data = { ["reason"] = "damaged" } };
    }

    /// <summary>A saved PDF piece: a project with its document and no notes.</summary>
    public static OpenDocument Open(Project project) =>
        project is { HasNotes: false, Document: not null } ? new OpenDocument(project) : throw new ArgumentException("The project is not a PDF piece.", nameof(project));

    /// <summary>The open answer: the project's summary and what the document is.</summary>
    public void Summary(Utf8JsonWriter json)
    {
        Answer.Project(json, "project", Project);
        json.WriteStartObject("document");
        json.WriteString("fileName", Project.DocumentFileName);
        json.WriteNumber("bytes", Project.Document!.Length);
        json.WriteEndObject();
    }

    public string Call(string name, Request request) => name switch
    {
        "project" => Answer.Write(json => Answer.Project(json, "project", Project)),
        "project.save" => Save(request),
        "document" => Answer.Write(json =>
        {
            json.WriteString("fileName", Project.DocumentFileName);
            json.WriteBase64String("pdf", Project.Document);
            Marks(json);
        }),
        "document.marks" => Answer.Write(Marks),
        "document.ink.add" => Change(marks => marks.WithInk([.. marks.Ink.Where(stroke => !Ids(request.Elements("strokes")).Contains(stroke.Id)), .. request.Elements("strokes").Select(ReadStroke)])),
        "document.ink.remove" => Change(marks => marks.WithInk([.. marks.Ink.Where(stroke => !request.Texts("ids").Contains(stroke.Id))])),
        "document.ink.clear" => Change(marks => marks.WithInk(request.Int("page") is { } page ? [.. marks.Ink.Where(stroke => stroke.Bar.Value != page)] : [])),
        "document.bookmark.add" => Change(marks => marks.AddBookmark(request.Int("page") ?? throw FacadeException.BadRequest("Send \"page\", from 1."), request.Text("name"), Now(request))),
        "document.bookmark.rename" => Change(marks => marks.RenameBookmark(request.Required("id"), request.Text("name"))),
        "document.bookmark.remove" => Change(marks => marks.RemoveBookmark(request.Required("id"))),
        _ => throw new FacadeException("noNotes", $"This piece is a PDF with no notes yet, so there is no call \"{name}\" on it. Read the notes from it first."),
    };

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