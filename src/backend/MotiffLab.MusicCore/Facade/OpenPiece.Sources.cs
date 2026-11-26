using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The piece's sources (testing item 16; contract 6): one piece, many sources, and the player's own version of the notes.
/// What the base is and what the player's version holds beyond it, a PDF kept inside the piece, and the notes read from
/// that PDF added as another version, lined up with the base.
/// </summary>
internal sealed partial class OpenPiece
{
    /// <summary>This piece's project as the sources' calls that need no notes change it (<see cref="SourceRequest.Calls"/>), a PDF's pages shared over the bars.</summary>
    internal SourceOwner HeldSources => new(() => Project, project => Project = project, pages => PageMap.Even(Piece.Meter, Piece.End, pages), Words);

    /// <summary>The sources answer (<see cref="SourceRequest.Sources"/>) of this piece; <paramref name="more"/> writes what a call adds first.</summary>
    private string SourcesAnswer(Action<Utf8JsonWriter>? more = null) => SourceRequest.Sources(Project, Words, more);

    /// <summary>The PDF kept in the piece, to send to the scan server: "fileName" and "pdf" (base64, the bytes as added).</summary>
    private string DocumentBytes()
    {
        byte[] pdf = Project.Document ?? throw FacadeException.Unavailable("This piece keeps no PDF.");
        return Answer.Write(json =>
        {
            json.WriteString("fileName", Project.DocumentFileName);
            json.WriteBase64String("pdf", pdf);
        });
    }

    /// <summary>Another PDF or photos kept in the file (version 0.6, "id"): "fileName" and "pdf" (base64, the bytes as added).</summary>
    private string OriginalBytes(string id)
    {
        if (Project.FindOriginal(id) is not { } original || !Project.Sources.Data.TryGetValue(id, out byte[]? bytes))
        {
            throw FacadeException.Unavailable($"The piece keeps no bytes of {id} in its file.");
        }

        return Answer.Write(json =>
        {
            json.WriteString("fileName", original.FileName);
            json.WriteBase64String("pdf", bytes);
        });
    }

    /// <summary>
    /// "pdf" (base64) and "pages" (how many it has, else counted from the PDF): the PDF kept inside this piece, its pages linked to the bars shared
    /// evenly until its notes are read (testing item 16). It stays on the device, as every PDF does.
    /// </summary>
    private string KeepDocument(Request request)
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

        if (PdfDocument.CheckSize(pdf.Length) is { } rejection)
        {
            throw new FacadeException("import", $"{rejection.Text(Words)} {rejection.TipIn(Words)}".Trim());
        }

        int pages = Math.Max(1, request.Int("pages") ?? PdfDocument.PageCount(pdf));
        try
        {
            Project = Project.WithDocument(pdf, PageMap.Even(Piece.Meter, Piece.End, pages));
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        return SourcesAnswer();
    }

    /// <summary>
    /// "musicXml" (base64) the scan server read from the PDF kept in this piece, "engine", "pages" and "name": the notes
    /// added as another version from the score (0022), and the PDF's pages linked to these bars by the bar numbers the scan
    /// began each page with. Answers the comparison, as performance.add does. The engine's mistakes are put right first
    /// (<see cref="ScanRepair"/>), as in document.read.
    /// </summary>
    private string AddDocumentNotes(Request request)
    {
        if (Project.Document is null)
        {
            throw FacadeException.Unavailable("Keep the PDF in the piece first (sources.document).");
        }

        byte[] musicXml;
        try
        {
            musicXml = ScanRepair.Repair(Convert.FromBase64String(request.Required("musicXml")));
        }
        catch (FormatException)
        {
            throw FacadeException.BadRequest("\"musicXml\" must be base64.");
        }

        string fileName = Project.DocumentFileName;
        PieceImportResult imported = PieceImporter.Import(Path.ChangeExtension(fileName, ".musicxml"), musicXml);
        if (imported.Piece is not { } read)
        {
            throw new FacadeException("import", imported.Rejection?.Text(Words) ?? Words.T("No notes could be read from these pages.", "На этих страницах не удалось прочитать нот."));
        }

        VersionFileResult version = VersionFile.Read(Path.ChangeExtension(fileName, ".musicxml"), musicXml);
        if (version.Midi is not { } midi)
        {
            throw FacadeException.BadRequest(version.Rejection is { } why ? $"{why.Message} {why.Tip}".Trim() : "The notes read have no notes.");
        }

        int pages = Math.Max(1, request.Int("pages") ?? Project.DocumentPages?.Pages ?? 1);
        Project = Project with { DocumentPages = PageMap.Of(read, pages).Through(read.Meter, Piece.Meter, Piece.End) };
        string name = request.Text("name") is { Length: > 0 } given ? given : Path.GetFileNameWithoutExtension(fileName);
        return AddVersion(name, midi, PerformanceSource.Score, 0, request.Text("engine"));
    }

    /// <summary>
    /// «Привязать оригинал» on the iPad, as a copy (version 0.3): "pdf" (base64), "pages" (else counted from the PDF) and
    /// "now". A PDF with the fingerprint of the piece's PDF original, whose bytes are on another device, is kept in the
    /// piece again, with no new original and no copy of the piece, its pages shared over the bars, and marked held by the
    /// iPad. Answers the sources with "linked": false, the piece unchanged, when the file is another one.
    /// </summary>
    private string RelinkDocument(Request request)
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

        if (Project.Document is not null || Project.FindOriginal(PieceOriginal.DocumentId) is not { Kind: OriginalKind.Pdf })
        {
            throw FacadeException.Unavailable("Only a piece whose PDF is on another device links it again.");
        }

        int pages = Math.Max(1, request.Int("pages") ?? PdfDocument.PageCount(pdf));
        if (Project.RelinkDocument(pdf, PageMap.Even(Piece.Meter, Piece.End, pages)) is not { } linked)
        {
            return SourcesAnswer(json => json.WriteBoolean("linked", false));
        }

        Project = linked.MarkHeld(PieceOriginal.DocumentId, HoldingDevice.Ipad, held: true, Now(request));
        return SourcesAnswer(json => json.WriteBoolean("linked", true));
    }

    /// <summary>
    /// The iPad's own mark on an original (version 0.3): "id", "held" (true when the iPad has the bytes, as a link to the
    /// file in Files or a copy; false when it no longer does) and "now". Only the iPad's mark changes, and the piece stays
    /// the same when the mark already is so. Answers the sources.
    /// </summary>
    private string MarkHeld(Request request)
    {
        string id = request.Required("id");
        bool held = request.Bool("held") ?? throw FacadeException.BadRequest("Send \"held\", true or false.");
        try
        {
            Project = Project.MarkHeld(id, HoldingDevice.Ipad, held, Now(request));
        }
        catch (KeyNotFoundException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        return SourcesAnswer();
    }
}