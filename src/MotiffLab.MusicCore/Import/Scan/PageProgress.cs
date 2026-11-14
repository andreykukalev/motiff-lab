namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>Where one page of a score being read is: its notes back, sent and being read, or waiting its turn.</summary>
public enum PageState
{
    Done,
    Current,
    Waiting,
}

/// <summary>
/// How far the notes of a score are read (v0.5.6, #533 section 1). The apps send a PDF one page a request
/// (<see cref="ScanParts.PagesARequest"/>), so each answer is a real event: the pages whose notes are back are done, the
/// page sent is current, the rest wait. Nothing moves by time alone. The time left is told only from the pages already
/// read, measured from send to answer: their mean times the pages not back yet; before the first page there is none.
/// </summary>
/// <remarks>Pure: the apps measure the milliseconds and pass them in.</remarks>
public sealed record PageProgress
{
    /// <summary>A score of <paramref name="pages"/> pages, none read yet: page 1 is current.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Fewer than 1 page.</exception>
    public PageProgress(int pages)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pages, 1);
        Pages = pages;
    }

    /// <summary>The score's pages.</summary>
    public int Pages { get; }

    /// <summary>The pages whose notes are back.</summary>
    public int Done { get; private init; }

    /// <summary>How long each page read took, from send to answer, in order.</summary>
    public IReadOnlyList<long> Milliseconds { get; private init; } = [];

    /// <summary>The page being read, from 1; null once every page is back.</summary>
    public int? Current => Done < Pages ? Done + 1 : null;

    /// <summary>Each page's state, page 1 first.</summary>
    public IReadOnlyList<PageState> States => [.. Enumerable.Range(1, Pages).Select(StateOf)];

    /// <summary>About how many minutes are left, at least one; null before the first page is back, or once all are.</summary>
    public int? MinutesLeft => MinutesFor(Milliseconds, Pages - Done);

    /// <summary>The state of a page, from 1.</summary>
    public PageState StateOf(int page) => page <= Done ? PageState.Done : page == Done + 1 ? PageState.Current : PageState.Waiting;

    /// <summary>The current page's notes came back after <paramref name="milliseconds"/>: the next page is current.</summary>
    public PageProgress Read(long milliseconds) =>
        Done < Pages ? this with { Done = Done + 1, Milliseconds = [.. Milliseconds, Math.Max(0, milliseconds)] } : this;

    /// <summary>
    /// About how many minutes <paramref name="pagesLeft"/> pages take, from the pages <paramref name="measured"/> so far:
    /// their mean, rounded up to whole minutes, at least one. Null with nothing measured or nothing left.
    /// </summary>
    public static int? MinutesFor(IReadOnlyList<long> measured, int pagesLeft)
    {
        ArgumentNullException.ThrowIfNull(measured);
        if (measured.Count == 0 || pagesLeft <= 0)
        {
            return null;
        }

        long left = measured.Sum() * pagesLeft / measured.Count;
        return (int)Math.Max(1, (left + 59_999) / 60_000);
    }
}