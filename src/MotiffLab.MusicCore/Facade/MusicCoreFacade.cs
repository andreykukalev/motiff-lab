using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
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
public sealed class MusicCoreFacade
{
    /// <summary>
    /// 12 lets the library keep a piece's title, composer and tags (version 0.2), with the project file still version 15 and
    /// an optional "metadata": the project summary (project.summary, open, project) answers "title" and "titleFrom"
    /// ("player", "file" or "fileName"), "composer" and "composerFrom" ("player" or "file", or null) and "tags";
    /// project.metadata sets them on an open piece or PDF piece, and on piece 0 in a project file sent as "file", answering
    /// that file changed. Within 12, the summary gained "playlists", the playlists the piece is on by name, and
    /// project.metadata takes them as it takes "tags", a field added. Also within 12, four calls on piece 0 sync projects
    /// (version 0.2, <see cref="ProjectSync"/>), each with project files as text: project.online (the copy saved online),
    /// project.sync (the next step), project.pulled (an online copy kept with this device's PDF) and project.resolve (two
    /// changed copies: the last saved kept, the other beside as a previous copy), calls added. Also within 12 (version 0.3,
    /// long recordings), recording.parts plans the whole recording rather than its first 7 minutes, and it, performance.add
    /// with "parts" and open with "transcription" take "levels", the sound's loudness that puts the seams at quiet moments,
    /// a field added.
    /// Also within 12, reference.key and reference.sound on piece 0 answer the circle of fifths with any key's card and its
    /// scales and chords to hear (the Reference experiment, <see cref="Analysis.KeyReference"/>), calls added. Also within 12
    /// (the Reference, part 2), reference.chord and reference.scale on piece 0 answer a chord's and a scale's own card,
    /// reference.sound sounds them, and reference.key on an open piece also answers "pieceKey", calls and fields added.
    /// Also within 12, for the iPad's practice experiments (#327, #328, #349): select takes "snap": "beat" and a selection
    /// answers "firstBeat" and "lastBeat"; playback takes "hands" ("both", "right", "left") and answers "hands" (which hands
    /// the notes carry, and whether any was inferred); resume.save takes "passage": null for no passage, fields added.
    /// Also within 12 (version 0.4), the calls write the fields the project file took within 15: parts.split's parts keep
    /// "partOf", which the project summary answers; document.read takes "reports" as scan.join does and keeps them, raising
    /// PageQuality, and check answers "pages"; ink.add and document.ink.add keep a stroke's "margin", which marks and
    /// document.marks answer, fields added.
    /// 11 lets doubt about recognised notes reach what is read from them (#352, 0027 section 12), adding fields only: a
    /// chord, a fact and a DNA chord or key built on recognised notes still to check (in a bar with an open Check flag)
    /// tell "doubt", a sentence that says where, and the chord's "certainty" and the DNA chord's "clear" say uncertain;
    /// a fact turns "uncertain" and a key read from those notes too; check answers "lengths", the bars of a recording
    /// whose notes' lengths are uncertain. The project file stays version 15.
    /// 10 removes the calls no app sends (#342): the assembly calls of 9 (the web assembles through music-core directly,
    /// 0024 and 0025), variation.start, .propose, .check, .accept, .reject, .undo and .redo (the iPad app runs a variation
    /// through try and variation.clear), and experiments, experiments.start and experiments.surprise. They answer
    /// unknownCall. The project file stays version 15 and keeps an assembly journal as it was read.
    /// 4 brings performances (0022) and project file version 13: performances, performance (the comparison bar by bar with
    /// its markers), performance.add, .rename, .dismiss, .take, .restore and .remove; a Check flag's source can be performance.
    /// 5 moves to project file version 14 (testing item 15): a correction can add a note, the note editor on the roll
    /// adds and sets notes (corrections.add, corrections.set) on a draft that the project keeps only once saved
    /// (corrections.begin, .save, .discard, .reset).
    /// 6 keeps a piece's sources in the piece (testing item 16): document.read fills the PDF piece itself with the notes read
    /// from it, a piece with notes and a PDF opens in its "PDF" view with "view": "pdf", sources tells the base and what the
    /// player's version holds, sources.document keeps a PDF inside a piece with notes, sources.documentNotes adds the notes
    /// read from it as a version, and performance.takeAll takes every bar the comparison is sure of.
    /// 8 explains Piece DNA (testing item 30): dna answers a "summary" line, every harmony bar its "degree" and
    /// "function", every cadence its "explanation", and "circles", the circle of fifths of each section with the key it
    /// comes from; a circle's chords tell their "function" and "role", and a circle its twelve "zones".
    /// 9 assembles the player's own version from the piece's sources (0024): assembly, assembly.source, .notation,
    /// .playback, .pick, .sheet, .carry, .transpose, .accept, .undo, .redo, .restart, .revert, .journal, .apply, .unapply
    /// and .recording, over project file version 15.
    /// 7 merges a version through a draft (testing items 32 and 34): performance.merge.open, .choose, .own, .undecide,
    /// .undo, .redo, .reset, .cancel and .apply, the three views engraved and heard (performance.merge.notation, .playback),
    /// performance.merge.undoApplied, and performance.putBack for a version removed while the piece is open; a performance
    /// tells "mergedAt" and "mergeKept". The project file stays version 14 with an optional "merge" on a performance.
    /// 3 brings the three phrase levels (0018, section 3; preparation items 7–9): the phrases answer names the level shown and
    /// the units of a level with their parent, "long" replaces "fitsSelection", the levels are short, phrase and sentence,
    /// phrases.level and phrases.edit take a level and split, understand ranks the facts and offers Try chips, and try sounds a chip.
    /// 2 moves to project file version 12 (0018): the project answer counts resume points, ideas, marks, bookmarks and
    /// open Check flags in place of sessions and observations, and the calls for marks, bookmarks, resume points, ideas,
    /// settings and Check join.
    /// 1 was the first contract: open and save, piece, phrases with their edits, select, understand,
    /// experiments, the variation chain, notation, playback, the catalogue and the Piece DNA placeholder.
    /// </summary>
    public const int Contract = 12;

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
            if (IsPdf(fileName, bytes) || (IsProjectFile(fileName, bytes) && ReadDocument(bytes, asPdf) is not null))
            {
                OpenDocument document = IsPdf(fileName, bytes) ? OpenDocument.Create(id, fileName, bytes, words, now, fields.Int("photos")) : OpenDocument.Open(ReadDocument(bytes, asPdf)!);
                _documents[number] = document;
                _next++;
                return Answer.Write(json =>
                {
                    json.WriteNumber("piece", number);
                    document.Summary(json);
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
                    "project.online" => SyncRequest.Online(fields),
                    "project.sync" => SyncRequest.Next(fields),
                    "project.pulled" => SyncRequest.Pulled(fields),
                    "project.resolve" => SyncRequest.Resolve(fields),
                    "recording.parts" => RecordingPartsOf(fields),
                    _ when name.StartsWith("reference.", StringComparison.Ordinal) => ReferenceRequest.Call(name, fields),
                    "scan.join" => ScanRequest.Join(fields),
                    "parts.split" => PartsRequest.Split(fields),
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

    /// <summary>The project in a project file when it is a PDF piece (a document and no notes); null for a piece with notes.</summary>
    private static Project? ReadDocument(byte[] projectFile, bool asPdf)
    {
        try
        {
            return ProjectFile.Read(projectFile) is { Document: not null } project && (asPdf || !project.HasNotes) ? project : null;
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