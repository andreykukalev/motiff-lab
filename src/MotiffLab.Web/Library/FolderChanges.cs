using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Library;

/// <summary>A piece whose file changed in the folder, with the file that is possibly it: «Похоже, это та же пьеса».</summary>
public sealed record ChangedPiece(LibraryOriginal Original, SamePiece Alike);

/// <summary>
/// «Проверить папку» for the pieces this browser linked in a folder (version 0.5; Andrei 2026-11-12 17:48 UTC): from the fresh
/// listing with every file's fingerprint, music-core's relink plan (<see cref="LibraryRelink.Plan"/>). A file that moved or was
/// renamed with the same content is linked again with no question, the piece's place moved with it; a piece whose file
/// changed is offered with the likeliest file, never linked by itself. The files are only read.
/// </summary>
public sealed class FolderChanges(PlayerFolders folders, ProjectStore projects)
{
    /// <summary>Relinks what moved and answers what changed; how many moved is <see cref="Moved"/>.</summary>
    public async Task<IReadOnlyList<ChangedPiece>> RelinkAsync(string folder, IReadOnlyList<FoundFile> files, IReadOnlyList<LibraryOriginal> library)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(library);
        Moved = 0;
        IReadOnlyDictionary<string, PieceLink> links = await folders.LinksAsync();
        LibraryOriginal[] here = [.. library.Where(original => original.OriginalId == PieceOriginal.DocumentId
            && links.TryGetValue(original.ProjectId, out PieceLink? link) && link.Folder == folder)];
        if (here.Length == 0)
        {
            return [];
        }

        RelinkPlan plan = LibraryRelink.Plan(here, files);
        List<ChangedPiece> changed = [];
        foreach (Relinked each in plan.Originals)
        {
            if (each is { Status: RelinkStatus.Moved, Path: { } path })
            {
                await MoveAsync(each.Original.ProjectId, folder, path);
            }
            else if (each.Status is RelinkStatus.Changed or RelinkStatus.Missing && each.Candidates.FirstOrDefault() is { } alike
                && files.FirstOrDefault(file => file.Path == alike.Path) is { } file)
            {
                changed.Add(new ChangedPiece(each.Original, new SamePiece(folder, file.Path, file.Bytes, file.Pages)));
            }
        }

        return changed;
    }

    /// <summary>How many pieces moved in the folder and were linked again at their new place in the last check.</summary>
    public int Moved { get; private set; }

    private async Task MoveAsync(string projectId, string folder, string path)
    {
        if (await projects.LoadAsync(projectId) is not { } project)
        {
            return;
        }

        int slash = path.LastIndexOf('/');
        Project moved = project.RelinkedAt(PieceOriginal.DocumentId, slash >= 0 ? path[..slash] : "", path[(slash + 1)..]);
        await projects.SaveAsync(moved);
        await folders.LinkAsync(projectId, new PieceLink(folder, path));
        Moved++;
    }
}