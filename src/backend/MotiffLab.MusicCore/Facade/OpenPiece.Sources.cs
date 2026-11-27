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
    internal SourceOwner HeldSources => new(() => Project, KeepSources, pages => PageMap.Even(Piece.Meter, Piece.End, pages), Words);

    /// <summary>
    /// The <paramref name="project"/> one of the sources' calls that need no notes makes, kept; when it took away a version
    /// with bars taken from it (sources.notes.remove, sources.original.remove with its notes), the notes read again without
    /// them, and the selection kept over them, as when the bars are restored.
    /// </summary>
    private void KeepSources(Project project)
    {
        bool takenChanged = !Project.TakenBars.SequenceEqual(project.TakenBars);
        Project = project;
        if (takenChanged)
        {
            RefreshNotesFollowingSelection();
        }
    }

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
    /// began each page with (<see cref="SourcesSession.WithPagesLinked"/>). The version is added over the pages linked, and both
    /// are kept together or not at all: a call refused, or failing while the version is added, leaves the piece as it was.
    /// Answers the comparison, as performance.add does.
    /// </summary>
    private string AddDocumentNotes(Request request)
    {
        SourcesSession.RefuseWithoutDocument(State);
        DocumentVersion read = SourcesSession.ReadDocument(State, SourceRequest.Base64(request.Required("musicXml"), "musicXml"));
        Project linked = SourcesSession.WithPagesLinked(State, read.Read, request.Int("pages"));
        string name = SourcesSession.VersionName(State, request.Text("name"));
        string? engine = request.Text("engine");
        return AddVersion(State with { Project = linked }, name, read.Midi, PerformanceSource.Score, 0, engine);
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

        Project = SourcesSession.HeldOnIpad(linked, PieceOriginal.DocumentId, held: true, request.Now());
        return SourcesAnswer(json => json.WriteBoolean("linked", true));
    }

    /// <summary>The iPad's own mark on an original (version 0.3, <see cref="SourcesSession.HeldOnIpad"/>): "id", "held" and "now". Answers the sources.</summary>
    private string MarkHeld(Request request)
    {
        string id = request.Required("id");
        bool held = request.Bool("held") ?? throw FacadeException.BadRequest("Send \"held\", true or false.");
        Project = SourcesSession.HeldOnIpad(Project, id, held, request.Now());
        return SourcesAnswer();
    }

    /// <summary>
    /// «Распознать ещё раз»: "original" (the PDF's id), "musicXml" (base64) the scan server read from it, "engine",
    /// "pages", "reports" (the scan server's page reports) and "now": other Notes beside the working ones, which keep their
    /// corrections (<see cref="NotesOperations.RecognisedAgain"/>), carried over to the new Notes where they still apply
    /// (<see cref="SourcesSession.CarriedOver"/>). Answers the sources with their "id" and, when corrections or confirmed
    /// bars were carried, "carried" (<see cref="CarryAnswer"/>); the working notes' marks «что не так» put into the reading first and
    /// carried to the new Notes (<see cref="SourcesSession.Marked"/>), answered as "marked" (<see cref="MarksAnswer"/>).
    /// </summary>
    private string RecogniseAgain(Request request)
    {
        string original = request.Required("original");
        (ReadScore read, SourcesSession.MarksCarried? marks) = SourcesSession.Marked(
            State, original, "notes.musicxml", SourcesSession.Read("notes.musicxml", SourceRequest.Base64(request.Required("musicXml"), "musicXml"), Words));
        string id = Project.NextNotesId();
        PageMap pages = SourcesSession.ReadPages(read, request.Int("pages"));
        List<PageReport>? reports = ScanRequest.ReportsIn(request);
        string engine = request.Required("engine");
        DateTimeOffset now = request.Now();
        ReadingCarry? carry = null;
        return Changed(
            request,
            project =>
            {
                (Project carried, carry) = SourcesSession.CarriedOver(State, project.RecognisedAgain(original, read.MusicXml, engine, pages, now, reports), id, original, read);
                return SourcesSession.WithMarks(carried, id, marks, read);
            },
            json =>
            {
                json.WriteString("id", id);
                CarryAnswer(json, carry, Words);
                MarksAnswer(json, marks, Words);
            });
    }

    /// <summary>
    /// "marked" (within contract 16, «Как напечатано», step 3): what became of the working notes' marks «что не так» on the
    /// notes read again, "used" and "unfit" (how many), "bars" (where those not used are, each flagged MarkUnfit) and "text"
    /// (the toast's line, null when it says nothing); nothing when the working notes had no marks.
    /// </summary>
    private static void MarksAnswer(Utf8JsonWriter json, SourcesSession.MarksCarried? marks, Words words)
    {
        if (marks is null)
        {
            return;
        }

        json.WriteStartObject("marked");
        json.WriteNumber("used", marks.Applied.Count);
        json.WriteNumber("unfit", marks.Unfit.Count);
        JsonWrite.Numbers(json, "bars", [.. marks.Unfit.Select(static mark => mark.Bar).Distinct().Order()]);
        json.WriteString("text", marks.Text(words));
        json.WriteEndObject();
    }

    /// <summary>
    /// "carried": what became of the working notes' corrections on the notes read again, "carried", "byReading" and
    /// "notCarried" (how many), "bars" (those with a correction not carried), "confirmed" (the bars confirmed again) and
    /// "text" (the toast's line, null when it says nothing); nothing without a carry.
    /// </summary>
    private static void CarryAnswer(Utf8JsonWriter json, ReadingCarry? carry, Words words)
    {
        if (carry is null)
        {
            return;
        }

        json.WriteStartObject("carried");
        json.WriteNumber("carried", carry.Count(CarryOutcome.Carried));
        json.WriteNumber("byReading", carry.Count(CarryOutcome.ByReading));
        json.WriteNumber("notCarried", carry.Count(CarryOutcome.NotCarried));
        JsonWrite.Numbers(json, "bars", carry.NotCarriedBars);
        JsonWrite.Numbers(json, "confirmed", carry.ConfirmedBars);
        if (carry.Text(words) is { } text)
        {
            json.WriteString("text", text);
        }
        else
        {
            json.WriteNull("text");
        }
        json.WriteEndObject();
    }
}