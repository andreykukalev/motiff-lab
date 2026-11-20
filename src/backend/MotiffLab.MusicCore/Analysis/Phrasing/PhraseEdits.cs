using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis.Phrasing;

/// <summary>
/// The player's changes to the suggested phrase boundaries (S1-I5, S1-A2), saved with the project:
/// boundaries they set and suggested ones they took away. Moving a boundary is one of each.
/// </summary>
/// <remarks>
/// The edit algebra every phrase level shares (D13): an edit sets a boundary at some ticks (<see cref="Adding"/>) and
/// takes it away at others (<see cref="Removing"/>); <see cref="LevelEditor"/> then keeps the result to what the
/// level can hold, and <see cref="AppliedTo"/> puts saved edits over the boundaries a level found.
/// </remarks>
/// <param name="Added">Boundaries the player set, in time order, each once.</param>
/// <param name="Removed">Suggested boundaries the player took away, in time order, each once.</param>
public sealed record PhraseEdits(IReadOnlyList<Tick> Added, IReadOnlyList<Tick> Removed)
{
    public static PhraseEdits None { get; } = new([], []);

    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;

    /// <summary>These edits with a boundary set at each of <paramref name="ticks"/>: added, and no longer taken away.</summary>
    public PhraseEdits Adding(IEnumerable<Tick> ticks)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        Tick[] set = [.. ticks];
        return new PhraseEdits([.. Added.Concat(set).Distinct().Order()], [.. Removed.Except(set).Order()]);
    }

    /// <summary>These edits with the boundary at each of <paramref name="ticks"/> taken away: removed, and no longer added.</summary>
    public PhraseEdits Removing(IEnumerable<Tick> ticks)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        Tick[] taken = [.. ticks];
        return new PhraseEdits([.. Added.Except(taken).Order()], [.. Removed.Concat(taken).Distinct().Order()]);
    }

    /// <summary>
    /// The boundaries in effect at a level, in time order: the <paramref name="found"/> ones the player did not take away,
    /// then each one they set where the level <paramref name="allows"/> one, the found boundary there
    /// <paramref name="confirmed"/>, or else a new one they <paramref name="set"/>.
    /// </summary>
    internal IReadOnlyList<T> AppliedTo<T>(IEnumerable<T> found, Func<T, Tick> at, Func<Tick, bool> allows, Func<T, T> confirmed, Func<Tick, T> set)
        where T : class
    {
        var removed = Removed.ToHashSet();
        var merged = new SortedDictionary<Tick, T>();
        foreach (T boundary in found.Where(boundary => !removed.Contains(at(boundary))))
        {
            merged[at(boundary)] = boundary;
        }

        foreach (Tick tick in Added.Where(allows))
        {
            merged[tick] = merged.TryGetValue(tick, out T? source) ? confirmed(source) : set(tick);
        }

        return [.. merged.Values];
    }

    public bool Equals(PhraseEdits? other) =>
        other is not null && Added.SequenceEqual(other.Added) && Removed.SequenceEqual(other.Removed);

    public override int GetHashCode() => HashCode.Combine(Added.Count, Removed.Count);
}