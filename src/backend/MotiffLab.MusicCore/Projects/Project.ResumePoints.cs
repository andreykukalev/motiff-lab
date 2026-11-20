namespace MotiffLab.MusicCore.Projects;

/// <summary>Where the player left off (0018, section 9): the resume points.</summary>
public sealed partial record Project
{
    /// <summary>Where the player left off (0018, section 9), in the order the points were made.</summary>
    public IReadOnlyList<ResumePoint> ResumePoints { get; init; } = [];

    /// <summary>The resume points most recently active first; Resume opens the first.</summary>
    public IReadOnlyList<ResumePoint> RecentResumePoints => [.. ResumePoints.OrderByDescending(point => point.LastActive).ThenByDescending(point => point.CreatedAt)];

    public ResumePoint? FindResumePoint(string id) => ResumePoints.FirstOrDefault(point => point.Id == id);

    /// <summary>The project with the resume point added after the others.</summary>
    /// <exception cref="ArgumentException">A resume point with the same id is already there.</exception>
    public Project AddResumePoint(ResumePoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return ResumePoints.Any(kept => kept.Id == point.Id)
            ? throw new ArgumentException($"The project already has a resume point {point.Id}.", nameof(point))
            : this with { ResumePoints = [.. ResumePoints, point] };
    }

    /// <summary>The project with one resume point changed by <paramref name="change"/>; the others stay as they are.</summary>
    /// <exception cref="KeyNotFoundException">No resume point has the id.</exception>
    public Project ChangeResumePoint(string id, Func<ResumePoint, ResumePoint> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        ResumePoint changed = change(FindResumePoint(id) ?? throw new KeyNotFoundException($"The project has no resume point {id}."));
        return this with { ResumePoints = [.. ResumePoints.Select(point => point.Id == id ? changed : point)] };
    }

    /// <exception cref="KeyNotFoundException">No resume point has the id.</exception>
    public Project RemoveResumePoint(string id)
    {
        _ = FindResumePoint(id) ?? throw new KeyNotFoundException($"The project has no resume point {id}.");
        return this with { ResumePoints = [.. ResumePoints.Where(point => point.Id != id)] };
    }

    /// <summary>"r_0004": one more than the highest resume point number, so ids stay stable for the life of the project.</summary>
    public string NextResumePointId() => NextId("r_", ResumePoints.Select(point => point.Id).Concat(UnknownIds(Unknown, "resumePoints")));
}