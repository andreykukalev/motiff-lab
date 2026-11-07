using System.Collections.Immutable;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Performance;

/// <summary>What the player chose for a bar the version plays otherwise, in the merge draft (0022, section 5; testing items 32 and 34).</summary>
public enum MergeChoice
{
    /// <summary>The player's own notes stay.</summary>
    Mine,

    /// <summary>The bar is taken from the version.</summary>
    Version,

    /// <summary>The player wrote the bar themselves in the roll, from either side.</summary>
    Own,

    /// <summary>Not a difference: nothing changes and the bar's marker stays away.</summary>
    NotADifference,
}

/// <summary>A choice for one bar. <paramref name="Own"/> holds the bar the player wrote, only for <see cref="MergeChoice.Own"/>.</summary>
public sealed record MergeDecision(int Bar, MergeChoice Choice, TakenBar? Own = null)
{
    public TakenBar? Own { get; } = Choice == MergeChoice.Own
        ? Own ?? throw new ArgumentException("A bar the player wrote needs its notes.", nameof(Own))
        : null;

    public bool Equals(MergeDecision? other) =>
        other is not null && Bar == other.Bar && Choice == other.Choice
        && (Own is null ? other.Own is null : other.Own is not null && Own.Bar == other.Own.Bar && Own.Removed.SequenceEqual(other.Own.Removed) && Own.Added.SequenceEqual(other.Own.Added));

    public override int GetHashCode() => HashCode.Combine(Bar, Choice);
}

/// <summary>
/// The merge kept with a version (project file, <c>merge</c>): the choices of the last draft applied, so the draft can
/// be opened again with them, and when it was applied; null <paramref name="AppliedAt"/> once the merge was undone.
/// </summary>
public sealed record MergeRecord(IReadOnlyList<MergeDecision> Decisions, DateTimeOffset? AppliedAt)
{
    public bool Equals(MergeRecord? other) => other is not null && AppliedAt == other.AppliedAt && Decisions.SequenceEqual(other.Decisions);

    public override int GetHashCode() => HashCode.Combine(Decisions.Count, AppliedAt);
}

/// <summary>How the draft opens: every bar kept as mine, the sure bars from the version, every bar from the version, or as last applied.</summary>
public enum MergeStart
{
    Mine,
    Sure,
    All,
    Saved,
}

/// <summary>How a differing bar is grouped in the comparison window: added notes, other notes, or rhythm.</summary>
public enum MergeGroup
{
    AddedNotes,
    OtherNotes,
    Rhythm,
}

/// <summary>
/// The merge draft (testing items 32 and 34): a choice for each bar the version plays otherwise, with undo and redo,
/// that changes nothing in the score until it is applied. Immutable: every change gives a new draft.
/// </summary>
/// <param name="Bars">The bars that can be decided, in bar order: aligned and played otherwise.</param>
public sealed record MergeDraft(string PerformanceId, IReadOnlyList<int> Bars)
{
    /// <summary>The choices made, by bar; a bar not here is undecided and stays as mine when the draft is applied.</summary>
    public ImmutableSortedDictionary<int, MergeDecision> Decisions { get; init; } = ImmutableSortedDictionary<int, MergeDecision>.Empty;

    /// <summary>The choices the draft opened with, which Reset goes back to.</summary>
    public ImmutableSortedDictionary<int, MergeDecision> Opened { get; init; } = ImmutableSortedDictionary<int, MergeDecision>.Empty;

    private ImmutableStack<ImmutableSortedDictionary<int, MergeDecision>> Past { get; init; } = [];

    private ImmutableStack<ImmutableSortedDictionary<int, MergeDecision>> Future { get; init; } = [];

    public bool CanUndo => !Past.IsEmpty;

    public bool CanRedo => !Future.IsEmpty;

    /// <summary>How many bars have a choice.</summary>
    public int Decided => Decisions.Count;

    /// <summary>Whether the choices are other than those the draft opened with.</summary>
    public bool Changed => !Same(Decisions, Opened);

    public MergeDecision? DecisionFor(int bar) => Decisions.GetValueOrDefault(bar);

    /// <summary>The same choice for these bars (one bar, or every differing bar of a phrase); bars that cannot be decided are left alone.</summary>
    public MergeDraft Choose(IEnumerable<int> bars, MergeChoice choice)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (choice == MergeChoice.Own)
        {
            throw new ArgumentException("A bar the player wrote is kept with WriteOwn.", nameof(choice));
        }

        HashSet<int> known = [.. Bars];
        ImmutableSortedDictionary<int, MergeDecision> next = Decisions;
        foreach (int bar in bars.Where(known.Contains))
        {
            next = next.SetItem(bar, new MergeDecision(bar, choice));
        }

