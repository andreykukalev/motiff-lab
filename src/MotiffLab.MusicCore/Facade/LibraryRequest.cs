using System.Globalization;
using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// library.plan, library.relink, library.confirm and library.link on piece 0 (contract 12, calls added; version 0.5, «Импорт папки»): the import plan
/// of a folder the app read (<see cref="LibraryImport"/>), the same for the iPad as for the web, and the metadata of a
/// piece once the player confirmed its suggestions. Nothing is kept and no file is touched: the app reads the folder and
/// saves the projects.
/// </summary>
internal static class LibraryRequest
{
    /// <summary>
    /// {"files": [{"path", "bytes", "sha256", "pages", "pdfTitle", "pdfAuthor", "title", "composer" (a MusicXML header as
    /// read) or "musicXml" (base64, the header read here), "unreadable"}], "library": [{"project", "original", "sha256",
    /// "bytes", "pages", "fileName", "folder", "title", "composer"}], "folders": "playlists" | "tags" | "composers" |
    /// "nothing", "linked"} → {"files": [{"path", "status", "sameAs", "project", "provisional", "title", "composer"
    /// ({"value", "from", "provenance", "confidence"} or null), "playlists", "tags", "collection", "possiblySame":
    /// {"files", "pieces"}}], "fingerprint": [paths], "counts": {"found", "new", "inLibrary", "duplicates", "unsuitable"}}.
    /// </summary>
    public static string Plan(Request fields)
    {
        ImportPlan plan;
        try
        {
            plan = LibraryImport.Plan(
                fields.Elements("files").Select(FoundOf),
                fields.Elements("library").Select(OriginalOf),
                new ImportOptions(fields.Enum<FolderMapping>("folders") ?? FolderMapping.Playlists, fields.Bool("linked") ?? false));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        return Answer.Write(json =>
        {
            json.WriteStartArray("files");
            foreach (PlannedFile file in plan.Files)
            {
                WriteFile(json, file);
            }

            json.WriteEndArray();
            WriteTexts(json, "fingerprint", plan.ToFingerprint);
            json.WriteStartObject("counts");
            json.WriteNumber("found", plan.Found);
            json.WriteNumber("new", plan.New);
            json.WriteNumber("inLibrary", plan.InLibrary);
            json.WriteNumber("duplicates", plan.Duplicates);
            json.WriteNumber("unsuitable", plan.Unsuitable);
            json.WriteEndObject();
        });
    }

    /// <summary>
    /// {"file": a project file, "title", "composer", "tags", "playlists"} → {"project", "file"}: the confirmed values fill
    /// only the title and composer the player has not set, and the tags and playlists are added (<see cref="LibraryImport.Confirmed"/>).
    /// </summary>
    public static string Confirm(Request fields)
    {
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(fields.Required("file")));
        project = project with
        {
            Metadata = LibraryImport.Confirmed(project.Metadata, fields.Text("title"), fields.Text("composer"), fields.Texts("tags"), fields.Texts("playlists")),
        };
        return Answer.Write(json =>
        {
            Answer.Project(json, "project", project);
            json.WriteString("file", Encoding.UTF8.GetString(ProjectFile.Write(project)));
        });
    }

