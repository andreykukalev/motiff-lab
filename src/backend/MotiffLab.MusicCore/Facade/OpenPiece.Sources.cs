using System.Text.Json;

using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The JSON adapter of the sources session (<see cref="SourcesSession"/>, audit W1.7): the piece's sources that need its
/// notes (testing item 16; contract 6; the layers of 0030, version 0.6), a PDF kept inside the piece and the notes read from
/// it added as another version, «Привязать оригинал», the iPad's own mark and «Распознать ещё раз». Each call's request read
/// into typed values in the order it always was, the time read here («now», else the clock), the session asked, the project
/// it makes kept, and the sources written as the answer. The session's state is changed only by the session; this file
/// holds no state. The sources' calls that need no notes (<see cref="SourceRequest.Calls"/>) and the drafts' reach this
/// piece's project through <see cref="HeldSources"/> and <see cref="Changed"/>. «Сделать рабочими» changes the notes the
/// piece is open on, so the facade opens it again (<c>sources.makeWorking</c>, <see cref="MusicCoreFacade"/>).
/// </summary>
internal sealed partial class OpenPiece
{
    internal static partial class Calls
    {
        /// <summary>The sources that need the notes (those that need none: <see cref="SourceRequest.Calls"/>).</summary>
        public static readonly FacadeCall<OpenPiece>[] Sources =
        [
            new("sources.document", static (piece, request) => piece.KeepDocument(request)),
            new("sources.documentNotes", static (piece, request) => piece.AddDocumentNotes(request)),
            new("sources.pdf", static (piece, request) => KeptFileAnswer(request.Text("id") is { } id && id != PieceOriginal.DocumentId ? SourcesSession.Original(piece.State, id) : SourcesSession.Document(piece.State))),
            new("sources.relink", static (piece, request) => piece.RelinkDocument(request)),
            new("sources.held", static (piece, request) => piece.MarkHeld(request)),
            new("sources.notes.recognise", static (piece, request) => piece.RecogniseAgain(request)),
        ];
    }

    /// <summary>The piece's sources, the drafts as they were before each change of their structure (<see cref="SourcesSession"/>).</summary>
    internal SourcesSession Sources { get; } = new();

    /// <summary>This piece's project as the sources' calls that need no notes change it (<see cref="SourceRequest.Calls"/>), a PDF's pages shared over the bars.</summary>
    internal SourceOwner HeldSources => new(() => Project, project => Project = project, pages => PageMap.Even(Piece.Meter, Piece.End, pages), Words);

    /// <summary>The piece changed by one of the Sources sheet's operations, a draft's among them, answered as the sources with what <paramref name="more"/> adds.</summary>
    internal string Changed(Request request, Func<Project, Project> change, Action<Utf8JsonWriter> more) =>
        SourceRequest.Changed(request, () => Project, project => Project = project, Words, change, more);

    /// <summary>The sources answer (<see cref="SourceRequest.Sources"/>) of this piece; <paramref name="more"/> writes what a call adds first.</summary>
    private string SourcesAnswer(Action<Utf8JsonWriter>? more = null) => SourceRequest.Sources(Project, Words, more);

    /// <summary>A PDF or photos kept in the file: "fileName" and "pdf" (base64, the bytes as added).</summary>
    private static string KeptFileAnswer(KeptFile file) => Answer.Write(json =>
    {
        json.WriteString("fileName", file.FileName);
        json.WriteBase64String("pdf", file.Bytes);
    });

    /// <summary>"pdf" (base64) and "pages" (how many it has, else counted from the PDF): the PDF kept inside this piece (<see cref="SourcesSession.WithDocument"/>).</summary>
    private string KeepDocument(Request request)
    {
        byte[] pdf = SourceRequest.Base64(request.Required("pdf"), "pdf");
        SourcesSession.RefuseTooLarge(pdf, Words);
        Project = SourcesSession.WithDocument(State, pdf, request.Int("pages"));
        return SourcesAnswer();
    }

    /// <summary>
    /// "musicXml" (base64) the scan server read from the PDF kept in this piece, "engine", "pages" and "name": the notes
    /// added as another version from the score (0022), and the PDF's pages linked to these bars by the bar numbers the scan
    /// began each page with (<see cref="SourcesSession.LinkedPages"/>) once the version is added, so a refused call leaves
    /// them as they were. Answers the comparison, as performance.add does.
    /// </summary>
    private string AddDocumentNotes(Request request)
    {
        SourcesSession.RefuseWithoutDocument(State);
        DocumentVersion read = SourcesSession.ReadDocument(State, SourceRequest.Base64(request.Required("musicXml"), "musicXml"));
        PageMap linked = SourcesSession.LinkedPages(State, read.Read, request.Int("pages"));
        string name = SourcesSession.VersionName(State, request.Text("name"));
        string answer = AddVersion(name, read.Midi, PerformanceSource.Score, 0, request.Text("engine"));
        Project = Project with { DocumentPages = linked };
        return answer;
    }

    /// <summary>
    /// «Привязать оригинал» on the iPad, as a copy (version 0.3): "pdf" (base64), "pages" (else counted from the PDF) and
    /// "now" (<see cref="SourcesSession.Relinked"/>), the PDF then marked held by the iPad. Answers the sources with
    /// "linked": false, the piece unchanged, when the file is another one.
    /// </summary>
    private string RelinkDocument(Request request)
    {
        byte[] pdf = SourceRequest.Base64(request.Required("pdf"), "pdf");
        SourcesSession.RefuseRelink(State);
        if (SourcesSession.Relinked(State, pdf, request.Int("pages")) is not { } linked)
        {
            return SourcesAnswer(json => json.WriteBoolean("linked", false));
        }

        Project = SourcesSession.HeldOnIpad(linked, PieceOriginal.DocumentId, held: true, Now(request));
        return SourcesAnswer(json => json.WriteBoolean("linked", true));
    }

    /// <summary>The iPad's own mark on an original (version 0.3, <see cref="SourcesSession.HeldOnIpad"/>): "id", "held" and "now". Answers the sources.</summary>
    private string MarkHeld(Request request)
    {
        string id = request.Required("id");
        bool held = request.Bool("held") ?? throw FacadeException.BadRequest("Send \"held\", true or false.");
        Project = SourcesSession.HeldOnIpad(Project, id, held, Now(request));
        return SourcesAnswer();
    }

    /// <summary>
    /// «Распознать ещё раз»: "original" (the PDF's id), "musicXml" (base64) the scan server read from it, "engine",
    /// "pages", "reports" (the scan server's page reports) and "now": other Notes beside the working ones, which keep their
    /// corrections (<see cref="SourcesSession.RecognisedAgain"/>). Answers the sources with their "id".
    /// </summary>
    private string RecogniseAgain(Request request)
    {
        ReadScore read = SourcesSession.Read("notes.musicxml", SourceRequest.Base64(request.Required("musicXml"), "musicXml"), Words);
        string id = Project.NextNotesId();
        PageMap pages = SourcesSession.ReadPages(read, request.Int("pages"));
        List<PageReport>? reports = ScanRequest.ReportsIn(request);
        string original = request.Required("original");
        string engine = request.Required("engine");
        DateTimeOffset now = Now(request);
        return Changed(request, project => SourcesSession.RecognisedAgain(project, original, read, engine, pages, now, reports), json => json.WriteString("id", id));
    }
}