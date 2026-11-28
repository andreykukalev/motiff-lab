using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Api;

/// <summary>
/// Where a player's projects and profile are kept: one document per project, in the player's own partition. The project
/// file (with its sessions, observations and source) is stored packed; the summary is kept beside it so a list
/// never loads the files. The profile (the name and photo the player chose) is one more document in the same partition,
/// and so are the player's playlists file (<see cref="PlaylistsEndpoints"/>) and the reset stamp of the develop deployment
/// (<see cref="TestReset"/>).
/// </summary>
internal interface IProjectStore
{
    Task<IReadOnlyList<StoredSummary>> ListAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>The project, or null when the player has none with this id.</summary>
    Task<StoredProject?> GetAsync(string ownerId, string id, CancellationToken cancellationToken);

    /// <summary>Writes the project and returns its new ETag.</summary>
    /// <param name="ifMatch">
    /// When given, the stored project is replaced only if it still has this ETag; when null, the project is created and
    /// never written over one the player already keeps with its id (#321, 0026 S4).
    /// </param>
    /// <exception cref="ConflictException">The stored project has changed since <paramref name="ifMatch"/>, is gone, or, with none, is there already.</exception>
    Task<string> PutAsync(string ownerId, ProjectSummary summary, string packedFile, string? ifMatch, CancellationToken cancellationToken);

    /// <summary>False when the player has no project with this id.</summary>
    /// <param name="ifMatch">When given, the project is deleted only if it still has this ETag.</param>
    /// <exception cref="ConflictException">The stored project has changed since <paramref name="ifMatch"/>.</exception>
    Task<bool> DeleteAsync(string ownerId, string id, string? ifMatch, CancellationToken cancellationToken);

    /// <summary>Deletes everything the player keeps here, their profile and playlists too, and returns how many projects that was.</summary>
    Task<int> DeleteAllAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>The player's last reset stamp (<see cref="TestReset"/>), or null when they never reset.</summary>
    Task<long?> GetResetAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes everything the player keeps here, as <see cref="DeleteAllAsync"/>, then keeps <paramref name="stamp"/> as
    /// their reset stamp, which no list of projects shows as a project. Returns how many projects went.
    /// </summary>
    Task<int> ResetAsync(string ownerId, long stamp, CancellationToken cancellationToken);

    /// <summary>The name and photo the player chose, or null when they have chosen none.</summary>
    Task<StoredProfile?> GetProfileAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>Keeps the player's name and photo, replacing what was kept.</summary>
    Task PutProfileAsync(string ownerId, StoredProfile profile, CancellationToken cancellationToken);

    /// <summary>The player's playlists file, packed, with its ETag and when it was saved; null when they keep none here.</summary>
    Task<StoredPlaylists?> GetPlaylistsAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>Writes the player's playlists file and returns its new ETag, by the rule of <see cref="PutAsync"/>.</summary>
    /// <param name="ifMatch">When given, the file kept is replaced only if it still has this ETag; when null, it is created and never written over one kept.</param>
    /// <exception cref="ConflictException">The file kept has changed since <paramref name="ifMatch"/>, is gone, or, with none, is there already.</exception>
    Task<string> PutPlaylistsAsync(string ownerId, string packedFile, string? ifMatch, CancellationToken cancellationToken);
}

/// <summary>The name a player chose to be shown, and their photo as a data URL; either may be null.</summary>
internal sealed record StoredProfile(string? Name, string? Photo);

/// <summary>A row of the player's project list.</summary>
internal sealed record StoredSummary(
    string Id,
    string FileName,
    int MotifCount,
    int VariationCount,
    DateTimeOffset LastOpened,
    int ResumeCount,
    int IdeaCount,
    DateTimeOffset SavedAt);

internal sealed record StoredProject(string PackedFile, string ETag);

/// <summary>The player's playlists file, packed as a project is (<see cref="ProjectEndpoints.Pack"/>), its ETag and when it was saved.</summary>
internal sealed record StoredPlaylists(string PackedFile, string ETag, DateTimeOffset SavedAt);

internal sealed class ConflictException() : Exception("The project changed since it was read.");