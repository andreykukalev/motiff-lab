using System.Text.Json;

using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Import;
using MotiffLab.Web.Localization;
using MotiffLab.Web.Pdf;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Library;

/// <summary>Where one piece of a batch is: «в очереди», «распознаётся», «есть ноты», «не вышло».</summary>
public enum RecognitionState
{
    Queued,
    Reading,
    Done,
    Failed,
}

/// <summary>One PDF piece of a batch, with its pages; while it is read, the page being read.</summary>
public sealed record RecognitionItem(string Project, string Title, int Pages)
{
    public RecognitionState State { get; init; }

    /// <summary>The page being read, from 1, while it is read.</summary>
    public int Page { get; init; }

    /// <summary>Why it did not work, in the player's words.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// The pieces the player sent to be read, to the server they agreed to, kept on this device so the batch goes on after a
/// reload. <see cref="Stopped"/> says why it waits for the player: "limit", "signIn", "needsPro", "unreachable", "off",
/// "folder" (the browser must be asked for the folder again), "server" (the address changed in Settings) or "reload".
/// </summary>
public sealed record RecognitionBatch(string Address, bool Hosted, IReadOnlyList<RecognitionItem> Items)
{
    public bool Paused { get; init; }

    public string? Stopped { get; init; }

    public int Done => Items.Count(static item => item.State is RecognitionState.Done or RecognitionState.Failed);

    public int Read => Items.Count(static item => item.State == RecognitionState.Done);

    public int Failed => Items.Count(static item => item.State == RecognitionState.Failed);

    public bool Finished => Done == Items.Count;

    /// <summary>The pages still to read, for «ещё около N минут».</summary>
    public int PagesLeft => Items.Where(static item => item.State is RecognitionState.Queued or RecognitionState.Reading)
        .Sum(static item => Math.Max(1, item.Pages - Math.Max(0, item.Page - 1)));
}

/// <summary>A PDF piece the player picked to be read: its id, title and pages.</summary>
public sealed record PieceToRead(string Project, string Title, int Pages);

/// <summary>
/// Batch recognition from the library (version 0.5, «Импорт папки», ux-prototype/v0.5/screens/import-web-4..5): the PDF
/// pieces the player picked go to the scan server one piece at a time and page by page, after one consent naming the
/// pieces, the pages and the server; the notes of the pages are joined by music-core (<see cref="ScanJoin"/>) and go into the
/// same piece, as «Распознать ноты» on one PDF does. A piece of more than <see cref="ScanParts.MostPages"/> pages is not read.
/// On Motiff Lab's server the day's 1000 pages are counted on this device (<see cref="HostedPages"/>): what does not fit waits
/// until tomorrow. Pausable; kept on the device. The PDFs are only read.
/// </summary>
public sealed class RecognitionQueue(
    PlayerFolders folders,
    ProjectStore projects,
    OriginalFinder finder,
    ScanService scans,
    HostedPages hosted,
    PdfPages pages,
    UiText text)
{
    private const string Key = "recognition.batch";

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private readonly List<long> _measured = [];
    private bool _running;
    private bool _stop;
    private bool _loaded;

    public RecognitionBatch? Batch { get; private set; }

    /// <summary>Whether a piece is being read now; else the batch, if any, waits for the player or is done.</summary>
    public bool Running => _running;

    /// <summary>
    /// About how many minutes the pages left take (v0.5.6, #533 section 1), from the pages this batch has read here, timed
    /// from send to answer; null before the first page is back.
    /// </summary>
    public int? MinutesLeft => Batch is { } batch ? PageProgress.MinutesFor(_measured, batch.PagesLeft) : null;

    /// <summary>After each page, each piece and each change of state.</summary>
    public event Action? Changed;

    /// <summary>After a piece got its notes, so the library lists it again.</summary>
    public event Action? Read;

    /// <summary>The batch kept on this device, as the page was left: it waits for the player to go on.</summary>
    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            Batch = await folders.LoadStateAsync(Key) is { } kept ? JsonSerializer.Deserialize<RecognitionBatch>(kept, s_json) : null;
        }
        catch (JsonException)
        {
            Batch = null;
        }

