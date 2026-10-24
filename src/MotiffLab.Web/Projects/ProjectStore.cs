using System.Globalization;
using System.Text;

using Microsoft.JSInterop;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Web.Projects;

/// <summary>
/// Projects saved on the device (S1-P1): a thin C# handle on the IndexedDB wrapper in
/// <c>Scripts/storage</c>. Each project is stored as its project file, so what autosave keeps and
/// what export hands over are the same bytes. Nothing leaves the device.
/// </summary>
public sealed class ProjectStore(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task SaveAsync(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        IJSObjectReference module = await ModuleAsync();
        ProjectSummary summary = project.Summary;
        await module.InvokeVoidAsync(
            "saveProject",
            project.Id,
            Encoding.UTF8.GetString(ProjectFile.Write(project)),
            new StoredSummary(summary.Id, summary.FileName, summary.MotifCount, summary.VariationCount, Iso(summary.LastOpened)));
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
            DateTimeOffset.Parse(row.LastOpened, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)))];
    }

    public async Task DeleteAsync(string id)
    {
        IJSObjectReference module = await ModuleAsync();
        await module.InvokeVoidAsync("deleteProject", id);
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

    private sealed record StoredSummary(string Id, string FileName, int MotifCount, int VariationCount, string LastOpened);

    private sealed record StorageUse(double Usage, double Quota, bool NearlyFull);
}