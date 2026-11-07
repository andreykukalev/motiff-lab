using System.Globalization;
using System.Text;

using Microsoft.JSInterop;

using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Account;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Projects;

/// <summary>
/// Projects saved on the device (S1-P1): a thin C# handle on the IndexedDB wrapper in
/// <c>Scripts/storage</c>. Each project is stored as its project file, so what autosave keeps and
/// what export hands over are the same bytes. Nothing leaves the device unless the player signed in and
/// turned saving online on (0014); then each save and delete is repeated online a moment later.
/// </summary>
public sealed class ProjectStore(IJSRuntime js, OnlineProjects online) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    /// <summary>
    /// Saves on this device, and online when saving online is on. A PDF (0023) stays on the device: it can run to tens of
    /// megabytes, more than an online copy holds. So the online copy of a piece with notes and a PDF holds the notes and
    /// what was made from them, without the PDF (testing item 16), and a piece that is only a PDF is not saved online.
    /// </summary>
    public async Task SaveAsync(Project project)
    {
        await SaveHereAsync(project);
        if (project.ForOnline() is { } copy)
        {
            await online.QueueAsync(copy);
        }
    }

    /// <summary>Saves on this device only, as for a project just fetched from its online copy.</summary>
    public async Task SaveHereAsync(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        IJSObjectReference module = await ModuleAsync();
        ProjectSummary summary = project.Summary;
        await module.InvokeVoidAsync(
            "saveProject",
            project.Id,
            Encoding.UTF8.GetString(ProjectFile.Write(project)),
            new StoredSummary(summary.Id, summary.FileName, summary.MotifCount, summary.VariationCount, Iso(summary.LastOpened), summary.ResumeCount, summary.IdeaCount)
            {
                Origin = summary.Origin.ToString(),
                CheckBars = summary.CheckBars,
                ReadFrom = summary.ReadFrom,
                Latest = summary.Latest is { } latest ? new StoredResume(latest.Id, latest.Name, latest.At, latest.From, latest.To, Iso(latest.LastActive)) : null,
            });
    }

    /// <summary>The saved project, or null when there is none. Throws <see cref="ProjectFileException"/> when it is damaged.</summary>
    public async Task<Project?> LoadAsync(string id)
    {
        IJSObjectReference module = await ModuleAsync();
        string? file = await module.InvokeAsync<string?>("loadProject", id);
        return file is null ? null : ProjectFile.Read(Encoding.UTF8.GetBytes(file));
    }

    /// <summary>Saved projects, most recently opened first.</summary>
    public async Task<IReadOnlyList<ProjectSummary>> ListAsync()
    {
        IJSObjectReference module = await ModuleAsync();
        StoredSummary[] rows = await module.InvokeAsync<StoredSummary[]>("listProjects");
        return [.. rows.Select(row => new ProjectSummary(
            row.Id,
            row.FileName,
            row.MotifCount,
            row.VariationCount,
            Time(row.LastOpened),
            row.ResumeCount,
            row.IdeaCount)
        {
            Origin = Enum.TryParse(row.Origin, out PieceOrigin origin) ? origin : PieceOrigin.File,
            CheckBars = row.CheckBars,
            ReadFrom = row.ReadFrom,
            Latest = row.Latest is { } latest ? new ResumeSummary(latest.Id, latest.Name, latest.At, latest.From, latest.To, Time(latest.LastActive)) : null,
        })];
    }

    /// <summary>The notes read from a PDF piece (0023), the ones opened last when it was read more than once; null when there are none.</summary>
    public async Task<ProjectSummary?> NotesReadFromAsync(string pdfId) =>
        (await ListAsync()).Where(summary => summary.ReadFrom == pdfId).OrderByDescending(summary => summary.LastOpened).FirstOrDefault();

    /// <summary>The covers saved, by project; each page is an image address that holds until the next listing.</summary>
    public async Task<IReadOnlyDictionary<string, PieceCover>> CoversAsync()
    {
        IJSObjectReference module = await ModuleAsync();
        PieceCover[] rows = await module.InvokeAsync<PieceCover[]>("listCovers");
        return rows.ToDictionary(row => row.Id);
    }

    /// <summary>Lets go of the covers' image addresses, when the library closes.</summary>
    public async Task ReleaseCoversAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("releaseCovers");
        }
    }

    public async Task DeleteAsync(string id)
    {
        IJSObjectReference module = await ModuleAsync();
        await module.InvokeVoidAsync("deleteProject", id);
        // The pencil marks on its score go with it (0015).
        await module.InvokeVoidAsync("deleteMarks", PencilMarks.ProjectKey(id));
        await online.DeleteAsync(id);
    }

    /// <summary>True when the browser says this site's storage is nearly full; false when it does not say.</summary>
    public async Task<bool> IsNearlyFullAsync()
    {
        IJSObjectReference module = await ModuleAsync();
        StorageUse? use = await module.InvokeAsync<StorageUse?>("storageUse");
        return use?.NearlyFull ?? false;
    }

    /// <summary>Hands the player the project file to save as a backup (AC-S1-20).</summary>
    public async Task ExportAsync(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        IJSObjectReference module = await ModuleAsync();
        string name = Path.GetFileNameWithoutExtension(project.FileName);
        await module.InvokeVoidAsync("downloadText", name + ProjectFile.Extension, Encoding.UTF8.GetString(ProjectFile.Write(project)));
    }

    /// <summary>Hands the player a file made on the device, such as a MIDI export (S1-X1).</summary>
    public async Task DownloadAsync(string fileName, byte[] data, string contentType)
    {
        IJSObjectReference module = await ModuleAsync();
        await module.InvokeVoidAsync("downloadBytes", fileName, data, contentType);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    /// <summary>UTC, so the script can order rows by comparing the strings.</summary>
    private static string Iso(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/storage/project-store.js");

    private static DateTimeOffset Time(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>A summary row as stored; rows saved before resume points existed have no counts of them, so they read as 0.</summary>
    private sealed record StoredSummary(string Id, string FileName, int MotifCount, int VariationCount, string LastOpened, int ResumeCount = 0, int IdeaCount = 0)
    {
        public string? Origin { get; init; }

        public int CheckBars { get; init; }

        public StoredResume? Latest { get; init; }

        public string? ReadFrom { get; init; }
    }

    private sealed record StoredResume(string Id, string? Name, long At, long? From, long? To, string LastActive);

    private sealed record StorageUse(double Usage, double Quota, bool NearlyFull);
}