        if (Batch is { Finished: false } batch)
        {
            Batch = batch with
            {
                Items = [.. batch.Items.Select(static item => item.State == RecognitionState.Reading ? item with { State = RecognitionState.Queued, Page = 0 } : item)],
                Paused = true,
                Stopped = batch.Paused ? batch.Stopped : "reload",
            };
        }
    }

    /// <summary>What is happening to the piece's notes in the batch, if it is in it.</summary>
    public RecognitionItem? StatusOf(string projectId) => Batch?.Items.FirstOrDefault(item => item.Project == projectId);

    /// <summary>
    /// Sends the pieces the player agreed to, to the server set now (<see cref="ScanService.Address"/>): added to the batch
    /// under way, or a new one; then reads them.
    /// </summary>
    public async Task SendAsync(IEnumerable<PieceToRead> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        await LoadAsync();
        if (scans.Address is not { } address)
        {
            return;
        }

        RecognitionBatch? under = Batch is { Finished: false } kept ? kept : null;
        if (under is null)
        {
            _measured.Clear();
        }

        HashSet<string> already = [.. under?.Items.Select(static item => item.Project) ?? []];
        RecognitionItem[] added = [.. pieces.Where(piece => already.Add(piece.Project)).Select(static piece => new RecognitionItem(piece.Project, piece.Title, Math.Max(1, piece.Pages)))];
        Batch = new RecognitionBatch(address.ToString(), scans.IsHosted, [.. under?.Items ?? [], .. added]);
        await SaveAsync();
        Start();
    }

    /// <summary>Goes on: asks the browser again for the kept folders first, from the player's tap.</summary>
    public async Task ResumeAsync()
    {
        if (Batch is not { } batch)
        {
            return;
        }

        foreach (ChosenFolder kept in (await folders.KeptAsync()).Where(static kept => kept.Kept))
        {
            if (await folders.PermissionAsync(kept.Id) == "prompt")
            {
                await folders.AskPermissionAsync(kept.Id);
            }
        }

        await scans.LoadAsync();
        // Going on after the server changed in Settings is the player's yes to the new one.
        Batch = batch with { Paused = false, Stopped = null, Address = scans.Address?.ToString() ?? batch.Address, Hosted = scans.Address is null ? batch.Hosted : scans.IsHosted };
        await SaveAsync();
        Start();
    }

    /// <summary>Stops after the page being read; the pieces read keep their notes.</summary>
    public async Task PauseAsync()
    {
        _stop = true;
        if (Batch is { } batch)
        {
            Batch = batch with { Paused = true, Stopped = null };
            await SaveAsync();
        }

        Changed?.Invoke();
    }

    /// <summary>Stops for good, or puts a finished batch's line away: the pieces read keep their notes.</summary>
    public async Task CloseAsync()
    {
        _stop = true;
        Batch = null;
        _measured.Clear();
        await folders.SaveStateAsync(Key, null);
        Changed?.Invoke();
    }

    /// <summary>Reads the batch in the background: the library, the pieces read and the rest of the app stay usable.</summary>
    private void Start() => _ = RunAsync();

    private async Task RunAsync()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _stop = false;
        Changed?.Invoke();
        try
        {
            await scans.LoadAsync();
            while (!_stop && Batch is { Paused: false } batch && batch.Items.FirstOrDefault(static item => item.State == RecognitionState.Queued) is { } next)
            {
                if (scans.Address?.ToString() != batch.Address)
                {
                    await WaitAsync(scans.Address is null ? "off" : "server");
                    return;
                }

                Put(next with { State = RecognitionState.Reading, Page = 0, Reason = null });
                await SaveAsync();
                (RecognitionState state, string? reason, string? stop) = await ReadSafelyAsync(next, batch.Hosted);
                if (stop is not null)
                {
                    Put(next with { State = RecognitionState.Queued, Page = 0 });
                    await WaitAsync(stop);
                    return;
                }

                Put(next with { State = state, Page = 0, Reason = reason });
                await SaveAsync();
                if (state == RecognitionState.Done)
                {
                    Read?.Invoke();
                }
            }
        }
        finally
        {
            _running = false;
            Changed?.Invoke();
        }
    }

    private async Task<(RecognitionState State, string? Reason, string? Stop)> ReadSafelyAsync(RecognitionItem item, bool onHosted)
    {
        try
        {
            return await ReadAsync(item, onHosted);
        }
        catch (Microsoft.JSInterop.JSException)
        {
            // The browser could not read or draw the PDF: this piece only.
            return (RecognitionState.Failed, text["pdf.unreadable"], null);
        }
    }

    /// <summary>One piece read page by page and its notes put into it: done, or failed and why, or why the batch must wait.</summary>
    private async Task<(RecognitionState State, string? Reason, string? Stop)> ReadAsync(RecognitionItem item, bool onHosted)
    {
        if (await LoadPieceAsync(item.Project) is not { } project)
        {
            return (RecognitionState.Failed, text["recognition.failMissing"], null);
        }

        if (project.HasNotes)
        {
            return (RecognitionState.Done, null, null);
        }

        if (project.Document is null)
        {
            Reached reached = await finder.FindAsync(project, ask: false);
            if (reached.State == Reach.Ask)
            {
                return (RecognitionState.Queued, null, "folder");
            }

            if (reached.Project is not { Document: not null } found)
            {
                return (RecognitionState.Failed, text["recognition.failAway"], null);
            }

            project = found;
        }

        int count = await pages.OpenAsync(project.Document!);
        try
        {
            if (count == 0)
            {
                return (RecognitionState.Failed, text["pdf.unreadable"], null);
            }

            if (count > ScanParts.MostPages)
            {
                return (RecognitionState.Failed, text.Format("recognition.failTooMany", ScanParts.MostPages), null);
            }

            if (onHosted && await hosted.LeftAsync() < count)
            {
                return (RecognitionState.Queued, null, "limit");
            }

            return await PagesAsync(item, project, count, onHosted);
        }
        finally
        {
            await pages.CloseAsync();
        }
    }

    private async Task<(RecognitionState State, string? Reason, string? Stop)> PagesAsync(RecognitionItem item, Project project, int count, bool onHosted)
    {
        List<byte[]> read = [];
        List<PageReport> reports = [];
        bool reported = true;
        string engine = "";
        string name = Path.GetFileNameWithoutExtension(project.FileName);
        for (int number = 1; number <= count; number++)
        {
            Put(item with { State = RecognitionState.Reading, Page = number });
            Changed?.Invoke();
            if (await pages.ImageAsync(number) is not { } image)
            {
                return (RecognitionState.Failed, text.Format("recognition.failPage", number, text["pdf.unreadable"]), null);
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            ScanResult result = await scans.RecogniseAsync($"{name} {number}.png", image);
            if (result.Stop is { } stop)
            {
                return (RecognitionState.Queued, null, StopKey(stop));
            }

            if (result.MusicXml is not { } musicXml)
            {
                return (RecognitionState.Failed, text.Format("recognition.failPage", number, result.Problem ?? text["pdf.readNone"]), null);
            }

            _measured.Add(watch.ElapsedMilliseconds);
            if (onHosted)
            {
                await hosted.AddAsync(1);
            }

            read.Add(musicXml);
            engine = result.Engine;
            reported &= result.Reports.Count == 1;
            reports.AddRange(result.Reports.Take(1));
        }

        ScanResult joined;
        try
        {
            joined = new ScanResult(ScanJoin.Join(read), engine, count, null) { Reports = reported ? reports : [] };
        }
        catch (ArgumentException)
        {
            return (RecognitionState.Failed, text["import.scanPagesNotJoined"], null);
        }

        (NotesRead? notes, string? problem) = NotesReading.Into(project, joined, count, text, DateTimeOffset.Now);
        if (notes is null)
        {
            return (RecognitionState.Failed, problem ?? text["pdf.readNone"], null);
        }

        await projects.SaveAsync(notes.Project);
        return (RecognitionState.Done, null, null);
    }

    private static string StopKey(ScanStop stop) => stop switch
    {
        ScanStop.Off => "off",
        ScanStop.SignIn => "signIn",
        ScanStop.NeedsPro => "needsPro",
        _ => "unreachable",
    };

    private async Task<Project?> LoadPieceAsync(string id)
    {
        try
        {
            return await projects.LoadAsync(id);
        }
        catch (ProjectFileException)
        {
            return null;
        }
    }

    private void Put(RecognitionItem changed)
    {
        if (Batch is { } batch)
        {
            Batch = batch with { Items = [.. batch.Items.Select(item => item.Project == changed.Project ? changed : item)] };
        }
    }

    private async Task WaitAsync(string why)
    {
        if (Batch is { } batch)
        {
            Batch = batch with { Paused = true, Stopped = why };
            await SaveAsync();
        }
    }

    private async Task SaveAsync()
    {
        await folders.SaveStateAsync(Key, Batch is null ? null : JsonSerializer.Serialize(Batch, s_json));
        Changed?.Invoke();
    }
}