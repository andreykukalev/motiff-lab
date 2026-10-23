using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;

namespace MotiffLab.Web.Import;

/// <summary>
/// The piece the player imported on N2 and where they chose to start, handed to the workspace.
/// It lives in memory only: the file never leaves the device, and saving projects comes with N1.
/// </summary>
public sealed class CurrentPiece
{
    public ImportedPiece? Piece { get; private set; }

    /// <summary>The track or hand the player picked to start with (N2 step 4).</summary>
    public SelectionFilter StartWith { get; private set; } = SelectionFilter.All;

    /// <summary>Bumped on every <see cref="Open"/>, so a page can tell a new piece from one it already showed.</summary>
    public int Version { get; private set; }

    public void Open(ImportedPiece piece, SelectionFilter startWith)
    {
        Piece = piece;
        StartWith = startWith;
        Version++;
    }
}