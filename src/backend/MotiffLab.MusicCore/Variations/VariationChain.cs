using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Variations;

/// <summary>One step of a chain: the operation, its parameters, and the variation it made of the notes before it.</summary>
public sealed record VariationStep(IVariationOperation Operation, OperationParameters Parameters, Variation Result);

/// <summary>
/// A variation as a chain of steps over the original motif (S1-E5, S1-E8). Each step is proposed,
/// then accepted or rejected. The original notes are never
/// changed: every step reads the notes the step before it made. Immutable: every action returns a
/// new chain, so the same choices always give the same chain.
/// </summary>
public sealed class VariationChain
{
    /// <summary>The most accepted steps a chain holds (S1-E5).</summary>
    public const int MaxSteps = 50;

    /// <summary>What joins the catalogue ids of a chain's steps in <see cref="Variation.Operation"/>.</summary>
    public const string StepSeparator = " › ";

    private VariationChain(
        IReadOnlyList<Note> original,
        VariationContext context,
        IReadOnlyList<VariationStep> steps,
        VariationStep? pending)
    {
        Original = original;
        Context = context;
        Steps = steps;
        Pending = pending;
        Current = steps.Count == 0 ? original : steps[^1].Result.Notes;
        IReadOnlyList<VariationStep> shown = pending is null ? steps : [.. steps, pending];
        Result = shown.Count == 0 ? null : Combine(original, context, shown);
    }

    /// <summary>The motif's notes, exactly as they were selected.</summary>
    public IReadOnlyList<Note> Original { get; }

    public VariationContext Context { get; }

    /// <summary>The accepted steps, oldest first.</summary>
    public IReadOnlyList<VariationStep> Steps { get; }

    /// <summary>The step the player is hearing but has not accepted or rejected yet, or null.</summary>
    public VariationStep? Pending { get; }

    /// <summary>The notes after the accepted steps: the original when there are none.</summary>
    public IReadOnlyList<Note> Current { get; }

    /// <summary>
    /// The original against the notes after every accepted step and the pending one: what is heard
    /// and drawn, with the Keep checks against the original. Null when there are no steps.
    /// </summary>
    public Variation? Result { get; }

    public bool CanUndo => Steps.Count > 0;

    /// <summary>Why the pending step cannot be accepted, or null when it can.</summary>
    public string? AcceptDisabledReason => Pending is null
        ? Context.Words.T("There is no step to accept.", "Нет шага, который можно принять.")
        : Steps.Count >= MaxSteps ? Full : null;

    /// <summary>Why a hand edit cannot be added, or null when it can: the chain is full.</summary>
    public string? EditDisabledReason => Steps.Count + (Pending is null ? 0 : 1) >= MaxSteps ? Full : null;

    private string Full => Context.Words.T($"A variation holds up to {MaxSteps} steps.", $"В вариации может быть не больше {MaxSteps} шагов.");

    /// <summary>A chain with no steps yet over the motif's notes.</summary>
    public static VariationChain Start(IReadOnlyList<Note> original, VariationContext context)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(context);
        return new VariationChain([.. original], context, [], null);
    }

    /// <summary>Why this step cannot be proposed on the current notes, or null when it can.</summary>
    public string? UnavailableReason(IVariationOperation operation, OperationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation.DisabledReason(Context.Keep, Context.Words) ?? operation.UnavailableReason(Current, Context, parameters);
    }

    /// <summary>Runs a step on the current notes and holds it as pending, in place of any pending step.</summary>
    /// <exception cref="InvalidOperationException">The Keep choices rule the operation out, or the parameters are unavailable.</exception>
    public VariationChain Propose(IVariationOperation operation, OperationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return new VariationChain(Original, Context, Steps, Step(operation, parameters, Current, Context));
    }

    /// <summary>Adds the pending step to the chain.</summary>
    public VariationChain Accept()
    {
        if (AcceptDisabledReason is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        return new VariationChain(Original, Context, [.. Steps, Pending!], null);
    }

    /// <summary>The notes as they are heard: after the accepted steps and the pending one.</summary>
    public IReadOnlyList<Note> Heard => Pending?.Result.Notes ?? Current;

    /// <summary>Drops the pending step; the accepted steps stay.</summary>
    internal VariationChain Reject() => new(Original, Context, Steps, null);

    private static VariationStep Step(IVariationOperation operation, OperationParameters parameters, IReadOnlyList<Note> notes, VariationContext context) =>
        new(operation, parameters, operation.Apply(notes, context, parameters));

    private static Variation Combine(IReadOnlyList<Note> original, VariationContext context, IReadOnlyList<VariationStep> steps)
    {
        IReadOnlyList<Note> final = steps[^1].Result.Notes;
        return VariationFacts.Build(
            string.Join(StepSeparator, steps.Select(static step => step.Operation.Id)),
            context.Span,
            original,
            final,
            context.Keep,
            OperationRules.Check(original, final, context.Words).Flags,
            context.Key,
            context.Meter,
            context.Words);
    }
}