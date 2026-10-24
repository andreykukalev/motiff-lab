using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>The parameters of one operation. Each operation derives its own record, so steps compare by value.</summary>
public abstract record OperationParameters;

/// <summary>
/// What an operation reads besides the notes and its parameters: the motif's span, the Keep choices,
/// a seed and the key. Operations that pick among equal choices use only <see cref="Seed"/>, never a
/// clock or a shared random source, so the same input always gives the same output.
/// </summary>
/// <param name="Key">
/// The key diatonic operations count scale steps in: the key the motif's analysis read
/// (<see cref="MotifAnalysis.Key"/>). Null when there is none, which leaves diatonic operations unavailable.
/// </param>
public sealed record VariationContext(TickSpan Span, IReadOnlySet<KeepProperty> Keep, int Seed = 0, MusicalKey? Key = null)
{
    public static VariationContext For(MotifSelection selection, IReadOnlySet<KeepProperty> keep, int seed = 0, MusicalKey? key = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return new VariationContext(selection.Span, keep, seed, key);
    }
}

/// <summary>
/// The contract every catalogue operation meets (S1-E1, S1-E3): input notes, context, parameters
/// and seed in; notes and a structured diff out. Operations are pure.
/// </summary>
public interface IVariationOperation
{
    /// <summary>The catalogue id, for example "V1".</summary>
    string Id { get; }

    /// <summary>The operation's name as the player reads it, for example "Rhythm reshape".</summary>
    string Name { get; }

    /// <summary>Why the Keep choices rule the operation out (its Keep table row, S1-E2), or null when it can run.</summary>
    string? DisabledReason(IReadOnlySet<KeepProperty> keep);

    /// <summary>
    /// Why these parameters cannot apply to these notes, including results that would break an
    /// operation rule (off the grid, out of range, voices crossing), or null when they can.
    /// </summary>
    string? UnavailableReason(IReadOnlyList<Note> notes, VariationContext context, OperationParameters parameters);

    /// <summary>Applies the operation and checks the result against <paramref name="notes"/>.</summary>
    /// <exception cref="InvalidOperationException">The Keep choices rule it out, or the parameters are unavailable.</exception>
    Variation Apply(IReadOnlyList<Note> notes, VariationContext context, OperationParameters parameters);
}

/// <summary>
/// The shared part of every operation: checks parameters, Keep choices and the operation rules, then
/// builds the diff, the Keep checks and the explanation from the notes the operation returns.
/// </summary>
/// <remarks>
/// <see cref="Transform"/> returns the varied notes. A note that keeps its input id is a changed
/// note; a new id is an added note and must be derived from the input deterministically; an input
/// id that is missing is a removed note. Notes the operation changes are marked user-edited.
/// </remarks>
public abstract class VariationOperation<TParameters> : IVariationOperation
    where TParameters : OperationParameters
{
    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract string? DisabledReason(IReadOnlySet<KeepProperty> keep);

    public string? UnavailableReason(IReadOnlyList<Note> notes, VariationContext context, TParameters parameters) =>
        Run(notes, context, parameters).Reason;

    public Variation Apply(IReadOnlyList<Note> notes, VariationContext context, TParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(context);
        string? disabled = DisabledReason(context.Keep);
        if (disabled is not null)
        {
            throw new InvalidOperationException(disabled);
        }

        (string? reason, Note[]? after, IReadOnlyList<string> flags) = Run(notes, context, parameters);
        if (reason is not null)
        {
            throw new InvalidOperationException(reason);
        }

        return VariationFacts.Build(Id, context.Span, notes, after!, context.Keep, flags);
    }

    /// <summary>Applies the operation to the selected notes of <paramref name="notes"/>.</summary>
    public Variation Apply(IEnumerable<Note> notes, MotifSelection selection, IReadOnlySet<KeepProperty> keep, TParameters parameters) =>
        Apply(VariationFacts.MotifNotes(notes, selection), VariationContext.For(selection, keep), parameters);

    string? IVariationOperation.UnavailableReason(IReadOnlyList<Note> notes, VariationContext context, OperationParameters parameters) =>
        UnavailableReason(notes, context, Cast(parameters));

    Variation IVariationOperation.Apply(IReadOnlyList<Note> notes, VariationContext context, OperationParameters parameters) =>
        Apply(notes, context, Cast(parameters));

    /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> when a parameter is outside the operation's range.</summary>
    protected abstract void Validate(TParameters parameters);

    /// <summary>Why the operation cannot run on these notes before trying it, or null.</summary>
    protected virtual string? Precondition(IReadOnlyList<Note> notes, VariationContext context, TParameters parameters) => null;

    /// <summary>The varied notes. Pure: reads only its arguments.</summary>
    protected abstract IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, TParameters parameters);

    private (string? Reason, Note[]? After, IReadOnlyList<string> Flags) Run(IReadOnlyList<Note> notes, VariationContext context, TParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parameters);
        Validate(parameters);
        if (notes.Count == 0)
        {
            return ("The selection has no notes to vary.", null, []);
        }

        string? reason = Precondition(notes, context, parameters);
        if (reason is not null)
        {
            return (reason, null, []);
        }

        Note[] after = [.. Transform(notes, context, parameters)];
        RuleReport rules = OperationRules.Check(notes, after);
        return rules.Violation is { } violation
            ? ($"{Name} {violation}, so it is not available for this selection.", null, [])
            : (null, after, rules.Flags);
    }

    private static TParameters Cast(OperationParameters parameters) => parameters as TParameters
        ?? throw new ArgumentException($"Expected {typeof(TParameters).Name}.", nameof(parameters));
}

/// <summary>The operations this version offers, by catalogue id. New operations register here.</summary>
public static class VariationOperations
{
    public static IReadOnlyList<IVariationOperation> All { get; } = [RhythmReshape.Instance, IntervalReshape.Instance, ChangeDirection.Instance];

    /// <summary>The operation with this catalogue id, or null when this version does not have it.</summary>
    public static IVariationOperation? Find(string id) => All.FirstOrDefault(operation => operation.Id == id);
}