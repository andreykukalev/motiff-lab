using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis.Phrasing;

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
    /// <summary>
    /// The most bars a phrase usually runs (decision record 0018, section 3: phrases are 4–8 bars). A longer
    /// phrase is <see cref="Phrase.IsLong"/>: the source gave no boundary inside it, and the evidence is partial.
    /// </summary>
    public const int LongPhraseBars = 8;

    private readonly MeterMap _meter;
    private IReadOnlyDictionary<Tick, Tick>? _replaced;

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
            : suggested.All(static phrase => !phrase.IsLong) ? PhraseEvidenceLevel.Enough : PhraseEvidenceLevel.Partial;
        return Build(found, edits ?? PhraseEdits.None, meter, end, evidence);
    }

    /// <summary>The phrase that holds a position; the last phrase for the end of the piece or later.</summary>
    public Phrase At(Tick position) =>
        Phrases.LastOrDefault(phrase => phrase.Span.Onset <= position) ?? Phrases[0];

    /// <summary>The phrase a selection of <paramref name="span"/> stands for: one whose span, or whose first bars a selection can hold, is exactly that span; or null.</summary>
    public Phrase? Matching(TickSpan span) => Phrases.FirstOrDefault(phrase => phrase.Span == span || Selectable(phrase) == span);

    /// <summary>The phrase that covers most of <paramref name="span"/>, the earliest of equals; the phrase at its start for an empty span.</summary>
    internal Phrase Covering(TickSpan span) => Phrases
        .Select(phrase => (Phrase: phrase, Shared: Math.Min(phrase.Span.End.Value, span.End.Value) - Math.Max(phrase.Span.Onset.Value, span.Onset.Value)))
        .Where(entry => entry.Shared > 0)
        .OrderByDescending(entry => entry.Shared)
        .ThenBy(entry => entry.Phrase.Number)
        .Select(entry => entry.Phrase)
        .FirstOrDefault() ?? At(span.Onset);

    /// <summary>
    /// The phrase <paramref name="span"/> belongs to among <paramref name="phrases"/> (0018, section 3; V16): the one it
    /// starts in; failing that, the one it overlaps most, the earliest of equals; null when it overlaps none. An idea's home
    /// (<c>Observation.Home</c>) and the phrase an anchor that no longer matches is attached to again.
    /// </summary>
    internal static TickSpan? HomeOf(TickSpan span, IEnumerable<TickSpan> phrases)
    {
        ArgumentNullException.ThrowIfNull(phrases);
        TickSpan? best = null;
        long bestOverlap = 0;
        foreach (TickSpan phrase in phrases)
        {
            if (phrase.Contains(span.Onset))
            {
                return phrase;
            }

            long overlap = Math.Min(phrase.End.Value, span.End.Value) - Math.Max(phrase.Onset.Value, span.Onset.Value);
            if (overlap > bestOverlap)
            {
                best = phrase;
                bestOverlap = overlap;
            }
        }

        return best;
    }

    /// <summary>
    /// The edits that make <paramref name="span"/> one phrase: boundaries at its start and end
    /// (unless those are the start or end of the piece), and none inside it. Moving an edge of a
    /// phrase and splitting one are both done this way. An edge less than a beat from a boundary
    /// in effect moves to that boundary and confirms it, so a selection snapped to beats keeps a
    /// phrase that starts on an upbeat, rather than leaving a sliver of it behind.
    /// </summary>
    public PhraseEdits MakePhrase(TickSpan span)
    {
        LevelEditor editor = Editor();
        return editor.Make(editor.Snap(span.Onset, _meter), editor.Snap(span.End > End ? End : span.End, _meter));
    }

    /// <summary>The edits that join a phrase with the one after it, by taking away the boundary between them. Unchanged for the last phrase.</summary>
    public PhraseEdits JoinWithNext(Phrase phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        return Editor().Join(phrase.Span.End);
    }

    /// <summary>
    /// The edits that take away every boundary strictly inside <paramref name="span"/> and leave its edges as
    /// they are: what joining at a finer level (decision record 0018, section 3) needs when a short unit
    /// is joined across a phrase boundary.
    /// </summary>
    public PhraseEdits JoinAcross(TickSpan span) => Editor().JoinBetween(span.Onset, span.End);

    /// <summary>
    /// The edits that add a boundary at <paramref name="at"/>, snapped like an edge of <see cref="MakePhrase"/>,
    /// and change nothing else: Split here. Unchanged at the start or the end of the piece.
    /// </summary>
    public PhraseEdits SplitAt(Tick at)
    {
        LevelEditor editor = Editor();
        return editor.Split(editor.Snap(at, _meter));
    }

    /// <summary>
    /// The edits that move the phrase start at <paramref name="from"/> to <paramref name="to"/> as one step (the iPad's drag
    /// of a mark): taken away there and set exactly here, the found boundary again when one was found here; the start of
    /// the piece or later than its end only takes it away. The app snaps <paramref name="to"/> as it likes; see
    /// <see cref="PhraseLevels.NoteAt"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">No phrase starts at <paramref name="from"/>.</exception>
    public PhraseEdits Move(Tick from, Tick to) => Editor().Move(InEffect(from).At, to);

    /// <summary>
    /// The edits that set a phrase start exactly at <paramref name="at"/> («Начать фразу здесь»); unchanged where a phrase
    /// already starts (a found start stays found, never one the player confirmed) and at the start or the end of the piece.
    /// </summary>
    public PhraseEdits StartAt(Tick at) => Boundaries.Any(boundary => boundary.At == at) ? Edits : Editor().Split(at);

    /// <summary>The edits that take the phrase start at <paramref name="at"/> away («Убрать границу»), joining the phrases on either side.</summary>
    /// <exception cref="ArgumentOutOfRangeException">No phrase starts at <paramref name="at"/>.</exception>
    public PhraseEdits Remove(Tick at) => Editor().Join(InEffect(at).At);

    /// <summary>
    /// The edits that put the found boundary back in place of the player's own at <paramref name="at"/> («Вернуть
    /// найденную»): one they confirmed where it was found is that found boundary again; one they set is moved back to the
    /// found boundary it took the place of (<see cref="Replaced"/>), or, when it replaced none, taken away.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">None of the player's phrase starts is at <paramref name="at"/>.</exception>
    public PhraseEdits PutBack(Tick at)
    {
        PhraseBoundary boundary = InEffect(at);
        if (!IsOwn(boundary))
        {
            throw new ArgumentOutOfRangeException(nameof(at), at, "The phrase starts there as found, not by the player.");
        }

        if (boundary.Attribution.Provenance == Provenance.UserConfirmed)
        {
            return new PhraseEdits([.. Edits.Added.Where(tick => tick != at)], Edits.Removed);
        }

        return Replaced.TryGetValue(at, out Tick found) ? Editor().Move(at, found) : Editor().Join(at);
    }

    /// <summary>Whether a boundary in effect is the player's own: set or confirmed by them.</summary>
    internal static bool IsOwn(PhraseBoundary boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        return boundary.Attribution.Provenance is Provenance.UserEdited or Provenance.UserConfirmed;
    }

    /// <summary>
    /// The found boundary each of the player's own boundaries took the place of, by the player's boundary: a found boundary
    /// they took away goes with the nearer of the boundaries in effect either side of it when that one is a new boundary
    /// of theirs (a mark dragged away from it), the earlier of two as near; and each of theirs keeps the nearest found one
    /// that goes with it. The saved edits keep no pairs, so this is read again from them; nothing else is paired.
    /// </summary>
    public IReadOnlyDictionary<Tick, Tick> Replaced => _replaced ??= Pair();

    /// <summary>
    /// How many boundaries are the player's own, as «Ваших границ: N» counts them before a reset: their boundaries in effect
    /// (a moved one counts once) and the found boundaries they took away without putting one of theirs beside it.
    /// </summary>
    public int OwnCount
    {
        get
        {
            HashSet<Tick> inEffect = [.. Boundaries.Select(static boundary => boundary.At)];
            HashSet<Tick> paired = [.. Replaced.Values];
            return Boundaries.Count(IsOwn) + TakenAway(inEffect).Count(tick => !paired.Contains(tick));
        }
    }

    /// <summary>
    /// Each phrase of <paramref name="before"/> that is not one of these phrases any more (numbered as it was), with the
    /// phrase here that covers most of it: what an edit changed, for the offer to select a phrase that was selected by its
    /// new boundaries.
    /// </summary>
    public IReadOnlyList<(Phrase Before, Phrase After)> ChangedFrom(PhraseMap before)
    {
        ArgumentNullException.ThrowIfNull(before);
        HashSet<TickSpan> now = [.. Phrases.Select(static phrase => phrase.Span)];
        return [.. before.Phrases.Where(phrase => !now.Contains(phrase.Span)).Select(phrase => (phrase, Covering(phrase.Span)))];
    }

    /// <summary>The same notes and marks with other edits, for example after the player changed a boundary.</summary>
    public PhraseMap With(PhraseEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        return Build(Found, edits, _meter, End, Evidence);
    }

    /// <summary>
    /// The span to select for a phrase: the whole phrase, since a structure the map knows is selected
    /// whole (<see cref="MotifSelection.MaxStructureBars"/>, decision record 0013); or, when it runs
    /// even longer than that, its first bars up to that many.
    /// </summary>
    public TickSpan Selectable(Phrase phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        if (phrase.BarCount <= MotifSelection.MaxStructureBars)
        {
            return phrase.Span;
        }

        Tick stop = _meter.GetBar(phrase.FirstBar + MotifSelection.MaxStructureBars - 1).Span.End;
        return TickSpan.FromBounds(phrase.Span.Onset, stop < phrase.Span.End ? stop : phrase.Span.End);
    }

    /// <summary>
    /// The selection <paramref name="span"/> run on into its neighbouring phrase (#126), so the player can
    /// hear the transition: to the end of the phrase its end falls in (<paramref name="direction"/> 1), or back
    /// to the start of the phrase before its start (-1). A selection holds at most
    /// <see cref="MotifSelection.MaxBars"/> bars, so the edge that moves stops there. Null at either end of the
    /// piece, and when the selection already holds as many bars as it can that way.
    /// </summary>
    internal PhraseExtension? ExtendInto(TickSpan span, int direction)
    {
        if (direction is not (1 or -1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Extend by -1 (the previous phrase) or 1 (the next).");
        }

        if (direction == 1)
        {
            if (span.End >= End)
            {
                return null;
            }

            Phrase next = At(span.End);
            int lastBar = _meter.BarAt(span.Onset).Number + MotifSelection.MaxBars - 1;
            Tick stop = lastBar < _meter.BarAt(new Tick(next.Span.End.Value - 1)).Number ? _meter.GetBar(lastBar).Span.End : next.Span.End;
            return stop > span.End ? new PhraseExtension(TickSpan.FromBounds(span.Onset, stop), next, stop < next.Span.End) : null;
        }

        if (span.Onset == Tick.Zero)
        {
            return null;
        }

        Phrase previous = At(new Tick(span.Onset.Value - 1));
        int firstBar = _meter.BarAt(new Tick(span.End.Value - 1)).Number - MotifSelection.MaxBars + 1;
        Tick start = firstBar > _meter.BarAt(previous.Span.Onset).Number ? _meter.GetBar(firstBar).Span.Onset : previous.Span.Onset;
        return start < span.Onset ? new PhraseExtension(TickSpan.FromBounds(start, span.End), previous, start > previous.Span.Onset) : null;
    }

    /// <summary>
    /// "after a rest of a beat or more", "at a slur and at a repeat sign", "set by you", "at a slur,
    /// confirmed by you", or for the first phrase "the start of the piece": how a phrase's start was found.
    /// </summary>
    public static string Describe(PhraseBoundary? start) => Describe(start, Words.English);

    /// <summary>
    /// How a phrase's start was found, in the player's language (#134); the Russian completes "начинается …". With
    /// <paramref name="after"/>, how the phrase before it closes («Почему здесь», the phrases spec): a full or a half close
    /// comes first for a boundary the player did not set ("after a full close and after a rest of a beat or more").
    /// </summary>
    public static string Describe(PhraseBoundary? start, Words words, CloseKind after = CloseKind.None)
    {
        if (words.IsRussian)
        {
            return RussianDescribe(start, after);
        }

        if (start is null)
        {
            return "the start of the piece";
        }

        string[] evidence = [.. Close(start, after, "after a full close", "after a half close"), .. start.Evidence.Select(static mark => mark switch
        {
            PhraseEvidence.Slur => "at a slur",
            PhraseEvidence.Rest => "after a rest of a beat or more",
            PhraseEvidence.RepeatSign => "at a repeat sign",
            PhraseEvidence.Repetition => "where an earlier passage comes back note for note",
            PhraseEvidence.Restatement => "at the edge of a passage that starts again at once, note for note",
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

    private static string RussianDescribe(PhraseBoundary? start, CloseKind after)
    {
        if (start is null)
        {
            return "с начала пьесы";
        }

        string[] evidence = [.. Close(start, after, "после полной каденции", "после половинной каденции"), .. start.Evidence.Select(static mark => mark switch
        {
            PhraseEvidence.Slur => "по лиге",
            PhraseEvidence.Rest => "после паузы в долю или больше",
            PhraseEvidence.RepeatSign => "по знаку репризы",
            PhraseEvidence.Repetition => "там, где нота в ноту возвращается более ранний отрывок",
            PhraseEvidence.Restatement => "на краю отрывка, который сразу начинается снова нота в ноту",
            _ => mark.ToString(),
        })];
        string source = evidence.Length == 0 ? "" : Words.Russian.Joined(evidence);
        return start.Attribution.Provenance switch
        {
            Provenance.UserEdited or Provenance.UserConfirmed when source.Length == 0 => "там, где вы поставили границу",
            Provenance.UserEdited or Provenance.UserConfirmed => $"{source}, подтверждено вами",
            _ => source,
        };
    }

    /// <summary>The close of the phrase before a boundary the source supports, as a reason for it; none for one the player set.</summary>
    private static IEnumerable<string> Close(PhraseBoundary start, CloseKind after, string full, string half) =>
        IsOwn(start) ? [] : after switch
        {
            CloseKind.Full => [full],
            CloseKind.Half => [half],
            _ => [],
        };

    /// <summary>The boundary in effect at <paramref name="at"/>.</summary>
    private PhraseBoundary InEffect(Tick at) =>
        Boundaries.FirstOrDefault(boundary => boundary.At == at) ?? throw new ArgumentOutOfRangeException(nameof(at), at, "No phrase starts there.");

    /// <summary>The found boundaries the player took away that are not in effect.</summary>
    private IEnumerable<Tick> TakenAway(HashSet<Tick> inEffect)
    {
        HashSet<Tick> found = [.. Found.Select(static boundary => boundary.At)];
        return Edits.Removed.Where(tick => found.Contains(tick) && !inEffect.Contains(tick));
    }

    private Dictionary<Tick, Tick> Pair()
    {
        Tick[] inEffect = [.. Boundaries.Select(static boundary => boundary.At)];
        HashSet<Tick> mine = [.. Boundaries.Where(static boundary => boundary.Attribution.Provenance == Provenance.UserEdited).Select(static boundary => boundary.At)];
        var pairs = new Dictionary<Tick, Tick>();
        foreach (Tick taken in TakenAway([.. inEffect]))
        {
            int next = ~Array.BinarySearch(inEffect, taken);
            Tick[] beside = [.. new[] { next - 1, next }.Where(index => index >= 0 && index < inEffect.Length).Select(index => inEffect[index]).Where(mine.Contains)];
            if (beside.Length == 0)
            {
                continue;
            }

            Tick at = beside.MinBy(tick => Math.Abs(tick.Value - taken.Value));
            if (!pairs.TryGetValue(at, out Tick kept) || Math.Abs(taken.Value - at.Value) < Math.Abs(kept.Value - at.Value))
            {
                pairs[at] = taken;
            }
        }

        return pairs;
    }

    /// <summary>The phrase edits at this map's boundaries: the player may set one anywhere inside the piece.</summary>
    private LevelEditor Editor() =>
        new(Edits, [.. Boundaries.Select(static boundary => boundary.At)], tick => Inside(tick, End), Found.Select(static boundary => boundary.At).ToHashSet());

    /// <summary>Whether <paramref name="tick"/> lies inside a piece ending at <paramref name="end"/>, where a boundary of any level can be.</summary>
    internal static bool Inside(Tick tick, Tick end) => tick > Tick.Zero && tick < end;

    private static PhraseMap Build(IReadOnlyList<PhraseBoundary> found, PhraseEdits edits, MeterMap meter, Tick end, PhraseEvidenceLevel evidence)
    {
        IReadOnlyList<PhraseBoundary> boundaries = PhraseBoundaryFinder.WithPlayerBoundaries(found, edits, tick => Inside(tick, end));
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