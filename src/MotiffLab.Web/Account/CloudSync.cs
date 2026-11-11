using MotiffLab.MusicCore.Accounts;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Localization;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Account;

/// <summary>
/// Sync between the player's devices (version 0.2; Andrei, 2026-11-11): new pieces and their changes go between the web
/// and the iPad by themselves, and when both changed one, the last save wins and the previous copy is kept beside as a
/// piece of its own. One pass compares each project here with the API's list by the rules of <see cref="ProjectSync"/>:
/// it sends what changed here, fetches what changed online or is new there, and resolves a change on both. It runs when
/// the library opens and when the page comes back into view, never inside a piece, so the open piece is not replaced.
/// </summary>
public sealed class CloudSync(ProjectStore store, OnlineProjects online, UiText text)
{
    private bool _running;

    // How many projects the API listed in this pass, so a piece refused for the quota is tried again once there is room.
    private int _listed;

    /// <summary>
    /// One pass; true when a project here was added or changed, so the library lists them again. Nothing happens while
    /// signed out, with saving online off, offline, or while a pass is running.
    /// </summary>
    public async Task<bool> RunAsync()
    {
        if (_running || !await online.IsSyncingAsync())
        {
            return false;
        }

        _running = true;
        try
        {
            (IReadOnlyList<OnlineRow>? rows, _) = await online.RowsAsync();
            if (rows is null)
            {
                return false;
            }

            _listed = rows.Count;
            Dictionary<string, OnlineRow> listed = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
            Dictionary<string, ProjectSummary> here = (await store.ListAsync()).ToDictionary(summary => summary.Id, StringComparer.Ordinal);
            IReadOnlyDictionary<string, SyncRecord> records = await online.AllRecordsAsync();
            bool changed = false;
            foreach (string id in here.Keys.Union(listed.Keys).Union(records.Keys).ToList())
            {
                changed |= await SyncAsync(id, here.GetValueOrDefault(id), listed.GetValueOrDefault(id), await online.RecordAsync(id));
            }

            return changed;
        }
        finally
        {
            _running = false;
        }
    }

    private async Task<bool> SyncAsync(string id, ProjectSummary? here, OnlineRow? row, SyncRecord record)
    {
        // A piece that is only a PDF stays on this device (0026 C3) and is not synced.
        if (here?.Origin == PieceOrigin.Pdf)
        {
            return false;
        }

        switch (ProjectSync.Next(Facts(here, row, record)))
        {
            case SyncStep.Upload:
                await UploadAsync(id, row, record);
                return false;
            case SyncStep.Download:
                return await DownloadAsync(id, row!, record);
            case SyncStep.Resolve:
                return await ResolveAsync(id, here!, row!, record);
            case SyncStep.Forget:
                await online.NoteAsync(id, here is null ? null : SyncRecord.None with { DeletedOnline = true });
                return false;
            default:
                return false;
        }
    }

    private static SyncFacts Facts(ProjectSummary? here, OnlineRow? row, SyncRecord record) => new(
        Here: here is not null,
        Online: row is not null,
        Known: record.Known,
        ChangedHere: !record.Known || record.Dirty,
        ChangedOnline: row is not null && record.Known && record.SavedAt != row.SavedAt,
        DeletedOnline: record.DeletedOnline);

    private async Task UploadAsync(string id, OnlineRow? row, SyncRecord record)
    {
        // Not sent again over the quota until it is saved here or the account has room: the library says so meanwhile.
        if (record.OverQuota && !record.Dirty && _listed >= Plans.FreeProjectsOnline)
        {
            return;
        }

        if (await LoadAsync(id) is { } project && project.ForOnline() is { } copy)
        {
            await online.UploadAsync(copy, row is null ? null : record.ETag);
        }
    }

    private async Task<bool> DownloadAsync(string id, OnlineRow row, SyncRecord record)
    {
        (Project? fetched, string? etag, _) = await online.FetchAsync(id);
        if (fetched is null || fetched.Id != id)
        {
            return false;
        }

        if (etag is not null && etag == record.ETag)
        {
            // This device's own save coming back: only its time online was not known yet.
            await online.NoteAsync(id, record with { SavedAt = row.SavedAt });
            return false;
        }

        await store.SaveHereAsync(ProjectSync.Pulled(fetched, await LoadAsync(id)));
        await online.NoteAsync(id, new SyncRecord(etag, row.SavedAt, false, null));
        return true;
    }

    private async Task<bool> ResolveAsync(string id, ProjectSummary summary, OnlineRow row, SyncRecord record)
    {
        (Project? fetched, string? etag, _) = await online.FetchAsync(id);
        if (fetched is null || await LoadAsync(id) is not { } project)
        {
            return false;
        }

        if (etag is not null && etag == record.ETag)
        {
            // The online copy is the one this device sent last, so only this device changed it.
            if (project.ForOnline() is { } copy)
            {
                await online.UploadAsync(copy, etag);
            }

            return false;
        }

        string copyTitle = text.Format("sync.previousCopy", "{0}", DateTimeOffset.Now.ToString("g", text.Culture));
        SyncResolution resolved = ProjectSync.Resolve(project, record.ChangedAt ?? summary.LastOpened, fetched, row.SavedAt, copyTitle, MusicCoreFacade.NewProjectId());
        bool changed = false;
        if (resolved.PreviousCopy is { } previous)
        {
            // Saved as any other piece, so it goes online too and nothing is lost on either device.
            await store.SaveAsync(previous);
            changed = true;
        }

        if (resolved.Upload)
        {
            if (resolved.Kept.ForOnline() is { } copy)
            {
                await online.UploadAsync(copy, etag);
            }
        }
        else if (!ReferenceEquals(resolved.Kept, project))
        {
            await store.SaveHereAsync(resolved.Kept);
            await online.NoteAsync(id, new SyncRecord(etag, row.SavedAt, false, null));
            changed = true;
        }
        else
        {
            await online.NoteAsync(id, new SyncRecord(etag, row.SavedAt, false, null));
        }

        return changed;
    }

    private async Task<Project?> LoadAsync(string id)
    {
        try
        {
            return await store.LoadAsync(id);
        }
        catch (ProjectFileException)
        {
            // A damaged project here is left as it is, and not sent.
            return null;
        }
    }
}