using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Api;

/// <summary>
/// Where a player's projects are kept: one document per project, in the player's own partition. The project
/// file (with its sessions, observations and source) is stored packed; the summary is kept beside it so a list
/// never loads the files.
/// </summary>
internal interface IProjectStore
{
    Task<IReadOnlyList<StoredSummary>> ListAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>The project, or null when the player has none with this id.</summary>
    Task<StoredProject?> GetAsync(string ownerId, string id, CancellationToken cancellationToken);

    /// <summary>Writes the project and returns its new ETag.</summary>
    /// <param name="ifMatch">When given, the write happens only if the stored project still has this ETag.</param>
    /// <exception cref="ConflictException">The stored project has changed since <paramref name="ifMatch"/>.</exception>
    Task<string> PutAsync(string ownerId, ProjectSummary summary, string packedFile, string? ifMatch, CancellationToken cancellationToken);

    /// <summary>False when the player has no project with this id.</summary>
    Task<bool> DeleteAsync(string ownerId, string id, CancellationToken cancellationToken);

    /// <summary>Deletes everything the player keeps here, and returns how many projects that was.</summary>
    Task<int> DeleteAllAsync(string ownerId, CancellationToken cancellationToken);
}

/// <summary>A row of the player's project list.</summary>
internal sealed record StoredSummary(
    string Id,
    string FileName,
    int MotifCount,
    int VariationCount,
    DateTimeOffset LastOpened,
    int SessionCount,
    int IdeaCount,
    DateTimeOffset SavedAt);

internal sealed record StoredProject(string PackedFile, string ETag);

internal sealed class ConflictException() : Exception("The project changed since it was read.");