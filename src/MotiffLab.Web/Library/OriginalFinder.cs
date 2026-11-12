using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Library;

/// <summary>Where a PDF piece's file stands on this device.</summary>
public enum Reach
{
    /// <summary>Read and matched: the piece opens with it.</summary>
    Opened,

    /// <summary>The browser must be asked again for the folder or the file, from a tap.</summary>
    Ask,

    /// <summary>It was linked on this device and is not where it was, or is another file now: «Файл не найден».</summary>
    Missing,

    /// <summary>Never linked on this device: its PDF is on another device or was not kept («Привязать оригинал»).</summary>
    Away,
}

/// <param name="Folder">The kept folder it was linked in, for «Проверить папку»; null when it was picked as one file.</param>
public sealed record Reached(Reach State, Project? Project, string? Folder = null);

/// <summary>A file in the folder that is possibly the piece, offered by «Проверить папку» («Похоже, это та же пьеса»).</summary>
public sealed record SamePiece(string Folder, string Path, long Bytes, int? Pages);

/// <summary>What «Проверить папку» found for one piece: opened, a file to confirm, or nothing.</summary>
public sealed record Rechecked(Project? Opened, SamePiece? Alike);

/// <summary>
/// The PDF of a piece whose file is not in its project (version 0.5, «Импорт папки»): read where it is in the player's
/// folder (Chrome, Edge) or from the file the player picked for it, matched by its fingerprint, and found again by «Проверить
/// папку» when the folder was reorganised. A linked PDF is never written into the project; a PDF kept in the file on another
/// device («оригинал на другом устройстве») is kept here again once the player links it. The player's files are only read.
/// </summary>
public sealed class OriginalFinder(PlayerFolders folders, ProjectStore projects)
{
    /// <summary>The piece with its PDF from this device, or where it stands. Asks the browser again only with <paramref name="ask"/>.</summary>
    public async Task<Reached> FindAsync(Project project, bool ask)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (await folders.LinkOfAsync(project.Id) is { } link)
        {
            string permission = await folders.PermissionAsync(link.Folder);
            if (permission == "prompt" && ask && await folders.AskPermissionAsync(link.Folder))
            {
                permission = "granted";
            }

            if (permission == "granted")
            {
                return await folders.ReadAsync(link.Folder, link.Path) is { } data && await OpenAsync(project, data) is { } opened
                    ? new Reached(Reach.Opened, opened)
                    : new Reached(Reach.Missing, null, link.Folder);
            }

            if (permission == "prompt")
            {
                return new Reached(Reach.Ask, null, link.Folder);
            }
        }

