using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

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

    /// <summary>What became of the working notes' marks on notes read again: all of them on the new reading's bars, those put in, and those that could not be.</summary>
    public sealed record MarksCarried(IReadOnlyList<ReadingMark> Marks, IReadOnlyList<ReadingMark> Applied, IReadOnlyList<ReadingMark> Unfit)
    {
        /// <summary>The toast's line: «учтено отметок: 2 · не учтено, в «Проверить»: 1 (такт 3)»; null when no mark was put in or left out.</summary>
        public string? Text(Words words)
        {
            List<string> parts = [];
            if (Applied.Count > 0)
            {
                string marks = words.Count(Applied.Count, "mark", "marks", "отметка", "отметки", "отметок");
                parts.Add(words.T($"{marks} used", $"учтено: {marks}"));
            }

            int[] bars = [.. Unfit.Select(static mark => mark.Bar).Distinct().Order()];
            if (bars.Length > 0)
            {
                string where = string.Join(", ", bars);
                parts.Add(words.T($"not used, in Check: {Unfit.Count} ({(bars.Length == 1 ? "bar" : "bars")} {where})", $"не учтено, в «Проверить»: {Unfit.Count} ({(bars.Length == 1 ? "такт" : "такты")} {where})"));
            }

            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }
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
    /// <paramref name="original"/> as <paramref name="readScore"/>, with the working notes' corrections and the bars the player
    /// confirmed carried over to them («Как напечатано», step 2, <see cref="ReadingCarry"/>), the corrections carried in one
    /// group, and each bar of theirs where a correction was not carried flagged in Check (<see cref="CheckReason.CorrectionUnfit"/>)
    /// beside the flags of the reading; with no carry when the working notes are no reading of that original, or the player corrected and confirmed
    /// nothing on them. The working notes keep theirs.
    /// </summary>
    public static (Project Project, ReadingCarry? Carry) CarriedOver(SessionPiece piece, Project again, string id, string original, ReadScore readScore)
    {
        if (piece.Project.Scan is not { } scan || (scan.From ?? PieceOriginal.DocumentId) != original || (piece.Project.Edits.IsEmpty && scan.ConfirmedBars.Count == 0))
        {
            return (again, null);
        }

        ReadingCarry carry = ReadingCarry.Of(piece.Piece, piece.Project.Edits, scan.ConfirmedBars, readScore.Piece, "g_1");
        OtherNotes[] notes = [.. again.Sources.Notes.Select(notes => notes.Id == id && notes.Scan is { } read ? Carried(notes, read.Confirm(carry.ConfirmedBars), carry, readScore) : notes)];
        return (again with { Sources = again.Sources with { Notes = notes } }, carry);
    }

    /// <summary>The Notes read again with the carry: its corrections, the bars confirmed, and the reading's Check flags with a <see cref="CheckReason.CorrectionUnfit"/> on each bar a correction was not carried to.</summary>
    private static OtherNotes Carried(OtherNotes notes, ScanOrigin scan, ReadingCarry carry, ReadScore read) =>
        Flagged(notes with { Edits = carry.Edits }, scan, read, carry.NotCarriedBars.Select(static bar => new CheckFlag(bar, CheckSource.Scan, CheckReason.CorrectionUnfit, Resolved: false)));

    /// <summary>The Notes with <paramref name="scan"/> and Check: the reading's flags, its bars confirmed, and <paramref name="more"/>, by bar and reason.</summary>
    private static OtherNotes Flagged(OtherNotes notes, ScanOrigin scan, ReadScore read, IEnumerable<CheckFlag> more) => notes with
    {
        Scan = scan,
        Check = [.. (notes.Check ?? CheckFlag.FromScan(scan.CheckOf(read.Piece), scan.ConfirmedBars)).Concat(more).OrderBy(static flag => flag.Bar).ThenBy(static flag => flag.Reason)],
    };

    /// <summary>
    /// The working notes' marks «что не так» put into <paramref name="read"/>, the notes read again from the original
    /// <paramref name="original"/> («Как напечатано», step 3, <see cref="ReadingMarks"/>), when the working notes are a reading
    /// of it with marks; each mark first placed on the bar of the new reading its bar's music starts (<see cref="ReadingMatch"/>),
    /// a mark whose bar has no such place placed where its music lies and not put in; <paramref name="fileName"/> names the reading as it is imported. Answers
    /// the reading with the marks in and what became of them; the reading as it was and null with no marks.
    /// </summary>
    public static (ReadScore Read, MarksCarried? Marks) Marked(SessionPiece piece, string original, string fileName, ReadScore read)
    {
        if (piece.Project.Scan is not { Marks.Count: > 0 } scan || (scan.From ?? PieceOriginal.DocumentId) != original)
        {
            return (read, null);
        }

        ReadingMatch match = new(piece.Piece, read.Piece);
        int first = piece.Piece.Meter.BarAt(Tick.Zero).Number;
        List<ReadingMark> moved = [], lost = [];
        foreach (ReadingMark mark in scan.Marks)
        {
            Bar earlier = piece.Piece.Meter.GetBar(Math.Max(first, mark.Bar));
            if (match.Moved(earlier) is { } bar)
            {
                moved.Add(mark.At(bar.Number));
                continue;
            }

            // Where the bar's music lies in the new reading, as a correction not carried is flagged there.
            ReadingMark there = mark.At(read.Piece.Meter.BarAt(match.Place(earlier.Span.Onset, end: false).At).Number);
            moved.Add(there);
            lost.Add(there);
        }

        MarkedReading marked = ReadingMarks.Apply(read.MusicXml, read.Piece, moved, lost);
        // Imported as marked, not repaired again: the engines' rules would take a meter the player marked back to the bars' median.
        ReadScore? again = ReferenceEquals(marked.Score, read.MusicXml) ? read
            : PieceImporter.Import(fileName, marked.Score).Piece is { } made ? new ReadScore(marked.Score, made) : null;
        if (again is null)
        {
            (again, marked) = (read, marked with { Score = read.MusicXml, Marks = [.. marked.Marks.Select(static each => each with { Outcome = MarkOutcome.Unfit })] });
        }

        // Numbered by the bars of the notes made, which a meter mark may number otherwise than the reading did.
        IReadOnlyList<(ReadingMark Mark, MarkOutcome Outcome)> numbered = marked.Numbered(again.Piece);
        return (again, new MarksCarried(
            ScanOrigin.Ordered(numbered.Select(static each => each.Mark)),
            [.. numbered.Where(static each => each.Outcome == MarkOutcome.Applied).Select(static each => each.Mark)],
            [.. numbered.Where(static each => each.Outcome == MarkOutcome.Unfit).Select(static each => each.Mark)]));
    }

    /// <summary>
    /// <paramref name="again"/> with the Notes <paramref name="id"/> (read again, <paramref name="read"/>) keeping the marks
    /// carried, each mark that could not be put in flagged in Check (<see cref="CheckReason.MarkUnfit"/>) on its bar; as it was
    /// without marks.
    /// </summary>
    public static Project WithMarks(Project again, string id, MarksCarried? marks, ReadScore read)
    {
        if (marks is null)
        {
            return again;
        }

        IEnumerable<CheckFlag> unfit = marks.Unfit.Select(static mark => mark.Bar).Distinct().Select(static bar => new CheckFlag(bar, CheckSource.Scan, CheckReason.MarkUnfit, Resolved: false));
        OtherNotes[] notes = [.. again.Sources.Notes.Select(notes => notes.Id == id && notes.Scan is { } scan ? Flagged(notes, scan with { Marks = marks.Marks }, read, unfit) : notes)];
        return again with { Sources = again.Sources with { Notes = notes } };
    }

    /// <summary>The <paramref name="pages"/> (else one) of the PDF notes were read again from, by the bars the scan began each with.</summary>
    public static PageMap ReadPages(ReadScore read, int? pages) => PageMap.Of(read.Piece, Math.Max(1, pages ?? 1));
}