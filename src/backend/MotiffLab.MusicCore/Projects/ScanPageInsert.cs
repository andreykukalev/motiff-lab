using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>A page no engine read, read again by itself and put into the same Notes (v1.3 i-1b, Andrei 2026-11-24 «Вставить в те же ноты»).</summary>
internal static class ScanPageInsert
{
    /// <summary>
    /// The notes with a page no engine read before, read now and merged as scan.join merges a page (<paramref name="musicXml"/>),
    /// put in as their page <paramref name="page"/> (<see cref="ScanJoin.Insert"/>): its bars at its place, the bars after it
    /// numbered on. The player's corrections are carried over to the same music (<see cref="NoteEditLog.CarriedOver"/>), the
    /// checked bars and Check flags after it move on by its bars, its own bars get the scan's flags, the page's report goes in
    /// at its place when the notes keep reports (with no <paramref name="report"/>, a page read again without one, the notes keep
    /// none from then on: one report per page or none, v1.5 i-7), the PDF's
    /// <paramref name="documentPages"/> pages are mapped again as <c>document.read</c> maps them, and the player's work at or after
    /// the page (bookmarks, resume points, phrases, ideas, motifs, marks) moves on by its length (<see cref="FollowedWork.Inserted"/>,
    /// v1.5 i-7 and r-2b). Refused, with why, while <see cref="Project.CanInsertPage"/> is false or when the corrections, the bars
    /// or what names a note cannot be carried over exactly: nothing is dropped.
    /// </summary>
    /// <exception cref="ArgumentException">The place or the page is not one.</exception>
    public static PageInserted Into(Project project, int page, byte[] musicXml, PageReport? report, int documentPages)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!project.CanInsertPage)
        {
            return PageInserted.Not(PageRefusal.Work);
        }

        int pages = ScanJoin.Split(project.Source).Count;
        byte[] score = ScanJoin.Insert(project.Source, page, musicXml);
        ImportedPiece before = Imported(project, project.Source), after = Imported(project, score);
        PageMap map = PageMap.Of(after, pages + 1);
        if (map.Pages != pages + 1)
        {
            throw new ArgumentException("The page has no bars of its own.", nameof(musicXml));
        }

        Tick at = map.StartOf(page);
        long length = (page <= pages ? map.StartOf(page + 1) : after.MusicXml!.Measures[^1].Span.End).Value - at.Value;
        ScanBar[] put = [.. ScanCheck.Of(after, null).Bars.Where(bar => bar.Span.Onset >= at && bar.Span.Onset.Value < at.Value + length)];
        if (project.Edits.Crosses(at))
        {
            return PageInserted.Not(PageRefusal.Across);
        }

        if (put.Length == 0 || NoteEditLog.IdsMovedOn(before.Notes, after.Notes, at, length) is not { } ids
            || project.Edits.CarriedOver(before.Notes, after.Notes, at, length, ids) is not { } edits)
        {
            return PageInserted.Not(PageRefusal.NotExact);
        }

        int first = put[0].Bar, bars = put.Length;
        if (!BarsMoveOn(before, after, at, length, first, bars))
        {
            return PageInserted.Not(PageRefusal.NotExact);
        }

        IReadOnlyList<PageReport> reports = Reports(project.Scan!.Pages, page, report, score);
        // Everything else the scan keeps goes with it, its unread pages too (version 18): scan.insert's "pdfPage" marks one read.
        ScanOrigin scan = project.Scan!.WithConfirmed(project.Scan.ConfirmedBars.Select(bar => MovedOn(bar, first, bars))) with { Pages = reports };
        IEnumerable<CheckFlag> own = CheckFlag.FromScan(scan.CheckOf(after), scan.ConfirmedBars).Where(flag => flag.Bar >= first && flag.Bar < first + bars);
        // The player's work in ticks moves on with the bars after the page, by one rule (v1.5 i-7, r-2b); nothing else is there to move (CanInsertPage).
        if (FollowedWork.Inserted(project, before.Meter, after.Meter, at, length, ids) is not { } followed)
        {
            return PageInserted.Not(PageRefusal.Notes);
        }

        Project inserted = followed.Project with
        {
            Source = score,
            Edits = edits,
            Scan = scan,
            Check = [.. (project.Check ?? []).Select(flag => new CheckFlag(MovedOn(flag.Bar, first, bars), flag.Source, flag.Reason, flag.Resolved)), .. own],
            DocumentPages = PageMap.Of(after, Math.Max(1, documentPages)),
        };
        return new PageInserted(inserted, first, bars);
    }

    /// <summary>Whether every bar of <paramref name="before"/> is in <paramref name="after"/> at its place, those from the page's on <paramref name="bars"/> on.</summary>
    private static bool BarsMoveOn(ImportedPiece before, ImportedPiece after, Tick at, long length, int first, int bars)
    {
        Dictionary<long, int> numbers = ScanCheck.Of(after, null).Bars.ToDictionary(static bar => bar.Span.Onset.Value, static bar => bar.Bar);
        return ScanCheck.Of(before, null).Bars.All(bar => numbers.GetValueOrDefault(bar.Span.Onset.Value + (bar.Span.Onset < at ? 0 : length), -1) == MovedOn(bar.Bar, first, bars));
    }

    /// <summary>The notes' page reports with the page's at its place; none when the notes keep none or the page came without one.</summary>
    private static IReadOnlyList<PageReport> Reports(IReadOnlyList<PageReport> kept, int page, PageReport? report, byte[] score) => kept.Count == 0 || report is null ? []
        : PageReport.ForScore([.. kept.Take(page - 1), report, .. kept.Skip(page - 1)], score);

    private static ImportedPiece Imported(Project project, byte[] score) => PieceImporter.Import(project.FileName, score).Piece
        ?? throw new ArgumentException("The notes with the page could not be read as music.", nameof(score));

    /// <summary>A bar number from <paramref name="first"/> on moved on by <paramref name="bars"/>; an earlier one as it is.</summary>
    private static int MovedOn(int bar, int first, int bars) => bar < first ? bar : bar + bars;
}