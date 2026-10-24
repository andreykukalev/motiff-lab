using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Variations;

/// <summary>One step of a chain: the operation, its parameters, and the variation it made of the notes before it.</summary>
public sealed record VariationStep(IVariationOperation Operation, OperationParameters Parameters, Variation Result);

/// <summary>
/// A variation as a chain of steps over the original motif (S1-E5, S1-E8). Each step is proposed,
/// then accepted or rejected; accepted steps can be undone and redone. The original notes are never
/// changed: every step reads the notes the step before it made. Immutable: every action returns a
/// new chain, so the same choices always give the same chain.
/// </summary>
public sealed class VariationChain
{
    /// <summary>The most accepted steps a chain holds, and so the depth of undo and redo (S1-E5).</summary>
    public const int MaxSteps = 50;

    /// <summary>What joins the catalogue ids of a chain's steps in <see cref="Variation.Operation"/>.</summary>
    public const string StepSeparator = " › ";

    private VariationChain(
        IReadOnlyList<Note> original,
        VariationContext context,
        IReadOnlyList<VariationStep> steps,
        VariationStep? pending,
        IReadOnlyList<VariationStep> undone)
    {
        Original = original;
        Context = context;
        Steps = steps;
        Pending = pending;
        Undone = undone;
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

    /// <summary>The undone steps that <see cref="Redo"/> brings back, the next one last.</summary>
    public IReadOnlyList<VariationStep> Undone { get; }

    /// <summary>The notes after the accepted steps: the original when there are none.</summary>
    public IReadOnlyList<Note> Current { get; }

    /// <summary>
    /// The original against the notes after every accepted step and the pending one: what is heard
    /// and drawn, with the Keep checks against the original. Null when there are no steps.
    /// </summary>
    public Variation? Result { get; }

    public bool CanUndo => Steps.Count > 0;

    public bool CanRedo => Undone.Count > 0;

    /// <summary>Why the pending step cannot be accepted, or null when it can.</summary>
    public string? AcceptDisabledReason => Pending is null
        ? "There is no step to accept."
        : Steps.Count >= MaxSteps ? $"A variation holds up to {MaxSteps} steps." : null;

    /// <summary>Why a hand edit cannot be added, or null when it can: the chain is full.</summary>
    public string? EditDisabledReason => Steps.Count + (Pending is null ? 0 : 1) >= MaxSteps ? $"A variation holds up to {MaxSteps} steps." : null;

    /// <summary>A chain with no steps yet over the motif's notes.</summary>
    public static VariationChain Start(IReadOnlyList<Note> original, VariationContext context)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(context);
        return new VariationChain([.. original], context, [], null, []);
    }

    /// <summary>Why this step cannot be proposed on the current notes, or null when it can.</summary>
    public string? UnavailableReason(IVariationOperation operation, OperationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation.DisabledReason(Context.Keep) ?? operation.UnavailableReason(Current, Context, parameters);
    }

    /// <summary>Runs a step on the current notes and holds it as pending, in place of any pending step.</summary>
    /// <exception cref="InvalidOperationException">The Keep choices rule the operation out, or the parameters are unavailable.</exception>
    public VariationChain Propose(IVariationOperation operation, OperationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return new VariationChain(Original, Context, Steps, Step(operation, parameters, Current, Context), Undone);
    }

    /// <summary>Adds the pending step to the chain. Redo is no longer possible after it.</summary>
    public VariationChain Accept()
    {
        if (AcceptDisabledReason is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        return new VariationChain(Original, Context, [.. Steps, Pending!], null, []);
    }

    /// <summary>
    /// Edits one note of the variation as it is heard (S1-E7): a pending step is accepted first, then
    /// the edit is added as an accepted step, so it can be undone like any other. Redo is no longer
    /// possible after it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The chain is full, or the edit cannot apply to the notes.</exception>
    public VariationChain Edit(HandEditParameters edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (EditDisabledReason is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        VariationChain heard = Pending is null ? this : Accept();
        return heard.Propose(HandEdit.Instance, edit).Accept();
    }

    /// <summary>Why the edit cannot apply to the notes as they are heard, or null when it can.</summary>
    public string? EditUnavailableReason(HandEditParameters edit) =>
        EditDisabledReason ?? HandEdit.Instance.UnavailableReason(Heard, Context, edit);

    /// <summary>The notes as they are heard: after the accepted steps and the pending one.</summary>
    public IReadOnlyList<Note> Heard => Pending?.Result.Notes ?? Current;

    /// <summary>Drops the pending step; the accepted steps stay.</summary>
    public VariationChain Reject() => new(Original, Context, Steps, null, Undone);

    /// <summary>Takes back the last accepted step, dropping any pending one.</summary>
    public VariationChain Undo()
    {
        if (!CanUndo)
        {
            throw new InvalidOperationException("There is no step to undo.");
        }

        return new VariationChain(Original, Context, Steps.Take(Steps.Count - 1).ToArray(), null, [.. Undone, Steps[^1]]);
    }

    /// <summary>Brings back the last undone step, dropping any pending one.</summary>
    public VariationChain Redo()
    {
        if (!CanRedo)
        {
            throw new InvalidOperationException("There is no step to redo.");
        }

        return new VariationChain(Original, Context, [.. Steps, Undone[^1]], null, Undone.Take(Undone.Count - 1).ToArray());
    }

    /// <summary>
    /// The same steps run again over other notes or Keep choices, for example when the selection
    /// changes. Null when a step can no longer run; undone steps that can no longer run are dropped.
    /// </summary>
    public VariationChain? Replay(IReadOnlyList<Note> original, VariationContext context)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(context);
        Note[] notes = [.. original];
        IReadOnlyList<Note> current = notes;
        var steps = new List<VariationStep>();
        foreach (VariationStep step in Pending is null ? Steps : [.. Steps, Pending])
        {
            if (Unavailable(step.Operation, step.Parameters, current, context) is not null)
            {
                return null;
            }

            VariationStep replayed = Step(step.Operation, step.Parameters, current, context);
            steps.Add(replayed);
            current = replayed.Result.Notes;
        }

        VariationStep? pending = null;
        if (Pending is not null)
        {
            pending = steps[^1];
            steps.RemoveAt(steps.Count - 1);
            current = steps.Count == 0 ? notes : steps[^1].Result.Notes;
        }

        var undone = new List<VariationStep>();
        for (int i = Undone.Count - 1; i >= 0 && Unavailable(Undone[i].Operation, Undone[i].Parameters, current, context) is null; i--)
        {
            VariationStep replayed = Step(Undone[i].Operation, Undone[i].Parameters, current, context);
            undone.Insert(0, replayed);
            current = replayed.Result.Notes;
        }

        return new VariationChain(notes, context, steps, pending, undone);
    }

    private static string? Unavailable(IVariationOperation operation, OperationParameters parameters, IReadOnlyList<Note> notes, VariationContext context) =>
        operation.DisabledReason(context.Keep) ?? operation.UnavailableReason(notes, context, parameters);

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
            OperationRules.Check(original, final).Flags,
            context.Key,
            context.Meter);
    }
}