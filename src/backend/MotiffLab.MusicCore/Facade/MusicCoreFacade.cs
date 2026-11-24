using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The one entry point for an app (0020): keeps the pieces the app has open, numbered from 1, and
/// answers calls by name with JSON text. The iPad app calls it through the C functions of the native
/// build; the web app calls music-core directly and uses only <see cref="NewProjectId"/> and <see cref="Imported"/> (0025). The same calls in the same order give the same answers, apart from a new project's id
/// and creation time when the request names neither.
/// </summary>
/// <remarks>
/// Not thread-safe: an app calls it from one thread at a time. Every answer is one JSON object; a
/// failure is {"error": code, "message": text} and leaves the piece as it was.
/// </remarks>
public sealed partial class MusicCoreFacade
{
    /// <summary>
    /// 14 removes phrases.level and its "remember" (version 1.2), which no app sends since the score's phrase marks; it
    /// answers unknownCall. "levels" and phrases.granularity stay, so a file carrying the setting opens as before.
    /// Also within 14 (version 1.3, ADR 0035 section 5): a page of scan.join's "pages" may be its readings by several engines,
    /// [{"engine", "musicxml"}], merged staff by staff (<see cref="Import.Scan.ScanMerge"/>), and such a page answers "merge":
    /// its engines and each bar's "level" (sure, probable, check) with the engines of each staff; a page sent as text and
    /// "score" with "replace" answer as before; a form and a field added. Also within 14 (c-1b): such a page may be
    /// {"readings": [...], "evidence": the scan server's page evidence}, the evidence settling the staves no two engines
    /// agree on (<see cref="Import.Scan.ScanEvidence"/>); a form added, the answer as before.
    /// Also within 14 (v1.3 i-2, ADR 0035 section 6): scan.join answers "fingerprint" (the score as its Notes keep it) and a
    /// merged bar to check its "variants" (the engines' other readings of its staves to check) and "crop" (its box on the page
    /// image); check answers "fingerprint", the working notes' music; corrections.variant puts a variant in that bar as the
    /// player's corrections, one group undone whole by corrections.undoDraft; fields and a call added.
    /// 13 removes the calls of the bar-by-bar merge, which left the iPad in version 1.1 (ADR 0034, section 8) and which no app
    /// sends: those of 7 (performance.merge, its .open, .choose, .own, .undecide, .undo, .redo, .reset, .cancel, .apply,
    /// .undoApplied, .notation and .playback, and performance.putBack) and performance.remove of 4, as 10 removed calls; they
    /// answer unknownCall, and a performance no longer tells "mergedAt" and "mergeKept". A version is removed as other Notes
    /// are (sources.notes.remove). The project file stays version 17 and reads a performance's "taken" and "merge" and writes
    /// them back as before.
    /// Also within 13 (version 1.1, ADR 0034 section 9 step 2, the iPad's step 5): drafts.sheet, drafts.add, drafts.open,
    /// drafts.rename and drafts.remove (<see cref="DraftRequest"/>), on the piece open, on Notes or a version open only to be
    /// shown, or with "into" from another piece; sources.layers and the Sources sheet's answers gain "drafts", their views
    /// the kind "draft", which sources.view.hide hides as any other; calls, a field and a value added.
    /// Also within 13 (version 1.1, ADR 0034 section 9 step 3, the iPad's step 6): drafts.techniques answers the приёмы of a
    /// place with their numbers, kinds, labels, confidence, whether sure, and where each is found, drafts.take and drafts.leave
    /// take or leave one everywhere or at one bar, drafts.takeSure takes every sure one as a group, drafts.takeFrom a hand or
    /// a span of a variant, drafts.undo the draft's last step (<see cref="DraftTakeRequest"/>); calls added.
    /// Also within 13 (version 1.1, ADR 0034 section 9 step 4, the iPad's step 7): drafts.seams answers a draft's seams, what
    /// differs of key, meter and tempo either side and what is offered, drafts.transpose and drafts.tempo add the step an
    /// offer names, drafts.leaveSeam keeps a seam as it is (<see cref="DraftSeamRequest"/>); calls added.
    /// Also within 13 (version 1.1, the iPad's step 7, the draft's map): drafts.open gives each place its variants and steps
    /// and the draft its shelf, drafts.place moves a variant and drafts.base chooses a place's base (<see cref="DraftRequest"/>);
    /// calls added; drafts.undo takes them back while the piece is open (a draft's structure is its state, not in its journal).
    /// Also within 13 (version 1.2, the phrases spec, «Engine»): the phrases answer is the one answer of the score's marks: a
    /// phrase's start tells "bar", "beat", "found" (what a moved one replaced), "close" and "why" («Почему здесь»), a phrase
    /// its "motifs", the answer "own" and "sections" (dna's form, each with its "phrase", bar and beat); phrases.edit takes
    /// "move", "start", "remove", "putBack", "resetAll" (the phrases only, "replaced") and "restore" with "expected" (Undo,
    /// refused when stale), answering "previous" and "changed"; fields added. A piece opens in phrases, no edit moves a bookmark.
    /// Earlier contracts: ADR 0020, point 2.
    /// </summary>
    public const int Contract = 14;

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
    /// or "ru"), "id" for the new project (without one it gets <see cref="NewProjectId"/>) and "now" (ISO 8601) for its
    /// creation time. A PDF, or the project file of a PDF piece, opens as a PDF piece (0023): the answer has "document" in place of
    /// "summary", and only the document's calls work on it; with "photos" (v0.4) the PDF is that many photographed pages, and
    /// its original is described so. With "transcription" ({"engine", "milliseconds", "parts"}, the
    /// notes a recording's parts were heard as, as performance.add takes them) the data is not read: the new project is
    /// the recording's notes, worked out as the web app works them out (0014, 0016). For a project file, "edits" ("on" or
    /// "off", version 0.5) lays the corrections over the notes one by one or leaves them out; without it they must all fit.
    /// With "recording" ({"durationMs", "sha256"}, version 1.1, finding 29) the file is a recording the player added as it
    /// is, the data its sound (or nothing): the new piece is only that recording, with no notes, and opens as a PDF piece
    /// does, its answer naming the "recording" in place of "document"; so does the project file of such a piece.
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
            string id = fields.Text("id") ?? NewProjectId();
            if (fields.Has("transcription"))
            {
                OpenPiece heard = OpenPiece.Transcribe(id, fileName, fields.Object("transcription"), words, now);
                _pieces[number] = heard;
                _next++;
                return Answer.Write(json =>
                {
                    json.WriteNumber("piece", number);
                    heard.Summary(json);
                });
            }

