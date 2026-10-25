using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The player's changes to the suggested phrase boundaries (S1-I5, S1-A2), saved with the project:
/// boundaries they set and suggested ones they took away. Moving a boundary is one of each.
/// </summary>
/// <param name="Added">Boundaries the player set, in time order, each once.</param>
/// <param name="Removed">Suggested boundaries the player took away, in time order, each once.</param>
public sealed record PhraseEdits(IReadOnlyList<Tick> Added, IReadOnlyList<Tick> Removed)
{
    public static PhraseEdits None { get; } = new([], []);

    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;

    public bool Equals(PhraseEdits? other) =>
        other is not null && Added.SequenceEqual(other.Added) && Removed.SequenceEqual(other.Removed);

    public override int GetHashCode() => HashCode.Combine(Added.Count, Removed.Count);
}

/// <summary>How much the source says about where its phrases are, before the player changes anything.</summary>
public enum PhraseEvidenceLevel
{
    /// <summary>Every suggested phrase fits a selection.</summary>
    Enough,

    /// <summary>Some boundaries were found, but part of the piece runs longer than a selection without one.</summary>
    Partial,

    /// <summary>Nothing in the source marks a boundary, so no phrase is suggested.</summary>
    None,
}

/// <summary>One phrase of the piece, from where it starts to where the next one starts.</summary>
/// <param name="Number">1 for the first phrase of the piece, then in order.</param>
/// <param name="Span">From the phrase's start to the next phrase's start, or the end of the piece.</param>
/// <param name="FirstBar">The bar the phrase starts in.</param>
/// <param name="LastBar">The bar the phrase ends in.</param>
/// <param name="Start">The boundary it starts at, with its evidence; null for the first phrase, which starts with the piece.</param>
public sealed record Phrase(int Number, TickSpan Span, int FirstBar, int LastBar, PhraseBoundary? Start)
{
    /// <summary>The number of bars the phrase touches, counting partly covered bars.</summary>
    public int BarCount => LastBar - FirstBar + 1;

    /// <summary>Whether the whole phrase fits a selection (<see cref="MotifSelection.MaxBars"/>).</summary>
    public bool FitsSelection => BarCount <= MotifSelection.MaxBars;
}

/// <summary>
/// The piece as phrases (S1-I5): the boundaries <see cref="PhraseBoundaryFinder"/> finds in the
/// source, with the player's own changes, cut into the phrases between them. The phrases are the
/// working context of the Central Product Model (docs/08-central-product-model.md); they are
/// suggestions, never facts, so each one keeps the evidence and attribution of its start.
/// </summary>
/// <remarks>
/// <para>
/// Phrases cover the piece without gaps: the first starts at the start of the piece, each later one
/// at a boundary, and the last ends with the piece. A boundary the player set where the source has
/// one confirms it; one they took away is gone until they set it again or go back to the suggestions.
/// Where the source marks no boundary, none is invented: the map is one phrase and its
/// <see cref="Evidence"/> says so, so the app can fall back to selecting bars.
/// </para>
/// <para>
/// Phrases are read across all the notes of the piece, so a boundary from a rest needs every part
/// to rest. The map and its edits are pure: the same notes, meter, marks and edits give the same phrases.
/// </para>
/// </remarks>
public sealed class PhraseMap
{
    private readonly MeterMap _meter;

    private PhraseMap(IReadOnlyList<PhraseBoundary> found, PhraseEdits edits, IReadOnlyList<PhraseBoundary> boundaries, IReadOnlyList<Phrase> phrases, PhraseEvidenceLevel evidence, MeterMap meter, Tick end)
    {
        _meter = meter;
        Found = found;
        Edits = edits;
        Boundaries = boundaries;
        Phrases = phrases;
        Evidence = evidence;
        End = end;
    }

    /// <summary>The boundaries the source supports, before the player's changes.</summary>
    public IReadOnlyList<PhraseBoundary> Found { get; }

    /// <summary>The player's changes, as saved with the project.</summary>
    public PhraseEdits Edits { get; }

    /// <summary>The boundaries in effect, in time order: the ones found, less those the player took away, with those they set.</summary>
    public IReadOnlyList<PhraseBoundary> Boundaries { get; }

    /// <summary>The phrases between the boundaries in effect, in time order; at least one for a piece with notes.</summary>
    public IReadOnlyList<Phrase> Phrases { get; }

