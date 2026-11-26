using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;

namespace MotiffLab.MusicCore.Facade.Sessions;

/// <summary>
/// The piece's sources (audit W1.7, the third typed session; testing item 16, contract 6; the layers of 0030): the PDF kept
/// inside the piece and the notes read from it, «Привязать оригинал» and the iPad's own mark on an original, «Распознать ещё
/// раз», and the drafts as they were before each change of their structure, which drafts.undo takes back. The sources
/// themselves are the project's: each change here answers the project it makes, and the open piece keeps it. The session
/// owns the rest: what is kept for drafts.undo changes only through a method here that says so. It reads the piece through
/// <see cref="SessionPiece"/>, takes typed values (the time as <c>now</c>), never a request, and returns typed results; it
/// writes no JSON and reads no clock. OpenPiece.Sources.cs is its JSON adapter; the calls that need no notes, the same on a
/// piece without them, are <see cref="SourceRequest.Calls"/>.
/// </summary>
internal sealed class SourcesSession
{
    /// <summary>
    /// The drafts as they were before each change of their structure made while the piece is open (drafts.place, drafts.base),
    /// the latest last: a draft's structure is its state, not a step of its journal, so only these let drafts.undo take
    /// such a change back; they are never in the file.
    /// </summary>
    private readonly List<Draft> _draftsBefore = [];

    /// <summary>Keeps <paramref name="before"/>, the draft as it was before a change of its structure, for drafts.undo.</summary>
    public void KeepDraftBefore(Draft before) => _draftsBefore.Add(before);

    /// <summary>Forgets what was kept of the draft <paramref name="id"/> for drafts.undo: it changed otherwise (added to, renamed, removed).</summary>
    public void ForgetDraftsBefore(string id) => _draftsBefore.RemoveAll(each => each.Id == id);

    /// <summary>
    /// What drafts.undo takes back of <paramref name="now"/>: the draft before its latest change of structure, when no step
    /// of the journal was added after that change (its number at or above the one that change left next), its step numbers
    /// counting on; null when the last change was a step. That kept draft is forgotten.
    /// </summary>
    public Draft? TakeDraftBefore(Draft now)
    {
        int index = _draftsBefore.FindLastIndex(each => each.Id == now.Id);
        if (index < 0 || now.Steps.Any(step => step.Number >= _draftsBefore[index].Next))
        {
            return null;
        }

        Draft before = _draftsBefore[index];
        _draftsBefore.RemoveAt(index);
        return before with { Next = now.Next };
    }

    /// <summary>The PDF kept in the piece, to send to the scan server; refused when it keeps none.</summary>
    public static KeptFile Document(SessionPiece piece) =>
        piece.Project.Document is { } pdf ? new KeptFile(piece.Project.DocumentFileName, pdf) : throw FacadeException.Unavailable("This piece keeps no PDF.");

    /// <summary>Another PDF or photos kept in the file (version 0.6), the original <paramref name="id"/>; refused when the file keeps no bytes of it.</summary>
    public static KeptFile Original(SessionPiece piece, string id) =>
        piece.Project.FindOriginal(id) is { } original && piece.Project.Sources.Data.TryGetValue(id, out byte[]? bytes)
            ? new KeptFile(original.FileName, bytes)
            : throw FacadeException.Unavailable($"The piece keeps no bytes of {id} in its file.");

    /// <summary>Refuses a PDF too large to keep, as an import is refused.</summary>
    public static void RefuseTooLarge(byte[] pdf, Words words)
    {
        if (PdfDocument.CheckSize(pdf.Length) is { } rejection)
        {
            throw new FacadeException("import", $"{rejection.Text(words)} {rejection.TipIn(words)}".Trim());
        }
    }

