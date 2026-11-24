namespace MotiffLab.MusicCore.Projects;

/// <summary>A page put into a scan's notes (<see cref="ScanPageInsert.Into"/>): the piece, and the bars the page became.</summary>
/// <param name="FirstBar">The page's first bar; the bars from it on before moved on by <paramref name="Bars"/>.</param>
public sealed record PageInserted(Project Project, int FirstBar, int Bars);

public sealed partial record Project
{
    /// <summary>
    /// Whether a page read again can be put into these notes (v1.3 i-1b; as <see cref="CanChangeBars"/>): only notes a scan
    /// read from this piece's PDF, while nothing the player put on the score stands in ticks that would move, except the
    /// corrections and the checked bars, which <see cref="ScanPageInsert.Into"/> carries over, and resume points, which it keeps.
    /// </summary>
    public bool CanInsertPage => HasNotes && Scan is not null && Document is not null && !PlacedInBars
        && Performances.Count == 0 && Assembly.IsEmpty && Drafts.Count == 0;
}