            // A piece with notes and a PDF (testing item 16) opens as notes, or in its "PDF" view when "view" is "pdf".
            bool asPdf = fields.Text("view") == "pdf";
            bool recording = fields.Has("recording");
            if (recording || IsPdf(fileName, bytes) || (IsProjectFile(fileName, bytes) && ReadWithoutNotes(bytes, asPdf) is not null))
            {
                OpenDocument document = recording ? OpenDocument.CreateRecording(id, fileName, data, fields, now)
                    : IsPdf(fileName, bytes) ? OpenDocument.Create(id, fileName, bytes, words, now, fields.Int("photos"))
                    : OpenDocument.Open(ReadWithoutNotes(bytes, asPdf)!);
                _documents[number] = document;
                _next++;
                return Answer.Write(json =>
                {
                    json.WriteNumber("piece", number);
                    document.Summary(json);
                });
            }

            if (IsProjectFile(fileName, bytes) && IsAway(bytes))
            {
                if (!fields.Has("pdf"))
                {
                    throw new FacadeException("originalAway", "This piece's PDF is not in its file: link the file again where it is (Привязать оригинал).");
                }

                // Version 0.5, «Импорт папки»: the PDF read from where it is, matched by its fingerprint; never written into the file.
                OpenDocument away = OpenDocument.Open(AwayOpened(bytes, fields.Required("pdf")));
                _documents[number] = away;
                _next++;
                return Answer.Write(json =>
                {
                    json.WriteNumber("piece", number);
                    away.Summary(json);
                });
            }

