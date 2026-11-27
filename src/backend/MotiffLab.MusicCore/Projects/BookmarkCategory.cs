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
    internal const string PerformanceId = "performance";
    public const string StructureId = "structure";
    internal const string TeacherId = "teacher";

    /// <summary>
    /// The pieces layer (version 0.5, <see cref="PieceLayer"/>): a reserved id, written with a name in the player's language
    /// so that builds before 0.5 read it as a category of the player's own and keep it; this build names it by its id.
    /// </summary>
    public const string PiecesId = "pieces";

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

    /// <summary>The name shown: the pieces layer's in the language, the player's, or the built-in name in the language.</summary>
    public string NameIn(Words words) => Id == PiecesId ? PiecesNameIn(words) : Name ?? Id switch
    {
        PractiseId => words.T("Practise", "Поиграть"),
        PerformanceId => words.T("Performance", "Исполнение"),
        StructureId => words.T("Structure", "Структура"),
        _ => words.T("Teacher", "Учитель"),
    };

    /// <summary>"Pieces"; "Пьесы": the pieces layer's name.</summary>
    public static string PiecesNameIn(Words words) => words.T("Pieces", "Пьесы");

    /// <summary>The category under another name; blank gives a built-in category its own name back.</summary>
    public BookmarkCategory Rename(string? name) => new(Id, name, Ink);
}