using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>The parameters of one operation. Each operation derives its own record, so steps compare by value.</summary>
public abstract record OperationParameters
{
    /// <summary>
    /// Which of the selected notes the step may change; the others stay exactly as they were. Each
    /// step has its own scope, so one step can change the melody and the next the accompaniment.
    /// Operations that already choose their notes accept only <see cref="OperationScope.All"/>.
    /// </summary>
    public OperationScope Scope { get; init; } = OperationScope.All;
}

/// <summary>
/// What an operation reads besides the notes and its parameters: the motif's span, the Keep choices,
/// a seed, the key and the meter. Operations that pick among equal choices use only <see cref="Seed"/>, never a
/// clock or a shared random source, so the same input always gives the same output.
/// </summary>
/// <param name="Key">
/// The key diatonic operations count scale steps in: the key the motif's analysis read
/// (<see cref="MotifAnalysis.Key"/>). Null when there is none, which leaves diatonic operations unavailable.
/// </param>
/// <param name="Meter">
/// The piece's bars and beats, which beat-based operations (V1 Syncopate and Dotted) and the
/// explanation's beat names read. Null reads <see cref="MeterMap.Default"/>, 4/4 from the start.
/// </param>
/// <param name="Words">The language the explanation is written in (#134); English by default. It never changes the notes.</param>
public sealed record VariationContext(TickSpan Span, IReadOnlySet<KeepProperty> Keep, int Seed = 0, MusicalKey? Key = null, MeterMap? Meter = null, Words Words = default)
{
    /// <summary>The meter to count beats in: <see cref="Meter"/>, or 4/4 from the start when there is none.</summary>
    public MeterMap Beats => Meter ?? MeterMap.Default;

