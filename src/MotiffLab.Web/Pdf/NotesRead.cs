using MotiffLab.MusicCore;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Import;
using MotiffLab.Web.Localization;

namespace MotiffLab.Web.Pdf;

/// <summary>The PDF piece with the notes the scan read, the notes as a piece, what the scan's check asks about, and where work starts.</summary>
public sealed record NotesRead(Project Project, ImportedPiece Piece, ScanCheck Check, ResumePoint Point, PageMap Pages);

/// <summary>
/// Puts the notes the scan server read from a PDF piece into that same piece (0011, 0023; testing item 16), as «Распознать
/// ноты» on one PDF and the library's batch (version 0.5) both do: the PDF, its marks and its bookmarks stay as they were,
/// the scan's Check flags start open and a resume point is set at the start. Nothing is saved here.
/// </summary>
public static class NotesReading
{
    /// <summary>The piece with its notes, or why the notes cannot be used.</summary>
    public static (NotesRead? Read, string? Problem) Into(Project project, ScanResult result, int pageCount, UiText text, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(text);
        if (project.HasNotes || result.MusicXml is not { } musicXml)
        {
            return (null, result.Problem);
        }

        // A PDF read in one request has not been through ScanJoin: its engine's mistakes are put right here (#495 BUG-03).
        musicXml = ScanRepair.Repair(musicXml);
        string name = Path.ChangeExtension(project.FileName, ".musicxml");
        PieceImportResult imported = PieceImporter.Import(name, musicXml);
        if (imported.Piece is not { } piece)
        {
            return (null, imported.Rejection?.Text(Words.For(text.Language)) ?? text["pdf.readNone"]);
        }

        PageMap pages = PageMap.Of(piece, Math.Max(1, pageCount));
        Project read = Recognised(project, musicXml, result, pages, now);
        ScanCheck check = read.Scan!.CheckOf(piece);
        read = read with { Check = CheckFlag.FromScan(check, []) };
        ResumePoint point = ResumePoint.Start(read.NextResumePointId(), now);
        return (new NotesRead(read.AddResumePoint(point), piece, check, point, pages), null);
    }

    /// <summary>
    /// The PDF piece with the notes read, keeping the server's report on each page (version 0.4) so the check counts each
    /// page's quality, and Check flags the bars of a poor page; without the reports when they are not one per page of the
    /// notes, as from a server before 0.4.
    /// </summary>
    private static Project Recognised(Project project, byte[] musicXml, ScanResult result, PageMap pages, DateTimeOffset now)
    {
        if (result.Reports.Count > 0)
        {
            try
            {
                return project.Recognised(musicXml, result.Engine, pages, now, result.Reports);
            }
            catch (ArgumentException)
            {
                // The reports do not match the pages of the notes: the notes are kept without them.
            }
        }

        return project.Recognised(musicXml, result.Engine, pages, now);
    }
}