    /// <summary>
    /// The project with <paramref name="pdf"/> kept inside the piece, its <paramref name="pages"/> (else counted from the
    /// PDF) linked to the bars shared evenly until its notes are read (testing item 16). It stays on the device, as every PDF does.
    /// </summary>
    public static Project WithDocument(SessionPiece piece, byte[] pdf, int? pages)
    {
        PageMap even = PageMap.Even(piece.Piece.Meter, piece.Piece.End, Math.Max(1, pages ?? PdfDocument.PageCount(pdf)));
        try
        {
            return piece.Project.WithDocument(pdf, even);
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }

    /// <summary>Refuses the notes of a PDF while the piece keeps none (sources.document first).</summary>
    public static void RefuseWithoutDocument(SessionPiece piece)
    {
        if (piece.Project.Document is null)
        {
            throw FacadeException.Unavailable("Keep the PDF in the piece first (sources.document).");
        }
    }

    /// <summary>The notes the scan server read as <paramref name="musicXml"/>, the engine's mistakes put right first (<see cref="ScanRepair"/>); refused as an import when none can be read.</summary>
    public static ReadScore Read(string fileName, byte[] musicXml, Words words)
    {
        byte[] repaired = ScanRepair.Repair(musicXml);
        PieceImportResult imported = PieceImporter.Import(fileName, repaired);
        return imported.Piece is { } read
            ? new ReadScore(repaired, read)
            : throw new FacadeException("import", imported.Rejection?.Text(words) ?? words.T("No notes could be read from these pages.", "На этих страницах не удалось прочитать нот."));
    }

    /// <summary>The notes the scan server read from the PDF kept in the piece (0022), as the notes of another version.</summary>
    public static DocumentVersion ReadDocument(SessionPiece piece, byte[] musicXml)
    {
        string fileName = Path.ChangeExtension(piece.Project.DocumentFileName, ".musicxml");
        ReadScore read = Read(fileName, musicXml, piece.Words);
        VersionFileResult version = VersionFile.Read(fileName, read.MusicXml);
        return version.Midi is { } midi
            ? new DocumentVersion(read.Piece, midi)
            : throw FacadeException.BadRequest(version.Rejection is { } why ? $"{why.Message} {why.Tip}".Trim() : "The notes read have no notes.");
    }

    /// <summary>
    /// The project with the PDF's <paramref name="pages"/> (else as many as it has) linked to the piece's bars by the bar
    /// numbers the scan began each page with, as the notes <paramref name="read"/> from it say.
    /// </summary>
    public static Project WithPagesLinked(SessionPiece piece, ImportedPiece read, int? pages) =>
        piece.Project with { DocumentPages = PageMap.Of(read, Math.Max(1, pages ?? piece.Project.DocumentPages?.Pages ?? 1)).Through(read.Meter, piece.Piece.Meter, piece.Piece.End) };

    /// <summary>The name of the version read from the PDF: <paramref name="name"/> when given, else the PDF's file name.</summary>
    public static string VersionName(SessionPiece piece, string? name) =>
        name is { Length: > 0 } ? name : Path.GetFileNameWithoutExtension(piece.Project.DocumentFileName);

    /// <summary>Refuses «Привязать оригинал» unless the piece's PDF original has its bytes on another device.</summary>
    public static void RefuseRelink(SessionPiece piece)
    {
        if (piece.Project.Document is not null || piece.Project.FindOriginal(PieceOriginal.DocumentId) is not { Kind: OriginalKind.Pdf })
        {
            throw FacadeException.Unavailable("Only a piece whose PDF is on another device links it again.");
        }
    }

    /// <summary>
    /// «Привязать оригинал» as a copy (version 0.3): the project with <paramref name="pdf"/> kept in the piece again, its
    /// <paramref name="pages"/> (else counted from the PDF) shared over the bars, with no new original and no copy of the
    /// piece; null when the file is another one than the piece's PDF original.
    /// </summary>
    public static Project? Relinked(SessionPiece piece, byte[] pdf, int? pages) =>
        piece.Project.RelinkDocument(pdf, PageMap.Even(piece.Piece.Meter, piece.Piece.End, Math.Max(1, pages ?? PdfDocument.PageCount(pdf))));

    /// <summary>
    /// <paramref name="project"/> with the iPad's own mark on the original <paramref name="id"/> (version 0.3): held when the
    /// iPad has the bytes, as a link to the file in Files or a copy, or no longer. Only the iPad's mark changes, and the
    /// project stays the same when the mark already is so.
    /// </summary>
    public static Project HeldOnIpad(Project project, string id, bool held, DateTimeOffset now)
    {
        try
        {
            return project.MarkHeld(id, HoldingDevice.Ipad, held, now);
        }
        catch (KeyNotFoundException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }

    /// <summary>
    /// <paramref name="again"/>, the project with the Notes <paramref name="id"/> read again from the original
    /// <paramref name="original"/> as <paramref name="read"/>, with the working notes' corrections and the bars the player
    /// confirmed carried over to them («Как напечатано», step 2, <see cref="ReadingCarry"/>), the corrections carried in one
    /// group; with no carry when the working notes are no reading of that original, or the player corrected and confirmed
    /// nothing on them. The working notes keep theirs.
    /// </summary>
    public static (Project Project, ReadingCarry? Carry) CarriedOver(SessionPiece piece, Project again, string id, string original, ReadScore read)
    {
        if (piece.Project.Scan is not { } scan || (scan.From ?? PieceOriginal.DocumentId) != original || (piece.Project.Edits.IsEmpty && scan.ConfirmedBars.Count == 0))
        {
            return (again, null);
        }

        ReadingCarry carry = ReadingCarry.Of(piece.Piece, piece.Project.Edits, scan.ConfirmedBars, read.Piece, "g_1");
        OtherNotes[] notes = [.. again.Sources.Notes.Select(notes => notes.Id == id ? notes with { Edits = carry.Edits, Scan = notes.Scan?.Confirm(carry.ConfirmedBars) } : notes)];
        return (again with { Sources = again.Sources with { Notes = notes } }, carry);
    }

    /// <summary>The <paramref name="pages"/> (else one) of the PDF notes were read again from, by the bars the scan began each with.</summary>
    public static PageMap ReadPages(ReadScore read, int? pages) => PageMap.Of(read.Piece, Math.Max(1, pages ?? 1));
}