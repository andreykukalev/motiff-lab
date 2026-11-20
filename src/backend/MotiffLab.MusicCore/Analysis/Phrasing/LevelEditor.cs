using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis.Phrasing;

/// <summary>
/// The edits of one phrase level, made the same way at every level (D13): the player's edit (make a unit, join, split)
/// as <see cref="PhraseEdits.Adding"/> and <see cref="PhraseEdits.Removing"/> boundary ticks over the level's edits now,
/// kept <see cref="Within"/> what the level can hold. <see cref="PhraseMap"/>, <see cref="ShortMap"/> and
/// <see cref="SentenceMap"/> each say where their boundaries are, where they may be and what they found.
/// </summary>
/// <remarks>Pure: the same edits, places, rule and found boundaries give the same edits.</remarks>
internal sealed class LevelEditor
{
    private readonly PhraseEdits _edits;
    private readonly IReadOnlyList<Tick> _places;
    private readonly Func<Tick, bool> _allows;
    private readonly IReadOnlySet<Tick>? _found;

    /// <param name="edits">The level's edits now.</param>
    /// <param name="places">
    /// Where the level has a boundary in effect, or may have one, in time order: the boundaries in effect for the phrases
    /// and the short units, every phrase boundary for the sentences.
    /// </param>
    /// <param name="allows">Whether the level can hold a boundary of its own at a tick; an edit anywhere else is left out.</param>
    /// <param name="found">
    /// The boundaries the level found, when its edits are the changes from them: taking away a boundary that was not found
    /// is then no edit, and setting one that was confirms it. Null for the sentences, whose edits are decisions that stand
    /// however the grouping falls.
    /// </param>
    public LevelEditor(PhraseEdits edits, IReadOnlyList<Tick> places, Func<Tick, bool> allows, IReadOnlySet<Tick>? found)
    {
        _edits = edits;
        _places = places;
        _allows = allows;
        _found = found;
    }

    /// <summary>The edits that make the stretch from <paramref name="start"/> to <paramref name="stop"/> one unit: a boundary at each edge, none at a place strictly between.</summary>
    public PhraseEdits Make(Tick start, Tick stop) =>
        Within(_edits.Adding(stop > start ? [start, stop] : [start]).Removing(Between(start, stop)));

    /// <summary>The edits that take away the boundary at <paramref name="at"/>, joining the units on either side.</summary>
    public PhraseEdits Join(Tick at) => Within(_edits.Removing([at]));

    /// <summary>The edits that take away every boundary strictly between <paramref name="start"/> and <paramref name="stop"/> and leave the edges as they are.</summary>
    public PhraseEdits JoinBetween(Tick start, Tick stop) => Within(_edits.Removing(Between(start, stop)));

    /// <summary>The edits that set a boundary at <paramref name="at"/> and change nothing else; the edits as they are where the level allows none.</summary>
    public PhraseEdits Split(Tick at) => _allows(at) ? Within(_edits.Adding([at])) : _edits;

    /// <summary>
    /// The edits that move the boundary at <paramref name="from"/> to <paramref name="to"/> as one edit: taken away there and
    /// set here. Set where the level found one, it is that found boundary back rather than one of the player's own; set where
    /// the level allows none (the start or the end of the piece), the boundary is only taken away.
    /// </summary>
    public PhraseEdits Move(Tick from, Tick to)
    {
        PhraseEdits moved = Within(_edits.Removing([from]).Adding([to]));
        return _found is not null && _found.Contains(to) ? new PhraseEdits([.. moved.Added.Where(tick => tick != to)], moved.Removed) : moved;
    }

    /// <summary>A place less than a beat from <paramref name="edge"/>, the nearest, the earlier of two as near; or the edge itself.</summary>
    public Tick Snap(Tick edge, MeterMap meter)
    {
        long beat = meter.SignatureAt(edge).BeatTicks;
        return _places
            .Where(tick => Math.Abs(tick.Value - edge.Value) < beat)
            .OrderBy(tick => Math.Abs(tick.Value - edge.Value))
            .Cast<Tick?>()
            .FirstOrDefault() ?? edge;
    }

    /// <summary>
    /// <paramref name="edits"/> kept to what the level can hold: only ticks it allows, a boundary taken away only where
    /// one was found (when the level compares with what it found), and none both set and taken away.
    /// </summary>
    private PhraseEdits Within(PhraseEdits edits)
    {
        Tick[] added = [.. edits.Added.Where(_allows)];
        return new PhraseEdits(added, [.. edits.Removed.Where(tick => _allows(tick) && (_found is null || _found.Contains(tick)) && Array.BinarySearch(added, tick) < 0)]);
    }

    private IEnumerable<Tick> Between(Tick start, Tick stop) => _places.Where(tick => tick > start && tick < stop);
}