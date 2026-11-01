using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Invitations;

/// <summary>The part of the track page an invitation belongs to.</summary>
public enum InvitationKind
{
    /// <summary>Things worth noticing: a question answered by listening.</summary>
    Noticing,

    /// <summary>Experiments: a question answered by applying a catalogue operation and comparing.</summary>
    Experiment,
}

/// <summary>
/// Whether the player can take an invitation's action in this version, as the track page labels it
/// (docs/07-product-definition.md, Rules for every invitation).
/// </summary>
public enum ActionAvailability
{
    /// <summary>The app offers it today.</summary>
    AvailableNow,

    /// <summary>A Must of Stage 1 that is not built yet.</summary>
    PlannedForStage1,

    /// <summary>Needs a Should item of Stage 1, for example chord detection (S1-A3).</summary>
    NeedsShouldItem,

    /// <summary>Waits for a later stage.</summary>
    LaterStage,
}

/// <summary>A passage of whole bars to play, with where it sounds.</summary>
/// <param name="FirstBar">0 for a pickup, otherwise from 1.</param>
public sealed record Passage(TickSpan Span, int FirstBar, int LastBar)
{
    /// <summary>The bars from <paramref name="firstBar"/> to <paramref name="lastBar"/>, from the first one's start to the last one's end.</summary>
    public static Passage Bars(MeterMap meter, int firstBar, int lastBar)
    {
        ArgumentNullException.ThrowIfNull(meter);
        return new(TickSpan.FromBounds(meter.GetBar(firstBar).Span.Onset, meter.GetBar(lastBar).Span.End), firstBar, lastBar);
    }
}

/// <summary>What an invitation asks the player to do: listen, or apply a catalogue operation.</summary>
public abstract record InvitationAction;

/// <summary>How a listening action plays its passages.</summary>
public enum ListeningKind
{
    /// <summary>Loop one passage.</summary>
    Loop,

    /// <summary>Loop one passage at a slower tempo.</summary>
    Slow,

    /// <summary>Loop two passages one after the other, to compare them.</summary>
    Compare,
}

/// <summary>A listening action (S1-W4): loop, slow down, or compare two passages. Listening always works.</summary>
/// <param name="Passages">One passage, or two for <see cref="ListeningKind.Compare"/>, in time order.</param>
/// <param name="TempoPercent">The playback tempo as a share of the written one, 25–150; 100 unless slowed down.</param>
public sealed record ListeningAction(ListeningKind Kind, IReadOnlyList<Passage> Passages, int TempoPercent = 100) : InvitationAction
{
    public bool Equals(ListeningAction? other) =>
        other is not null && Kind == other.Kind && TempoPercent == other.TempoPercent && Passages.SequenceEqual(other.Passages);

    public override int GetHashCode() => HashCode.Combine(Kind, TempoPercent, Passages.Count);
}

/// <summary>
/// A catalogue operation opened with its Keep preset and parameters (S1-E1, S1-E2): the step the
/// invitation proposes, which the player still applies.
/// </summary>
/// <param name="OperationId">The catalogue id, for example "V1".</param>
/// <param name="Option">The operation's option as the player reads it, for example "dotted".</param>
/// <param name="Parameters">The parameters to open it with; null when this version does not have the operation yet.</param>
/// <param name="KeepPresetId">The Keep preset to open it with (<see cref="KeepTable.Presets"/>).</param>
public sealed record OperationAction(string OperationId, string Option, OperationParameters? Parameters, string KeepPresetId) : InvitationAction;

/// <summary>
/// One invitation on the track page: a question that rests on facts, and an action that answers it
/// (docs/decisions/0009-exploration-layer-and-notebook.md). The facts are cited by their IDs in
/// the motif's analysis, so a notebook entry or an AI rewording can point back to them.
/// </summary>
/// <param name="RuleId">The catalogue rule that made it, for example "repeat.exact"; stable once shipped.</param>
/// <param name="FactIds">The facts the question rests on, at least one.</param>
/// <param name="Question">The question as the player reads it, stating only what the cited facts state.</param>
public sealed record Invitation(string RuleId, InvitationKind Kind, IReadOnlyList<string> FactIds, string Question, InvitationAction Action)
{
    /// <summary>Whether the action can be taken in this version, from <see cref="ActionStatus"/>.</summary>
    public ActionAvailability Availability => ActionStatus.Of(Action);

