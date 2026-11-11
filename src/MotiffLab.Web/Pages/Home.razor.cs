using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Account;
using MotiffLab.Web.Import;
using MotiffLab.Web.Localization;
using MotiffLab.Web.Projects;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The library (0018 section 9): the pieces saved on this device as cards, each with its first page, its size, its ideas
/// and Check count and its latest resume point; Resume opens that point, the first page opens a new one; the resume
/// points are listed, named and removed in a sheet; Add piece opens a file on the device or leads to the scan and
/// recording routes.
/// </summary>
public partial class Home
{
    private enum Sheet
    {
        None,
        AddPiece,
        Resumes,
        Account,
        Edit,
    }

    /// <summary>The menu id of the library's own overflow; a card's menu is named by its project id.</summary>
    private const string LibraryMenu = "library";

    /// <summary>How long a press on a card lasts before it lists the resume points.</summary>
    private static readonly TimeSpan s_longPress = TimeSpan.FromMilliseconds(550);

    [Inject]
    private ProjectStore Projects { get; set; } = default!;

    [Inject]
    private CurrentPiece CurrentPiece { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private UiText Text { get; set; } = default!;

    [Inject]
    private OnlineProjects Online { get; set; } = default!;

    [Inject]
    private CloudAccount Account { get; set; } = default!;

    [Inject]
    private ScanService Scans { get; set; } = default!;

    [Inject]
    private MotiffLab.Web.Settings.DeviceSettings SettingsStore { get; set; } = default!;

    private IReadOnlyList<ProjectSummary>? _summaries;
    private IReadOnlyDictionary<string, PieceCover> _covers = new Dictionary<string, PieceCover>();
    private IReadOnlyList<ProjectSummary>? _onlineOnly;
    private bool _nearlyFull;
    private string? _message;
    private string? _player;

    /// <summary>What scanning and transcription need before they work here, or null when they are ready.</summary>
    private string? _serverNote;

    private string? _menu;
    private string? _confirmDelete;
    private bool _opening;

    private Sheet _sheet;
    private ElementReference _sheetElement;

    /// <summary>Whether the Settings sheet is open over the library (0018 section 10).</summary>
    private bool _settingsOpen;

    /// <summary>Whether this is the first run, which asks for a preset before the library; null until the settings are read.</summary>
    private bool? _firstRun;
    private bool _focusSheet;

    private Project? _resumeProject;
    private ImportedPiece? _resumePiece;
    private string? _resumeTitle;
    private string? _confirmRemove;
    private string? _newName;

    private CancellationTokenSource? _press;
    private bool _pressed;

    /// <summary>Scanning and transcription go to Motiff Lab's server, which is Pro, unless the player set a server of their own.</summary>
    private bool UsesOurServer => Scans.Address is null || Scans.IsHosted;

    protected override void OnInitialized() => Text.Changed += OnChanged;

    public async ValueTask DisposeAsync()
    {
        Text.Changed -= OnChanged;
        _press?.Cancel();
        _press?.Dispose();
        try
        {
            await Projects.ReleaseCoversAsync();
        }
        catch (Microsoft.JSInterop.JSDisconnectedException)
        {
        }
    }

    private void OnChanged()
    {
        _serverNote = ServerNote();
        Shelve();
        _ = InvokeAsync(StateHasChanged);
    }

    protected override async Task OnInitializedAsync()
    {
        await SettingsStore.LoadAsync();
        _firstRun = !SettingsStore.PresetChosen;
        await Shelf.LoadViewAsync();
        await Scans.LoadAsync();
        _player = await Account.PlayerAsync();
        _serverNote = ServerNote();
    }

    /// <summary>The same states the scan and recording screens explain: no server set, Motiff Lab's server turned off, or not signed in for it.</summary>
    private string? ServerNote() => Scans.Address is null
        ? Text[Scans.HostedAddress is null ? "home.needsServer" : "home.scanTurnedOff"]
        : Scans.IsHosted && _player is null ? Text["home.needsSignIn"] : null;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RefreshAsync();
            StateHasChanged();
            await CompleteRowsAsync();
            StateHasChanged();
        }