        return await folders.OriginalStateAsync(project.Id, ask) switch
        {
            "ready" => await folders.ReadOriginalAsync(project.Id) is { } data && await OpenAsync(project, data) is { } opened
                ? new Reached(Reach.Opened, opened)
                : new Reached(Reach.Missing, null),
            "prompt" => new Reached(Reach.Ask, null),
            _ => new Reached(Reach.Away, null),
        };
    }

    /// <summary>
    /// «Привязать оригинал»: the PDF the player picked is the piece's by its fingerprint. A linked piece keeps the file's
    /// handle (Chrome, Edge) or a copy on this device; a piece whose PDF was kept in its file elsewhere keeps it in the file
    /// here again. Null when it is another file (nothing is kept then).
    /// </summary>
    public async Task<Project?> LinkPickedAsync(Project project, byte[] data, bool handleKept)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(data);
        if (await OpenAsync(project, data) is not { } opened)
        {
            if (handleKept)
            {
                await folders.KeepCopyAsync(project.Id, null);
            }

            return null;
        }

        if (project.LinksDocument && !handleKept)
        {
            await folders.KeepCopyAsync(project.Id, data);
        }

        await folders.LinkAsync(project.Id, null);
        return opened;
    }

    /// <summary>
    /// «Проверить папку» for one piece: lists the kept folder, reads only the files of its size, and opens the piece when its
    /// file moved or was renamed (linked there with no question); else offers the likeliest file that is possibly it.
    /// </summary>
    public async Task<Rechecked> RecheckAsync(Project project, string folder)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.FindOriginal(PieceOriginal.DocumentId) is not { } original || await folders.ListAsync(folder, static _ => { }) is not { } listing)
        {
            return new Rechecked(null, null);
        }

        PieceLink? link = await folders.LinkOfAsync(project.Id);
        bool sameRoot = link?.Folder == folder;
        List<FoundFile> files = [.. listing.Files.Select(static file => new FoundFile(file.Path, file.Bytes))];
        LibraryOriginal[] originals = [Described(project, original, sameRoot)];
        RelinkPlan plan = LibraryRelink.Plan(originals, files);
        if (plan.ToFingerprint.Count > 0)
        {
            Dictionary<string, string?> read = [];
            await folders.DetailsAsync(folder, plan.ToFingerprint.Select(static path => (path, false)), details =>
            {
                foreach (FileDetails each in details)
                {
                    read[each.Path] = each.Sha256;
                }
            });
            files = [.. files.Select(file => read.GetValueOrDefault(file.Path) is { } sha ? file with { Sha256 = sha } : file)];
            plan = LibraryRelink.Plan(originals, files);
        }

        Relinked found = plan.Originals[0];
        if (found is { Status: RelinkStatus.Linked or RelinkStatus.Unconfirmed or RelinkStatus.Moved, Path: { } path }
            && await folders.ReadAsync(folder, path) is { } data
            && await OpenAsync(sameRoot ? Moved(project, path) : project, data) is { } opened)
        {
            await projects.SaveAsync(opened);
            await folders.LinkAsync(project.Id, new PieceLink(folder, path));
            return new Rechecked(opened, null);
        }

        RelinkCandidate? alike = found.Candidates.FirstOrDefault();
        return alike is null || files.FirstOrDefault(file => file.Path == alike.Path) is not { } file
            ? new Rechecked(null, null)
            : new Rechecked(null, new SamePiece(folder, file.Path, file.Bytes, null));
    }

    /// <summary>«Это она: открывать этот файл»: the changed file becomes the piece's PDF; its notes, marks and edits stay.</summary>
    public async Task<Project?> ConfirmAsync(Project project, SamePiece alike)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(alike);
        if (await folders.ReadAsync(alike.Folder, alike.Path) is not { } data || !PdfDocument.IsPdf(data))
        {
            return null;
        }

        // At its new place when this device linked it in this folder; else it keeps the place another device knows it by.
        PieceOriginal original = project.FindOriginal(PieceOriginal.DocumentId)!;
        bool sameRoot = original.Folder is null || (await folders.LinkOfAsync(project.Id))?.Folder == alike.Folder;
        (string directory, string name) = sameRoot ? Split(alike.Path) : (original.Folder!, original.FileName);
        Project changed = project.RelinkedAt(PieceOriginal.DocumentId, directory, name, PieceOriginal.Fingerprint(data), data.LongLength, PdfDocument.PageCount(data));
        if (await OpenAsync(changed, data) is not { } opened)
        {
            return null;
        }

        await projects.SaveAsync(opened);
        await folders.LinkAsync(project.Id, new PieceLink(alike.Folder, alike.Path));
        return opened;
    }

    /// <summary>The piece's PDF as music-core plans relinks: by place only in the same folder this device linked it in.</summary>
    private static LibraryOriginal Described(Project project, PieceOriginal original, bool sameRoot) =>
        new(project.Id, original.Id, original.Sha256)
        {
            Bytes = original.Bytes,
            Pages = original.Pages,
            FileName = original.FileName,
            Folder = sameRoot ? original.Folder : null,
            Title = project.Metadata.Title,
            Composer = project.Metadata.Composer,
        };

    /// <summary>The piece at the file's new place in the same folder, so the iPad and this browser agree on it.</summary>
    private static Project Moved(Project project, string path)
    {
        (string directory, string name) = Split(path);
        return project.RelinkedAt(PieceOriginal.DocumentId, directory, name);
    }

    private static (string Directory, string Name) Split(string path) =>
        path.LastIndexOf('/') is var slash and >= 0 ? (path[..slash], path[(slash + 1)..]) : ("", path);

    /// <summary>
    /// The piece with these bytes as its PDF when they are its file: a linked PDF only while it is open; a PDF kept in the file
    /// on another device kept here again, this browser marked as holding it.
    /// </summary>
    private async Task<Project?> OpenAsync(Project project, byte[] data)
    {
        if (project.Document is not null)
        {
            return project;
        }

        if (project.LinksDocument)
        {
            return project.WithLinkedDocument(data)?.MarkHeld(PieceOriginal.DocumentId, HoldingDevice.Web, held: true, DateTimeOffset.Now);
        }

        if (project.FindOriginal(PieceOriginal.DocumentId) is not { Kind: OriginalKind.Pdf } || project.RelinkDocument(data, new PageMap([Tick.Zero])) is not { } relinked)
        {
            return null;
        }

        Project held = relinked.MarkHeld(PieceOriginal.DocumentId, HoldingDevice.Web, held: true, DateTimeOffset.Now);
        await projects.SaveAsync(held);
        return held;
    }
}