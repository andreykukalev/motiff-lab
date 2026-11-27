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

    /// <summary>The notes are compared with a version, assembled or drafted from, or keep what a later build wrote (<see cref="Project.CanInsertPage"/>).</summary>
    Work,

    /// <summary>A correction records a note sounding across the page's place, which the page would cut (<see cref="Events.NoteEditLog.Crosses"/>).</summary>
    Across,

    /// <summary>A correction, or a bar, would not stay on the same music (<see cref="Events.NoteEditLog.CarriedOver"/>).</summary>
    NotExact,

    /// <summary>A motif, an idea's variation or a mark on a note would not stay on the same notes (<see cref="FollowedWork.Inserted"/>).</summary>
    Notes,
}

public sealed partial record Project
{
    /// <summary>
    /// Whether a page read again can be put into these notes (v1.3 i-1b): only notes a scan read from this piece's PDF, while
    /// they are not compared with a version (nor have bars taken from one), assembled or drafted from, and keep nothing a later
    /// build wrote, which would not move with them. The rest of the player's work moves on by the page
    /// (<see cref="ScanPageInsert.Into"/>: the corrections and checked bars carried over, the bookmarks and resume points in
    /// v1.5 i-7, the phrases, ideas, motifs and marks in v1.5 r-2b, by one rule, <see cref="FollowedWork.Inserted"/>).
    /// </summary>
    public bool CanInsertPage => HasNotes && Scan is not null && Document is not null && !TakenBars.Any()
        && Performances.Count == 0 && Assembly.IsEmpty && Drafts.Count == 0 && Unknown.Count == 0 && Marks.Unknown.Count == 0;
}