    public bool Equals(Invitation? other) =>
        other is not null
        && RuleId == other.RuleId
        && Kind == other.Kind
        && Question == other.Question
        && Action.Equals(other.Action)
        && FactIds.SequenceEqual(other.FactIds, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(RuleId, Kind, Question, Action);
}

/// <summary>
/// The one place that says whether an action is available, and why not (AC of #79): listening is
/// available now; each catalogue operation has the status the Stage 1 spec gives it.
/// </summary>
public static class ActionStatus
{
    private static readonly Dictionary<string, (ActionAvailability Availability, string Note)> s_operations = new(StringComparer.Ordinal)
    {
        ["V1"] = (ActionAvailability.AvailableNow, "In the playback lab."),
        ["V13"] = (ActionAvailability.AvailableNow, "In the playback lab, and on the Explore card."),
        ["V4"] = (ActionAvailability.AvailableNow, "In the playback lab."),
        ["V9"] = (ActionAvailability.AvailableNow, "In the playback lab, and on the Explore card."),
        ["V10"] = (ActionAvailability.AvailableNow, "In the playback lab, and on the Explore card."),
        ["V7"] = (ActionAvailability.AvailableNow, "In the playback lab, and as the More air Try chip."),
        ["V8"] = (ActionAvailability.AvailableNow, "In the playback lab, and as the Shift the accents Try chip."),
        ["V11"] = (ActionAvailability.AvailableNow, "In the playback lab, and as the Passing notes Try chip."),
        ["V2"] = (ActionAvailability.AvailableNow, "In the playback lab, and on the Explore card: it replays each bar's own accompaniment notes, so it needs no chord detection."),
        ["V12"] = (ActionAvailability.AvailableNow, "In the playback lab, and as the Pedal bass Try chip: it builds the bass on the chords chord detection (S1-A3) reads."),
        ["V3"] = (ActionAvailability.AvailableNow, "In the playback lab, and as the Another voicing Try chip: it revoices the chords chord detection (S1-A3) reads, or moves notes an octave."),
        ["V6"] = (ActionAvailability.AvailableNow, "In the playback lab, and as the Simplify Try chip, levels 1 to 3."),
        ["V5"] = (ActionAvailability.LaterStage, "Harmonic colour waits for Stage 4."),
        ["V14"] = (ActionAvailability.AvailableNow, "In the playback lab, and as the Ornament Try chip (decision record 0018)."),
        ["V15"] = (ActionAvailability.AvailableNow, "In the playback lab, and as the Reharmonise Try chip (decision record 0018): relative and parallel chords only."),
    };

    /// <summary>The catalogue ids the table knows, V1–V15.</summary>
    public static IEnumerable<string> OperationIds => s_operations.Keys;

    public static ActionAvailability Of(InvitationAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action switch
        {
            OperationAction operation => Of(operation.OperationId),
            _ => ActionAvailability.AvailableNow,
        };
    }

    /// <exception cref="ArgumentException">The catalogue has no operation with this id.</exception>
    public static ActionAvailability Of(string operationId) => Entry(operationId).Availability;

    /// <summary>The status as a sentence for the page, for example "Needs chord detection (S1-A3), a Should item of Stage 1."</summary>
    public static string Note(InvitationAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action is OperationAction operation ? Entry(operation.OperationId).Note : "Listening and looping always work.";
    }

    private static (ActionAvailability Availability, string Note) Entry(string operationId)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        return s_operations.TryGetValue(operationId, out (ActionAvailability, string) entry)
            ? entry
            : throw new ArgumentException($"The catalogue has no operation {operationId}.", nameof(operationId));
    }
}