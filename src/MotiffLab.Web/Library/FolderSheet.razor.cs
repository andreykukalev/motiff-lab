using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Localization;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Library;

/// <summary>
/// «Что нашлось»: lists the folder, plans it against the library, reads the files' fingerprints and PDF details in the
/// worker while the player looks (the plan is made again as they come), and starts the import with what the rows say.
/// </summary>
public partial class FolderSheet
{
    /// <summary>A copy is refused when it would leave less than this share of the browser's room free.</summary>
    private const double SpareRoom = 0.1;

    [Inject]
    private UiText Text { get; set; } = default!;

    [Inject]
    private PlayerFolders Folders { get; set; } = default!;

    [Inject]
    private ProjectStore Projects { get; set; } = default!;

    [Inject]
    private ImportJob Job { get; set; } = default!;

    [Inject]
    private FolderChanges Changes { get; set; } = default!;

    [Inject]
    private OriginalFinder Finder { get; set; } = default!;

    /// <summary>The folder the player chose.</summary>
    [Parameter, EditorRequired]
    public ChosenFolder Folder { get; set; } = default!;

    [Parameter]
    public EventCallback Closed { get; set; }

    /// <summary>«Проверить папку»: what changed since the folder was added: new files, and pieces that moved or changed.</summary>
    [Parameter]
    public bool Recheck { get; set; }

    private ElementReference _element;
    private FolderScan? _scan;
    private FoundFilter _filter = FoundFilter.All;
    private List<FoundRow> _rows = [];
    private int _listed;
    private string? _problem;
    private long? _free;
    private bool _spaceShort;
    private bool _closed;
    private DateTime _shown;
    private IReadOnlyList<LibraryOriginal> _library = [];
    private List<ChangedPiece> _changed = [];
    private int _moved;

    protected override void OnInitialized() => _filter = Recheck ? FoundFilter.New : FoundFilter.All;

