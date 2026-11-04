namespace MotiffLab.Web.Projects;

/// <summary>
/// A piece's cover in the library (0018 section 9), as the score saved it when it was last shown: the first page
/// as an image address, the title and lines it is headed with, the number of pages, and the ticks where each
/// page, phrase and bar starts, so a card says "p. 2 · phrase 5" for a resume point without opening the piece.
/// </summary>
public sealed record PieceCover(string Id, string Url, string? Title, IReadOnlyList<string> Lines, int Pages, IReadOnlyList<long> PageStarts, IReadOnlyList<long> PhraseStarts, IReadOnlyList<long> BarStarts, int FirstBar)
{
    /// <summary>The page a tick is on, from 1.</summary>
    public int PageAt(long tick) => Math.Max(1, Index(PageStarts, tick) + 1);

    /// <summary>The phrase a tick is in, from 1, or null before the first.</summary>
    public int? PhraseAt(long tick) => Index(PhraseStarts, tick) is >= 0 and var index ? index + 1 : null;

    /// <summary>The number of the bar a tick is in, counted from the first bar's number (0 for a pickup).</summary>
    public int BarAt(long tick) => FirstBar + Math.Max(0, Index(BarStarts, tick));

    /// <summary>The last start at or before the tick, or -1.</summary>
    private static int Index(IReadOnlyList<long> starts, long tick)
    {
        int found = -1;
        for (int index = 0; index < starts.Count && starts[index] <= tick; index++)
        {
            found = index;
        }

        return found;
    }
}