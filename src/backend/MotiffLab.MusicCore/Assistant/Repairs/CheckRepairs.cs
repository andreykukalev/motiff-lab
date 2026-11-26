using System.Globalization;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Assistant.Repairs;

/// <summary>One way to repair a flagged bar: its id, its title for a button, and the draft (the notes before and after, as corrections).</summary>
/// <param name="Id">Stable for the same notes, for example "barTooLong.doubled.7.R.4".</param>
public sealed record RepairCandidate(string Id, string Title, AssistantDraft Draft);

/// <summary>The repairs music-core offers for one Check flag, best first, or why it offers none.</summary>
/// <param name="NoneReason">Why there is no safe candidate, in the player's language; null when there are candidates.</param>
public sealed record RepairOptions(CheckFlag Flag, IReadOnlyList<RepairCandidate> Candidates, string? NoneReason)
{
    /// <summary>The bars a repair reads and changes: the flagged bar, with the short bar the importer split off after it when the scan read it too long; empty for a bar outside the piece.</summary>
    public TickSpan Measure { get; init; }
}

/// <summary>
/// Repairs by Check reason, with no AI (0028 section 6; the AI companion study, section 2.2): for a flagged bar, the
/// engine works out concrete candidates from the flag's reason, each a draft of note corrections the player hears and
/// accepts or dismisses. A reason the notes alone cannot settle gets no candidate and says why, rather than a guess.
/// Corrections never change the bars (the meter), a note's hand or the Check flag itself: the player resolves the flag.
/// Pure: the same notes and flag give the same candidates in the same order.
/// </summary>
public static partial class CheckRepairs
{
    /// <summary>The most candidates offered for one flag.</summary>
    public const int MostCandidates = 3;

    /// <summary>The tool name a repair draft carries.</summary>
    public const string Tool = "repair.candidates";

    /// <summary>The repairs for <paramref name="flag"/> in <paramref name="piece"/>.</summary>
    public static RepairOptions For(AssistantPiece piece, CheckFlag flag)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(flag);
        Words w = piece.Words;
        if (flag.Bar < piece.FirstBar || flag.Bar > piece.LastBar)
        {
            return None(flag, w.T($"The piece has bars {piece.FirstBar} to {piece.LastBar}.", $"В пьесе такты с {piece.FirstBar} по {piece.LastBar}."));
        }

