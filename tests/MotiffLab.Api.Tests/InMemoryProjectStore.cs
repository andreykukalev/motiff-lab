using System.Collections.Concurrent;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Api.Tests;

/// <summary>The store's contract without Cosmos DB: documents keyed by player and project, with ETags.</summary>
internal sealed class InMemoryProjectStore : IProjectStore
{
    private readonly ConcurrentDictionary<(string Owner, string Id), (StoredSummary Summary, StoredProject Project)> _documents = new();
    private int _version;

    public Task<IReadOnlyList<StoredSummary>> ListAsync(string ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredSummary>>([.. _documents
            .Where(entry => entry.Key.Owner == ownerId)
            .Select(entry => entry.Value.Summary)
            .OrderByDescending(summary => summary.LastOpened)]);

    public Task<StoredProject?> GetAsync(string ownerId, string id, CancellationToken cancellationToken) =>
        Task.FromResult(_documents.TryGetValue((ownerId, id), out var document) ? document.Project : null);

    public Task<string> PutAsync(string ownerId, ProjectSummary summary, string packedFile, string? ifMatch, CancellationToken cancellationToken)
    {
        if (ifMatch is not null && (!_documents.TryGetValue((ownerId, summary.Id), out var current) || current.Project.ETag != ifMatch))
        {
            throw new ConflictException();
        }

        string etag = $"\"{Interlocked.Increment(ref _version)}\"";
        var row = new StoredSummary(summary.Id, summary.FileName, summary.MotifCount, summary.VariationCount, summary.LastOpened, summary.ResumeCount, summary.IdeaCount, DateTimeOffset.UtcNow);
        _documents[(ownerId, summary.Id)] = (row, new StoredProject(packedFile, etag));
        return Task.FromResult(etag);
    }

    public Task<bool> DeleteAsync(string ownerId, string id, CancellationToken cancellationToken) =>
        Task.FromResult(_documents.TryRemove((ownerId, id), out _));

    public Task<int> DeleteAllAsync(string ownerId, CancellationToken cancellationToken)
    {
        var keys = _documents.Keys.Where(key => key.Owner == ownerId).ToList();
        keys.ForEach(key => _documents.TryRemove(key, out _));
        return Task.FromResult(keys.Count);
    }
}