    /// <summary>How much the source says about its phrases, whatever the player changed.</summary>
    public PhraseEvidenceLevel Evidence { get; }

    /// <summary>The end of the piece, where the last phrase ends.</summary>
    public Tick End { get; }

    /// <summary>
    /// The phrases of a piece ending at <paramref name="end"/>, from the boundaries its notes and
    /// marks support and the player's <paramref name="edits"/>. Edits outside the piece are left out.
    /// </summary>
    public static PhraseMap Suggest(IEnumerable<Note> notes, MeterMap meter, PhraseMarks marks, Tick end, PhraseEdits? edits = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(marks);
        if (end <= Tick.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "The piece is empty.");
        }

        IReadOnlyList<PhraseBoundary> found = [.. PhraseBoundaryFinder.Find(notes, meter, marks).Where(boundary => boundary.At < end)];
        IReadOnlyList<Phrase> suggested = Cut(found, meter, end);
        PhraseEvidenceLevel evidence = found.Count == 0
            ? PhraseEvidenceLevel.None
            : suggested.All(static phrase => phrase.FitsSelection) ? PhraseEvidenceLevel.Enough : PhraseEvidenceLevel.Partial;
        return Build(found, edits ?? PhraseEdits.None, meter, end, evidence);
    }

    /// <summary>The phrase that holds a position; the last phrase for the end of the piece or later.</summary>
    public Phrase At(Tick position) =>
        Phrases.LastOrDefault(phrase => phrase.Span.Onset <= position) ?? Phrases[0];

    /// <summary>The phrase a selection of <paramref name="span"/> stands for: one whose span, or whose first bars a selection can hold, is exactly that span; or null.</summary>
    public Phrase? Matching(TickSpan span) => Phrases.FirstOrDefault(phrase => phrase.Span == span || Selectable(phrase) == span);

    /// <summary>The phrase that covers most of <paramref name="span"/>, the earliest of equals; the phrase at its start for an empty span.</summary>
    public Phrase Covering(TickSpan span) => Phrases
        .Select(phrase => (Phrase: phrase, Shared: Math.Min(phrase.Span.End.Value, span.End.Value) - Math.Max(phrase.Span.Onset.Value, span.Onset.Value)))
        .Where(entry => entry.Shared > 0)
        .OrderByDescending(entry => entry.Shared)
        .ThenBy(entry => entry.Phrase.Number)
        .Select(entry => entry.Phrase)
        .FirstOrDefault() ?? At(span.Onset);

    /// <summary>
    /// The edits that make <paramref name="span"/> one phrase: boundaries at its start and end
    /// (unless those are the start or end of the piece), and none inside it. Moving an edge of a
    /// phrase and splitting one are both done this way. An edge less than a beat from a boundary
    /// in effect moves to that boundary and confirms it, so a selection snapped to beats keeps a
    /// phrase that starts on an upbeat, rather than leaving a sliver of it behind.
    /// </summary>
    public PhraseEdits MakePhrase(TickSpan span)
    {
        Tick start = Snap(span.Onset);
        Tick stop = Snap(span.End > End ? End : span.End);
        var at = Boundaries.Select(static boundary => boundary.At).Where(tick => tick <= start || tick >= stop).ToHashSet();
        var confirm = new HashSet<Tick>();
        foreach (Tick edge in stop > start ? [start, stop] : new[] { start })
        {
            if (edge > Tick.Zero && edge < End)
            {
                at.Add(edge);
                confirm.Add(edge);
            }
        }

        return EditsFor(at, confirm);
    }

    /// <summary>The edits that join a phrase with the one after it, by taking away the boundary between them. Unchanged for the last phrase.</summary>
    public PhraseEdits JoinWithNext(Phrase phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        return EditsFor(Boundaries.Select(static boundary => boundary.At).Where(tick => tick != phrase.Span.End), []);
    }

    /// <summary>The same notes and marks with other edits, for example after the player changed a boundary.</summary>
    public PhraseMap With(PhraseEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        return Build(Found, edits, _meter, End, Evidence);
    }

    /// <summary>
    /// The span to select for a phrase: the whole phrase, or when it runs longer than a selection
    /// can (<see cref="MotifSelection.MaxBars"/>), its first bars up to that many.
    /// </summary>
    public TickSpan Selectable(Phrase phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        if (phrase.FitsSelection)
        {
            return phrase.Span;
        }

        Tick stop = _meter.GetBar(phrase.FirstBar + MotifSelection.MaxBars - 1).Span.End;
        return TickSpan.FromBounds(phrase.Span.Onset, stop < phrase.Span.End ? stop : phrase.Span.End);
    }

    /// <summary>
    /// "after a rest of a beat or more", "at a slur and at a repeat sign", "set by you", "at a slur,
    /// confirmed by you", or for the first phrase "the start of the piece": how a phrase's start was found.
    /// </summary>
    public static string Describe(PhraseBoundary? start)
    {
        if (start is null)
        {
            return "the start of the piece";
        }

        string[] evidence = [.. start.Evidence.Select(static mark => mark switch
        {
            PhraseEvidence.Slur => "at a slur",
            PhraseEvidence.Rest => "after a rest of a beat or more",
            PhraseEvidence.RepeatSign => "at a repeat sign",
            PhraseEvidence.Repetition => "where an earlier passage comes back note for note",
            _ => mark.ToString(),
        })];
        string source = evidence.Length switch
        {
            0 => "",
            1 => evidence[0],
            _ => $"{string.Join(", ", evidence[..^1])} and {evidence[^1]}",
        };

        return start.Attribution.Provenance switch
        {
            Provenance.UserEdited or Provenance.UserConfirmed when source.Length == 0 => "set by you",
            Provenance.UserEdited or Provenance.UserConfirmed => $"{source}, confirmed by you",
            _ => source,
        };
    }

    /// <summary>A boundary in effect less than a beat from <paramref name="edge"/>, or the edge itself.</summary>
    private Tick Snap(Tick edge)
    {
        long beat = _meter.SignatureAt(edge).BeatTicks;
        PhraseBoundary? near = Boundaries
            .Where(boundary => Math.Abs(boundary.At.Value - edge.Value) < beat)
            .OrderBy(boundary => Math.Abs(boundary.At.Value - edge.Value))
            .FirstOrDefault();
        return near?.At ?? edge;
    }

    /// <summary>
    /// The smallest edits from the boundaries found to exactly <paramref name="boundaries"/>. A found
    /// boundary the player confirmed before, or confirms now, is kept in the added list so it stays confirmed.
    /// </summary>
    private PhraseEdits EditsFor(IEnumerable<Tick> boundaries, IEnumerable<Tick> confirm)
    {
        var wanted = boundaries.Where(tick => tick > Tick.Zero && tick < End).ToHashSet();
        var found = Found.Select(static boundary => boundary.At).ToHashSet();
        IEnumerable<Tick> confirmed = Edits.Added.Concat(confirm).Where(tick => found.Contains(tick) && wanted.Contains(tick));
        return new PhraseEdits(
            [.. wanted.Where(tick => !found.Contains(tick)).Concat(confirmed).Distinct().Order()],
            [.. found.Where(tick => !wanted.Contains(tick)).Order()]);
    }

    private static PhraseMap Build(IReadOnlyList<PhraseBoundary> found, PhraseEdits edits, MeterMap meter, Tick end, PhraseEvidenceLevel evidence)
    {
        var removed = edits.Removed.ToHashSet();
        IEnumerable<PhraseBoundary> kept = found.Where(boundary => !removed.Contains(boundary.At));
        IReadOnlyList<PhraseBoundary> boundaries = [.. PhraseBoundaryFinder
            .WithPlayerBoundaries(kept, edits.Added.Where(tick => tick > Tick.Zero && tick < end))];
        return new PhraseMap(found, edits, boundaries, Cut(boundaries, meter, end), evidence, meter, end);
    }

    private static IReadOnlyList<Phrase> Cut(IReadOnlyList<PhraseBoundary> boundaries, MeterMap meter, Tick end)
    {
        var phrases = new List<Phrase>();
        PhraseBoundary? start = null;
        foreach (PhraseBoundary? next in boundaries.Append<PhraseBoundary?>(null))
        {
            Tick from = start?.At ?? Tick.Zero;
            Tick to = next?.At ?? end;
            if (to > from)
            {
                phrases.Add(new Phrase(
                    phrases.Count + 1,
                    TickSpan.FromBounds(from, to),
                    meter.BarAt(from).Number,
                    meter.BarAt(new Tick(to.Value - 1)).Number,
                    start));
            }

            start = next;
        }

        return phrases;
    }
}