namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>Pages of a PDF, from <see cref="First"/> to <see cref="Last"/>, both counted from 1.</summary>
public readonly record struct PageRange(int First, int Last)
{
    /// <summary>How many pages the range holds.</summary>
    public int Count => Last - First + 1;
}

/// <summary>
/// A score longer than the scan server reads in one request, sent in parts (v0.5, issue #479): the server says how many
/// pages it reads at a time (<c>maxPages</c> on <c>/health</c>; 4 on Motiff Lab's server, 0014), and the apps cut the PDF
/// into consecutive parts of at most that many pages, send them one after another and join what each part reads with
/// <see cref="ScanJoin.Join"/>, as the server joins its pages: the measures of part 2 follow those of part 1, and each
/// part keeps its own page marks, so <see cref="Projects.PageMap"/> still finds where each page of the PDF starts.
/// </summary>
/// <remarks>Pure and deterministic. Nothing changes on the server: a part is an ordinary PDF of fewer pages.</remarks>
public static class ScanParts
{
    /// <summary>The most pages a score may have to be recognised (Andrei, 2026-11-13).</summary>
    public const int MostPages = 100;

    /// <summary>
    /// The pages any scan server reads in one request: Motiff Lab's reads 4 (0014), a player's own 8 unless told otherwise.
    /// Used when the server does not say, and a PDF of this many pages or fewer goes whole without asking.
    /// </summary>
    public const int SafePages = 4;

    /// <summary>
    /// The pages the apps send a request (v0.5.6, #533 section 1): one, so a score's progress is its pages' real answers
    /// (<see cref="PageProgress"/>). Any server reads a one-page PDF, an older one on the player's own computer too, and
    /// homr reads a page at a time whatever the request holds, so only the upload's small overhead is added a page.
    /// </summary>
    public const int PagesARequest = 1;

    /// <summary>
    /// The parts a PDF of <paramref name="pages"/> pages goes in, for a server that reads <paramref name="maxPages"/> pages a
    /// request (fewer than 1 counts as <see cref="SafePages"/>): consecutive, in order, each as full as it can be, the last
    /// with what is left. One part when the PDF fits.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Fewer than 1 page, or more than <see cref="MostPages"/>.</exception>
    public static IReadOnlyList<PageRange> Plan(int pages, int maxPages)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pages, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pages, MostPages);
        int size = maxPages < 1 ? SafePages : maxPages;
        var parts = new List<PageRange>((pages + size - 1) / size);
        for (int first = 1; first <= pages; first += size)
        {
            parts.Add(new PageRange(first, Math.Min(pages, first + size - 1)));
        }

        return parts;
    }
}