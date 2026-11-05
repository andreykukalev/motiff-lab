using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The one entry point for an app (0020): keeps the pieces the app has open, numbered from 1, and
/// answers calls by name with JSON text. The web app calls it from C#; the iPad app through the C
/// functions of the native build. The same calls in the same order give the same answers.
/// </summary>
/// <remarks>
/// Not thread-safe: an app calls it from one thread at a time. Every answer is one JSON object; a
/// failure is {"error": code, "message": text} and leaves the piece as it was.
/// </remarks>
public sealed class MusicCoreFacade
{
    /// <summary>
    /// 4 brings performances (0022) and project file version 13: performances, performance (the comparison bar by bar with
    /// its markers), performance.add, .rename, .dismiss, .take, .restore and .remove; a Check flag's source can be performance.
    /// 3 brings the three phrase levels (0018, section 3; preparation items 7–9): the phrases answer names the level shown and
    /// the units of a level with their parent, "long" replaces "fitsSelection", the levels are short, phrase and sentence,
    /// phrases.level and phrases.edit take a level and split, understand ranks the facts and offers Try chips, and try sounds a chip.
    /// 2 moves to project file version 12 (0018): the project answer counts resume points, ideas, marks, bookmarks and
    /// open Check flags in place of sessions and observations, and the calls for marks, bookmarks, resume points, ideas,
    /// settings and Check join.
    /// 1 was the first contract: open and save, piece, phrases with their edits, select, understand,
    /// experiments, the variation chain, notation, playback, the catalogue and the Piece DNA placeholder.
    /// </summary>
    public const int Contract = 4;

    private readonly Dictionary<int, OpenPiece> _pieces = [];
    private readonly Dictionary<int, OpenDocument> _documents = [];
    private int _next = 1;

    /// <summary>How many pieces are open, PDF pieces with them.</summary>
    public int Count => _pieces.Count + _documents.Count;

    /// <summary>The runtime the facade runs on, for the version call and for logs.</summary>
    public static string Runtime =>
        $"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.RuntimeIdentifier}; " +
        (OperatingSystem.IsBrowser() ? "WebAssembly" : System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT");

    /// <summary>
    /// Opens a MIDI or MusicXML file as a new project, or a project file (.motiff.json), and answers
    /// with the piece's number, its project and its summary. The request may carry "language" ("en"
    /// or "ru"), "id" for the new project and "now" (ISO 8601) for its creation time. A PDF, or the
    /// project file of a PDF piece, opens as a PDF piece (0023): the answer has "document" in place of
    /// "summary", and only the document's calls work on it.
    /// </summary>
    public string Open(string fileName, ReadOnlySpan<byte> data, string? request = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        try
        {
            using Request fields = Request.Parse(request);
            Words words = Words.For(fields.Text("language"));
            DateTimeOffset now = fields.Text("now") is { } text
                ? DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : throw FacadeException.BadRequest("\"now\" must be an ISO 8601 time.")
                : DateTimeOffset.UtcNow;
            byte[] bytes = data.ToArray();
            int number = _next;
            string id = fields.Text("id") ?? string.Create(CultureInfo.InvariantCulture, $"p_{number:0000}");
            if (IsPdf(fileName, bytes) || (IsProjectFile(fileName, bytes) && ReadDocument(bytes) is not null))
            {
                OpenDocument document = IsPdf(fileName, bytes) ? OpenDocument.Create(id, fileName, bytes, words, now) : OpenDocument.Open(ReadDocument(bytes)!);
                _documents[number] = document;
                _next++;
                return Answer.Write(json =>
                {
                    json.WriteNumber("piece", number);
                    document.Summary(json);
                });
            }

            OpenPiece piece = IsProjectFile(fileName, bytes)
                ? OpenPiece.Open(bytes, words)
                : OpenPiece.Import(id, fileName, bytes, words, now);
            _pieces[number] = piece;
            _next++;
            return Answer.Write(json =>
            {
                json.WriteNumber("piece", number);
                piece.Summary(json);
            });
        }
        catch (Exception exception)
        {
            return Failure(exception);
        }
    }

    /// <summary>Runs one call on an open piece; piece 0 is for the stateless calls (version, catalogue, settings, settings.state).</summary>
    public string Call(int piece, string name, string? request = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        try
        {
            using Request fields = Request.Parse(request);
            if (piece == 0)
            {
                return name switch
                {
                    "version" => Version(),
                    "catalogue" => Answer.Write(json => Answer.Catalogue(json, Words.For(fields.Text("language")))),
                    "settings" => Answer.Write(json => Answer.SettingsCatalogue(json)),
                    "settings.state" => OpenPiece.SettingsState(fields),
                    "project.summary" => ProjectSummary(fields),
                    _ when Count == 0 && IsPieceCall(name) => throw new FacadeException("noPiece", "Open a piece first."),
                    _ => throw new FacadeException("unknownCall", $"There is no call \"{name}\" without a piece."),
                };
            }

            if (_documents.TryGetValue(piece, out OpenDocument? document))
            {
                return document.Call(name, fields);
            }

            if (!_pieces.TryGetValue(piece, out OpenPiece? open))
            {
                throw new FacadeException("noPiece", $"Piece {piece} is not open.");
            }

            return open.Call(name, fields);
        }
        catch (Exception exception)
        {
            return Failure(exception);
        }
    }

    /// <summary>
    /// What a library card shows of a project file without opening the piece (within contract 4): {"project": {…}} as an
    /// open piece's summary has it. The request's "file" is the project file's text.
    /// </summary>
    private static string ProjectSummary(Request fields)
    {
        string file = fields.Required("file");
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(file));
        return Answer.Write(json => Answer.Project(json, "project", project));
    }

