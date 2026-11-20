using MotiffLab.MusicCore.Import;

namespace MotiffLab.MusicCore.Projects;

/// <summary>The performances compared with the piece (decision record 0022) and the bars taken from them.</summary>
public sealed partial record Project
{
    /// <summary>The performances compared with the piece (decision record 0022), in the order they were added.</summary>
    public IReadOnlyList<Performance.SavedPerformance> Performances { get; init; } = [];

    public Performance.SavedPerformance? FindPerformance(string id) => Performances.FirstOrDefault(performance => performance.Id == id);

    /// <summary>The next free performance id: "perf_" and one more than the highest number used.</summary>
    public string NextPerformanceId() =>
        $"perf_{Performances.Select(performance => int.TryParse(performance.Id.AsSpan(5), out int number) ? number : 0).DefaultIfEmpty(0).Max() + 1}";

    /// <exception cref="ArgumentException">A performance with the same id is already there.</exception>
    public Project AddPerformance(Performance.SavedPerformance performance)
    {
        ArgumentNullException.ThrowIfNull(performance);
        return FindPerformance(performance.Id) is not null
            ? throw new ArgumentException($"There is already a performance {performance.Id}.", nameof(performance))
            : this with { Performances = [.. Performances, performance] };
    }

    /// <exception cref="KeyNotFoundException">No performance has the id.</exception>
    public Project ChangePerformance(string id, Func<Performance.SavedPerformance, Performance.SavedPerformance> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Performance.SavedPerformance changed = change(FindPerformance(id) ?? throw new KeyNotFoundException($"There is no performance {id}."));
        return changed.Id != id
            ? throw new ArgumentException("A performance keeps its id.", nameof(change))
            : this with { Performances = [.. Performances.Select(performance => performance.Id == id ? changed : performance)] };
    }

    /// <summary>The project without the performance, the bars taken from it and its Check flags.</summary>
    /// <exception cref="KeyNotFoundException">No performance has the id.</exception>
    public Project RemovePerformance(string id)
    {
        _ = FindPerformance(id) ?? throw new KeyNotFoundException($"There is no performance {id}.");
        List<Performance.SavedPerformance> left = [.. Performances.Where(performance => performance.Id != id)];
        return this with
        {
            Performances = left,
            KeptOriginals = [.. KeptOriginals.Where(original => original.Id != id)],
            Check = left.Count == 0 ? [.. (Check ?? []).Where(flag => flag.Source != CheckSource.Performance)] : Check,
        };
    }

    /// <summary>The project with a performance's Check flags (0022, section 6) in place of earlier ones from performances; flags the player resolved stay resolved.</summary>
    public Project WithPerformanceChecks(IEnumerable<CheckFlag> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        List<CheckFlag> earlier = [.. (Check ?? []).Where(flag => flag.Source == CheckSource.Performance)];
        IEnumerable<CheckFlag> fresh = flags.Select(flag => earlier.Any(old => old.Bar == flag.Bar && old.Reason == flag.Reason && old.Resolved) ? flag with { Resolved = true } : flag);
        return this with { Check = [.. (Check ?? []).Where(flag => flag.Source != CheckSource.Performance).Concat(fresh).OrderBy(flag => flag.Bar).ThenBy(flag => flag.Reason)] };
    }

    /// <summary>The bars taken from every performance into the score.</summary>
    public IEnumerable<Performance.TakenBar> TakenBars => Performances.SelectMany(performance => performance.Taken);
}