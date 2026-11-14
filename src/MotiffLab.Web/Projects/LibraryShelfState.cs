using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Localization;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Projects;

/// <summary>
/// The library's shelf as the page shows it (version 0.2): the lines made from the summaries, the composers, tags and
/// playlists to filter by, what the player searched and picked, the tab and the composer or playlist opened on it. One for
/// each visit to the library; the tab is kept on the device.
/// </summary>
public sealed class LibraryShelfState(Preferences preferences, UiText text)
{
    /// <summary>The device preference that keeps the tab the library was left on.</summary>
    private const string TabPreference = "library.tab";

    private LibraryPage? _page;

    public IReadOnlyList<LibraryEntry> Entries { get; private set; } = [];

    public IReadOnlyList<string> Composers { get; private set; } = [];

    public IReadOnlyList<string> Tags { get; private set; } = [];

    public IReadOnlyList<string> Playlists { get; private set; } = [];

    public LibraryQuery Query { get; private set; } = LibraryQuery.All;

    public LibraryTab Tab { get; private set; }

    /// <summary>The recordings this browser holds, as "&lt;project&gt;/&lt;original&gt;" (version 0.3).</summary>
    public IReadOnlySet<string> HeldHere { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Asks the device which recordings this browser holds, for the badges.</summary>
    public async Task KnowHeldHereAsync(ProjectStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        HeldHere = await store.HeldHereAsync();
    }

    /// <summary>«PDF: только на iPad»: where a piece's original is when it is not here; null when every one is.</summary>
    public string? Elsewhere(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return OriginalPlaces.Badge(text, entry.Summary, HeldHere);
    }

    /// <summary>The composer or playlist opened on its tab; null while the tab lists them.</summary>
    public LibraryGroup? Picked { get; private set; }

    /// <summary>What the tab shows: its pieces searched, filtered and ordered as asked, or its composers or playlists.</summary>
    public LibraryPage Page => _page ??= LibraryShelf.Page(Entries, Query, Tab, Picked, text.Culture);

    /// <summary>The filters show on the tabs that list every piece or the last opened.</summary>
    public bool Filters => Tab is LibraryTab.All or LibraryTab.Recent;

    public async Task LoadTabAsync()
    {
        if (Enum.TryParse(await preferences.GetAsync(TabPreference), out LibraryTab tab) && Enum.IsDefined(tab))
        {
            Show(tab);
        }
    }

    /// <summary>The lines made again from the summaries and covers, after a listing or a change to one piece.</summary>
    public void Restock(IEnumerable<ProjectSummary> summaries, IReadOnlyDictionary<string, PieceCover> covers)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(covers);
        Entries = [.. summaries.Select(summary => LibraryEntry.Of(summary, covers.GetValueOrDefault(summary.Id)))];
        Composers = LibraryShelf.Composers(Entries, text.Culture);
        Tags = LibraryShelf.Tags(Entries, text.Culture);
        Playlists = LibraryShelf.Playlists(Entries, text.Culture);
        // A composer, tag or playlist no piece has any more stops filtering.
        Query = Query with
        {
            Composer = LibraryShelf.Kept(Query.Composer, Composers),
            Tag = LibraryShelf.Kept(Query.Tag, Tags),
            Playlist = LibraryShelf.Kept(Query.Playlist, Playlists),
        };
        if (Picked is { Name: { } name } && LibraryShelf.Kept(name, Tab == LibraryTab.Playlists ? Playlists : Composers) is null)
        {
            Picked = null;
        }

        _page = null;
    }

    public void Narrow(LibraryQuery query)
    {
        Query = query ?? LibraryQuery.All;
        _page = null;
    }

    /// <summary>Search and filters cleared; the order stays.</summary>
    public void ClearFilters() => Narrow(Query.Cleared);

    /// <summary>Another tab, kept on the device: Recent lists the last opened first, the others from A.</summary>
    public async Task ChooseAsync(LibraryTab tab)
    {
        Show(tab);
        await preferences.SetAsync(TabPreference, tab.ToString());
    }

    /// <summary>Opens a composer's or a playlist's pieces on its tab; null goes back to the list of them.</summary>
    public void Pick(LibraryGroup? group)
    {
        Picked = group;
        _page = null;
    }

    private void Show(LibraryTab tab)
    {
        Tab = tab;
        Picked = null;
        Query = Query with { Sort = tab == LibraryTab.Recent ? LibrarySort.Opened : LibrarySort.Title, Ascending = true };
        _page = null;
    }
}