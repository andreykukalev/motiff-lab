using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// Marks a project whose source was read from a photo or PDF by sheet music recognition (decision
/// record 0011): which engine read it, and the bars the player checked and said look right.
/// </summary>
/// <remarks>
/// The project's source is the MusicXML the engine wrote; the picture itself is never kept. On every
/// open the notes are marked from <see cref="ScanCheck"/> and these bars, so the marks need not be stored.
/// Compares by value.
/// </remarks>
/// <param name="Engine">The engine and its version as it reported them, for example "homr 0.7.0".</param>
public sealed record ScanOrigin(string Engine, IReadOnlyList<int> ConfirmedBars)
{
    public string Engine { get; } = !string.IsNullOrWhiteSpace(Engine) ? Engine : throw new ArgumentException("The engine is required.", nameof(Engine));

    /// <summary>The bars the player said look right, in bar order, each once.</summary>
    public IReadOnlyList<int> ConfirmedBars { get; } = [.. (ConfirmedBars ?? throw new ArgumentNullException(nameof(ConfirmedBars))).Distinct().Order()];

    /// <summary>The original the notes were read from (<see cref="PieceOriginal.Id"/>, version 0.3); null when not kept.</summary>
    public string? From { get; init; }

    /// <summary>When the notes were read (version 0.3); null when not kept.</summary>
    public DateTimeOffset? At { get; init; }

    /// <summary>
    /// What the scan server saw of each page it read (version 0.4, within version 15), in page order: the raw numbers of
    /// its reports, so a page's quality is worked out again on every open (<see cref="PageReport.Quality"/>) and the
    /// thresholds can change without touching the file. Empty when the server sent none, as for every scan before 0.4.
    /// </summary>
    public IReadOnlyList<PageReport> Pages { get; init; } = [];

    /// <summary>
    /// The pages of the PDF, from 1, that no engine read, so the notes lack them (version 18; v1.5 i-7 kept them only in the
    /// iPad's recognition queue): the app marks each at its place and offers to read it again. Empty when every page was read.
    /// </summary>
    public IReadOnlyList<int> Unread { get; init; } = [];

    /// <summary>The pages of the PDF, from 1, that the notes have, in order (version 18), where a page read again goes among them; empty with <see cref="Unread"/>.</summary>
    public IReadOnlyList<int> ReadPages { get; init; } = [];

    /// <summary>
    /// The origin with the PDF's pages no engine read and those the notes have (version 18), each a page from 1, once and in
    /// order; none unread leaves both empty.
    /// </summary>
    /// <exception cref="ArgumentException">A page below 1, or one both unread and read.</exception>
    public ScanOrigin WithUnread(IEnumerable<int> unread, IEnumerable<int> read)
    {
        int[] missing = [.. unread.Distinct().Order()];
        int[] have = [.. read.Distinct().Order()];
        return missing.Concat(have).Any(static page => page < 1) || missing.Intersect(have).Any()
            ? throw new ArgumentException("Pages count from 1, and a page is either read or not.", nameof(unread))
            : this with { Unread = missing, ReadPages = missing.Length == 0 ? [] : have };
    }

    /// <summary>The origin once the PDF's page <paramref name="page"/> no engine read is in the notes (scan.insert): read now; as it was when the page was not kept as unread.</summary>
    public ScanOrigin WithPageRead(int page) =>
        Unread.Contains(page) ? WithUnread(Unread.Where(unread => unread != page), [.. ReadPages, page]) : this;

    /// <summary>A scan just read, with no bar checked yet.</summary>
    public static ScanOrigin Read(string engine) => new(engine, []);

    internal bool IsConfirmed(int bar) => ConfirmedBars.Contains(bar);

    /// <summary>
    /// The check of the notes this scan read (<paramref name="piece"/>, its source as imported), with the page reports it
    /// keeps (<see cref="Pages"/>, version 0.4): bars on a weak page less sure, bars on a poor page flagged
    /// <see cref="ScanFlag.PageQuality"/>. Without reports, as <see cref="ScanCheck.Of(ImportedPiece)"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The piece was not imported from MusicXML.</exception>
    public ScanCheck CheckOf(ImportedPiece piece) => ScanCheck.Of(piece, Pages.Count > 0 ? Pages : null);

    /// <summary>The origin with the bars added to the confirmed ones.</summary>
    public ScanOrigin Confirm(IEnumerable<int> bars) => WithConfirmed([.. ConfirmedBars, .. bars]);

    /// <summary>The origin with the bar taken out of the confirmed ones, so its notes are detected again.</summary>
    public ScanOrigin Unconfirm(int bar) => WithConfirmed(ConfirmedBars.Where(confirmed => confirmed != bar));

    /// <summary>The origin with these confirmed bars and everything else it keeps (the reports, the unread pages, version 18).</summary>
    public ScanOrigin WithConfirmed(IEnumerable<int> bars) => new(Engine, [.. bars]) { From = From, At = At, Pages = Pages, Unread = Unread, ReadPages = ReadPages };

    public bool Equals(ScanOrigin? other) =>
        other is not null && Engine == other.Engine && ConfirmedBars.SequenceEqual(other.ConfirmedBars) && From == other.From && At == other.At
        && Pages.SequenceEqual(other.Pages) && Unread.SequenceEqual(other.Unread) && ReadPages.SequenceEqual(other.ReadPages);

    public override int GetHashCode() => ConfirmedBars.Aggregate(Engine.GetHashCode(StringComparison.Ordinal), HashCode.Combine);
}