    /// <summary>Forgets an open piece; false when it was not open.</summary>
    public bool Close(int piece) => _pieces.Remove(piece) || _documents.Remove(piece);

    public static string Version() => Answer.Write(json =>
    {
        json.WriteNumber("contract", Contract);
        json.WriteNumber("projectFile", ProjectFile.Version);
        json.WriteString("runtime", Runtime);
    });

    private static bool IsPieceCall(string name) =>
        name is "piece" or "phrases" or "select" or "understand" or "experiments" or "try" or "notation" or "playback" or "roll" or "dna" or "marks" or "bookmarks" or "resume" or "ideas" or "check" or "performances" or "performance"
        || (name.Contains('.') && !name.StartsWith("settings.", StringComparison.Ordinal));

    /// <summary>A PDF the player added to read as it is (0023): by its first bytes, or by its name with bytes that are not a project file.</summary>
    private static bool IsPdf(string fileName, byte[] data) =>
        PdfDocument.IsPdf(data) || (fileName.EndsWith(PdfDocument.Extension, StringComparison.OrdinalIgnoreCase) && !IsProjectFile(fileName, data));

    /// <summary>The project in a project file when it is a PDF piece (a document and no notes); null for a piece with notes.</summary>
    private static Project? ReadDocument(byte[] projectFile)
    {
        try
        {
            return ProjectFile.Read(projectFile) is { HasNotes: false, Document: not null } project ? project : null;
        }
        catch (ProjectFileException)
        {
            return null;
        }
    }

    /// <summary>A project file by its extension, or by its first bytes: a JSON object naming the format.</summary>
    private static bool IsProjectFile(string fileName, byte[] data)
    {
        if (fileName.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (data.Length == 0 || data[0] != (byte)'{')
        {
            return false;
        }

        string head = Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 256));
        return head.Contains(ProjectFile.FormatName, StringComparison.Ordinal);
    }

    private static string Failure(Exception exception) => exception switch
    {
        FacadeException failure when failure.Code == "import" => Answer.Write(json =>
        {
            json.WriteString("error", failure.Code);
            json.WriteString("message", failure.Message);
            json.WriteString("reason", failure.Data["reason"] as string ?? "damaged");
            if (failure.Data["tip"] is string tip)
            {
                json.WriteString("tip", tip);
            }
        }),
        FacadeException failure => Answer.Error(failure.Code, failure.Message),
        ArgumentException argument => Answer.Error("badRequest", argument.Message),
        ProjectFileException file => Answer.Error("badRequest", file.Message),
        _ => Answer.Error("internal", $"{exception.GetType().Name}: {exception.Message}"),
    };
}