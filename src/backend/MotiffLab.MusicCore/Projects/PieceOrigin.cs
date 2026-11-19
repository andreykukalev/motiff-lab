namespace MotiffLab.MusicCore.Projects;

/// <summary>Where a piece came from: a file opened on the device, a page scanned (0011) or a recording heard (0014).</summary>
public enum PieceOrigin
{
    File,
    Scan,
    Recording,

    /// <summary>A PDF added to read as it is, with no notes (0023).</summary>
    Pdf,

    /// <summary>A recording added as it is, with no notes yet (finding 29, project file version 17).</summary>
    Audio,
}