    /// <summary>
    /// {"library": [originals, as library.plan takes them, with "folder"], "files": [a fresh listing, as library.plan takes
    /// it]} → {"originals": [{"project", "original", "status" ("linked", "unconfirmed", "moved", "pending", "changed",
    /// "missing"), "path", "candidates": [{"path", "score", "samePlace"}]}], "fingerprint": [paths], "confirm": [paths]}
    /// (<see cref="LibraryRelink"/>): where each original is now, and which files to hash to decide and to confirm.
    /// </summary>
    public static string Relink(Request fields)
    {
        RelinkPlan plan;
        try
        {
            plan = LibraryRelink.Plan(fields.Elements("library").Select(OriginalOf), fields.Elements("files").Select(FoundOf));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        return Answer.Write(json =>
        {
            json.WriteStartArray("originals");
            foreach (Relinked original in plan.Originals)
            {
                json.WriteStartObject();
                json.WriteString("project", original.Original.ProjectId);
                json.WriteString("original", original.Original.OriginalId);
                json.WriteString("status", Answer.Name(original.Status));
                json.WriteString("path", original.Path);
                json.WriteStartArray("candidates");
                foreach (RelinkCandidate candidate in original.Candidates)
                {
                    json.WriteStartObject();
                    json.WriteString("path", candidate.Path);
                    json.WriteNumber("score", candidate.Score);
                    json.WriteBoolean("samePlace", candidate.SamePlace);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            WriteTexts(json, "fingerprint", plan.ToFingerprint);
            WriteTexts(json, "confirm", plan.ToConfirm);
        });
    }

    /// <summary>
    /// {"fileName", "folder", "sha256", "bytes", "pages", "id", "now", "device"} → {"project", "file"}: a new piece for a PDF
    /// that stays in the player's folder (<see cref="Project.CreateLinkedDocument"/>), held by the device ("ipad", the
    /// default, or "web"). With "file", a project file whose PDF is kept by link, that PDF found again: its place in the
    /// folder, and the fingerprint, size and pages of the file there (the same file moved, or the one the player said is
    /// it), held by the device; its notes, marks and metadata stay as they were. Nothing is read or changed on disk.
    /// </summary>
    public static string Link(Request fields)
    {
        string fileName = fields.Required("fileName");
        string folder = fields.Text("folder") ?? string.Empty;
        string sha256 = fields.Required("sha256");
        long bytes = fields.Long("bytes") ?? throw FacadeException.BadRequest("Send \"bytes\", the file's size.");
        int? pages = fields.Int("pages");
        HoldingDevice device = fields.Enum<HoldingDevice>("device") ?? HoldingDevice.Ipad;
        DateTimeOffset now = fields.Text("now") is { } text
            ? DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : throw FacadeException.BadRequest("\"now\" must be an ISO 8601 time.")
            : DateTimeOffset.UtcNow;
        Project project;
        if (fields.Text("file") is { } file)
        {
            Project linked = ProjectFile.Read(Encoding.UTF8.GetBytes(file));
            PieceOriginal original = linked.FindOriginal(PieceOriginal.DocumentId) is { Kind: OriginalKind.Pdf, Kept: OriginalKept.Link } kept
                ? kept
                : throw FacadeException.Unavailable("Only a piece whose PDF is kept by link in a folder is linked again so.");
            var found = new PieceOriginal(original.Id, original.Kind, fileName, OriginalKept.Link, original.AddedAt)
            {
                Name = original.Name,
                Folder = folder,
                Sha256 = sha256,
                Bytes = bytes,
                Pages = pages ?? (original.Sha256 == sha256 ? original.Pages : null),
                Held = original.Held,
            };
            project = linked.WithOriginal(found.HeldBy(device, now));
        }
        else
        {
            project = Project.CreateLinkedDocument(fields.Text("id") ?? MusicCoreFacade.NewProjectId(), fileName, folder, sha256, bytes, pages, device, now);
        }

        return Answer.Write(json =>
        {
            Answer.Project(json, "project", project);
            json.WriteString("file", Encoding.UTF8.GetString(ProjectFile.Write(project)));
        });
    }

    private static FoundFile FoundOf(JsonElement element)
    {
        Request file = Request.Of(element);
        return new FoundFile(file.Required("path"), file.Long("bytes") ?? throw FacadeException.BadRequest("A found file needs \"bytes\"."))
        {
            Sha256 = file.Text("sha256"),
            Pages = file.Int("pages"),
            PdfTitle = file.Text("pdfTitle"),
            PdfAuthor = file.Text("pdfAuthor"),
            Header = file.Text("musicXml") is { } data ? ScoreHeader.Read(Convert.FromBase64String(data)) : new ScoreHeader(file.Text("title"), file.Text("composer")),
            Unreadable = file.Bool("unreadable") ?? false,
        };
    }

    private static LibraryOriginal OriginalOf(JsonElement element)
    {
        Request original = Request.Of(element);
        return new LibraryOriginal(original.Required("project"), original.Required("original"), original.Text("sha256"))
        {
            Bytes = original.Long("bytes"),
            Pages = original.Int("pages"),
            FileName = original.Text("fileName"),
            Folder = original.Text("folder"),
            Title = original.Text("title"),
            Composer = original.Text("composer"),
        };
    }

    private static void WriteFile(Utf8JsonWriter json, PlannedFile file)
    {
        json.WriteStartObject();
        json.WriteString("path", file.File.Path);
        json.WriteString("status", Answer.Name(file.Status));
        json.WriteString("sameAs", file.SameAs);
        json.WriteString("project", file.Project);
        json.WriteBoolean("provisional", file.Provisional);
        WriteSuggestion(json, "title", file.Title);
        WriteSuggestion(json, "composer", file.Composer);
        WriteTexts(json, "playlists", file.Playlists);
        WriteTexts(json, "tags", file.Tags);
        json.WriteBoolean("collection", file.PossibleCollection);
        json.WriteStartObject("possiblySame");
        WriteTexts(json, "files", file.PossiblySameFiles);
        WriteTexts(json, "pieces", file.PossiblySamePieces);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static void WriteSuggestion(Utf8JsonWriter json, string name, Suggestion? suggestion)
    {
        if (suggestion is null)
        {
            json.WriteNull(name);
            return;
        }

        json.WriteStartObject(name);
        json.WriteString("value", suggestion.Value);
        json.WriteString("from", Answer.Name(suggestion.Source));
        Answer.Attribution(json, suggestion.Attribution);
        json.WriteEndObject();
    }

    private static void WriteTexts(Utf8JsonWriter json, string name, IEnumerable<string> texts)
    {
        json.WriteStartArray(name);
        foreach (string text in texts)
        {
            json.WriteStringValue(text);
        }

        json.WriteEndArray();
    }
}