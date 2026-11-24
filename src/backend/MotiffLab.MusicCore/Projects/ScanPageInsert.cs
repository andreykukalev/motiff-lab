using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>A page no engine read, read again by itself and put into the same Notes (v1.3 i-1b, Andrei 2026-11-24 «Вставить в те же ноты»).</summary>
public static class ScanPageInsert
{
    /// <summary>
    /// The notes with a page no engine read before, read now and merged as scan.join merges a page (<paramref name="musicXml"/>),
    /// put in as their page <paramref name="page"/> (<see cref="ScanJoin.Insert"/>): its bars at its place, the bars after it
    /// numbered on. The player's corrections are carried over to the same music (<see cref="NoteEditLog.CarriedOver"/>), the
    /// checked bars and Check flags after it move on by its bars, its own bars get the scan's flags, the page's report goes in
    /// at its place when the notes keep reports (<paramref name="report"/> is then required), the PDF's
    /// <paramref name="documentPages"/> pages are mapped again as <c>document.read</c> maps them, and a resume point at or
    /// after the page goes back to the start, as <see cref="Project.ChangeBars"/> does. Null when the corrections or the bars cannot be
    /// carried over exactly: nothing is dropped.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="Project.CanInsertPage"/> is false.</exception>
    /// <exception cref="ArgumentException">The place or the page is not one, or the notes keep reports and none is sent.</exception>
    public static PageInserted? Into(Project project, int page, byte[] musicXml, PageReport? report, int documentPages)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!project.CanInsertPage)
        {
            throw new InvalidOperationException("A page goes into a scan's notes only before the player put anything on the score but corrections.");
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
        if (put.Length == 0 || project.Edits.CarriedOver(before.Notes, after.Notes, at, length) is not { } edits)
        {
            return null;
        }

        int first = put[0].Bar, bars = put.Length;
        if (!BarsMoveOn(before, after, at, length, first, bars))
        {
            return null;
        }

        IReadOnlyList<PageReport> reports = Reports(project.Scan!.Pages, page, report, score);
        ScanOrigin scan = new(project.Scan!.Engine, [.. project.Scan.ConfirmedBars.Select(bar => MovedOn(bar, first, bars))]) { From = project.Scan.From, At = project.Scan.At, Pages = reports };
        IEnumerable<CheckFlag> own = CheckFlag.FromScan(scan.CheckOf(after), scan.ConfirmedBars).Where(flag => flag.Bar >= first && flag.Bar < first + bars);
        Project inserted = project with
        {
            Source = score,
            Edits = edits,
            Scan = scan,
            Check = [.. (project.Check ?? []).Select(flag => new CheckFlag(MovedOn(flag.Bar, first, bars), flag.Source, flag.Reason, flag.Resolved)), .. own],
            DocumentPages = PageMap.Of(after, Math.Max(1, documentPages)),
            ResumePoints = [.. project.ResumePoints.Select(point => point.At < at && (point.Passage is null || point.Passage.Span.End <= at) ? point
                : point with { At = Tick.Zero, Passage = null, Focused = false, Loop = false })],
        };
        return new PageInserted(inserted, first, bars);
    }

    /// <summary>Whether every bar of <paramref name="before"/> is in <paramref name="after"/> at its place, those from the page's on <paramref name="bars"/> on.</summary>
    private static bool BarsMoveOn(ImportedPiece before, ImportedPiece after, Tick at, long length, int first, int bars)
    {
        Dictionary<long, int> numbers = ScanCheck.Of(after, null).Bars.ToDictionary(static bar => bar.Span.Onset.Value, static bar => bar.Bar);
        return ScanCheck.Of(before, null).Bars.All(bar => numbers.GetValueOrDefault(bar.Span.Onset.Value + (bar.Span.Onset < at ? 0 : length), -1) == MovedOn(bar.Bar, first, bars));
    }

    /// <summary>The notes' page reports with the page's at its place; none when the notes keep none.</summary>
    private static IReadOnlyList<PageReport> Reports(IReadOnlyList<PageReport> kept, int page, PageReport? report, byte[] score) => kept.Count == 0 ? []
        : PageReport.ForScore([.. kept.Take(page - 1), report ?? throw new ArgumentException("These notes keep each page's report: send the page's.", nameof(report)), .. kept.Skip(page - 1)], score);

    private static ImportedPiece Imported(Project project, byte[] score) => PieceImporter.Import(project.FileName, score).Piece
        ?? throw new ArgumentException("The notes with the page could not be read as music.", nameof(score));

    /// <summary>A bar number from <paramref name="first"/> on moved on by <paramref name="bars"/>; an earlier one as it is.</summary>
    private static int MovedOn(int bar, int first, int bars) => bar < first ? bar : bar + bars;
}