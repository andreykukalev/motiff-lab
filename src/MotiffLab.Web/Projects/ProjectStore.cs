using System.Globalization;
using System.Text;

using Microsoft.JSInterop;

using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Account;

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

    public async Task SaveAsync(Project project)
    {
        await SaveHereAsync(project);
        await online.QueueAsync(project);
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
            new StoredSummary(summary.Id, summary.FileName, summary.MotifCount, summary.VariationCount, Iso(summary.LastOpened), summary.SessionCount, summary.IdeaCount));
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
            DateTimeOffset.Parse(row.LastOpened, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            row.SessionCount,
            row.IdeaCount))];
    }

    public async Task DeleteAsync(string id)
    {
        IJSObjectReference module = await ModuleAsync();
        await module.InvokeVoidAsync("deleteProject", id);
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

    /// <summary>A summary row as stored; rows saved before sessions existed have no counts of them, so they read as 0.</summary>
    private sealed record StoredSummary(string Id, string FileName, int MotifCount, int VariationCount, string LastOpened, int SessionCount = 0, int IdeaCount = 0);

    private sealed record StorageUse(double Usage, double Quota, bool NearlyFull);
}