        return Step(next);
    }

    /// <summary>The bar as the player wrote it in the roll: "свой".</summary>
    public MergeDraft WriteOwn(TakenBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        return Bars.Contains(bar.Bar) ? Step(Decisions.SetItem(bar.Bar, new MergeDecision(bar.Bar, MergeChoice.Own, bar))) : this;
    }

    /// <summary>The bars back to undecided: "Сбросить такт".</summary>
    public MergeDraft Undecide(IEnumerable<int> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        return Step(Decisions.RemoveRange(bars));
    }

    /// <summary>Every choice back as the draft opened: "Сбросить черновик". Undone like any other step.</summary>
    public MergeDraft Reset() => Step(Opened);

    public MergeDraft Undo() => Past.IsEmpty ? this : this with
    {
        Decisions = Past.Peek(),
        Past = Past.Pop(),
        Future = Future.Push(Decisions),
    };

    public MergeDraft Redo() => Future.IsEmpty ? this : this with
    {
        Decisions = Future.Peek(),
        Future = Future.Pop(),
        Past = Past.Push(Decisions),
    };

    private MergeDraft Step(ImmutableSortedDictionary<int, MergeDecision> next) =>
        Same(next, Decisions) ? this : this with { Decisions = next, Past = Past.Push(Decisions), Future = [] };

    private static bool Same(ImmutableSortedDictionary<int, MergeDecision> one, ImmutableSortedDictionary<int, MergeDecision> other) =>
        one.Count == other.Count && one.All(pair => other.TryGetValue(pair.Key, out MergeDecision? same) && same.Equals(pair.Value));
}

/// <summary>
/// Merging a version into the player's notes through a draft (testing items 32 and 34; 0022, section 5): which bars can be
/// decided, the notes each choice gives, the result the draft shows before it is applied, and applying or undoing it.
/// Pure and deterministic.
/// </summary>
public static class PerformanceMerge
{
    /// <summary>The prefix of the notes of a bar the player wrote, after the version's id: "perf-perf_1-own-".</summary>
    public static string OwnPrefix(string performanceId) => $"{PerformanceTake.IdPrefix}{performanceId}-own-";

    /// <summary>The bars a draft can decide: aligned bars the version plays otherwise, dismissed ones included.</summary>
    public static IReadOnlyList<int> DraftBars(PerformanceComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return [.. comparison.Differences.Where(bar => bar.Aligned).Select(bar => bar.Bar).Order()];
    }

    /// <summary>The bars the comparison is at least <see cref="PerformanceTake.AutomaticConfidence"/> percent sure of.</summary>
    public static IReadOnlyList<int> SureBars(PerformanceComparison comparison) =>
        [.. comparison.Differences.Where(bar => bar.Aligned && bar.Confidence >= PerformanceTake.AutomaticConfidence).Select(bar => bar.Bar).Order()];

    /// <summary>The group a kind of difference is listed under.</summary>
    public static MergeGroup GroupOf(BarDifference difference) => difference switch
    {
        BarDifference.AddedNotes or BarDifference.Ornament => MergeGroup.AddedNotes,
        BarDifference.Rhythm => MergeGroup.Rhythm,
        _ => MergeGroup.OtherNotes,
    };

    /// <summary>
    /// A draft for the version. <see cref="MergeStart.Saved"/> opens with the choices last applied, or, for a version
    /// merged before drafts, with its taken bars as taken from the version and its dismissed bars as not a difference.
    /// </summary>
    public static MergeDraft Open(SavedPerformance performance, PerformanceComparison comparison, MergeStart start)
    {
        ArgumentNullException.ThrowIfNull(performance);
        ArgumentNullException.ThrowIfNull(comparison);
        IReadOnlyList<int> bars = DraftBars(comparison);
        HashSet<int> known = [.. bars];
        IEnumerable<MergeDecision> decisions = start switch
        {
            MergeStart.Mine => bars.Select(bar => new MergeDecision(bar, MergeChoice.Mine)),
            MergeStart.Sure => SureBars(comparison).Where(bar => !performance.Dismissed.Contains(bar)).Select(bar => new MergeDecision(bar, MergeChoice.Version)),
            MergeStart.All => bars.Where(bar => !performance.Dismissed.Contains(bar)).Select(bar => new MergeDecision(bar, MergeChoice.Version)),
            MergeStart.Saved => performance.Merge is { } merge
                ? merge.Decisions
                : performance.Taken.Select(bar => new MergeDecision(bar.Bar, MergeChoice.Version))
                    .Concat(performance.Dismissed.Select(bar => new MergeDecision(bar, MergeChoice.NotADifference))),
            _ => throw new ArgumentOutOfRangeException(nameof(start), start, "Unknown start."),
        };

        ImmutableSortedDictionary<int, MergeDecision> opened = ImmutableSortedDictionary.CreateRange(
            decisions.Where(decision => known.Contains(decision.Bar)).GroupBy(decision => decision.Bar).Select(group => KeyValuePair.Create(group.Key, group.Last())));
        return new MergeDraft(performance.Id, bars) { Decisions = opened, Opened = opened };
    }

