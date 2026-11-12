using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Projects;
using MotiffLab.Web.Settings;

namespace MotiffLab.Web.Import;

/// <summary>
/// The piece the player opened, from the import screen (N2) or the projects screen (N1), and where
/// they chose to start, handed to the workspace. The piece's project autosaves on the device, and the
/// workspace's address names it, so a reload or a link opens it again from there.
/// </summary>
public sealed class CurrentPiece(ProjectStore projects, DeviceSettings settings, Preferences preferences)
{
    /// <summary>The experiment's switch in Settings · Experimental («Мои правки: вкл / выкл», version 0.5), off by default.</summary>
    public const string EditsLayerSetting = "experimental.editsLayer";

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
    /// ("&amp;resume=r_…", 0018) and an idea to open on arrival ("&amp;idea=o_…") when given. A PDF with no notes opens
    /// in its own view, "pdf?project=p_…" (0023), which has no resume points or ideas.
    /// </summary>
    public static string WorkspaceUrl(Project project, string? resumeId = null, string? ideaId = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!project.HasNotes)
        {
            return $"pdf?project={Uri.EscapeDataString(project.Id)}";
        }

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
    /// longer saved; throws <see cref="ProjectFileException"/> when it is damaged. A PDF with no notes (0023) is
    /// only marked as opened: it has no piece to rebuild, so <see cref="Piece"/> is null after it.
    /// </summary>
    public async Task<bool> OpenSavedAsync(string id)
    {
        if (await projects.LoadAsync(id) is not { } saved)
        {
            return false;
        }

        Project project = saved with { LastOpened = DateTimeOffset.Now };
        if (!project.HasNotes)
        {
            await projects.SaveAsync(project);
            (Piece, Project, Opened, StartWith) = (null, project, null, SelectionFilter.All);
            Version++;
            return true;
        }

        OpenedProject opened = await OpenAsync(project);
        await projects.SaveAsync(project);
        Open(opened.Piece, opened.Motifs.FirstOrDefault()?.Filter ?? SelectionFilter.All, project, opened);
        return true;
    }

    /// <summary>
    /// How a piece's corrections are laid over its notes: strictly, as before, while the experiment is off; else one by one,
    /// or not at all when the player turned them off for this piece on this device.
    /// </summary>
    public async Task<EditsLayer> EditsLayerAsync(string projectId)
    {
        await settings.LoadAsync();
        if (!settings.IsOn(EditsLayerSetting))
        {
            return EditsLayer.Strict;
        }

        return await preferences.EditsOffAsync(projectId) ? EditsLayer.Off : EditsLayer.Lenient;
    }

    /// <summary>
    /// The project rebuilt from its source, its corrections laid as <see cref="EditsLayerAsync"/> says. Throws
    /// <see cref="ProjectFileException"/> when it is damaged.
    /// </summary>
    public async Task<OpenedProject> OpenAsync(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return OpenedProject.Open(project, await EditsLayerAsync(project.Id));
    }

    /// <summary>Keeps the latest save, so the next one builds on it.</summary>
    public void Saved(Project project) => Project = project;
}