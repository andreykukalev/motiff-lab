using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Localization;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Projects;

/// <summary>
/// The library's shelf as the page shows it (version 0.2): the cards made from the summaries, the composers and tags to
/// filter by, what the player searched and picked, and cards or a list. One for each visit to the library; the view picked
/// is kept on the device.
/// </summary>
public sealed class LibraryShelfState(Preferences preferences, UiText text)
{
    /// <summary>The device preference that keeps the shelf as cards or as a list.</summary>
    private const string ViewPreference = "library.view";

    /// <summary>A shelf longer than this opens as a list until the player picks a view.</summary>
    private const int ListFrom = 12;

    private LibraryView? _chosen;
    private IReadOnlyList<LibraryEntry>? _shown;

    public IReadOnlyList<LibraryEntry> Entries { get; private set; } = [];

    public IReadOnlyList<string> Composers { get; private set; } = [];

    public IReadOnlyList<string> Tags { get; private set; } = [];

    public LibraryQuery Query { get; private set; } = LibraryQuery.All;

    public LibraryView View { get; private set; }

    public bool IsList => View == LibraryView.List;

    /// <summary>The cards that show: the shelf searched, filtered and ordered as asked.</summary>
    public IReadOnlyList<LibraryEntry> Shown => _shown ??= LibraryShelf.Apply(Entries, Query, text.Culture);

    /// <summary>The shelf is narrowed and nothing on it matches.</summary>
    public bool NoneMatch => Query.Narrows && Shown.Count == 0;

    public async Task LoadViewAsync() =>
        _chosen = Enum.TryParse(await preferences.GetAsync(ViewPreference), out LibraryView view) ? view : null;

    /// <summary>The cards made again from the summaries and covers, after a listing or a change to one piece.</summary>
    public void Restock(IEnumerable<ProjectSummary> summaries, IReadOnlyDictionary<string, PieceCover> covers)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(covers);
        Entries = [.. summaries.Select(summary => LibraryEntry.Of(summary, covers.GetValueOrDefault(summary.Id)))];
        Composers = LibraryShelf.Composers(Entries, text.Culture);
        Tags = LibraryShelf.Tags(Entries, text.Culture);
        View = _chosen ?? (Entries.Count > ListFrom ? LibraryView.List : LibraryView.Cards);
        // A composer or tag no piece has any more stops filtering.
        Query = Query with
        {
            Composer = Composers.FirstOrDefault(composer => string.Equals(composer, Query.Composer, StringComparison.OrdinalIgnoreCase)),
            Tag = Tags.FirstOrDefault(tag => string.Equals(tag, Query.Tag, StringComparison.OrdinalIgnoreCase)),
        };
        _shown = null;
    }

    public void Narrow(LibraryQuery query)
    {
        Query = query ?? LibraryQuery.All;
        _shown = null;
    }

    /// <summary>Search and filters cleared; the order stays.</summary>
    public void ClearFilters() => Narrow(Query with { Search = null, Composer = null, Tag = null });

    public async Task ChooseAsync(LibraryView view)
    {
        View = view;
        _chosen = view;
        await preferences.SetAsync(ViewPreference, view.ToString());
    }
}