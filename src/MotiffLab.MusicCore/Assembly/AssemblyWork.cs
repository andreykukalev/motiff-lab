using System.Collections.Immutable;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assembly;

/// <summary>What «Вернуть что было» for one step would take with it, said before it is done (decision record 0024, section 4).</summary>
/// <param name="Number">The step to return.</param>
/// <param name="Dependents">Steps that lean on it and go with it: a note change of a note it put in, a carry into its music.</param>
/// <param name="Staying">Later steps that do not lean on it and stay.</param>
/// <param name="Delta">How much longer the notes become, in ticks (negative: shorter).</param>
public sealed record RevertPlan(int Number, IReadOnlyList<int> Dependents, IReadOnlyList<int> Staying, long Delta);

/// <summary>
/// An assembly at work (decision record 0024): the base the player's notes start from, the sources, the journal and its
/// undo history, and the notes rebuilt from them. Immutable: every operation gives a new work, so undo is keeping the
/// one before.
/// </summary>
public sealed record AssemblyWork
{
    private readonly Dictionary<string, AssemblyMatch> _matches = new(StringComparer.Ordinal);

    /// <param name="baseMusic">The notes the assembly starts from: the piece's notes, or the notes before the first apply.</param>
    /// <param name="sources">The other versions' music, by id.</param>
    /// <param name="names">Each source's name, by id.</param>
    /// <param name="steps">The journal as kept with the piece.</param>
    /// <param name="next">The number the next step takes.</param>
    public AssemblyWork(SourceMusic baseMusic, IReadOnlyDictionary<string, SourceMusic> sources, IReadOnlyDictionary<string, string> names, IEnumerable<AssemblyStep> steps, int next)
    {
        Base = baseMusic ?? throw new ArgumentNullException(nameof(baseMusic));
        Sources = sources ?? throw new ArgumentNullException(nameof(sources));
        Names = names ?? throw new ArgumentNullException(nameof(names));
        Steps = [.. steps ?? throw new ArgumentNullException(nameof(steps))];
        Next = Math.Max(next, Steps.Count == 0 ? 1 : Steps.Max(step => step.Number) + 1);
    }

    public SourceMusic Base { get; }

    public IReadOnlyDictionary<string, SourceMusic> Sources { get; }

    public IReadOnlyDictionary<string, string> Names { get; }

    public ImmutableList<AssemblyStep> Steps { get; private init; }

    public int Next { get; private init; }

    private ImmutableStack<(ImmutableList<AssemblyStep> Steps, int Next)> Past { get; init; } = [];

    private ImmutableStack<(ImmutableList<AssemblyStep> Steps, int Next)> Future { get; init; } = [];

    private Assembled? Cached { get; set; }

    public bool CanUndo => !Past.IsEmpty || !Steps.IsEmpty;

    public bool CanRedo => !Future.IsEmpty;

    /// <summary>The journal's stamp: a comparison made for another stamp is stale.</summary>
    public string Stamp => string.Join(",", Steps.Select(step => step.Number));

    /// <summary>The player's notes as the journal builds them.</summary>
    public Assembled Result => Cached ??= Assembled.Build(Base, Source, Steps);

    /// <summary>Whether the notes differ from the base: there is at least one step that holds.</summary>
    public bool Changed => Result.Steps.Any(step => step.Applied);

    public SourceMusic? Source(string id) => Sources.GetValueOrDefault(id);

    public AssemblyNames NamesIn(Words words) => new(Names, Steps, words);

    /// <summary>A source compared with the notes as they are now; made once per journal.</summary>
    public AssemblyMatch Match(string sourceId)
    {
        if (!_matches.TryGetValue(sourceId, out AssemblyMatch? match) || match.Stamp != Stamp)
        {
            SourceMusic source = Source(sourceId) ?? throw new ArgumentException($"There is no source {sourceId}.", nameof(sourceId));
            match = AssemblyMatch.Of(sourceId, source, Result.Music, Stamp);
            _matches[sourceId] = match;
        }

        return match;
    }

    /// <summary>The carry a selection in a source would make, not yet in the journal.</summary>
    public CarryStep Carry(string sourceId, TickSpan from, AssemblyPlace to, int transpose, DateTimeOffset at, bool keep = false) =>
        new(Next, at, sourceId, from, to, transpose, keep && !to.Inserts);

    /// <summary>The sheet for a carry before it is made, with its ghost; <paramref name="heard"/> when its source is a recording.</summary>
    public CarrySheet Sheet(CarryStep carry, AssemblyPlace? replaceable, Tick playhead, Words words, bool heard = false) =>
        CarrySheet.Of(Base, Source, Steps, Result, carry, NamesIn(words), replaceable, playhead, words, heard);

    /// <summary>The work with a step added to the journal; the redo history goes.</summary>
    public AssemblyWork With(AssemblyStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Number != Next)
        {
            throw new ArgumentException($"The next step is No. {Next}.", nameof(step));
        }

