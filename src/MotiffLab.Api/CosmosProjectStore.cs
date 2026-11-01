using System.Net;

using Microsoft.Azure.Cosmos;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Api;

/// <summary>
/// Projects in the <c>projects</c> container of the <c>motifflab</c> database, partitioned by <c>/ownerId</c>,
/// so every read and write stays inside the calling player's partition.
/// </summary>
internal sealed class CosmosProjectStore(Container container) : IProjectStore
{
    private const string Kind = "project";

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

    public async Task<StoredProject?> GetAsync(string ownerId, string id, CancellationToken cancellationToken)
    {
        try
        {
            ItemResponse<ProjectDocument> read = await container.ReadItemAsync<ProjectDocument>(id, new PartitionKey(ownerId), cancellationToken: cancellationToken);
            return new StoredProject(read.Resource.File, read.ETag);
        }
        catch (CosmosException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string> PutAsync(string ownerId, ProjectSummary summary, string packedFile, string? ifMatch, CancellationToken cancellationToken)
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
        try
        {
            ItemResponse<ProjectDocument> written = await container.UpsertItemAsync(
                document,
                new PartitionKey(ownerId),
                new ItemRequestOptions { IfMatchEtag = ifMatch, EnableContentResponseOnWrite = false },
                cancellationToken);
            return written.ETag;
        }
        catch (CosmosException error) when (error.StatusCode is HttpStatusCode.PreconditionFailed)
        {
            throw new ConflictException();
        }
    }

    public async Task<bool> DeleteAsync(string ownerId, string id, CancellationToken cancellationToken)
    {
        try
        {
            await container.DeleteItemAsync<ProjectDocument>(id, new PartitionKey(ownerId), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<int> DeleteAllAsync(string ownerId, CancellationToken cancellationToken)
    {
        // A player's documents are few, so they are deleted one by one rather than by partition.
        var ids = new List<string>();
        using (FeedIterator<IdOnly> feed = container.GetItemQueryIterator<IdOnly>(
            new QueryDefinition("SELECT c.id FROM c"),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(ownerId) }))
        {
            while (feed.HasMoreResults)
            {
                ids.AddRange((await feed.ReadNextAsync(cancellationToken)).Select(row => row.Id));
            }
        }

        foreach (string id in ids)
        {
            await DeleteAsync(ownerId, id, cancellationToken);
        }

        return ids.Count;
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

    private sealed record IdOnly(string Id);
}