        var context = new RepairContext(piece, flag);
        (IReadOnlyList<RepairCandidate> candidates, string? none) = flag.Reason switch
        {
            CheckReason.BarTooLong => TooLong(context),
            CheckReason.RecordingBarLength => context.Measure.Duration > context.Bar.Signature.BarTicks || context.Overrun ? TooLong(context) : ([], ShortReason(w)),
            CheckReason.BarTooShort => ([], ShortReason(w)),
            CheckReason.StavesDiffer => StavesDiffer(context),
            CheckReason.OutOfPianoRange => OutOfRange(context),
            CheckReason.PartSeam => Seam(context),
            CheckReason.TempoUncertain => Snapped(context),
            CheckReason.DenseCluster => Crowded(context),
            CheckReason.GraceNotes => ([], w.T(
                "Grace notes are kept beside the notes and are not played, so there is nothing among the notes to correct. Compare the bar with the page.",
                "Форшлаги хранятся рядом с нотами и не звучат, поэтому среди нот исправлять нечего. Сверьте такт со страницей.")),
            CheckReason.PageQuality => ([], w.T(
                "The whole page was read poorly, and no rule tells which note is wrong. Compare the bar with the page.",
                "Вся страница распознана плохо, и никакое правило не скажет, какая нота неверна. Сверьте такт со страницей.")),
            CheckReason.EnginesDisagree => ([], w.T(
                "The engines read this bar in different ways, and no rule tells which reading is right. Compare the bar with the page.",
                "Движки распознавания прочитали этот такт по-разному, и никакое правило не скажет, какое прочтение верно. Сверьте такт со страницей.")),
            CheckReason.CorrectionUnfit => ([], w.T(
                "Only you know which reading of this note is right: correct it with «Fix notes», or take the earlier reading in Sources.",
                "Какое чтение этой ноты верно, знаете только вы: исправьте её через «Исправить ноты» или возьмите прежнее чтение в «Источниках».")),
            CheckReason.HandsGuessed => ([], w.T(
                "Corrections change a note's pitch, timing and loudness, not its hand, so the hands cannot be repaired here.",
                "Правки меняют высоту, время и громкость ноты, но не руку, поэтому руки здесь не исправить.")),
            CheckReason.RecordingOctave => ([], w.T(
                "The recording tells which hand sounds an octave away and which way; use «Fix from the recording» in Check.",
                "Какая рука звучит на октаву иначе и в какую сторону, знает запись: используйте «Исправить по записи» в «Проверить».")),
            _ => throw new ArgumentOutOfRangeException(nameof(flag), flag.Reason, "Unknown Check reason."),
        };
        RepairOptions options = candidates.Count > 0 ? new RepairOptions(flag, [.. candidates.Take(MostCandidates)], null) : None(flag, none!);
        return options with { Measure = context.Measure };
    }

    private static RepairOptions None(CheckFlag flag, string reason) => new(flag, [], reason);

    private static string ShortReason(Words w) => w.T(
        "The bar itself is shorter in the piece's time line, and which note or rest is missing cannot be told from the notes; corrections do not change the bars. Compare the bar with the page.",
        "Сам такт короче в шкале времени пьесы, а какой ноты или паузы не хватает, по нотам не понять; правки не меняют такты. Сверьте такт со страницей.");

    /// <summary>A candidate from the notes before and after, or null when the result breaks an operation rule (range, grid, crossing) or changes nothing.</summary>
    private static RepairCandidate? Candidate(RepairContext context, string id, string title, IReadOnlyList<Note> before, IEnumerable<Note> after)
    {
        Note[] result = [.. after];
        RuleReport rules = OperationRules.Check(before, result, context.Piece.Words);
        if (rules.Violation is not null)
        {
            return null;
        }

        Variation variation = VariationFacts.Build(Tool, context.Measure, before, result, new HashSet<KeepProperty>(), rules.Flags, null, context.Piece.Meter, context.Piece.Words);
        var draft = new AssistantDraft(Tool, before, variation) { Title = title };
        return draft.IsEmpty ? null : new RepairCandidate($"{ToolSchema.Camel(context.Flag.Reason.ToString())}.{id}", title, draft);
    }

    /// <summary>The note moved earlier by <paramref name="ticks"/>, marked as the player's edit.</summary>
    private static Note Earlier(Note note, long ticks) => note with { Span = new TickSpan(new Tick(note.Span.Onset.Value - ticks), note.Span.Duration), Attribution = Attribution.UserEdited };

    private static Note Lasting(Note note, long duration) => note with { Span = new TickSpan(note.Span.Onset, duration), Attribution = Attribution.UserEdited };

    private static string Ticks(long value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The flagged bar as a repair reads it: the bar of the meter, the measure (the bar and, when the scan read it too long,
/// the short bar the importer split off after it), its notes by line (hand, or track without hands) and the address book.
/// </summary>
internal sealed class RepairContext
{
    public RepairContext(AssistantPiece piece, CheckFlag flag)
    {
        Piece = piece;
        Flag = flag;
        Bar = piece.Meter.GetBar(flag.Bar);
        // A measure read too long is split by the importer into this bar and a short one after it (MusicXML AlignBarsToMeasures).
        Bar next = flag.Bar < piece.LastBar ? piece.Meter.GetBar(flag.Bar + 1) : Bar;
        bool split = flag.Bar < piece.LastBar && !next.IsComplete && !next.IsPickup;
        Measure = split ? TickSpan.FromBounds(Bar.Span.Onset, next.Span.End) : Bar.Span;
        Notes = [.. piece.Notes.Where(note => Measure.Contains(note.Span.Onset)).OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Pitch.MidiNumber).ThenBy(static note => note.Id, StringComparer.Ordinal)];
        Lines = [.. Notes.GroupBy(static note => (note.Hand?.Value, note.Hand is null ? note.Track : 0)).Select(static line => line.ToArray())];
    }

    public AssistantPiece Piece { get; }

    public CheckFlag Flag { get; }

    public Bar Bar { get; }

    public TickSpan Measure { get; }

    /// <summary>The measure's notes in time order, then by pitch.</summary>
    public IReadOnlyList<Note> Notes { get; }

    /// <summary>The notes of each hand (or track), each line in time order.</summary>
    public IReadOnlyList<Note[]> Lines { get; }

    /// <summary>Whether a line's notes reach past the bar's end.</summary>
    public bool Overrun => Lines.Any(line => line.Max(static note => note.Span.End) > Bar.Span.End);

    public string Address(Note note) => Piece.Addresses.TextOf(note.Id) ?? note.Pitch.ToString();
}