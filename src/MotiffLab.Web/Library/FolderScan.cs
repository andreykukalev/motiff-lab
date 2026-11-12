using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Web.Library;

/// <summary>The filters of «Что нашлось»: its counters, each a filter of the list below them.</summary>
public enum FoundFilter
{
    All,
    New,
    InLibrary,
    Duplicates,
    Unsuitable,
}

/// <summary>A row of «Что нашлось»: the planned file, and the title and composer it will be added with.</summary>
/// <param name="TitleEdited">The player wrote the title in the row; else it is the suggestion, shown grey with its source.</param>
public sealed record FoundRow(PlannedFile Planned, string Title, Suggestion? TitleSuggestion, bool TitleEdited, string? Composer, Suggestion? ComposerSuggestion, bool ComposerEdited, bool Included)
{
    public string Path => Planned.File.Path;
}

/// <summary>
/// A look through the player's folder (version 0.5, «Импорт папки»), as «Что нашлось» shows it: what was listed, what the
/// worker read of each file, and the plan music-core makes of it (<see cref="LibraryImport.Plan"/>) with the player's
/// choices: what the folders become, the titles and composers they changed, and the files they left out. A suggestion
/// becomes the piece's metadata only when the player adds with it in the row; the plan itself is music-core's.
/// </summary>
public sealed class FolderScan(ChosenFolder folder)
{
    private readonly Dictionary<string, FileDetails> _details = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScoreHeader> _headers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _titles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _composers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _left = new(StringComparer.Ordinal);
    private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);
    private IReadOnlyList<LibraryOriginal> _library = [];

    public ChosenFolder Folder { get; } = folder ?? throw new ArgumentNullException(nameof(folder));

    /// <summary>The files are linked where they are (the browser keeps the folder); else each piece keeps a copy.</summary>
    public bool Linked => Folder.Kept;

    public FolderListing Listing { get; private set; } = new([], 0);

    public FolderMapping Mapping { get; private set; } = FolderMapping.Playlists;

    public ImportPlan Plan { get; private set; } = new([], []);

    /// <summary>How many files the worker has read so far.</summary>
    public int Read => _details.Count;

    /// <summary>The files whose fingerprint or PDF details are still to read.</summary>
    public IReadOnlyList<(string Path, bool Pdf)> ToRead =>
        [.. Listing.Files.Where(file => !_details.ContainsKey(file.Path)).Select(static file => (file.Path, IsPdf(file.Path)))];

    public void Listed(FolderListing listing, IReadOnlyList<LibraryOriginal> library)
    {
        Listing = listing ?? throw new ArgumentNullException(nameof(listing));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        Replan();
    }

    /// <summary>What the worker read of some files; the plan is made again.</summary>
    public void Detailed(IEnumerable<FileDetails> details)
    {
        ArgumentNullException.ThrowIfNull(details);
        foreach (FileDetails each in details)
        {
            _details[each.Path] = each;
        }

        Replan();
    }

    /// <summary>A MusicXML score's own title and composer, read here from its first bytes.</summary>
    public void Headed(string path, ScoreHeader header) => _headers[path] = header;

    public void MapFolders(FolderMapping mapping)
    {
        Mapping = mapping;
        Replan();
    }

    /// <summary>The player's own title for a file; empty puts the suggestion back.</summary>
    public void WriteTitle(string path, string? title) => Write(_titles, path, title);

    public void WriteComposer(string path, string? composer) => Write(_composers, path, composer);

    /// <summary>Leaves a new file out of the import, or takes it back in.</summary>
    public void Include(string path, bool included)
    {
        if (included)
        {
            _left.Remove(path);
        }
        else
        {
            _left.Add(path);
        }
    }

    /// <summary>A new file that is possibly a piece of the library, until the player answers, or is one («Это она»): it is not added.</summary>
    public void Claim(string path) => _claimed.Add(path);

    /// <summary>The file is among the new ones again («Нет, добавить отдельной пьесой»).</summary>
    public void Release(string path) => _claimed.Remove(path);

    /// <summary>The new files, less those the player said are pieces of the library.</summary>
    public int NewCount => Plan.Files.Count(file => file.Status == ImportStatus.New && !_claimed.Contains(file.File.Path));

    /// <summary>Why a file cannot be added, in the words' keys: a locked PDF, not a PDF, too big, not notes, not readable.</summary>
    public string? Problem(PlannedFile planned)
    {
        ArgumentNullException.ThrowIfNull(planned);
        FileDetails? details = _details.GetValueOrDefault(planned.File.Path);
        return planned.Status switch
        {
            ImportStatus.Unsupported => "folder.problemKind",
            ImportStatus.TooBig => "folder.problemBig",
            ImportStatus.Unreadable when details?.Info?.Locked == true => "folder.problemLocked",
            ImportStatus.Unreadable when details?.Info?.Damaged == true => "folder.problemDamaged",
            ImportStatus.Unreadable => "folder.problemUnreadable",
            _ => null,
        };
    }

    /// <summary>The rows a filter shows, by title from A.</summary>
    public List<FoundRow> Rows(FoundFilter filter, System.Globalization.CultureInfo culture)
    {
        StringComparer byTitle = StringComparer.Create(culture, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace);
        return [.. Plan.Files.Where(file => Shows(filter, file.Status) && !_claimed.Contains(file.File.Path)).Select(Row).OrderBy(row => row.Title, byTitle).ThenBy(row => row.Path, StringComparer.Ordinal)];
    }

    /// <summary>The new files the player keeps in: what «Добавить» adds.</summary>
    public IReadOnlyList<FoundRow> ToAdd => [.. Plan.Files.Where(file => file.Status == ImportStatus.New && !_claimed.Contains(file.File.Path)).Select(Row).Where(static row => row.Included)];

    /// <summary>The files the library has already, with the piece that has each: linked on this device as they are.</summary>
    public IReadOnlyList<PlannedFile> Known => [.. Plan.Files.Where(static file => file.Status == ImportStatus.InLibrary && file.Project is not null)];

    /// <summary>What a copy of the new files takes, in bytes: for the estimate where the files cannot be linked.</summary>
    public long CopyBytes => ToAdd.Sum(static row => row.Planned.File.Bytes);

    /// <summary>The listed files as music-core sees them, with what the worker read.</summary>
    public IReadOnlyList<FoundFile> Files => [.. Listing.Files.Select(Found)];

    public FileDetails? DetailsOf(string path) => _details.GetValueOrDefault(path);

    public static bool IsPdf(string path) => path.EndsWith(PdfDocument.Extension, StringComparison.OrdinalIgnoreCase);

    private FoundRow Row(PlannedFile planned)
    {
        string? title = _titles.GetValueOrDefault(planned.File.Path);
        string? composer = _composers.GetValueOrDefault(planned.File.Path);
        return new FoundRow(
            planned,
            title ?? planned.Title?.Value ?? PieceDetails.TitleOf(planned.File.Name),
            planned.Title,
            title is not null,
            composer ?? planned.Composer?.Value,
            planned.Composer,
            composer is not null,
            planned.Status == ImportStatus.New && !_left.Contains(planned.File.Path));
    }

    private static bool Shows(FoundFilter filter, ImportStatus status) => filter switch
    {
        FoundFilter.New => status == ImportStatus.New,
        FoundFilter.InLibrary => status == ImportStatus.InLibrary,
        FoundFilter.Duplicates => status == ImportStatus.Duplicate,
        FoundFilter.Unsuitable => status is ImportStatus.Unsupported or ImportStatus.TooBig or ImportStatus.Unreadable,
        _ => true,
    };

    private static void Write(Dictionary<string, string> edits, string path, string? text)
    {
        if (PieceMetadata.Clean(text) is { } clean)
        {
            edits[path] = clean;
        }
        else
        {
            edits.Remove(path);
        }
    }

    private void Replan() => Plan = LibraryImport.Plan(Listing.Files.Select(Found), _library, new ImportOptions(Mapping, Linked));

    private FoundFile Found(ListedFile listed)
    {
        FileDetails? details = _details.GetValueOrDefault(listed.Path);
        PdfDetails? info = details?.Info;
        return new FoundFile(listed.Path, listed.Bytes)
        {
            Sha256 = details?.Sha256,
            Pages = info?.Pages is > 0 ? info.Pages : null,
            PdfTitle = info?.Title,
            PdfAuthor = info?.Author,
            Header = _headers.GetValueOrDefault(listed.Path) ?? ScoreHeader.None,
            Unreadable = details is { Unreadable: true } || info is { Locked: true } or { Damaged: true },
        };
    }
}