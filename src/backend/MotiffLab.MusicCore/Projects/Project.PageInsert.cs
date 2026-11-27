namespace MotiffLab.MusicCore.Projects;

/// <summary>A page put into a scan's notes (<see cref="ScanPageInsert.Into"/>): the piece, and the bars the page became; or why not.</summary>
/// <param name="Project">The piece with the page in; null when <paramref name="Refused"/> says why not.</param>
/// <param name="FirstBar">The page's first bar; the bars from it on before moved on by <paramref name="Bars"/>.</param>
internal sealed record PageInserted(Project? Project, int FirstBar, int Bars, PageRefusal Refused = PageRefusal.None)
{
    public static PageInserted Not(PageRefusal why) => new(null, 0, 0, why);
}

/// <summary>Why a page read again is not put into the notes (v1.3 i-1b); nothing is dropped.</summary>
internal enum PageRefusal
{
    None,

    /// <summary>Something the player put on the score but corrections and bookmarks stands in ticks that would move (<see cref="Project.CanInsertPage"/>).</summary>
    Work,

    /// <summary>A correction records a note sounding across the page's place, which the page would cut (<see cref="Events.NoteEditLog.Crosses"/>).</summary>
    Across,

    /// <summary>A correction, or a bar, would not stay on the same music (<see cref="Events.NoteEditLog.CarriedOver"/>).</summary>
    NotExact,
}

public sealed partial record Project
{
    /// <summary>
    /// Whether a page read again can be put into these notes (v1.3 i-1b; as <see cref="CanChangeBars"/>): only notes a scan
    /// read from this piece's PDF, while nothing the player put on the score stands in ticks that would move, except the
    /// corrections and the checked bars, which <see cref="ScanPageInsert.Into"/> carries over, and the bookmarks and resume
    /// points, which it moves on by the page (v1.5 i-7).
    /// </summary>
    public bool CanInsertPage => HasNotes && Scan is not null && Document is not null && !PlacedInBarsBesideBookmarks
        && Performances.Count == 0 && Assembly.IsEmpty && Drafts.Count == 0;
}