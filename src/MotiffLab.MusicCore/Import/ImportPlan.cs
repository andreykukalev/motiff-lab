using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Import;

/// <summary>
/// A file found in the folder the player chose (version 0.5, «Импорт папки»), as the app read it without changing it:
/// where it is inside the folder, its size, and what the app worked out so far. The app only reads the player's files.
/// </summary>
/// <param name="Path">Inside the chosen folder, "/" between the folders: "Бах/ХТК/Prelude 1.pdf". Kept in Unicode's composed form.</param>
/// <param name="Bytes">The file's size.</param>
public sealed record FoundFile(string Path, long Bytes)
{
    public string Path { get; } = PathOf(Path);

    public long Bytes { get; } = Bytes >= 0 ? Bytes : throw new ArgumentOutOfRangeException(nameof(Bytes));

    /// <summary>The SHA-256 of its bytes as <see cref="PieceOriginal.Fingerprint"/> gives it; null until the app worked it out.</summary>
    public string? Sha256
    {
        get;
        init => field = value is null || (value.Length == 64 && value.All(char.IsAsciiHexDigitLower)) ? value : throw new ArgumentException("A fingerprint is 64 lower-case hex digits.", nameof(Sha256));
    }

    /// <summary>A PDF's pages; null when not known.</summary>
    public int? Pages { get; init => field = value is null or > 0 ? value : throw new ArgumentOutOfRangeException(nameof(Pages)); }

    /// <summary>The Title in the PDF's own information, as written; null when it has none. Not suggested (version 0.5.1).</summary>
    public string? PdfTitle { get; init; }

    /// <summary>The Author in the PDF's own information, as written; null when it has none. Not suggested (version 0.5.1).</summary>
    public string? PdfAuthor { get; init; }

    /// <summary>The title and composer a MusicXML score is headed with (<see cref="ScoreHeader.Read"/>); none for other files.</summary>
    public ScoreHeader Header { get; init; } = ScoreHeader.None;

    /// <summary>The app could not read it: no permission, a PDF with a password or broken, a file not downloaded.</summary>
    public bool Unreadable { get; init; }

    /// <summary>The file's own name: "Prelude 1.pdf".</summary>
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];

    /// <summary>The folders it is in inside the chosen one: "Бах/ХТК"; empty for a file right in the chosen folder.</summary>
    public string Folder => Path.LastIndexOf('/') is var slash and >= 0 ? Path[..slash] : string.Empty;

    /// <summary>What it is by its extension, as the app adds such a file: a PDF, MIDI or MusicXML; null for any other file.</summary>
    public OriginalKind? Kind => System.IO.Path.GetExtension(Name).Equals(PdfDocument.Extension, StringComparison.OrdinalIgnoreCase) ? OriginalKind.Pdf : PieceOriginals.FileKind(Name);

    /// <summary>A path inside the chosen folder, composed, "/" between the folders, with no empty, "." or ".." folder.</summary>
    public static string PathOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string[] parts = NameText.Composed(path).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts.All(static part => part is not ("." or "..") && part.Trim().Length > 0)
            ? string.Join('/', parts)
            : throw new ArgumentException("A path inside the folder names a file in it.", nameof(path));
    }
}

/// <summary>
/// One original the library has, for the plan to know a file it has already (by fingerprint) and a piece it may be: the
/// piece's id, the original's id and fingerprint, and the piece's title and composer as its card shows them.
/// </summary>
public sealed record LibraryOriginal(string ProjectId, string OriginalId, string? Sha256)
{
    /// <summary>The original's size in bytes; null when not known.</summary>
    public long? Bytes { get; init; }

    /// <summary>A PDF's pages; null when not known.</summary>
    public int? Pages { get; init; }

    /// <summary>The original's file name; null when not known.</summary>
    public string? FileName { get; init; }

    /// <summary>Where in the chosen folder the file was, "Бах/ХТК"; null when it did not come from a folder.</summary>
    public string? Folder { get; init; }

    /// <summary>The piece's title as its card shows it; null when not known.</summary>
    public string? Title { get; init; }

    /// <summary>The piece's composer as its card shows it; null when none.</summary>
    public string? Composer { get; init; }

    /// <summary>The originals of a piece, each with the piece's title and composer.</summary>
    public static IEnumerable<LibraryOriginal> Of(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        PieceDetails details = project.Details;
        return project.Originals.Select(original => new LibraryOriginal(project.Id, original.Id, original.Sha256)
        {
            Bytes = original.Bytes,
            Pages = original.Pages,
            FileName = original.FileName,
            Folder = original.Folder,
            Title = details.Title.Value,
            Composer = details.Composer?.Value,
        });
    }
}

