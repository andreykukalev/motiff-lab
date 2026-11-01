using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// A category of bookmarks (0018, section 5): four come with the app, Practise, Performance, Structure and
/// Teacher, each in its own ink and renamable; the player adds their own in any of the four inks.
/// </summary>
/// <param name="Id">"practise", "performance", "structure" or "teacher" for the built-in ones; "c_0001"… for the player's.</param>
/// <param name="Name">The player's name for it, trimmed; null for a built-in category under its own name (<see cref="NameIn"/>).</param>
public sealed record BookmarkCategory(string Id, string? Name, InkColour Ink)
{
    public const string PractiseId = "practise";
    public const string PerformanceId = "performance";
    public const string StructureId = "structure";
    public const string TeacherId = "teacher";

    /// <summary>The four categories every project starts with, in the order the Contents drawer lists them.</summary>
    public static IReadOnlyList<BookmarkCategory> BuiltIn { get; } =
    [
        new(PractiseId, null, InkColour.Carmine),
        new(PerformanceId, null, InkColour.Forest),
        new(StructureId, null, InkColour.Graphite),
        new(TeacherId, null, InkColour.Ochre),
    ];

    public string Id { get; } = !string.IsNullOrWhiteSpace(Id) ? Id : throw new ArgumentException("A category needs an id.", nameof(Id));

    public string? Name { get; } = string.IsNullOrWhiteSpace(Name)
        ? IsBuiltInId(Id) ? null : throw new ArgumentException("A category of the player's own needs a name.", nameof(Name))
        : Name.Trim();

    public InkColour Ink { get; } = Ink != InkColour.Amber ? Ink : throw new ArgumentException("Bookmarks are written in a pencil ink, not the highlighter.", nameof(Ink));

    public bool IsBuiltIn => IsBuiltInId(Id);

    public static bool IsBuiltInId(string id) => id is PractiseId or PerformanceId or StructureId or TeacherId;

    /// <summary>The name shown: the player's, or the built-in name in the language.</summary>
    public string NameIn(Words words) => Name ?? Id switch
    {
        PractiseId => words.T("Practise", "Поиграть"),
        PerformanceId => words.T("Performance", "Исполнение"),
        StructureId => words.T("Structure", "Структура"),
        _ => words.T("Teacher", "Учитель"),
    };

    /// <summary>The category under another name; blank gives a built-in category its own name back.</summary>
    public BookmarkCategory Rename(string? name) => new(Id, name, Ink);
}

/// <summary>
/// A bookmark (0018, section 5): a titled stretch of the piece in a category, the player's own entry in
/// the Contents of the piece. Bookmarks replace the Tag mark of 0013.
/// </summary>
/// <remarks>
/// A bookmark is kept as its span, so it stays put when the layout or the phrase level changes.
/// One made from a detected phrase (<paramref name="FromPhrase"/>) follows that phrase through merges and
/// splits: after the phrases change it takes the span of the phrase its onset lies in
/// (<see cref="Follow"/>). The order of a project's bookmarks is the player's order; the score order is
/// by span.
/// </remarks>
/// <param name="Title">The title, trimmed and never blank; pre-filled with the bar range when made.</param>
public sealed record Bookmark(string Id, string Title, string CategoryId, TickSpan Span, bool FromPhrase, DateTimeOffset CreatedAt)
{
    public string Id { get; } = !string.IsNullOrWhiteSpace(Id) ? Id : throw new ArgumentException("A bookmark needs an id.", nameof(Id));

    public string Title { get; init; } = !string.IsNullOrWhiteSpace(Title) ? Title.Trim() : throw new ArgumentException("A bookmark needs a title.", nameof(Title));

    public string CategoryId { get; init; } = !string.IsNullOrWhiteSpace(CategoryId) ? CategoryId : throw new ArgumentException("A bookmark needs a category.", nameof(CategoryId));

    /// <summary>
    /// The bookmark where it belongs after the phrases changed: a bookmark made from a phrase takes the
    /// span of the phrase its onset lies in, so a merge widens it and a split keeps it on the first part.
    /// Any other bookmark, or one whose onset lies in no phrase, stays as it is.
    /// </summary>
    public Bookmark Follow(IEnumerable<TickSpan> phrases)
    {
        ArgumentNullException.ThrowIfNull(phrases);
        if (!FromPhrase)
        {
            return this;
        }

        TickSpan? home = phrases.Cast<TickSpan?>().FirstOrDefault(phrase => phrase!.Value.Contains(Span.Onset));
        return home is { } span && span != Span ? this with { Span = span } : this;
    }
}