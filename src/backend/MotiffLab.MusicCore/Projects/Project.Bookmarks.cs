using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>The player's bookmarks and their categories (0018, section 5).</summary>
public sealed partial record Project
{
    /// <summary>The bookmark categories (0018, section 5): the four built-in ones, as named here, then the player's own.</summary>
    public IReadOnlyList<BookmarkCategory> Categories { get; init; } = BookmarkCategory.BuiltIn;

    /// <summary>The player's bookmarks, in the player's order; <see cref="BookmarksInScoreOrder"/> sorts them by place.</summary>
    public IReadOnlyList<Bookmark> Bookmarks { get; init; } = [];

    public IReadOnlyList<Bookmark> BookmarksInScoreOrder => [.. Bookmarks.OrderBy(bookmark => bookmark.Span.Onset).ThenBy(bookmark => bookmark.Span.End)];

    public BookmarkCategory? FindCategory(string id) => Categories.FirstOrDefault(category => category.Id == id);

    public Bookmark? FindBookmark(string id) => Bookmarks.FirstOrDefault(bookmark => bookmark.Id == id);

    /// <summary>The project with the bookmark at the end of the player's order.</summary>
    /// <exception cref="ArgumentException">
    /// A bookmark with the same id is already there, one kept unknown too (S6, #364: the file never holds two), or its category
    /// is not.
    /// </exception>
    public Project AddBookmark(Bookmark bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        if (Bookmarks.Any(kept => kept.Id == bookmark.Id) || UnknownIds(Unknown, "bookmarks.items").Contains(bookmark.Id, StringComparer.Ordinal))
        {
            throw new ArgumentException($"There is already a bookmark {bookmark.Id}.", nameof(bookmark));
        }

        return FindCategory(bookmark.CategoryId) is null
            ? throw new ArgumentException($"There is no bookmark category {bookmark.CategoryId}.", nameof(bookmark))
            : this with { Bookmarks = [.. Bookmarks, bookmark] };
    }

    /// <summary>The project with one bookmark changed by <paramref name="change"/>, keeping its id and place in the order.</summary>
    /// <exception cref="KeyNotFoundException">No bookmark has the id.</exception>
    /// <exception cref="ArgumentException">The change moved it to a category that is not there, or changed its id.</exception>
    public Project ChangeBookmark(string id, Func<Bookmark, Bookmark> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Bookmark changed = change(FindBookmark(id) ?? throw new KeyNotFoundException($"There is no bookmark {id}."));
        if (changed.Id != id)
        {
            throw new ArgumentException("A bookmark keeps its id.", nameof(change));
        }

        return FindCategory(changed.CategoryId) is null
            ? throw new ArgumentException($"There is no bookmark category {changed.CategoryId}.", nameof(change))
            : this with { Bookmarks = [.. Bookmarks.Select(bookmark => bookmark.Id == id ? changed : bookmark)] };
    }

    /// <summary>The project with the bookmark moved to <paramref name="index"/> in the player's order.</summary>
    /// <exception cref="KeyNotFoundException">No bookmark has the id.</exception>
    public Project MoveBookmark(string id, int index)
    {
        Bookmark moved = FindBookmark(id) ?? throw new KeyNotFoundException($"There is no bookmark {id}.");
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Bookmarks.Count);
        var order = Bookmarks.Where(bookmark => bookmark.Id != id).ToList();
        order.Insert(index, moved);
        return this with { Bookmarks = order };
    }

    /// <exception cref="KeyNotFoundException">No bookmark has the id.</exception>
    public Project RemoveBookmark(string id)
    {
        _ = FindBookmark(id) ?? throw new KeyNotFoundException($"There is no bookmark {id}.");
        return this with { Bookmarks = [.. Bookmarks.Where(bookmark => bookmark.Id != id)] };
    }

    /// <summary>
    /// The project with the form of Piece DNA written into the Structure category (testing item 22, Add Structure
    /// bookmarks on the map): one bookmark per section, "Section A′, bars 17–32", after the player's own. A section already
    /// bookmarked in Structure over the same bars is left as it is, so a second call adds nothing.
    /// </summary>
    public Project AddFormBookmarks(IEnumerable<DnaSection> sections, Words words, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sections);
        Project project = this;
        foreach (DnaSection section in sections)
        {
            if (project.Bookmarks.Any(bookmark => bookmark.CategoryId == BookmarkCategory.StructureId && bookmark.Span == section.Span))
            {
                continue;
            }

            string bars = section.FirstBar == section.LastBar
                ? words.T($"bar {section.FirstBar}", $"такт {section.FirstBar}")
                : words.T($"bars {section.FirstBar}–{section.LastBar}", $"такты {section.FirstBar}–{section.LastBar}");
            string title = words.T($"Section {section.Label}, {bars}", $"Раздел {section.Label}, {bars}");
            project = project.AddBookmark(new Bookmark(project.NextBookmarkId(), title, BookmarkCategory.StructureId, section.Span, FromPhrase: false, now));
        }

        return project;
    }

    /// <summary>The project with a category of the player's own after the others.</summary>
    /// <exception cref="ArgumentException">A category with the same id is already there.</exception>
    public Project AddCategory(BookmarkCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return FindCategory(category.Id) is not null
            ? throw new ArgumentException($"There is already a category {category.Id}.", nameof(category))
            : this with { Categories = [.. Categories, category] };
    }

    /// <summary>The project with the category renamed; blank gives a built-in one its own name back.</summary>
    /// <exception cref="KeyNotFoundException">No category has the id.</exception>
    public Project RenameCategory(string id, string? name)
    {
        BookmarkCategory renamed = (FindCategory(id) ?? throw new KeyNotFoundException($"There is no category {id}.")).Rename(name);
        return this with { Categories = [.. Categories.Select(category => category.Id == id ? renamed : category)] };
    }

    /// <summary>
    /// The project without a category of the player's own; its bookmarks move to Practise. The built-in
    /// categories stay.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No category has the id.</exception>
    /// <exception cref="ArgumentException">The category is built in.</exception>
    public Project RemoveCategory(string id)
    {
        BookmarkCategory category = FindCategory(id) ?? throw new KeyNotFoundException($"There is no category {id}.");
        if (category.IsBuiltIn)
        {
            throw new ArgumentException("The four built-in categories stay.", nameof(id));
        }

        return this with
        {
            Categories = [.. Categories.Where(kept => kept.Id != id)],
            Bookmarks = [.. Bookmarks.Select(bookmark => bookmark.CategoryId == id ? bookmark with { CategoryId = BookmarkCategory.PractiseId } : bookmark)],
        };
    }

    /// <summary>"b_0004": one more than the highest bookmark number.</summary>
    public string NextBookmarkId() => NextId("b_", Bookmarks.Select(bookmark => bookmark.Id).Concat(UnknownIds(Unknown, "bookmarks.items")));

    /// <summary>"c_0004": one more than the highest number among the player's own categories.</summary>
    public string NextCategoryId() => NextId("c_", Categories.Select(category => category.Id).Concat(UnknownIds(Unknown, "bookmarks.categories")));
}