using System.Net;

using Microsoft.Azure.Cosmos;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Api;

/// <summary>
/// Projects, and the player's profile, playlists and reset stamp, in the <c>projects</c> container of the <c>motifflab</c>
/// database, partitioned by <c>/ownerId</c>, so every read and write stays inside the calling player's partition.
/// </summary>
internal sealed class CosmosProjectStore(Container container) : IProjectStore
{
    private const string Kind = "project";

    /// <summary>The profile's kind and id: not a project id (those have no dot), so no project can take its place.</summary>
    private const string ProfileKind = "profile";
    private const string ProfileId = "player.profile";

    /// <summary>The reset stamp's kind and id (TestReset), with a dot for the same reason; lists read only projects.</summary>
    private const string ResetKind = "reset";
    private const string ResetId = "player.reset";

    /// <summary>The playlists file's kind and id, with a dot for the same reason.</summary>
    private const string PlaylistsKind = "playlists";
    private const string PlaylistsId = "player.playlists";

    public async Task<IReadOnlyList<StoredSummary>> ListAsync(string ownerId, CancellationToken cancellationToken)
    {
        // Every field but the file.
        QueryDefinition query = new QueryDefinition(
            "SELECT c.id, c.fileName, c.motifCount, c.variationCount, c.lastOpened, c.sessionCount, c.ideaCount, c.savedAt " +
            "FROM c WHERE c.kind = @kind ORDER BY c.lastOpened DESC")
            .WithParameter("@kind", Kind);
        var rows = new List<StoredSummary>();
        using FeedIterator<StoredSummary> feed = container.GetItemQueryIterator<StoredSummary>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(ownerId) });
        while (feed.HasMoreResults)
        {
            rows.AddRange(await feed.ReadNextAsync(cancellationToken));
        }

        return rows;
    }

    public async Task<StoredProject?> GetAsync(string ownerId, string id, CancellationToken cancellationToken) =>
        await ReadAsync<ProjectDocument>(ownerId, id, cancellationToken) is { } read ? new StoredProject(read.Resource.File, read.ETag) : null;

    public Task<string> PutAsync(string ownerId, ProjectSummary summary, string packedFile, string? ifMatch, CancellationToken cancellationToken)
    {
        var document = new ProjectDocument(
            summary.Id,
            ownerId,
            Kind,
            summary.FileName,
            summary.MotifCount,
            summary.VariationCount,
            summary.LastOpened,
            summary.ResumeCount,
            summary.IdeaCount,
            DateTimeOffset.UtcNow,
            packedFile);
        return WriteAsync(document, summary.Id, ownerId, ifMatch, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string ownerId, string id, string? ifMatch, CancellationToken cancellationToken)
    {
        try
        {
            await container.DeleteItemAsync<ProjectDocument>(id, new PartitionKey(ownerId), new ItemRequestOptions { IfMatchEtag = ifMatch }, cancellationToken);
            return true;
        }
        catch (CosmosException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (CosmosException error) when (error.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new ConflictException();
        }
    }

    public async Task<int> DeleteAllAsync(string ownerId, CancellationToken cancellationToken)
    {
        // A player's documents are few, so they are deleted one by one rather than by partition.
        var rows = new List<IdOnly>();
        using (FeedIterator<IdOnly> feed = container.GetItemQueryIterator<IdOnly>(
            new QueryDefinition("SELECT c.id, c.kind FROM c"),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(ownerId) }))
        {
            while (feed.HasMoreResults)
            {
                rows.AddRange(await feed.ReadNextAsync(cancellationToken));
            }
        }

        foreach (IdOnly row in rows)
        {
            await DeleteAsync(ownerId, row.Id, ifMatch: null, cancellationToken);
        }

        return rows.Count(row => row.Kind == Kind);
    }

    public async Task<long?> GetResetAsync(string ownerId, CancellationToken cancellationToken) =>
        (await ReadAsync<ResetDocument>(ownerId, ResetId, cancellationToken))?.Resource.Stamp;

    public async Task<int> ResetAsync(string ownerId, long stamp, CancellationToken cancellationToken)
    {
        int deleted = await DeleteAllAsync(ownerId, cancellationToken);
        await container.UpsertItemAsync(
            new ResetDocument(ResetId, ownerId, ResetKind, stamp),
            new PartitionKey(ownerId),
            new ItemRequestOptions { EnableContentResponseOnWrite = false },
            cancellationToken);
        return deleted;
    }

    public async Task<StoredProfile?> GetProfileAsync(string ownerId, CancellationToken cancellationToken) =>
        await ReadAsync<ProfileDocument>(ownerId, ProfileId, cancellationToken) is { } read ? new StoredProfile(read.Resource.Name, read.Resource.Photo) : null;

    public Task PutProfileAsync(string ownerId, StoredProfile profile, CancellationToken cancellationToken) =>
        container.UpsertItemAsync(
            new ProfileDocument(ProfileId, ownerId, ProfileKind, profile.Name, profile.Photo, DateTimeOffset.UtcNow),
            new PartitionKey(ownerId),
            new ItemRequestOptions { EnableContentResponseOnWrite = false },
            cancellationToken);

    public async Task<StoredPlaylists?> GetPlaylistsAsync(string ownerId, CancellationToken cancellationToken) =>
        await ReadAsync<PlaylistsDocument>(ownerId, PlaylistsId, cancellationToken) is { } read ? new StoredPlaylists(read.Resource.File, read.ETag, read.Resource.SavedAt) : null;

    public Task<string> PutPlaylistsAsync(string ownerId, string packedFile, string? ifMatch, CancellationToken cancellationToken) =>
        WriteAsync(new PlaylistsDocument(PlaylistsId, ownerId, PlaylistsKind, DateTimeOffset.UtcNow, packedFile), PlaylistsId, ownerId, ifMatch, cancellationToken);

    /// <summary>The player's document with this id, or null when there is none.</summary>
    private async Task<ItemResponse<T>?> ReadAsync<T>(string ownerId, string id, CancellationToken cancellationToken)
    {
        try
        {
            return await container.ReadItemAsync<T>(id, new PartitionKey(ownerId), cancellationToken: cancellationToken);
        }
        catch (CosmosException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Writes a document over the one that still has <paramref name="ifMatch"/>, or, without it, only as a new one; its new ETag.</summary>
    private async Task<string> WriteAsync<T>(T document, string id, string ownerId, string? ifMatch, CancellationToken cancellationToken)
    {
        try
        {
            var options = new ItemRequestOptions { IfMatchEtag = ifMatch, EnableContentResponseOnWrite = false };
            ItemResponse<T> written = ifMatch is null
                ? await container.CreateItemAsync(document, new PartitionKey(ownerId), options, cancellationToken)
                : await container.ReplaceItemAsync(document, id, new PartitionKey(ownerId), options, cancellationToken);
            return written.ETag;
        }
        // 409: created over one kept with the id; 412: changed since; 404: gone since.
        catch (CosmosException error) when (error.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict or HttpStatusCode.NotFound)
        {
            throw new ConflictException();
        }
    }

    // Field names are camelCase through the Web serializer defaults: id, ownerId, kind, … file.
    private sealed record ProjectDocument(
        string Id,
        string OwnerId,
        string Kind,
        string FileName,
        int MotifCount,
        int VariationCount,
        DateTimeOffset LastOpened,
        int ResumeCount,
        int IdeaCount,
        DateTimeOffset SavedAt,
        string File);

    private sealed record ProfileDocument(string Id, string OwnerId, string Kind, string? Name, string? Photo, DateTimeOffset SavedAt);

    private sealed record PlaylistsDocument(string Id, string OwnerId, string Kind, DateTimeOffset SavedAt, string File);

    private sealed record ResetDocument(string Id, string OwnerId, string Kind, long Stamp);

    private sealed record IdOnly(string Id, string? Kind);
}