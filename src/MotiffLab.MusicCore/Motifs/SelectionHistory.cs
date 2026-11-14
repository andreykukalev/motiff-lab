namespace MotiffLab.MusicCore.Motifs;

/// <summary>
/// Undo and Redo of the selection (#511 section 11): one step is one finished gesture that changed the selection: made,
/// an edge moved (from inside or by a handle), taken away (✕ or a hold outside), or replaced (a hold, the selection gone,
/// a drag: one step). Every change a gesture makes carries its name, so they are kept as one step. A touch that changes
/// nothing, a gesture cancelled, scrolling, the zoom, playing and the loop are no steps. A new step drops what Redo had.
/// Up to <see cref="Depth"/> steps for the piece while it is open, apart from any other Undo. The iPad keeps the same
/// rules in Swift (ipad/App/MotiffLab/SelectionHistory.swift).
/// </summary>
/// <typeparam name="T">The selection as the owner selects it again, such as its span; null is no selection.</typeparam>
public sealed class SelectionHistory<T>
    where T : struct, IEquatable<T>
{
    /// <summary>The most steps kept: older ones go.</summary>
    public const int Depth = 50;

    private readonly List<SelectionStep<T>> _done = [];
    private readonly List<SelectionStep<T>> _undone = [];

    public bool CanUndo => _done.Count > 0;

    public bool CanRedo => _undone.Count > 0;

    /// <summary>The steps that Undo takes back, oldest first.</summary>
    public IReadOnlyList<SelectionStep<T>> Done => _done;

    /// <summary>
    /// The selection changed from <paramref name="before"/> to <paramref name="after"/>, by the gesture named (null for a
    /// change of its own, such as ✕ or a menu): the step it makes, or the step of that gesture it joins; null when the
    /// selection ends as it was.
    /// </summary>
    public SelectionStep<T>? Record(T? before, T? after, string? gesture = null)
    {
        if (gesture is not null && _done.Count > 0 && _done[^1].Gesture == gesture)
        {
            SelectionStep<T> last = _done[^1];
            _done.RemoveAt(_done.Count - 1);
            SelectionStep<T> joined = new(last.Before, after, gesture, last.Cleared || after is null);
            if (Nullable.Equals(joined.Before, joined.After))
            {
                return null;
            }

            _done.Add(joined);
            return joined;
        }

        if (Nullable.Equals(before, after))
        {
            return null;
        }

        SelectionStep<T> step = new(before, after, gesture, after is null);
        _done.Add(step);
        if (_done.Count > Depth)
        {
            _done.RemoveRange(0, _done.Count - Depth);
        }

        _undone.Clear();
        return step;
    }

    /// <summary>Takes the last step back: the owner selects its <see cref="SelectionStep{T}.Before"/> again.</summary>
    public SelectionStep<T>? Undo()
    {
        if (_done.Count == 0)
        {
            return null;
        }

        SelectionStep<T> step = _done[^1];
        _done.RemoveAt(_done.Count - 1);
        _undone.Add(step);
        return step;
    }

    /// <summary>Takes the step undone last again: the owner selects its <see cref="SelectionStep{T}.After"/> again.</summary>
    public SelectionStep<T>? Redo()
    {
        if (_undone.Count == 0)
        {
            return null;
        }

        SelectionStep<T> step = _undone[^1];
        _undone.RemoveAt(_undone.Count - 1);
        _done.Add(step);
        return step;
    }

    /// <summary>The gesture named was cancelled: its step goes, as if it never was; the owner selects its before again.</summary>
    public SelectionStep<T>? Revert(string gesture)
    {
        if (_done.Count == 0 || _done[^1].Gesture != gesture)
        {
            return null;
        }

        SelectionStep<T> step = _done[^1];
        _done.RemoveAt(_done.Count - 1);
        return step;
    }
}

/// <summary>What a step of the selection's history did; the toast «Вернуть» comes after a clear and a replace.</summary>
public enum SelectionStepKind
{
    Create,
    Change,
    Clear,
    Replace,
}

/// <summary>One step: the selection before and after it, the gesture that made it, and whether it was gone on the way.</summary>
public sealed record SelectionStep<T>(T? Before, T? After, string? Gesture, bool Cleared)
    where T : struct, IEquatable<T>
{
    public SelectionStepKind Kind =>
        Before is null ? SelectionStepKind.Create
        : After is null ? SelectionStepKind.Clear
        : Cleared ? SelectionStepKind.Replace
        : SelectionStepKind.Change;
}