        if (_focusSheet && _sheet != Sheet.None)
        {
            _focusSheet = false;
            try
            {
                await _sheetElement.FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // Not on the page yet.
            }
        }
    }

    private async Task RefreshAsync()
    {
        // One piece, one card (testing item 16): a PDF whose notes were read into a separate piece, as between 2026-11-05 and
        // 2026-11-07, is reached from its notes with PDF | Notes, so only the notes' card shows.
        IReadOnlyList<ProjectSummary> all = await Projects.ListAsync();
        HashSet<string> readFrom = [.. all.Select(summary => summary.ReadFrom).OfType<string>()];
        _summaries = [.. all.Where(summary => summary.Origin != PieceOrigin.Pdf || !readFrom.Contains(summary.Id))];
        _covers = await Projects.CoversAsync();
        Shelve();
        _nearlyFull = await Projects.IsNearlyFullAsync();
        _onlineOnly = null;
        if (await Online.IsOnAsync())
        {
            // Offline or signed out, the list is just left out: this screen is about the projects here.
            (IReadOnlyList<ProjectSummary>? online, _) = await Online.ListAsync();
            HashSet<string> here = [.. _summaries.Select(summary => summary.Id)];
            _onlineOnly = online?.Where(summary => !here.Contains(summary.Id)).ToList();
        }
    }

    /// <summary>"Beethoven · 4 pages · 16 phrases", "From a recording · 5 pages · 9 phrases": what is known before the piece opens.</summary>
    private string MetaLine(ProjectSummary summary, PieceCover? cover, string? composer)
    {
        List<string> parts = [];
        if (composer is not null)
        {
            parts.Add(composer);
        }

        if (summary.Origin != PieceOrigin.File)
        {
            parts.Add(Text[OriginKey(summary.Origin)]);
        }
        else if (composer is null && cover?.Lines is [var first, ..])
        {
            parts.Add(first);
        }

        if (cover is not null)
        {
            parts.Add(Text.Count("library.pages", cover.Pages));
            if (cover.PhraseStarts.Count > 0)
            {
                parts.Add(Text.Count("library.phrases", cover.PhraseStarts.Count));
            }
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : Text.Format("home.lastOpened", Day(summary.LastOpened));
    }

    /// <summary>The resume point's name, or the passage it was on, or that it is where the player left off.</summary>
    private string ResumeName(ResumeSummary latest, PieceCover? cover) =>
        latest.Name ?? (latest is { From: { } from, To: { } to } && cover is not null ? Capital(BarsText(cover.BarAt(from), cover.BarAt(Math.Max(from, to - 1)))) : Text["project.unnamedResume"]);

    /// <summary>"Bars 12–26" as a name, from "bars 12–26" as a phrase in a sentence.</summary>
    private string Capital(string text) => text.Length == 0 ? text : string.Concat(char.ToUpper(text[0], Text.Culture).ToString(), text.AsSpan(1));

    /// <summary>"yesterday · p. 1 · phrase 3".</summary>
    private string ResumeLine(ResumeSummary latest, PieceCover? cover)
    {
        List<string> parts = [Day(latest.LastActive)];
        if (cover is not null)
        {
            long at = latest.From ?? latest.At;
            parts.Add(Text.Format("contents.page", cover.PageAt(at)));
            if (latest.From is not null && cover.PhraseAt(at) is { } phrase)
            {
                parts.Add(Text.Format("contents.pagePhrase", phrase));
            }
        }

        return string.Join(" · ", parts);
    }

    /// <summary>"today", "yesterday", "3 days ago", then the date.</summary>
    private string Day(DateTimeOffset time)
    {
        DateTime day = time.ToLocalTime().Date;
        int days = (DateTime.Now.Date - day).Days;
        return days switch
        {
            <= 0 => Text["library.today"],
            1 => Text["library.yesterday"],
            < 7 => Text.Count("library.daysAgo", days),
            _ => day.ToString("d MMMM", Text.Culture),
        };
    }

    private string BarsText(int first, int last) => (first, last) switch
    {
        (0, 0) => Text["bars.pickup"],
        (0, 1) => Text["bars.pickupAndOne"],
        (0, _) => Text.Format("bars.pickupAndRange", last),
        _ when first == last => Text.Format("bars.one", first),
        _ => Text.Format("bars.range", first, last),
    };

    private void ToggleMenu(string? menu)
    {
        _menu = _menu == menu ? null : menu;
        _confirmDelete = null;
    }

    private void PressStart(ProjectSummary summary)
    {
        _press?.Cancel();
        _press?.Dispose();
        _press = new CancellationTokenSource();
        _pressed = false;
        CancellationToken token = _press.Token;
        _ = Task.Delay(s_longPress, token).ContinueWith(
            _ => InvokeAsync(async () =>
            {
                _pressed = true;
                await OpenResumesAsync(summary);
                StateHasChanged();
            }),
            token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private void PressEnd() => _press?.Cancel();

    /// <summary>A tap on the card resumes the piece; the tap that ends a long press does not.</summary>
    private Task CardClickedAsync(ProjectSummary summary)
    {
        if (_pressed)
        {
            _pressed = false;
            return Task.CompletedTask;
        }

        return _menu is null && _sheet == Sheet.None ? ResumeAsync(summary.Id, null) : Task.CompletedTask;
    }

    /// <summary>Opens the piece at a resume point: the one named, else the latest, else a new one; with Piece DNA over it when asked.</summary>
    private async Task ResumeAsync(string id, string? resumeId, bool dna = false)
    {
        if (_opening)
        {
            return;
        }

        _opening = true;
        _message = null;
        try
        {
            if (!await CurrentPiece.OpenSavedAsync(id))
            {
                _message = Text["project.gone"];
                return;
            }

            Project project = CurrentPiece.Project!;
            string? point = resumeId ?? project.RecentResumePoints.FirstOrDefault()?.Id;
            Navigation.NavigateTo(CurrentPiece.WorkspaceUrl(project, point) + (dna ? "&view=dna" : ""));
        }
        catch (ProjectFileException exception)
        {
            _message = exception.Message;
        }
        finally
        {
            _opening = false;
        }
    }

    /// <summary>Opens the piece afresh at a new resume point on page 1, named by the player's words when they gave any.</summary>
    private async Task OpenFreshAsync(string id, string? name)
    {
        if (_opening)
        {
            return;
        }

        _opening = true;
        _message = null;
        try
        {
            if (!await CurrentPiece.OpenSavedAsync(id))
            {
                _message = Text["project.gone"];
                return;
            }

            Project project = CurrentPiece.Project!;
            if (!project.HasNotes)
            {
                // A PDF has no resume points: page 1 is where it opens afresh.
                Navigation.NavigateTo(CurrentPiece.WorkspaceUrl(project) + "&page=1");
                return;
            }

            ResumePoint point = ResumePoint.Start(project.NextResumePointId(), DateTimeOffset.Now, name);
            project = project.AddResumePoint(point);
            await Projects.SaveAsync(project);
            CurrentPiece.Saved(project);
            Navigation.NavigateTo(CurrentPiece.WorkspaceUrl(project, point.Id));
        }
        catch (ProjectFileException exception)
        {
            _message = exception.Message;
        }
        finally
        {
            _opening = false;
        }
    }

    private void OpenAddPiece()
    {
        _menu = null;
        _sheet = Sheet.AddPiece;
    }

    /// <summary>Lists a piece's resume points: the project is read, and its piece imported again for the bars of each passage.</summary>
    private async Task OpenResumesAsync(ProjectSummary summary)
    {
        if (summary.Origin == PieceOrigin.Pdf)
        {
            return;
        }

        _menu = null;
        _message = null;
        _confirmRemove = null;
        _newName = null;
        try
        {
            _resumeProject = await Projects.LoadAsync(summary.Id);
            if (_resumeProject is null)
            {
                _message = Text["project.gone"];
                return;
            }

            _resumeTitle = LibraryEntry.Of(summary, _covers.GetValueOrDefault(summary.Id)).Title;
            _resumePiece = PieceImporter.Import(_resumeProject.FileName, _resumeProject.Source).Piece;
            _sheet = Sheet.Resumes;
            _focusSheet = true;
        }
        catch (ProjectFileException exception)
        {
            _message = exception.Message;
        }
    }

    private void OpenAccount()
    {
        _menu = null;
        _sheet = Sheet.Account;
        _focusSheet = true;
    }

    private void CloseSheet()
    {
        _sheet = Sheet.None;
        _editing = null;
        _resumeProject = null;
        _resumePiece = null;
    }

    private void OnSheetKey(KeyboardEventArgs key)
    {
        if (key.Key == "Escape")
        {
            CloseSheet();
        }
    }

    private string PointFallbackName(ResumePoint point) =>
        point.Passage is { } passage ? Capital(PassageBars(passage.Span)) : Text["project.unnamedResume"];

    /// <summary>"bars 9–24 · 60% · yesterday".</summary>
    private string PointLine(ResumePoint point) =>
        $"{(point.Passage is { } passage ? PassageBars(passage.Span) : Text["project.noPassage"])} · {point.TempoPercent}% · {Day(point.LastActive)}";

    private string PassageBars(TickSpan span) => _resumePiece is { } piece
        ? BarsText(piece.Meter.BarAt(span.Onset).Number, piece.Meter.BarAt(new Tick(Math.Max(span.Onset.Value, span.End.Value - 1))).Number)
        : "";

    private async Task RenameAsync(ResumePoint point, string? name)
    {
        if (_resumeProject is not { } project || project.FindResumePoint(point.Id) is null)
        {
            return;
        }

        _resumeProject = project.ChangeResumePoint(point.Id, kept => kept.Rename(name));
        await Projects.SaveAsync(_resumeProject);
        await RefreshAsync();
    }

    private async Task RemoveAsync(ResumePoint point)
    {
        _confirmRemove = null;
        if (_resumeProject is not { } project)
        {
            return;
        }

        _resumeProject = project.RemoveResumePoint(point.Id);
        await Projects.SaveAsync(_resumeProject);
        await RefreshAsync();
    }

    private async Task GetHereAsync(string id)
    {
        _message = null;
        (Project? project, string? problem) = await Online.DownloadAsync(id);
        if (project is null)
        {
            _message = problem;
            return;
        }

        await Projects.SaveHereAsync(project);
        await RefreshAsync();
    }

    private async Task ExportAsync(string id)
    {
        _menu = null;
        _message = null;
        try
        {
            if (await Projects.LoadAsync(id) is { } project)
            {
                await Projects.ExportAsync(project);
            }
        }
        catch (ProjectFileException exception)
        {
            _message = exception.Message;
        }
    }

    private async Task DeleteAsync(string id)
    {
        _confirmDelete = null;
        _menu = null;
        await Projects.DeleteAsync(id);
        await RefreshAsync();
    }

    /// <summary>Re-imports an exported project (AC-S1-20). It replaces a saved project with the same id.</summary>
    private async Task ImportProjectAsync(InputFileChangeEventArgs args)
    {
        _menu = null;
        _message = null;
        try
        {
            // A project holds a source of at most 50 MB, which base64 and the JSON around it grow by about a third.
            await using Stream stream = args.File.OpenReadStream(70 * 1024 * 1024);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            Project file = ProjectFile.Read(memory.ToArray());
            // A different piece that carries the id of a saved one is saved beside it, never over it (V8); the player's
            // own export coming back replaces itself (AC-S1-20).
            (Project? saved, bool unreadable) = await SavedAsync(file.Id);
            Project project = MusicCoreFacade.Imported(file, saved, unreadable);
            OpenedProject.Open(project);
            await Projects.SaveAsync(project);
            await RefreshAsync();
        }
        catch (ProjectFileException exception)
        {
            _message = $"{args.File.Name}: {exception.Message}";
        }
        catch (IOException)
        {
            _message = Text.Format("home.tooLarge", args.File.Name);
        }
    }

    /// <summary>The project saved on this device under an id, null when there is none; unreadable when it is damaged.</summary>
    private async Task<(Project? Saved, bool Unreadable)> SavedAsync(string id)
    {
        try
        {
            return (await Projects.LoadAsync(id), false);
        }
        catch (ProjectFileException)
        {
            return (null, true);
        }
    }
}