namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// Where a piece is inside the collection it was made from (version 0.4, «Пьесы в файле»; v0.4 design B1, within version
/// 15): the collection's original, by its id in the piece's own originals (<see cref="PieceParts.OriginalId"/>), and the
/// pages of its PDF or photos the piece takes, or the stretch of its recording. A link only: the piece has its own notes
/// and bytes, and a build that does not know the field opens it as a piece of its own.
/// </summary>
public sealed record PartOf
{
    private PartOf(string original, (int First, int Last)? pages, (long From, long To)? milliseconds)
    {
        Original = !string.IsNullOrWhiteSpace(original) ? original : throw new ArgumentException("A part names the collection's original.", nameof(original));
        Pages = pages;
        Milliseconds = milliseconds;
    }

    /// <summary>The collection's original, as an id in <see cref="Project.Originals"/> of the same piece.</summary>
    public string Original { get; }

    /// <summary>The collection's pages the piece takes, from 1, both ends included; null for a stretch of a recording.</summary>
    public (int First, int Last)? Pages { get; }

    /// <summary>The stretch of the collection's recording the piece takes, in milliseconds from its start; null for pages.</summary>
    public (long From, long To)? Milliseconds { get; }

    /// <summary>A piece on the pages <paramref name="first"/> to <paramref name="last"/> of the collection's PDF or photos.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The pages are not numbered from 1 in order.</exception>
    public static PartOf OnPages(string original, int first, int last) =>
        first >= 1 && last >= first
            ? new PartOf(original, (first, last), null)
            : throw new ArgumentOutOfRangeException(nameof(first), $"Pages {first}–{last} are not pages from 1 in order.");

    /// <summary>A piece in the stretch <paramref name="from"/> to <paramref name="to"/> milliseconds of the collection's recording.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The stretch does not start at 0 or later and end after it starts.</exception>
    public static PartOf InRecording(string original, long from, long to) =>
        from >= 0 && to > from
            ? new PartOf(original, null, (from, to))
            : throw new ArgumentOutOfRangeException(nameof(from), $"{from}–{to} ms is not a stretch of a recording.");
}