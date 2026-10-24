using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Web.Import;

/// <summary>
/// The piece the player opened, from the import screen (N2) or the projects screen (N1), and where
/// they chose to start, handed to the workspace. The piece's project autosaves on the device.
/// </summary>
public sealed class CurrentPiece
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

    /// <summary>Keeps the latest save, so the next one builds on it.</summary>
    public void Saved(Project project) => Project = project;
}