        return Change(Steps.Add(step), Next + 1);
    }

    /// <summary>Several steps at once, as «Принять совпавшее» makes them: one undo takes them all back.</summary>
    public AssemblyWork With(IReadOnlyList<AssemblyStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return steps.Count == 0 ? this : Change(Steps.AddRange(steps), steps.Max(step => step.Number) + 1);
    }

    /// <summary>A note changed: removed, or moved by semitones.</summary>
    public AssemblyWork ChangeNote(string noteId, int? semitones, DateTimeOffset at) => With(new NoteStep(Next, at, noteId, semitones));

    /// <summary>A place transposed.</summary>
    public AssemblyWork Transpose(AssemblyPlace place, int semitones, DateTimeOffset at) => With(new TransposeStep(Next, at, place, semitones));

    /// <summary>
    /// «Принять совпавшее»: a carry for every place where the source matches the notes but has corrections, made only
    /// when <paramref name="match"/> was made for the journal as it is; null when it is stale.
    /// </summary>
    public AssemblyWork? Accept(AssemblyMatch match, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (match.Stamp != Stamp)
        {
            return null;
        }

        SourceMusic source = Source(match.SourceId) ?? throw new ArgumentException($"There is no source {match.SourceId}.", nameof(match));
        IReadOnlyList<Note> notes = Result.Music.Notes;
        int number = Next;
        List<AssemblyStep> steps = [];

        // From the end, so a place that changes length never moves the ones before it.
        foreach ((TickSpan from, TickSpan to) in match.Corrections(source, Result.Music).Reverse())
        {
            steps.Add(new CarryStep(number++, at, match.SourceId, from, AssemblyPlace.Over(notes, to)));
        }

        return With(steps);
    }

    /// <summary>⌘Z: the journal as it was before the last change, or without its last step after the piece was opened again.</summary>
    public AssemblyWork Undo()
    {
        if (!Past.IsEmpty)
        {
            (ImmutableList<AssemblyStep> steps, int next) = Past.Peek();
            return this with { Steps = steps, Next = next, Past = Past.Pop(), Future = Future.Push((Steps, Next)), Cached = null };
        }

        return Steps.IsEmpty ? this : this with { Steps = Steps.RemoveAt(Steps.Count - 1), Future = Future.Push((Steps, Next)), Cached = null };
    }

    /// <summary>⇧⌘Z.</summary>
    public AssemblyWork Redo()
    {
        if (Future.IsEmpty)
        {
            return this;
        }

        (ImmutableList<AssemblyStep> steps, int next) = Future.Peek();
        return this with { Steps = steps, Next = next, Future = Future.Pop(), Past = Past.Push((Steps, Next)), Cached = null };
    }

    /// <summary>What returning one step would take with it.</summary>
    public RevertPlan PlanRevert(int number)
    {
        if (Steps.All(step => step.Number != number))
        {
            throw new ArgumentException($"There is no step No. {number}.", nameof(number));
        }

        HashSet<int> going = [number];
        Assembled without = Result;
        for (bool more = true; more;)
        {
            without = Assembled.Build(Base, Source, Steps.Where(step => !going.Contains(step.Number)));
            more = false;
            foreach (StepOutcome outcome in without.Steps)
            {
                bool held = Result.OutcomeOf(outcome.Number)?.Applied ?? false;
                if (held && (!outcome.Applied || outcome.Leans.Any(going.Contains)) && going.Add(outcome.Number))
                {
                    more = true;
                }
            }
        }

        int[] dependents = [.. going.Where(step => step != number).Order()];
        int[] staying = [.. Steps.Select(step => step.Number).Where(step => step > number && !going.Contains(step))];
        return new RevertPlan(number, dependents, staying, without.Music.End.Value - Result.Music.End.Value);
    }

    /// <summary>«Вернуть что было»: the step and every step that leans on it leave the journal, as one undoable change.</summary>
    public AssemblyWork Revert(int number)
    {
        RevertPlan plan = PlanRevert(number);
        HashSet<int> going = [number, .. plan.Dependents];
        return Change(Steps.RemoveAll(step => going.Contains(step.Number)), Next);
    }

    /// <summary>«Начать заново»: the notes back to the base, as one undoable change.</summary>
    public AssemblyWork Restart() => Steps.IsEmpty ? this : Change([], Next);

    /// <summary>The same journal over another base or other sources: after a source is added or removed, or after applying.</summary>
    public AssemblyWork Over(SourceMusic baseMusic, IReadOnlyDictionary<string, SourceMusic> sources, IReadOnlyDictionary<string, string> names) =>
        new(baseMusic, sources, names, Steps, Next) { Past = Past, Future = Future };

    private AssemblyWork Change(ImmutableList<AssemblyStep> steps, int next) =>
        this with { Steps = steps, Next = next, Past = Past.Push((Steps, Next)), Future = [], Cached = null };
}