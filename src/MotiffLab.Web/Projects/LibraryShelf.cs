using System.Globalization;
using System.Text;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Projects;

/// <summary>
/// The library's tabs (design: ux-prototype/v2/screens/lib-1-list.png, lib-3-sessions.png), in Andrei's order: every piece
/// as a list, the composers with their pieces, the pieces opened last, the playlists with their pieces, and the sessions of
/// current work as cards.
/// </summary>
public enum LibraryTab
{
    All,
    Composers,
    Recent,
    Playlists,
    Sessions,
}

/// <summary>The list's columns that order it: the title (the default), the composer, or when the piece was opened, last first.</summary>
public enum LibrarySort
{
    Title,
    Composer,
    Opened,
}

/// <summary>
/// What the library is narrowed to: words to find in a title, composer, tag or playlist, one composer, one tag and one
/// playlist; and the order of the list, by the column chosen, either way.
/// </summary>
public sealed record LibraryQuery(string? Search, LibrarySort Sort, bool Ascending, string? Composer, string? Tag, string? Playlist)
{
    public static LibraryQuery All { get; } = new(null, LibrarySort.Title, true, null, null, null);

    /// <summary>Only the PDF pieces with no notes yet («Без нот», version 0.5, with the import's experiment).</summary>
    public bool WithoutNotes { get; init; }

    public bool Narrows => !string.IsNullOrWhiteSpace(Search) || Composer is not null || Tag is not null || Playlist is not null || WithoutNotes;

    /// <summary>Search and filters cleared; the order stays.</summary>
    public LibraryQuery Cleared => this with { Search = null, Composer = null, Tag = null, Playlist = null, WithoutNotes = false };

    /// <summary>
    /// Ordered by this column: the same column again turns the order round, another one starts its natural way (titles and
    /// composers from A, the last opened first).
    /// </summary>
    public LibraryQuery SortedBy(LibrarySort column) => this with { Sort = column, Ascending = column != Sort || !Ascending };
}

/// <summary>One piece on the shelf: its summary and the title, composer, tags and playlists its line shows.</summary>
/// <param name="TitleIsPlayers">Whether the title is the one the player set, as against the file's or its name.</param>
public sealed record LibraryEntry(ProjectSummary Summary, string Title, string? Composer, IReadOnlyList<string> Tags, IReadOnlyList<string> Playlists, bool TitleIsPlayers)
{
    /// <summary>The title, composer, tags and playlists in one folded text, for search.</summary>
    internal string Folded { get; } = LibraryShelf.Fold(string.Join('\n', [Title, Composer ?? "", .. Tags, .. Playlists]));

    /// <summary>The first letter of the title, folded and in capitals, for the index at the list's edge ("É" files under "E").</summary>
    public string Initial => LibraryShelf.Initial(Title);

    /// <summary>Whether there is work on it to come back to: a resume point, or a PDF read as it is (it opens where it was left).</summary>
    public bool InSession => Summary.Origin == PieceOrigin.Pdf || Summary.Latest is not null;

    /// <summary>
    /// The line of a saved piece: its details where the summary keeps them (version 0.2), else as earlier builds titled it.
    /// A title from the file's name gives way to the title the score was engraved with, when the piece was shown once.
    /// </summary>
    public static LibraryEntry Of(ProjectSummary summary, PieceCover? cover)
    {
        ArgumentNullException.ThrowIfNull(summary);
        PieceDetails? details = summary.Details;
        bool named = details is not null && details.Title.Attribution.Provenance != Provenance.Inferred;
        string title = named ? details!.Title.Value
            : cover?.Title is { Length: > 0 } engraved ? engraved
            : details?.Title.Value ?? ScoreTitle.Of(summary.FileName) ?? summary.FileName;
        return new(summary, title, details?.Composer?.Value, details?.Tags ?? [], details?.Playlists ?? [], details?.Title.Attribution.Provenance == Provenance.UserEdited);
    }
}

/// <summary>A line of the Composers or Playlists tab: a composer or a playlist and how many pieces it holds; no name for the pieces without a composer.</summary>
public sealed record LibraryGroup(string? Name, int Count)
{
    public string Initial => Name is null ? "" : LibraryShelf.Initial(Name);
}

/// <summary>
/// What one tab of the library shows: its pieces, searched, filtered and ordered as asked, or on the Composers and Playlists
/// tabs their lines until one is picked; and whether the index at the list's edge leads into it (a list ordered from A).
/// </summary>
public sealed record LibraryPage(IReadOnlyList<LibraryEntry> Pieces, IReadOnlyList<LibraryGroup>? Groups, bool Indexed)
{
    public bool IsEmpty => Groups?.Count == 0 || (Groups is null && Pieces.Count == 0);

