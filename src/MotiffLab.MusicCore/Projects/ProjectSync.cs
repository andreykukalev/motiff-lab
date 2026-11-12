namespace MotiffLab.MusicCore.Projects;

/// <summary>What a device does next with one project against its copy saved online (version 0.2 sync).</summary>
public enum SyncStep
{
    /// <summary>Both copies are as they were when the device last synced them, or the piece is not synced.</summary>
    Nothing,

    /// <summary>Only this device changed it, or it is not online yet: send it.</summary>
    Upload,

    /// <summary>Only the online copy changed, or it is not here yet: fetch it and keep it here.</summary>
    Download,

    /// <summary>Both changed it since they last met, or they never met: fetch the online copy and <see cref="ProjectSync.Resolve"/>.</summary>
    Resolve,

    /// <summary>The online copy was deleted on another device and nothing changed here since: stop syncing it, keep it here.</summary>
    Forget,
}

/// <summary>What a device knows about one project when it syncs: whether each copy exists and changed since it last synced it.</summary>
/// <param name="Here">The project is on this device.</param>
/// <param name="Online">The project is listed online.</param>
/// <param name="Known">This device synced it before, so it has a base: the online copy's ETag and save time it last saw.</param>
/// <param name="ChangedHere">Saved on this device since it last synced (or never synced).</param>
/// <param name="ChangedOnline">The online copy's save time is not the one this device saw last.</param>
/// <param name="DeletedOnline">This device saw the online copy go and keeps its own without sending it again.</param>
public sealed record SyncFacts(bool Here, bool Online, bool Known, bool ChangedHere, bool ChangedOnline, bool DeletedOnline = false);

/// <summary>What resolving two copies of one project keeps.</summary>
/// <param name="Kept">The project to keep on this device.</param>
/// <param name="Upload">True when <paramref name="Kept"/> goes online over the online copy (the device's copy won).</param>
/// <param name="PreviousCopy">The copy that lost, kept beside as a piece of its own with a new id; null when nothing was lost.</param>
public sealed record SyncResolution(Project Kept, bool Upload, Project? PreviousCopy);

/// <summary>
/// The rules both apps sync projects by (version 0.2, decided by Andrei 2026-11-11: new pieces and their changes go between
/// the devices by themselves; when both changed one, the last save wins and the previous copy is kept). The device stays the
/// working copy: it keeps for each project the online copy's ETag and save time it last saw (outside the project file), sends
/// what changed here with that ETag (<c>If-Match</c>) and fetches what changed online. A project deleted on this device is
/// never deleted online from here, and one deleted online stays here. The full merge of 0026 comes in a later version.
/// </summary>
public static class ProjectSync
{
    /// <summary>The next step for a project from what the device knows about it.</summary>
    public static SyncStep Next(SyncFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return (facts.Here, facts.Online) switch
        {
            (true, true) when !facts.Known => SyncStep.Resolve,
            (true, true) => (facts.ChangedHere, facts.ChangedOnline) switch
            {
                (false, false) => SyncStep.Nothing,
                (true, false) => SyncStep.Upload,
                (false, true) => SyncStep.Download,
                (true, true) => SyncStep.Resolve,
            },
            // Not online: new here, sent; or deleted online elsewhere, kept here unless it changed here since.
            (true, false) when !facts.Known => SyncStep.Upload,
            (true, false) when facts.DeletedOnline => facts.ChangedHere ? SyncStep.Upload : SyncStep.Nothing,
            (true, false) => facts.ChangedHere ? SyncStep.Upload : SyncStep.Forget,
            // Not here: new online, fetched; deleted here, left online unless it changed online since.
            (false, true) when !facts.Known => SyncStep.Download,
            (false, true) => facts.ChangedOnline ? SyncStep.Download : SyncStep.Nothing,
            (false, false) => SyncStep.Forget,
        };
    }

