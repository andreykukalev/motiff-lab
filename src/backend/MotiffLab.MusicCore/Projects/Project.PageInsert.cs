using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>A page put into a scan's notes (<see cref="Project.InsertPage"/>): the piece, and the bars the page became.</summary>
/// <param name="FirstBar">The page's first bar; the bars from it on before moved on by <paramref name="Bars"/>.</param>
public sealed record PageInserted(Project Project, int FirstBar, int Bars);

public sealed partial record Project
{
    /// <summary>
    /// Whether a page read again can be put into these notes (v1.3 i-1b; as <see cref="CanChangeBars"/>): only notes a scan
    /// read from this piece's PDF, while nothing the player put on the score stands in ticks that would move, except the
    /// corrections and the checked bars, which <see cref="InsertPage"/> carries over, and resume points, which it keeps.
    /// </summary>
    public bool CanInsertPage => HasNotes && Scan is not null && Document is not null && !TakenBars.Any()
        && Phrases.IsEmpty && ShortPhrases.IsEmpty && Sentences.IsEmpty && Marks.IsEmpty && Bookmarks.Count == 0
        && Motifs.Count == 0 && Variations.Count == 0 && Observations.Count == 0 && Performances.Count == 0
        && Assembly.IsEmpty && Drafts.Count == 0 && Unknown.Count == 0 && Marks.Unknown.Count == 0;

    /// <summary>
    /// The notes with a page no engine read before, read now and merged as scan.join merges a page (<paramref name="musicXml"/>),
    /// put in as their page <paramref name="page"/> (<see cref="ScanJoin.Insert"/>): its bars at its place, the bars after it
    /// numbered on. The player's corrections are carried over to the same music (<see cref="NoteEditLog.CarriedOver"/>), the
    /// checked bars and Check flags after it move on by its bars, its own bars get the scan's flags, the page's report goes in
    /// at its place when the notes keep reports (<paramref name="report"/> is then required), the PDF's
    /// <paramref name="documentPages"/> pages are mapped again as <c>document.read</c> maps them, and a resume point at or
    /// after the page goes back to the start, as <see cref="ChangeBars"/> does. Null when the corrections or the bars cannot be
    /// carried over exactly: nothing is dropped.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="CanInsertPage"/> is false.</exception>
    /// <exception cref="ArgumentException">The place or the page is not one, or the notes keep reports and none is sent.</exception>
    public PageInserted? InsertPage(int page, byte[] musicXml, PageReport? report, int documentPages)
    {
        if (!CanInsertPage)
        {
            throw new InvalidOperationException("A page goes into a scan's notes only before the player put anything on the score but corrections.");
        }

        int pages = ScanJoin.Split(Source).Count;
        byte[] score = ScanJoin.Insert(Source, page, musicXml);
        ImportedPiece before = Imported(Source), after = Imported(score);
        PageMap map = PageMap.Of(after, pages + 1);
        if (map.Pages != pages + 1)
        {
            throw new ArgumentException("The page has no bars of its own.", nameof(musicXml));
        }

        Tick at = map.StartOf(page);
        long length = (page <= pages ? map.StartOf(page + 1) : after.MusicXml!.Measures[^1].Span.End).Value - at.Value;
        ScanBar[] put = [.. ScanCheck.Of(after, null).Bars.Where(bar => bar.Span.Onset >= at && bar.Span.Onset.Value < at.Value + length)];
        if (put.Length == 0 || Edits.CarriedOver(before.Notes, after.Notes, at, length) is not { } edits)
        {
            return null;
        }

        int first = put[0].Bar, bars = put.Length;
        Dictionary<long, int> numbers = ScanCheck.Of(after, null).Bars.ToDictionary(static bar => bar.Span.Onset.Value, static bar => bar.Bar);
        if (ScanCheck.Of(before, null).Bars.Any(bar => numbers.GetValueOrDefault(bar.Span.Onset.Value + (bar.Span.Onset < at ? 0 : length), -1) != MovedOn(bar.Bar, first, bars)))
        {
            return null;
        }

        IReadOnlyList<PageReport> reports = Scan!.Pages.Count == 0 ? []
            : PageReport.ForScore([.. Scan.Pages.Take(page - 1), report ?? throw new ArgumentException("These notes keep each page's report: send the page's.", nameof(report)), .. Scan.Pages.Skip(page - 1)], score);
        ScanOrigin scan = new(Scan.Engine, [.. Scan.ConfirmedBars.Select(bar => MovedOn(bar, first, bars))]) { From = Scan.From, At = Scan.At, Pages = reports };
        IEnumerable<CheckFlag> own = CheckFlag.FromScan(scan.CheckOf(after), scan.ConfirmedBars).Where(flag => flag.Bar >= first && flag.Bar < first + bars);
        Project inserted = this with
        {
            Source = score,
            Edits = edits,
            Scan = scan,
            Check = [.. (Check ?? []).Select(flag => new CheckFlag(MovedOn(flag.Bar, first, bars), flag.Source, flag.Reason, flag.Resolved)), .. own],
            DocumentPages = PageMap.Of(after, Math.Max(1, documentPages)),
            ResumePoints = [.. ResumePoints.Select(point => point.At < at && (point.Passage is null || point.Passage.Span.End <= at) ? point
                : point with { At = Tick.Zero, Passage = null, Focused = false, Loop = false })],
        };
        return new PageInserted(inserted, first, bars);
    }

    private ImportedPiece Imported(byte[] score) => PieceImporter.Import(FileName, score).Piece
        ?? throw new ArgumentException("The notes with the page could not be read as music.", nameof(score));

    /// <summary>A bar number from <paramref name="first"/> on moved on by <paramref name="bars"/>; an earlier one as it is.</summary>
    private static int MovedOn(int bar, int first, int bars) => bar < first ? bar : bar + bars;
}