            OpenPiece piece = IsProjectFile(fileName, bytes)
                ? OpenPiece.Open(bytes, words, OpenPiece.LayerOf(fields.Text("edits")), asTheFileSays: fields.Text("edits") == "piece")
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
                    "project.metadata" => ProjectMetadata(fields),
                    "project.drafts" => DraftRequest.Of(ProjectFile.Read(Encoding.UTF8.GetBytes(fields.Required("file"))), Words.For(fields.Text("language"))),
                    "project.online" => SyncRequest.Online(fields),
                    "project.sync" => SyncRequest.Next(fields),
                    "project.pulled" => SyncRequest.Pulled(fields),
                    "project.resolve" => SyncRequest.Resolve(fields),
                    "project.same" => SyncRequest.Same(fields),
                    "project.join" => SyncRequest.Join(fields),
                    "recording.parts" => RecordingPartsOf(fields),
                    _ when name.StartsWith("reference.", StringComparison.Ordinal) => ReferenceRequest.Call(name, fields),
                    "scan.join" => ScanRequest.Join(fields),
                    "scan.vector" => ScanRequest.Vector(fields),
                    "assistant.tools" => AssistantRequest.Tools(),
                    "parts.split" => PartsRequest.Split(fields),
                    "library.plan" => LibraryRequest.Plan(fields),
                    "library.confirm" => LibraryRequest.Confirm(fields),
                    "library.relink" => LibraryRequest.Relink(fields),
                    "library.linked" => LibraryRequest.Linked(fields),
                    "library.link" => LibraryRequest.Link(fields),
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

            if (name.StartsWith("drafts.", StringComparison.Ordinal))
            {
                return CallOnDrafts(piece, open, name, fields);
            }

            if (open.ReadOnly && !IsShowingCall(name))
            {
                throw new FacadeException("readOnly", "These notes are open only to be shown; make them the working notes to work on them.");
            }