    private string SpaceLine => _free is { } free
        ? Text.Format(_spaceShort ? "folder.spaceShort" : "folder.space", Size(_scan?.CopyBytes ?? 0), Size(free))
        : Text.Format("folder.spaceUnknown", Size(_scan?.CopyBytes ?? 0));

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            await _element.FocusAsync();
        }
        catch (InvalidOperationException)
        {
            // Not on the page yet.
        }

        await LookAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _closed = true;
        try
        {
            await Folders.StopDetailsAsync();
        }
        catch (Microsoft.JSInterop.JSDisconnectedException)
        {
        }
    }

    /// <summary>Lists the folder, plans it, then reads the files in the worker.</summary>
    private async Task LookAsync()
    {
        FolderListing? listing = await Folders.ListAsync(Folder.Id, count =>
        {
            _listed = count;
            _ = InvokeAsync(StateHasChanged);
        });
        if (listing is null)
        {
            _problem = Text["folder.noAccess"];
            StateHasChanged();
            return;
        }

        var scan = new FolderScan(Folder);
        _library = await LibraryAsync();
        scan.Listed(listing, _library);
        _scan = scan;
        _free = scan.Linked ? null : await Folders.FreeSpaceAsync();
        Show();
        await Folders.MarkCheckedAsync(Folder.Id);
        await ReadHeadersAsync(scan);
        await Folders.DetailsAsync(Folder.Id, scan.ToRead, details => Detailed(scan, details));
        if (Recheck && !_closed && scan.Linked && scan.Read == scan.Listing.Files.Count)
        {
            _changed = [.. await Changes.RelinkAsync(Folder.Id, scan.Files, _library)];
            foreach (ChangedPiece changed in _changed)
            {
                // Held out of the new ones until the player answers, so an unanswered question adds no second piece.
                scan.Claim(changed.Alike.Path);
            }
            _moved = Changes.Moved;
        }

        Show();
    }

    /// <summary>«Это она: открывать этот файл»: the changed file becomes the piece's PDF and is not added again.</summary>
    private async Task SameAsync(ChangedPiece changed)
    {
        if (await Projects.LoadAsync(changed.Original.ProjectId) is not { } project || await Finder.ConfirmAsync(project, changed.Alike) is null)
        {
            _scan?.Release(changed.Alike.Path);
        }

        _changed.Remove(changed);
        Show();
    }

    /// <summary>«Нет, добавить отдельной пьесой»: the file is among the new ones again.</summary>
    private void NotSame(ChangedPiece changed)
    {
        _scan?.Release(changed.Alike.Path);
        _changed.Remove(changed);
        Show();
    }

    /// <summary>The library's originals, after reading once each piece whose row was saved before rows kept them.</summary>
    private async Task<IReadOnlyList<LibraryOriginal>> LibraryAsync()
    {
        (IReadOnlyList<LibraryOriginal> originals, IReadOnlyList<string> unknown) = await Projects.LibraryOriginalsAsync();
        if (unknown.Count == 0)
        {
            return originals;
        }

        foreach (string id in unknown)
        {
            try
            {
                if (await Projects.LoadAsync(id) is { } project)
                {
                    await Projects.SaveSummaryAsync(project);
                }
            }
            catch (ProjectFileException)
            {
                // A damaged piece is left out; opening it says what is wrong.
            }
        }

        return (await Projects.LibraryOriginalsAsync()).Originals;
    }

    /// <summary>A MusicXML score's own title and composer, from the file itself: these files are small.</summary>
    private async Task ReadHeadersAsync(FolderScan scan)
    {
        foreach (ListedFile file in scan.Listing.Files.Where(static file => file.Bytes <= PieceImporter.MaxFileBytes && IsScore(file.Path)))
        {
            if (_closed)
            {
                return;
            }

            if (await Folders.ReadAsync(Folder.Id, file.Path) is { } data)
            {
                scan.Headed(file.Path, ScoreHeader.Read(data));
            }
        }
    }

    private static bool IsScore(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".musicxml" or ".xml" or ".mxl";

    private void Detailed(FolderScan scan, IReadOnlyList<FileDetails> details)
    {
        scan.Detailed(details);
        if (DateTime.UtcNow - _shown > TimeSpan.FromMilliseconds(500))
        {
            _ = InvokeAsync(Show);
        }
    }

    private void Show()
    {
        _shown = DateTime.UtcNow;
        if (_scan is { } scan)
        {
            _rows = scan.Rows(_filter, Text.Culture);
            _spaceShort = !scan.Linked && _free is { } free && scan.CopyBytes > free * (1 - SpareRoom);
        }

        StateHasChanged();
    }

    private void Filter(FoundFilter filter)
    {
        _filter = filter;
        Show();
    }

    private void Map(FolderMapping mapping)
    {
        _scan?.MapFolders(mapping);
        Show();
    }

    /// <summary>Starts the import with what the rows say and closes: the library shows the pieces as they come.</summary>
    private async Task AddAsync()
    {
        if (_scan is not { } scan || _spaceShort)
        {
            return;
        }

        await Folders.StopDetailsAsync();
        var state = new JobState(
            Folder.Id,
            Folder.Name,
            scan.Linked,
            [.. scan.ToAdd.Select(row => new JobFile(row.Path, row.Planned.File.Bytes, row.Planned.File.Sha256, row.Planned.File.Pages, row.Title, row.Composer, row.Planned.Tags, row.Planned.Playlists))],
            [.. scan.Known.Select(file => new JobKnown(file.Project!, file.File.Path, file.Tags, file.Playlists))]);
        _ = Job.StartAsync(state);
        await Closed.InvokeAsync();
    }

    private async Task CloseAsync()
    {
        await Folders.StopDetailsAsync();
        await Closed.InvokeAsync();
    }

    private async Task OnKey(KeyboardEventArgs key)
    {
        if (key.Key == "Escape")
        {
            await CloseAsync();
        }
    }

    private static string MappingKey(FolderMapping mapping) => mapping switch
    {
        FolderMapping.Tags => "folder.asTags",
        FolderMapping.Composers => "folder.asComposers",
        FolderMapping.Nothing => "folder.asNothing",
        _ => "folder.asPlaylists",
    };

    /// <summary>"1,9 ГБ", "640 МБ".</summary>
    private string Size(long bytes) => bytes >= 1L << 30
        ? Text.Format("folder.gigabytes", (bytes / (double)(1L << 30)).ToString("0.#", Text.Culture))
        : Text.Format("folder.megabytes", Math.Max(1, bytes >> 20).ToString("N0", Text.Culture));
}