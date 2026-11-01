using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Import;

/// <summary>
/// The piece the player opened, from the import screen (N2) or the projects screen (N1), and where
/// they chose to start, handed to the workspace. The piece's project autosaves on the device, and the
/// workspace's address names it, so a reload or a link opens it again from there.
/// </summary>
public sealed class CurrentPiece(ProjectStore projects)
{
    public ImportedPiece? Piece { get; private set; }

    /// <summary>The track or hand the player picked to start with (N2 step 4).</summary>
    public SelectionFilter StartWith { get; private set; } = SelectionFilter.All;

    /// <summary>The project the piece belongs to, saved on the device (S1-P1).</summary>
    public Project? Project { get; private set; }

    /// <summary>The motifs and variations rebuilt from a saved project, when it was opened from N1.</summary>
    public OpenedProject? Opened { get; private set; }

    /// <summary>Bumped on every <see cref="Open"/>, so a page can tell a new piece from one it already showed.</summary>
    public int Version { get; private set; }

    public void Open(ImportedPiece piece, SelectionFilter startWith, Project project, OpenedProject? opened = null)
    {
        Piece = piece;
        StartWith = startWith;
        Project = project;
        Opened = opened;
        Version++;
    }

    /// <summary>
    /// The workspace with the project open: "playback?project=p_…", with the resume point it works from
    /// ("&amp;resume=r_…", 0018) and an idea to open on arrival ("&amp;idea=o_…") when given.
    /// </summary>
    public static string WorkspaceUrl(Project project, string? resumeId = null, string? ideaId = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        string url = $"playback?project={Uri.EscapeDataString(project.Id)}";
        if (resumeId is not null)
        {
            url += $"&resume={Uri.EscapeDataString(resumeId)}";
        }

        if (ideaId is not null)
        {
            url += $"&idea={Uri.EscapeDataString(ideaId)}";
        }

        return url;
    }

    /// <summary>
    /// Rebuilds a saved project from its source and opens it, marked as opened now. False when it is no
    /// longer saved; throws <see cref="ProjectFileException"/> when it is damaged.
    /// </summary>
    public async Task<bool> OpenSavedAsync(string id)
    {
        if (await projects.LoadAsync(id) is not { } saved)
        {
            return false;
        }

        Project project = saved with { LastOpened = DateTimeOffset.Now };
        OpenedProject opened = OpenedProject.Open(project);
        await projects.SaveAsync(project);
        Open(opened.Piece, opened.Motifs.FirstOrDefault()?.Filter ?? SelectionFilter.All, project, opened);
        return true;
    }

    /// <summary>Keeps the latest save, so the next one builds on it.</summary>
    public void Saved(Project project) => Project = project;
}