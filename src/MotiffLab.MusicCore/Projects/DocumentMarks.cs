using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The player's marks on a PDF's pages (0023; Andrei, 2026-11-04): ink and bookmarks kept with the page they were made on, apart
/// from the marks of the piece's notes, so the "PDF" view and the "Notes" view each keep their own. A PDF's pages never reflow,
/// so a mark keeps its place on its page; nothing here is read as music.
/// </summary>
public sealed record DocumentMarks
{
    public static DocumentMarks Empty { get; } = new();

    /// <summary>
    /// The strokes, in the pencil layer's shape (<see cref="InkStroke"/>), with <see cref="InkStroke.Bar"/> holding the number of
    /// the page the stroke starts on (from 1) rather than a tick, and U and V its place on the page: U across from the page's
    /// left edge (0 to 1), V down from its top edge in the page's own spaces (a hundred and twentieth of its width, about a
    /// printed staff space).
    /// </summary>
    public IReadOnlyList<InkStroke> Ink { get; init; } = [];

    /// <summary>The pages the player bookmarked, in page order.</summary>
    public IReadOnlyList<PageBookmark> Bookmarks { get; init; } = [];

    /// <summary>The strokes of the file this build cannot read (S6), kept to be written back unchanged.</summary>
    public IReadOnlyList<UnknownEntity> Unknown { get; init; } = [];

    public bool IsEmpty => Ink.Count == 0 && Bookmarks.Count == 0;

    /// <summary>The marks with these strokes in place of the ones before; every stroke must name a page from 1.</summary>
    /// <exception cref="ArgumentException">A stroke names no page, or two strokes share an id.</exception>
    public DocumentMarks WithInk(IReadOnlyList<InkStroke> ink)
    {
        ArgumentNullException.ThrowIfNull(ink);
        if (ink.Any(static stroke => stroke.Bar.Value < 1))
        {
            throw new ArgumentException("A stroke on a PDF names its page, from 1.", nameof(ink));
        }

        if (ink.Select(static stroke => stroke.Id).Distinct(StringComparer.Ordinal).Count() != ink.Count)
        {
            throw new ArgumentException("Two strokes share an id.", nameof(ink));
        }

        return this with { Ink = [.. ink] };
    }

    /// <summary>The bookmark on a page, if there is one.</summary>
    public PageBookmark? BookmarkOn(int page) => Bookmarks.FirstOrDefault(bookmark => bookmark.Page == page);

    /// <summary>Bookmarks a page, named or not; a page already bookmarked keeps its bookmark.</summary>
    public DocumentMarks AddBookmark(int page, string? name, DateTimeOffset now)
    {
        if (BookmarkOn(page) is not null)
        {
            return this;
        }

        int next = Bookmarks.Select(static bookmark => int.TryParse(bookmark.Id.AsSpan(3), out int number) ? number : 0).DefaultIfEmpty(0).Max() + 1;
        var bookmark = new PageBookmark($"pb_{next:D4}", page, name, now);
        return this with { Bookmarks = [.. Bookmarks.Append(bookmark).OrderBy(static item => item.Page)] };
    }

    /// <summary>Gives a bookmark a name, or takes its name away with an empty one.</summary>
    public DocumentMarks RenameBookmark(string id, string? name) =>
        this with { Bookmarks = [.. Bookmarks.Select(bookmark => bookmark.Id == id ? bookmark with { Name = PageBookmark.Clean(name) } : bookmark)] };

    public DocumentMarks RemoveBookmark(string id) => this with { Bookmarks = [.. Bookmarks.Where(bookmark => bookmark.Id != id)] };

    public bool Equals(DocumentMarks? other) =>
        other is not null && Ink.SequenceEqual(other.Ink) && Bookmarks.SequenceEqual(other.Bookmarks);

    public override int GetHashCode() => HashCode.Combine(Ink.Count, Bookmarks.Count);
}

/// <summary>A page of a PDF the player bookmarked, with the name they gave it if any.</summary>
public sealed record PageBookmark(string Id, int Page, string? Name, DateTimeOffset CreatedAt)
{
    /// <summary>The longest name a bookmark keeps.</summary>
    public const int MaxName = 80;

    public string Id { get; } = !string.IsNullOrWhiteSpace(Id) ? Id : throw new ArgumentException("A bookmark needs an id.", nameof(Id));

    public int Page { get; } = Page >= 1 ? Page : throw new ArgumentOutOfRangeException(nameof(Page), Page, "Pages are numbered from 1.");

    public string? Name { get; init; } = Clean(Name);

    /// <summary>A name trimmed and cut to <see cref="MaxName"/>; null for none.</summary>
    internal static string? Clean(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Trim() is { Length: > MaxName } text ? text[..MaxName].TrimEnd() : name.Trim();
}