            return name switch
            {
                "sources.makeWorking" => MakeWorking(piece, open, fields),
                "sources.notes.open" => OpenNotes(piece, open, fields),
                _ => open.Call(name, fields),
            };
        }
        catch (Exception exception)
        {
            return Failure(exception);
        }
    }

    /// <summary>
    /// The calls that only show a piece, answered on Notes or a draft opened to be shown; "select" too (v1.1), which keeps
    /// the passage on that number alone and saves nothing, so a fragment is taken there into a draft and a place is played.
    /// </summary>
    private static bool IsShowingCall(string name) =>
        name is "piece" or "phrases" or "select" or "notation" or "playback" or "roll" or "dna" or "marks" or "check" or "language";

    /// <summary>
    /// «Открыть» on Notes that are not the working ones (version 0.6): "id", other Notes or a version, opened under a new
    /// number only to be shown (<see cref="NotesOperations.ViewOf"/>), so the showing calls (select among them) answer for
    /// them and every other call answers "readOnly". The piece itself does not change; close
    /// the new number when done. Answers as <c>open</c> does, with "readOnly": true.
    /// </summary>
    private string OpenNotes(int number, OpenPiece open, Request fields)
    {
        string id = fields.Required("id");
        return OpenShown(open, () => open.Project.ViewOf(id), new ShownView(number, id, Draft: false));
    }

    /// <summary>
    /// What <paramref name="shown"/> gives, open under a new number only to be shown, as <paramref name="view"/> says, in the
    /// language and layer of <paramref name="from"/>. Answers as <c>open</c> does, with "readOnly": true and what
    /// <paramref name="more"/> writes.
    /// </summary>
    private string OpenShown(OpenPiece from, Func<Project> shown, ShownView view, Action<Utf8JsonWriter>? more = null)
    {
        Project project;
        try
        {
            project = shown();
        }
        catch (KeyNotFoundException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw FacadeException.Unavailable(exception.Message);
        }

        OpenPiece opened = OpenPiece.Open(ProjectFile.Write(project), from.Words, from.Layer).AsReadOnly(view);
        int number = _next++;
        _pieces[number] = opened;
        return Answer.Write(json =>
        {
            json.WriteNumber("piece", number);
            json.WriteBoolean("readOnly", true);
            more?.Invoke(json);
            opened.Summary(json);
        });
    }

    /// <summary>
    /// «Сделать рабочими» (version 0.6; 0030, section 4 as amended): other Notes, "id", become the working notes and the
    /// working notes until now stay beside them, so the piece is opened again on the new notes under the same number. Answers
    /// as <c>open</c> does, with "project" the file to save; making the notes until now working again undoes it.
    /// </summary>
    private string MakeWorking(int number, OpenPiece open, Request fields)
    {
        Project changed;
        try
        {
            changed = open.Project.MakeWorking(fields.Required("id"));
        }
        catch (KeyNotFoundException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw FacadeException.Unavailable(exception.Message);
        }

        OpenPiece reopened = OpenPiece.Open(ProjectFile.Write(changed), open.Words, open.Layer);
        _pieces[number] = reopened;
        return Answer.Write(json =>
        {
            json.WriteNumber("piece", number);
            reopened.Summary(json);
        });
    }

    /// <summary>
    /// The parts a recording of "milliseconds" is heard in, all of it however long (until version 0.3, its first 7 minutes),
    /// with the seams at quiet moments when the request sends "levels", how loud the sound is in each 100 ms
    /// (<see cref="RecordingParts.Plan(long, IReadOnlyList{int})"/>):
    /// {"milliseconds": heard, "parts": [{index, sendFrom, sendTo, keepFrom, keepTo}]} in milliseconds, so an app sends each
    /// part's sound to the scan server and gives the notes back to `performance.add` as "parts", with the same "levels"
    /// (0014, 0022).
    /// </summary>
    private static string RecordingPartsOf(Request request)
    {
        long milliseconds = request.Long("milliseconds") is > 0 and var length ? length : throw FacadeException.BadRequest("Send \"milliseconds\", how long the recording is.");
        IReadOnlyList<RecordingPart> parts = OpenPiece.PlanOf(request, milliseconds);
        return Answer.Write(json =>
        {
            json.WriteNumber("milliseconds", milliseconds);
            json.WriteStartArray("parts");
            foreach (RecordingPart part in parts)
            {
                json.WriteStartObject();
                json.WriteNumber("index", part.Index);
                json.WriteNumber("sendFrom", part.SendFromMilliseconds);
                json.WriteNumber("sendTo", part.SendToMilliseconds);
                json.WriteNumber("keepFrom", part.KeepFromMilliseconds);
                json.WriteNumber("keepTo", part.KeepToMilliseconds);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        });
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

    /// <summary>
    /// The title, composer, tags and playlists set in a project file without opening the piece (contract 12), for the
    /// library's properties: the request's "file" is the project file's text, and "title", "composer", "tags" and "playlists"
    /// as on an open piece. Answers
    /// {"project": {…}} as project.summary does and "file", the project file to save in place of the one sent.
    /// </summary>
    private static string ProjectMetadata(Request fields)
    {
        Project project = MetadataRequest.Apply(ProjectFile.Read(Encoding.UTF8.GetBytes(fields.Required("file"))), fields);
        return Answer.Write(json =>
        {
            Answer.Project(json, "project", project);
            json.WriteString("file", Encoding.UTF8.GetString(ProjectFile.Write(project)));
        });
    }

    /// <summary>
    /// A new project's id: "p_" and the 32 lowercase hex digits of a new GUID, so projects made in different runs or on
    /// different devices never share one (V8). Ids numbered by a counter in each run ("p_0001") gave two different iPad
    /// pieces the same id; files that carry such ids still open as they are.
    /// </summary>
    public static string NewProjectId() => string.Create(CultureInfo.InvariantCulture, $"p_{Guid.NewGuid():N}");

    /// <summary>
    /// The project to save for a project file the player imports, given the project already saved under the file's id
    /// (V8). The same project, the same id and the same project-level "createdAt", is the player's own export coming back
    /// and replaces the saved one (AC-S1-20). A different piece that carries the same id, or a saved project that cannot
    /// be read to tell, is never overwritten: the file is saved beside it as a new project with a new id, and nothing else
    /// in it changes.
    /// </summary>
    /// <param name="file">The project read from the imported file.</param>
    /// <param name="saved">The project saved under <paramref name="file"/>'s id; null when there is none.</param>
    /// <param name="savedUnreadable">True when a project is saved under that id but is damaged and cannot be read.</param>
    public static Project Imported(Project file, Project? saved, bool savedUnreadable = false)
    {
        ArgumentNullException.ThrowIfNull(file);
        bool sameProject = saved is null
            ? !savedUnreadable
            : saved.Id == file.Id && saved.CreatedAt == file.CreatedAt;
        return sameProject ? file : file with { Id = NewProjectId() };
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
        name is "piece" or "phrases" or "select" or "understand" or "circle" or "try" or "notation" or "playback" or "roll" or "dna" or "marks" or "bookmarks" or "resume" or "ideas" or "check" or "performances" or "performance" or "corrections" or "sources"
        || (name.Contains('.') && !name.StartsWith("settings.", StringComparison.Ordinal));

    /// <summary>A PDF the player added to read as it is (0023): by its first bytes, or by its name with bytes that are not a project file.</summary>
    private static bool IsPdf(string fileName, byte[] data) =>
        PdfDocument.IsPdf(data) || (fileName.EndsWith(PdfDocument.Extension, StringComparison.OrdinalIgnoreCase) && !IsProjectFile(fileName, data));

    /// <summary>
    /// The project in a project file when it opens without its notes: a PDF piece (a document and no notes, or with notes
    /// and <paramref name="asPdf"/>), or (version 17) a piece that is only its recording; null otherwise.
    /// </summary>
    private static Project? ReadWithoutNotes(byte[] projectFile, bool asPdf)
    {
        try
        {
            return ProjectFile.Read(projectFile) is var project && ((project.Document is not null && (asPdf || !project.HasNotes)) || project.IsOnlyRecording) ? project : null;
        }
        catch (ProjectFileException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a project file is a piece with no notes and no PDF in it (version 0.5): its PDF is in the player's folder or
    /// on another device, so it is listed (project.summary) and opens only once the file is linked again.
    /// </summary>
    private static bool IsAway(byte[] projectFile)
    {
        try
        {
            return ProjectFile.Read(projectFile) is { HasNotes: false, Document: null, DescribesDocument: true };
        }
        catch (ProjectFileException)
        {
            return false;
        }
    }

    /// <summary>
    /// A PDF piece whose PDF is away, with the PDF the app read from the player's folder or a file the player picked
    /// (base64): the same file by its fingerprint, or "otherFile". A linked PDF stays out of the file when it is saved; a
    /// PDF kept in the file on another device is kept here again, as «Привязать оригинал» keeps it.
    /// </summary>
    private static Project AwayOpened(byte[] projectFile, string pdf)
    {
        byte[] data;
        try
        {
            data = Convert.FromBase64String(pdf);
        }
        catch (FormatException)
        {
            throw FacadeException.BadRequest("\"pdf\" must be base64.");
        }

        Project project = ProjectFile.Read(projectFile);
        Project? opened = project.LinksDocument ? project.WithLinkedDocument(data)
            : project.FindOriginal(PieceOriginal.DocumentId) is { Kind: OriginalKind.Pdf or OriginalKind.Images } original && PdfDocument.IsPdf(data) && original.Matches(PieceOriginal.Fingerprint(data)) ? project with { Document = data }
            : null;
        return opened ?? throw new FacadeException("otherFile", "This is another file, not the PDF of this piece: its fingerprint differs.");
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