/// <summary>What the folders become in the library, the player's choice on «Что нашлось».</summary>
public enum FolderMapping
{
    /// <summary>Each folder with pieces is a playlist of its name (the default): one piece can be on several.</summary>
    Playlists,

    /// <summary>Each folder a piece is in is a tag of it.</summary>
    Tags,

    /// <summary>The folder right inside the chosen one is the composer of the pieces in it.</summary>
    Composers,

    /// <summary>The folders are not kept: one library, as with files added one by one.</summary>
    Nothing,
}

/// <summary>What the import does with a found file.</summary>
public enum ImportStatus
{
    /// <summary>A piece to add.</summary>
    New,

    /// <summary>The library has a piece with an original of the same fingerprint: nothing is added.</summary>
    InLibrary,

    /// <summary>The same fingerprint as a file before it in this folder: one piece, listed in both folders.</summary>
    Duplicate,

    /// <summary>Not a PDF, MIDI or MusicXML file.</summary>
    Unsupported,

    /// <summary>Larger than a copy in the piece can be; a PDF left in the folder (linked) has no such limit.</summary>
    TooBig,

    /// <summary>The app could not read it, or it is empty.</summary>
    Unreadable,
}

/// <summary>Where a suggested title or composer comes from, from the most trusted.</summary>
public enum SuggestionSource
{
    /// <summary>The folder mapping the player chose: folders are composers.</summary>
    FolderChoice,

    /// <summary>The MusicXML score's own header.</summary>
    ScoreHeader,

    /// <summary>The PDF's own information, Title or Author. No longer suggested (version 0.5.1); kept so the facade's names stay.</summary>
    PdfInfo,

    /// <summary>The file's name.</summary>
    FileName,

    /// <summary>A folder's name, or the other pieces in the folder.</summary>
    Folder,
}

/// <summary>A suggested title or composer: a guess (provenance inferred) until the player confirms it, with its source.</summary>
public sealed record Suggestion(string Value, SuggestionSource Source, double Confidence)
{
    public Attribution Attribution => Attribution.Inferred(Confidence);
}

/// <summary>How the import plan is made: what the folders become, and whether the files stay in the folder (linked).</summary>
/// <param name="Linked">The files stay where they are and the pieces link them; else the pieces keep copies.</param>
public sealed record ImportOptions(FolderMapping Folders = FolderMapping.Playlists, bool Linked = false);

/// <summary>One found file in the plan: what the import does with it and what the piece is suggested to be.</summary>
public sealed record PlannedFile(FoundFile File, ImportStatus Status)
{
    /// <summary>For a duplicate, the path of the file before it with the same fingerprint.</summary>
    public string? SameAs { get; init; }

    /// <summary>For a file the library has, the piece that has it.</summary>
    public string? Project { get; init; }

    /// <summary>New only until its fingerprint is known: a file or original of its size exists, so it may be the same.</summary>
    public bool Provisional { get; init; }

    public Suggestion? Title { get; init; }

    public Suggestion? Composer { get; init; }

    /// <summary>The playlists the piece goes on (folders as playlists), its duplicates' folders included.</summary>
    public IReadOnlyList<string> Playlists { get; init; } = [];

    /// <summary>The tags the piece gets (folders as tags), its duplicates' folders included.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// A PDF that is perhaps several pieces in one score, a guess: more than <see cref="LibraryImport.CollectionPages"/>
    /// pages, or a name that says so (<see cref="FileNameDetails.NamesCollection"/>).
    /// </summary>
    public bool PossibleCollection { get; init; }

    /// <summary>Other files of the plan with the same title and composer, by path: possibly the same piece, never merged.</summary>
    public IReadOnlyList<string> PossiblySameFiles { get; init; } = [];

    /// <summary>Pieces of the library with the same title and composer, by id: possibly the same piece, never merged.</summary>
    public IReadOnlyList<string> PossiblySamePieces { get; init; } = [];
}

/// <summary>The import plan: every found file in path order, and the files to fingerprint before the plan is sure.</summary>
public sealed record ImportPlan(IReadOnlyList<PlannedFile> Files, IReadOnlyList<string> ToFingerprint)
{
    public int Found => Files.Count;

    public int New => Count(ImportStatus.New);

    public int InLibrary => Count(ImportStatus.InLibrary);

    public int Duplicates => Count(ImportStatus.Duplicate);

    /// <summary>Not suitable: unsupported, too big or unreadable.</summary>
    public int Unsuitable => Files.Count(static file => file.Status is ImportStatus.Unsupported or ImportStatus.TooBig or ImportStatus.Unreadable);

    private int Count(ImportStatus status) => Files.Count(file => file.Status == status);
}