    /// <summary>
    /// The online copy as it is kept here: a PDF stays on the device (0026 C3), so when the online copy has none, the
    /// device's own PDF and its pages stay with the piece. The marks on the PDF's pages (version 0.4) are the writer's
    /// (<see cref="Project.WritesDocumentMarks"/>): the device that writes them keeps its own, and every other device takes
    /// the online copy's, unless the online copy has none and this device has some, as a copy a build before 0.4 sent
    /// (it leaves them out), so they are never wiped by it.
    /// </summary>
    /// <remarks>
    /// The originals' descriptions (version 0.3) are a union of both copies by fingerprint, else by id, so an original
    /// one device knows is never lost; <paramref name="device"/>'s own holding marks come from <paramref name="here"/>
    /// and every other device's from the online copy (<see cref="PieceOriginals.Merge"/>).
    /// </remarks>
    public static Project Pulled(Project online, Project? here, HoldingDevice? device = null)
    {
        ArgumentNullException.ThrowIfNull(online);
        if (here is null || here.Id != online.Id)
        {
            return online;
        }

        bool keepsPdf = here.Document is not null && online.Document is null;
        Project kept = keepsPdf ? online with { Document = here.Document, DocumentPages = here.DocumentPages } : online;
        IReadOnlyList<PieceOriginal> originals = PieceOriginals.Merge(online.Originals, here.Originals, device);
        if (!originals.SequenceEqual(kept.Originals))
        {
            kept = kept with { KeptOriginals = originals };
        }

        bool ownMarks = (keepsPdf && kept.WritesDocumentMarks(device)) || (!HasPageMarks(online) && HasPageMarks(here));
        return ownMarks ? kept with { DocumentMarks = here.DocumentMarks } : kept;
    }

    private static bool HasPageMarks(Project project) => !project.DocumentMarks.IsEmpty || project.DocumentMarks.Unknown.Count > 0;

    /// <summary>
    /// Two copies of one project, both changed: the one saved last is kept, here and online, and the other is kept beside
    /// as a piece of its own named by <paramref name="copyTitle"/> ("{0}" stands for its title), so nothing is lost. Two
    /// copies that are the same keep this device's with nothing beside. A different piece that only carries the same id
    /// (an old numbered id, V8) is no previous copy: the online one is kept beside under a new id as it is, and this
    /// device's goes online.
    /// </summary>
    /// <param name="here">The project on this device.</param>
    /// <param name="savedHere">When it was saved here last.</param>
    /// <param name="online">The online copy, as fetched.</param>
    /// <param name="savedOnline">When the online copy was saved, as the API lists it.</param>
    /// <param name="copyTitle">The previous copy's title, with "{0}" for the piece's title.</param>
    /// <param name="newId">The previous copy's id (<c>MusicCoreFacade.NewProjectId</c>).</param>
    /// <param name="device">This device, whose marks of holding an original are its own (version 0.3); null keeps the online marks.</param>
    public static SyncResolution Resolve(Project here, DateTimeOffset savedHere, Project online, DateTimeOffset savedOnline, string copyTitle, string newId, HoldingDevice? device = null)
    {
        ArgumentNullException.ThrowIfNull(here);
        ArgumentNullException.ThrowIfNull(online);
        ArgumentNullException.ThrowIfNull(copyTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(newId);
        Project pulled = Pulled(online, here, device);
        if (ProjectFile.Write(pulled).AsSpan().SequenceEqual(ProjectFile.Write(here)))
        {
            return new SyncResolution(here, Upload: false, PreviousCopy: null);
        }

        if (here.Id != online.Id || here.CreatedAt != online.CreatedAt)
        {
            return new SyncResolution(here, Upload: true, PreviousCopy: online with { Id = newId });
        }

        // This device's copy wins with the page marks it does not write as the pull brings them, so it never sends old ones.
        Project mine = pulled.WritesDocumentMarks(device) || here.DocumentMarks.Equals(pulled.DocumentMarks) ? here : here with { DocumentMarks = pulled.DocumentMarks };
        return savedHere >= savedOnline
            ? new SyncResolution(mine, Upload: true, PreviousCopy: PreviousCopy(online, copyTitle, newId))
            : new SyncResolution(pulled, Upload: false, PreviousCopy: PreviousCopy(here, copyTitle, newId));
    }

    /// <summary>The copy that lost, as a piece of its own: a new id and the player's title from <paramref name="copyTitle"/>.</summary>
    public static Project PreviousCopy(Project project, string copyTitle, string newId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(copyTitle);
        PieceMetadata metadata = project.Metadata;
        string title = copyTitle.Replace("{0}", project.Details.Title.Value, StringComparison.Ordinal);
        return project with { Id = newId, Metadata = new PieceMetadata(title, metadata.Composer, metadata.Tags, metadata.Playlists) };
    }
}