    public static VariationContext For(MotifSelection selection, IReadOnlySet<KeepProperty> keep, int seed = 0, MusicalKey? key = null, MeterMap? meter = null, Words words = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return new VariationContext(selection.Span, keep, seed, key, meter, words);
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

    /// <summary>The operation's name in the player's language (#134); <see cref="Name"/> in English.</summary>
    string NameIn(Words words);

    /// <summary>Why the Keep choices rule the operation out (its Keep table row, S1-E2), or null when it can run.</summary>
    string? DisabledReason(IReadOnlySet<KeepProperty> keep);

    /// <summary>Why the Keep choices rule the operation out, in the player's language (#134).</summary>
    string? DisabledReason(IReadOnlySet<KeepProperty> keep, Words words);

    /// <summary>Whether a step can be limited to <paramref name="scope"/> (<see cref="OperationParameters.Scope"/>).</summary>
    bool Supports(OperationScope scope);

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

    /// <summary>Why the Keep choices rule the operation out, read from its <see cref="KeepTable"/> row (S1-E2), or null when it can run.</summary>
    public string? DisabledReason(IReadOnlySet<KeepProperty> keep) => KeepTable.DisabledReason(Id, keep);

    public string? DisabledReason(IReadOnlySet<KeepProperty> keep, Words words) => KeepTable.DisabledReason(Id, keep, words);

    /// <summary>The name in the player's language, from the operation's Keep table row.</summary>
    public string NameIn(Words words) => KeepTable.Find(Id)?.NameIn(words) ?? Name;

    /// <summary>"Для этого выделения … не подходит: …" in Russian; "… , so it is not available for this selection." in English.</summary>
    protected string NotAvailable(Words words, string english, string russian) =>
        words.IsRussian ? $"Для этого выделения «{NameIn(words)}» не подходит: {russian}." : $"{Name} {english}, so it is not available for this selection.";

    /// <summary>Why a step counted in scale steps cannot run without a key.</summary>
    protected string NoKey(Words words) => words.T(
        $"{Name} counts in steps of the key's scale, and this selection has no key, so it is not available.",
        $"Для этого выделения «{NameIn(words)}» не подходит: счёт идёт по ступеням гаммы тональности, а тональность здесь не определена.");

    /// <summary>Whether a step can be limited to <paramref name="scope"/>; by default only the whole selection.</summary>
    public virtual bool Supports(OperationScope scope) => scope == OperationScope.All;

    public string? UnavailableReason(IReadOnlyList<Note> notes, VariationContext context, TParameters parameters) =>
        Run(notes, context, parameters).Reason;

    public Variation Apply(IReadOnlyList<Note> notes, VariationContext context, TParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(context);
        string? disabled = DisabledReason(context.Keep, context.Words);
        if (disabled is not null)
        {
            throw new InvalidOperationException(disabled);
        }

        (string? reason, Note[]? after, IReadOnlyList<string> flags) = Run(notes, context, parameters);
        if (reason is not null)
        {
            throw new InvalidOperationException(reason);
        }

        return VariationFacts.Build(Id, context.Span, notes, after!, context.Keep, flags, context.Key, context.Meter, context.Words);
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

    /// <summary>Why the operation cannot run on these notes before trying it, or null. Reads only the notes in the step's scope.</summary>
    protected virtual string? Precondition(IReadOnlyList<Note> notes, VariationContext context, TParameters parameters) => null;

    /// <summary>The varied notes in the step's scope; notes outside it are kept as they are. Pure: reads only its arguments.</summary>
    protected abstract IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, TParameters parameters);

    private (string? Reason, Note[]? After, IReadOnlyList<string> Flags) Run(IReadOnlyList<Note> notes, VariationContext context, TParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parameters);
        Validate(parameters);
        Words w = context.Words;
        if (notes.Count == 0)
        {
            return (w.T("The selection has no notes to vary.", "В выделении нет нот для изменения."), null, []);
        }

        if (!Supports(parameters.Scope))
        {
            return (w.T(
                $"{Name} works on the whole selection, so it can't be limited to {OperationScopes.Describe(parameters.Scope)}.",
                $"«{NameIn(w)}» меняет всё выделение сразу: ограничить шаг слоем «{OperationScopes.Describe(parameters.Scope, w)}» нельзя."), null, []);
        }

        (Note[] inside, Note[] outside) = OperationScopes.Split(notes, parameters.Scope);
        if (inside.Length == 0)
        {
            return (w.T(
                $"The selection has no notes in {OperationScopes.Describe(parameters.Scope)} to vary.",
                $"{OperationScopes.RussianIn(parameters.Scope)} нет нот для изменения."), null, []);
        }

        string? reason = Precondition(inside, context, parameters);
        if (reason is not null)
        {
            return (reason, null, []);
        }

        Note[] after = [.. outside, .. Transform(inside, context, parameters)];
        RuleReport rules = OperationRules.Check(notes, after, w);
        return rules.Violation is { } violation
            ? (w.IsRussian ? $"Для этого выделения «{NameIn(w)}» не подходит: {violation}." : $"{Name} {violation}, so it is not available for this selection.", null, [])
            : (null, after, rules.Flags);
    }

    private static TParameters Cast(OperationParameters parameters) => parameters as TParameters
        ?? throw new ArgumentException($"Expected {typeof(TParameters).Name}.", nameof(parameters));
}

/// <summary>The operations this version offers, by catalogue id. New operations register here.</summary>
public static class VariationOperations
{
    public static IReadOnlyList<IVariationOperation> All { get; } = [RhythmReshape.Instance, Articulation.Instance, MelodicSequence.Instance, AccompanimentPattern.Instance, IntervalReshape.Instance, ChangeDirection.Instance];

    /// <summary>The operation with this catalogue id, or <see cref="HandEdit"/> for its step id; null when this version does not have it.</summary>
    public static IVariationOperation? Find(string id) =>
        id == HandEdit.OperationId ? HandEdit.Instance : All.FirstOrDefault(operation => operation.Id == id);
}