    /// <summary>Each line's initial, in the order shown, for the index.</summary>
    public IReadOnlyList<string> Initials => Groups is { } groups ? [.. groups.Select(group => group.Initial)] : [.. Pieces.Select(entry => entry.Initial)];
}

/// <summary>
/// The library's search, filters, tabs and order (version 0.2), the same rules as the iPad's (ipad/App/MotiffLab/LibraryShelf.swift):
/// pure functions over the lines, so the shelf narrows as the player types without reading a project again. Search ignores
/// letter case and accents ("fur elise" finds "Für Elise", "ежи" finds "Ёжик").
/// </summary>
public static class LibraryShelf
{
    /// <summary>How many pieces Recent lists, the last opened first.</summary>
    public const int RecentCount = 20;

    private const string Latin = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Cyrillic = "АБВГДЕЖЗИКЛМНОПРСТУФХЦЧШЩЭЮЯ";

    /// <summary>What a tab shows, with the Composers or Playlists line picked there, if any.</summary>
    public static LibraryPage Page(IReadOnlyList<LibraryEntry> entries, LibraryQuery query, LibraryTab tab, LibraryGroup? picked, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(query);
        bool fromA = query is { Sort: LibrarySort.Title, Ascending: true };
        switch (tab)
        {
            case LibraryTab.Composers or LibraryTab.Playlists:
                // The composer or playlist is picked here, not by a filter; search still narrows.
                IReadOnlyList<LibraryEntry> searched = Apply(entries, LibraryQuery.All with { Search = query.Search, Sort = query.Sort, Ascending = query.Ascending }, culture);
                bool composers = tab == LibraryTab.Composers;
                if (picked is not null)
                {
                    return new([.. searched.Where(entry => composers ? Holds(entry.Composer, picked.Name) : entry.Playlists.Any(name => Same(name, picked.Name!)))], null, fromA);
                }

                return new([], composers ? ComposerRows(searched, culture) : PlaylistRows(searched, culture), true);
            case LibraryTab.Sessions:
                return new(Apply([.. entries.Where(entry => entry.InSession)], LibraryQuery.All with { Search = query.Search, Sort = LibrarySort.Opened }, culture), null, false);
            case LibraryTab.Recent:
                return new(Apply([.. Recent(entries).Take(RecentCount)], query, culture), null, fromA);
            default:
                return new(Apply(entries, query, culture), null, fromA);
        }
    }

