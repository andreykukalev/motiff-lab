using System.Globalization;
using System.Text;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Projects;

/// <summary>How the library orders its pieces: the one opened last first (the default), by title, or by composer.</summary>
public enum LibrarySort
{
    Recent,
    Title,
    Composer,
}

/// <summary>The shelf as cards with their first page, or as a compact list of one line a piece.</summary>
public enum LibraryView
{
    Cards,
    List,
}

/// <summary>What the library is narrowed to: words to find in a title, composer or tag, and one composer and one tag.</summary>
public sealed record LibraryQuery(string? Search, LibrarySort Sort, string? Composer, string? Tag)
{
    public static LibraryQuery All { get; } = new(null, LibrarySort.Recent, null, null);

    public bool Narrows => !string.IsNullOrWhiteSpace(Search) || Composer is not null || Tag is not null;
}

/// <summary>One piece on the shelf: its summary and the title, composer and tags its card shows.</summary>
/// <param name="TitleIsPlayers">Whether the title is the one the player set, as against the file's or its name.</param>
public sealed record LibraryEntry(ProjectSummary Summary, string Title, string? Composer, IReadOnlyList<string> Tags, bool TitleIsPlayers)
{
    /// <summary>The title, composer and tags in one folded text, for search.</summary>
    internal string Folded { get; } = LibraryShelf.Fold(string.Join('\n', [Title, Composer ?? "", .. Tags]));

    /// <summary>
    /// The card of a saved piece: its details where the summary keeps them (version 0.2), else as earlier builds titled it.
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
        return new(summary, title, details?.Composer?.Value, details?.Tags ?? [], details?.Title.Attribution.Provenance == Provenance.UserEdited);
    }
}

/// <summary>
/// The library's search, filters and order (version 0.2): pure functions over the cards, so the shelf narrows as the
/// player types without reading anything again. Search ignores letter case and accents ("fur elise" finds "Für Elise",
/// "ежи" finds "Ёжик").
/// </summary>
public static class LibraryShelf
{
    public static IReadOnlyList<LibraryEntry> Apply(IReadOnlyList<LibraryEntry> entries, LibraryQuery query, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(query);
        string[] words = Fold(query.Search ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<LibraryEntry> found = entries.Where(entry =>
            words.All(word => entry.Folded.Contains(word, StringComparison.Ordinal))
            && (query.Composer is null || Same(entry.Composer, query.Composer))
            && (query.Tag is null || entry.Tags.Any(tag => Same(tag, query.Tag))));
        StringComparer byText = Comparer(culture);
        return query.Sort switch
        {
            LibrarySort.Title => [.. found.OrderBy(entry => entry.Title, byText).ThenBy(entry => entry.Summary.Id, StringComparer.Ordinal)],
            LibrarySort.Composer => [.. found.OrderBy(entry => entry.Composer is null).ThenBy(entry => entry.Composer ?? "", byText).ThenBy(entry => entry.Title, byText).ThenBy(entry => entry.Summary.Id, StringComparer.Ordinal)],
            _ => [.. found],
        };
    }

    /// <summary>The composers on the shelf, each once whatever its letter case, in the language's order.</summary>
    public static IReadOnlyList<string> Composers(IEnumerable<LibraryEntry> entries, CultureInfo culture) =>
        Distinct(entries.Select(entry => entry.Composer).OfType<string>(), culture);

    /// <summary>The tags on the shelf, each once whatever its letter case, in the language's order.</summary>
    public static IReadOnlyList<string> Tags(IEnumerable<LibraryEntry> entries, CultureInfo culture) =>
        Distinct(entries.SelectMany(entry => entry.Tags), culture);

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

    private static bool Same(string? a, string b) => a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static StringComparer Comparer(CultureInfo culture) =>
        StringComparer.Create(culture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    private static IReadOnlyList<string> Distinct(IEnumerable<string> texts, CultureInfo culture) =>
        [.. texts.Distinct(StringComparer.OrdinalIgnoreCase).Order(Comparer(culture))];
}