    /// <summary>The bars the draft takes into the score: each bar from the version as <see cref="PerformanceTake.Take"/> takes it, and each bar the player wrote.</summary>
    public static IReadOnlyList<TakenBar> Taken(MergeDraft draft, SavedPerformance performance, PerformanceComparison comparison, MeterMap meter, IReadOnlyList<Note> printed)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(performance);
        ArgumentNullException.ThrowIfNull(comparison);
        IReadOnlyList<PlayedNote>? played = null;
        Dictionary<int, BarComparison> byBar = comparison.Bars.ToDictionary(bar => bar.Bar);
        List<TakenBar> taken = [];
        foreach (MergeDecision decision in draft.Decisions.Values)
        {
            if (decision.Choice == MergeChoice.Own)
            {
                taken.Add(decision.Own!);
            }
            else if (decision.Choice == MergeChoice.Version && byBar.TryGetValue(decision.Bar, out BarComparison? bar) && bar.Aligned)
            {
                played ??= performance.Notes();
                TakenBar from = PerformanceTake.Take(performance.Id, bar, played, comparison.Alignment, meter, printed);
                if (from.Added.Count > 0 || from.Removed.Count > 0)
                {
                    taken.Add(from);
                }
            }
        }

        return taken;
    }

    /// <summary>The notes the draft gives, "Итог": the player's notes with the bars the draft takes.</summary>
    public static IReadOnlyList<Note> Result(MergeDraft draft, SavedPerformance performance, PerformanceComparison comparison, MeterMap meter, IReadOnlyList<Note> printed) =>
        PerformanceTake.Apply(printed, Taken(draft, performance, comparison, meter, printed));

    /// <summary>
    /// The version with the draft applied: the bars it takes in place of every bar taken before, its "not a difference" bars
    /// dismissed and the other bars it can decide shown again, and its choices kept so the draft can be opened again.
    /// </summary>
    public static SavedPerformance Apply(MergeDraft draft, SavedPerformance performance, PerformanceComparison comparison, MeterMap meter, IReadOnlyList<Note> printed, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(performance);
        IReadOnlyList<TakenBar> taken = Taken(draft, performance, comparison, meter, printed);
        HashSet<int> decidable = [.. draft.Bars];
        IEnumerable<int> dismissed = performance.Dismissed.Where(bar => !decidable.Contains(bar))
            .Concat(draft.Decisions.Values.Where(decision => decision.Choice == MergeChoice.NotADifference).Select(decision => decision.Bar));
        return performance with
        {
            Taken = [.. taken.OrderBy(bar => bar.Bar)],
            Dismissed = [.. dismissed.Distinct().Order()],
            Merge = new MergeRecord([.. draft.Decisions.Values], at),
        };
    }

    /// <summary>"Отменить слияние": the player's notes back in every bar taken, the choices kept so the draft can be opened again.</summary>
    public static SavedPerformance Undo(SavedPerformance performance)
    {
        ArgumentNullException.ThrowIfNull(performance);
        return performance with
        {
            Taken = [],
            Merge = performance.Merge is { } merge ? merge with { AppliedAt = null } : null,
        };
    }

    /// <summary>
    /// A bar as the player wrote it in the roll ("Править в ролле"), as a bar taken: the printed notes of the bar not kept
    /// as they were leave, and every other note comes in with an id of its own. <paramref name="notes"/> are the bar's notes
    /// as the player left them; notes outside the bar are ignored.
    /// </summary>
    public static TakenBar OwnBar(string performanceId, int bar, MeterMap meter, IReadOnlyList<Note> printed, IReadOnlyList<Note> notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(performanceId);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(printed);
        ArgumentNullException.ThrowIfNull(notes);
        TickSpan span = meter.GetBar(bar).Span;
        List<Note> inBar = [.. printed.Where(note => span.Contains(note.Span.Onset))];
        List<Note> wanted = [.. notes.Where(note => span.Contains(note.Span.Onset))
            .OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch).ThenBy(note => note.Id, StringComparer.Ordinal)];
        HashSet<string> kept = [.. wanted.Where(note => inBar.Any(same => same.Id == note.Id && same.Pitch == note.Pitch && same.Span == note.Span)).Select(note => note.Id)];
        string prefix = OwnPrefix(performanceId);
        List<TakenNote> added = [];
        foreach (Note note in wanted.Where(note => !kept.Contains(note.Id)))
        {
            Hand? hand = note.Hand?.Value ?? (note.Pitch.MidiNumber < 60 ? Hand.Left : Hand.Right);
            added.Add(new TakenNote($"{prefix}{bar}-{added.Count + 1}", note.Pitch, ClampTo(note.Span, span), note.Velocity, note.Track, hand));
        }

        return new TakenBar(bar, [.. inBar.Where(note => !kept.Contains(note.Id)).Select(note => note.Id).Order(StringComparer.Ordinal)], added);
    }

    private static TickSpan ClampTo(TickSpan note, TickSpan bar) =>
        note.End <= bar.End ? note : TickSpan.FromBounds(note.Onset, bar.End);
}