    public static IReadOnlyList<LibraryEntry> Apply(IReadOnlyList<LibraryEntry> entries, LibraryQuery query, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(query);
        string[] words = Fold(query.Search ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        LibraryEntry[] found = [.. entries.Where(entry =>
            words.All(word => entry.Folded.Contains(word, StringComparison.Ordinal))
            && (query.Composer is null || Same(entry.Composer, query.Composer))
            && (query.Tag is null || entry.Tags.Any(tag => Same(tag, query.Tag)))
            && (query.Playlist is null || entry.Playlists.Any(name => Same(name, query.Playlist)))
            && (!query.WithoutNotes || entry.Summary.Origin == PieceOrigin.Pdf))];
        StringComparer byText = Comparer(culture);
        IReadOnlyList<LibraryEntry> ordered = query.Sort switch
        {
            LibrarySort.Opened => [.. Recent(found)],
            LibrarySort.Composer => [.. found.OrderBy(entry => entry.Composer is null).ThenBy(entry => entry.Composer ?? "", byText).ThenBy(entry => entry.Title, byText).ThenBy(entry => entry.Summary.Id, StringComparer.Ordinal)],
            _ => [.. found.OrderBy(entry => entry.Title, byText).ThenBy(entry => entry.Summary.Id, StringComparer.Ordinal)],
        };
        return query.Ascending ? ordered : [.. ordered.Reverse()];
    }

    /// <summary>The composers of the pieces given, each once with how many pieces, in the language's order; the pieces without one last.</summary>
    public static IReadOnlyList<LibraryGroup> ComposerRows(IReadOnlyList<LibraryEntry> entries, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(entries);
        List<LibraryGroup> rows = [.. Composers(entries, culture).Select(name => new LibraryGroup(name, entries.Count(entry => Same(entry.Composer, name))))];
        int without = entries.Count(entry => entry.Composer is null);
        if (without > 0)
        {
            rows.Add(new LibraryGroup(null, without));
        }

        return rows;
    }

    /// <summary>The playlists of the pieces given, each once with how many pieces are on it, in the language's order.</summary>
    public static IReadOnlyList<LibraryGroup> PlaylistRows(IReadOnlyList<LibraryEntry> entries, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return [.. Playlists(entries, culture).Select(name => new LibraryGroup(name, entries.Count(entry => entry.Playlists.Any(each => Same(each, name)))))];
    }

    /// <summary>The composers on the shelf, each once whatever its letter case, in the language's order.</summary>
    public static IReadOnlyList<string> Composers(IEnumerable<LibraryEntry> entries, CultureInfo culture) =>
        Distinct(entries.Select(entry => entry.Composer).OfType<string>(), culture);

    /// <summary>The tags on the shelf, each once whatever its letter case, in the language's order.</summary>
    public static IReadOnlyList<string> Tags(IEnumerable<LibraryEntry> entries, CultureInfo culture) =>
        Distinct(entries.SelectMany(entry => entry.Tags), culture);

    /// <summary>The playlists on the shelf, each once whatever its letter case, in the language's order.</summary>
    public static IReadOnlyList<string> Playlists(IEnumerable<LibraryEntry> entries, CultureInfo culture) =>
        Distinct(entries.SelectMany(entry => entry.Playlists), culture);

    /// <summary>The name as the shelf writes it, when some piece has it in any letter case; null when none has.</summary>
    public static string? Kept(string? picked, IReadOnlyList<string> names) =>
        picked is null ? null : names.FirstOrDefault(name => Same(name, picked));

    /// <summary>The letters of the index at the list's edge: A to Z, and the Cyrillic capitals when a line starts with one.</summary>
    public static IReadOnlyList<string> IndexLetters(IReadOnlyList<string> initials)
    {
        ArgumentNullException.ThrowIfNull(initials);
        IEnumerable<char> letters = initials.Any(initial => initial.Length > 0 && Cyrillic.Contains(initial[0], StringComparison.Ordinal)) ? Latin + Cyrillic : Latin;
        return [.. letters.Select(letter => letter.ToString())];
    }

    /// <summary>
    /// Where a letter of the index leads in a list ordered from A, given each line's initial: the first line filed under it,
    /// else the first after it, else the last line; null for an empty list.
    /// </summary>
    public static int? Target(string letter, IReadOnlyList<string> initials, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(initials);
        StringComparer byText = Comparer(culture);
        for (int index = 0; index < initials.Count; index++)
        {
            if (initials[index] == letter || byText.Compare(initials[index], letter) > 0)
            {
                return index;
            }
        }

        return initials.Count > 0 ? initials.Count - 1 : null;
    }

    /// <summary>The composers in the library that hold what is typed, to pick instead of typing on; none once one is typed whole.</summary>
    public static IReadOnlyList<string> ComposerSuggestions(string? typed, IReadOnlyList<string> composers)
    {
        ArgumentNullException.ThrowIfNull(composers);
        if (PieceMetadata.Clean(typed) is not { } clean || composers.Any(composer => Fold(composer) == Fold(clean)))
        {
            return [];
        }

        string wanted = Fold(clean);
        return [.. composers.Where(composer => Fold(composer).Contains(wanted, StringComparison.Ordinal)).Take(5)];
    }

    /// <summary>Lower case without accents: "Für Élise" is "fur elise", "Ёлка" is "елка".</summary>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string decomposed;
        try
        {
            decomposed = text.Normalize(NormalizationForm.FormD);
        }
        catch (PlatformNotSupportedException)
        {
            decomposed = text;
        }

        var folded = new StringBuilder(decomposed.Length);
        foreach (char letter in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(char.ToLowerInvariant(letter));
            }
        }

        return folded.ToString();
    }

    internal static string Initial(string text) => Fold(text) is { Length: > 0 } folded ? char.ToUpperInvariant(folded[0]).ToString() : "";

    /// <summary>The last opened first.</summary>
    private static IEnumerable<LibraryEntry> Recent(IEnumerable<LibraryEntry> entries) =>
        entries.OrderByDescending(entry => entry.Summary.LastOpened).ThenBy(entry => entry.Summary.Id, StringComparer.Ordinal);

    private static bool Holds(string? composer, string? picked) => picked is null ? composer is null : Same(composer, picked);

    private static bool Same(string? a, string b) => a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static StringComparer Comparer(CultureInfo culture) =>
        StringComparer.Create(culture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    private static IReadOnlyList<string> Distinct(IEnumerable<string> texts, CultureInfo culture) =>
        [.. texts.Distinct(StringComparer.OrdinalIgnoreCase).Order(Comparer(culture))];
}