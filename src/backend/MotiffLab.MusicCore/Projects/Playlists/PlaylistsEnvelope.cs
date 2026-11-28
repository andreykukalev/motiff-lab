namespace MotiffLab.MusicCore.Projects.Playlists;

/// <summary>
/// What the envelope of a playlists file says (docs/playlists-file.md), read without its playlists, as
/// <see cref="ProjectEnvelope"/> is for a project file (0026 S2): the format, the version and the oldest version that may
/// read it. The API checks a file by it, so it keeps a file of any version, a later one too, exactly as sent.
/// </summary>
/// <param name="Version">The file's version, 1 or more; it may be later than this build's.</param>
/// <param name="MinReader">The oldest version that may read it, 1 or more.</param>
public sealed record PlaylistsEnvelope(int Version, int MinReader)
{
    /// <exception cref="ProjectFileException">Not a playlists file, or its "version" or "minReader" is not a whole number of 1 or more.</exception>
    public static PlaylistsEnvelope Read(ReadOnlySpan<byte> data) => PlaylistsFile.ReadEnvelope(data);
}