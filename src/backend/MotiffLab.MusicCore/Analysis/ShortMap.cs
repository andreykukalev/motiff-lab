using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>What in the notes starts a short unit (decision record 0018, section 3: motifs and half-phrases of 1–4 bars).</summary>
public enum ShortEvidence
{
    /// <summary>The unit starts with its phrase; the phrase's own evidence says why.</summary>
    PhraseStart,

    /// <summary>Half a beat or more of silence across every part ends before the bar line it starts at.</summary>
    Rest,

    /// <summary>The figure that opens the phrase, on the top line, comes back here.</summary>
    Return,

    /// <summary>A note held for longer than a beat ends exactly at the bar line, and nothing sounds across it.</summary>
    HeldNote,

    /// <summary>Nothing in the notes marks it: the phrase was divided for its length, at the bar line nearest its middle.</summary>
    Length,
}

/// <summary>Where a short unit starts: the onset of its first note, with the evidence for cutting there.</summary>
/// <param name="Attribution">
/// The phrase's own attribution for a phrase start; inferred with the evidence's confidence for a cut read from the
/// notes; user-edited when the player cut here, user-confirmed when they cut where the notes already suggested it.
/// </param>
public sealed record ShortBoundary(Tick At, ShortEvidence Evidence, Attribution Attribution);

/// <summary>One short unit: a motif or half-phrase of a phrase, from where it starts to where the next one starts.</summary>
/// <param name="Number">1 for the first unit of the piece, then in order.</param>
/// <param name="Phrase">The phrase the unit divides; a phrase of up to <see cref="ShortMap.MaxBars"/> bars is one unit.</param>
public sealed record ShortUnit(int Number, TickSpan Span, int FirstBar, int LastBar, Phrase Phrase, ShortBoundary Start)
{
    /// <summary>The number of bars the unit touches, counting partly covered bars.</summary>
    public int BarCount => LastBar - FirstBar + 1;
}

/// <summary>
/// The piece as short units (decision record 0018, section 3): the Short level under the phrases, from one
/// analysis. Every phrase longer than <see cref="MaxBars"/> bars is divided at bar lines inside it, where the
/// notes give a reason first (a rest, the opening figure coming back, a held note ending) and for length
/// where they give none, until every unit is at most <see cref="MaxBars"/> bars; a shorter phrase is one unit.
/// </summary>
/// <remarks>
/// <para>
/// Units nest in phrases by construction: a unit never crosses a phrase boundary, and the first unit of a
/// phrase starts with it. The player's edits at this level, <see cref="Edits"/>, add cuts inside phrases and
/// take suggested cuts away; a cut the player took away is not replaced by another, even when the unit stays
/// long, since the player's word stands. Edits at phrase boundaries, or outside the piece, are left out.
/// </para>
/// <para>Pure: the same phrases, notes, meter and edits give the same units.</para>
/// </remarks>
public sealed class ShortMap
{
    /// <summary>The most bars a short unit runs when the notes alone decide (decision record 0018: 1–4 bars).</summary>
    public const int MaxBars = 4;

    /// <summary>Half a beat of silence across every part before a bar line often ends a figure.</summary>
    public const double RestConfidence = 0.7;

    /// <summary>The opening figure coming back often starts the figure again, but it can also continue one.</summary>
    public const double ReturnConfidence = 0.6;

    /// <summary>A long note ending at the bar line often closes a figure, but many figures run on across one.</summary>
    public const double HeldNoteConfidence = 0.5;

    /// <summary>A cut for length alone is a guess at the middle.</summary>
    public const double LengthConfidence = 0.4;

    /// <summary>The fewest onsets of the top line that must come back for a return to count.</summary>
    public const int MinReturnNotes = 3;

    private readonly MeterMap _meter;

    private ShortMap(PhraseMap phrases, MeterMap meter, IReadOnlyList<ShortBoundary> found, PhraseEdits edits, IReadOnlyList<ShortUnit> units)
    {
        Phrases = phrases;
        _meter = meter;
        Found = found;
        Edits = edits;
        Units = units;
    }

    /// <summary>The phrases the units divide.</summary>
    public PhraseMap Phrases { get; }

    /// <summary>The cuts inside phrases the notes suggest, in time order, before the player's changes.</summary>
    public IReadOnlyList<ShortBoundary> Found { get; }

    /// <summary>The player's changes at this level: cuts they set and suggested cuts they took away.</summary>
    public PhraseEdits Edits { get; }

    /// <summary>The units, in time order, covering every phrase; at least one per phrase.</summary>
    public IReadOnlyList<ShortUnit> Units { get; }

    /// <summary>The starts of every unit but the first: the phrase boundaries and the cuts in effect, in time order.</summary>
    public IEnumerable<ShortBoundary> Boundaries => Units.Skip(1).Select(static unit => unit.Start);

    /// <summary>The units of <paramref name="phrases"/>, divided by what <paramref name="notes"/> say, with the player's <paramref name="edits"/>.</summary>
    public static ShortMap Suggest(PhraseMap phrases, IEnumerable<Note> notes, MeterMap meter, PhraseEdits? edits = null)
    {
        ArgumentNullException.ThrowIfNull(phrases);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        Note[] sorted = [.. notes.OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Pitch.MidiNumber).ThenBy(static note => note.Id, StringComparer.Ordinal)];
        var found = new List<ShortBoundary>();
        foreach (Phrase phrase in phrases.Phrases)
        {
            found.AddRange(Divide(phrase, sorted, meter));
        }

        return Build(phrases, meter, found, edits ?? PhraseEdits.None);
    }

    /// <summary>The same phrases and notes with other edits.</summary>
    public ShortMap With(PhraseEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        return Build(Phrases, _meter, Found, edits);
    }

    /// <summary>The unit that holds a position; the last unit for the end of the piece or later.</summary>
    public ShortUnit At(Tick position) => Units.LastOrDefault(unit => unit.Span.Onset <= position) ?? Units[0];

    /// <summary>The unit whose span is exactly <paramref name="span"/>, or null.</summary>
    public ShortUnit? Matching(TickSpan span) => Units.FirstOrDefault(unit => unit.Span == span);

    /// <summary>The units of a phrase, in order.</summary>
    public IReadOnlyList<ShortUnit> Of(Phrase phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        return [.. Units.Where(unit => unit.Phrase.Number == phrase.Number)];
    }

    /// <summary>
    /// The edits that make <paramref name="span"/> one unit of its phrase: cuts at its edges, unless those are
    /// phrase boundaries or the piece's ends, and none inside it. An edge less than a beat from a cut or a
    /// phrase boundary moves to it. The span must lie within one phrase; <see cref="PhraseLevels"/> joins
    /// phrases first when it does not.
    /// </summary>
    public PhraseEdits MakeUnit(TickSpan span)
    {
        Tick start = Snap(span.Onset);
        Tick stop = Snap(span.End > Phrases.End ? Phrases.End : span.End);
        var phraseBoundaries = Phrases.Boundaries.Select(static boundary => boundary.At).ToHashSet();
        var cuts = Boundaries.Select(static boundary => boundary.At).Where(tick => !phraseBoundaries.Contains(tick) && (tick <= start || tick >= stop)).ToHashSet();
        var confirm = new HashSet<Tick>();
        foreach (Tick edge in stop > start ? [start, stop] : new[] { start })
        {
            if (edge > Tick.Zero && edge < Phrases.End && !phraseBoundaries.Contains(edge))
            {
                cuts.Add(edge);
                confirm.Add(edge);
            }
        }

        return EditsFor(cuts, confirm);
    }

    /// <summary>
    /// The edits that join a unit with the one after it by taking its cut away. Unchanged for the last unit of a
    /// phrase: joining across a phrase boundary is a phrase edit, which <see cref="PhraseLevels"/> makes.
    /// </summary>
    public PhraseEdits JoinWithNext(ShortUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var phraseBoundaries = Phrases.Boundaries.Select(static boundary => boundary.At).ToHashSet();
        return EditsFor(Boundaries.Select(static boundary => boundary.At).Where(tick => !phraseBoundaries.Contains(tick) && tick != unit.Span.End), []);
    }

    /// <summary>The edits that cut at <paramref name="at"/>, snapped like an edge of <see cref="MakeUnit"/>, and change nothing else.</summary>
    public PhraseEdits SplitAt(Tick at)
    {
        Tick edge = Snap(at);
        var phraseBoundaries = Phrases.Boundaries.Select(static boundary => boundary.At).ToHashSet();
        if (edge <= Tick.Zero || edge >= Phrases.End || phraseBoundaries.Contains(edge))
        {
            return Edits;
        }

        return EditsFor(Boundaries.Select(static boundary => boundary.At).Where(tick => !phraseBoundaries.Contains(tick)).Append(edge), [edge]);
    }

    /// <summary>How a unit's start was found, in the player's language; the Russian completes "начинается …".</summary>
    public static string Describe(ShortBoundary start, Words words)
    {
        ArgumentNullException.ThrowIfNull(start);
        string evidence = start.Evidence switch
        {
            ShortEvidence.Rest => words.T("after half a beat or more of rest", "после паузы в полдоли или больше"),
            ShortEvidence.Return => words.T("where the phrase's opening figure comes back", "там, где возвращается начальная фигура фразы"),
            ShortEvidence.HeldNote => words.T("where a held note ends at the bar line", "там, где выдержанная нота заканчивается на тактовой черте"),
            ShortEvidence.Length => words.T("at the middle of a long phrase", "в середине длинной фразы"),
            _ => words.T("with its phrase", "вместе со своей фразой"),
        };
        return start.Attribution.Provenance switch
        {
            Provenance.UserEdited when start.Evidence == ShortEvidence.Length => words.T("set by you", "там, где вы поставили границу"),
            Provenance.UserEdited or Provenance.UserConfirmed when start.Evidence != ShortEvidence.PhraseStart => words.T($"{evidence}, confirmed by you", $"{evidence}, подтверждено вами"),
            _ => evidence,
        };
    }

    /// <summary>A cut or phrase boundary in effect less than a beat from <paramref name="edge"/>, or the edge itself.</summary>
    private Tick Snap(Tick edge)
    {
        long beat = _meter.SignatureAt(edge).BeatTicks;
        return Boundaries.Select(static boundary => boundary.At)
            .Where(tick => Math.Abs(tick.Value - edge.Value) < beat)
            .OrderBy(tick => Math.Abs(tick.Value - edge.Value))
            .Cast<Tick?>()
            .FirstOrDefault() ?? edge;
    }

    /// <summary>The smallest edits from the cuts found to exactly <paramref name="cuts"/>; a found cut confirmed stays in the added list.</summary>
    private PhraseEdits EditsFor(IEnumerable<Tick> cuts, IEnumerable<Tick> confirm)
    {
        var wanted = cuts.Where(tick => tick > Tick.Zero && tick < Phrases.End).ToHashSet();
        var found = Found.Select(static boundary => boundary.At).ToHashSet();
        IEnumerable<Tick> confirmed = Edits.Added.Concat(confirm).Where(tick => found.Contains(tick) && wanted.Contains(tick));
        return new PhraseEdits(
            [.. wanted.Where(tick => !found.Contains(tick)).Concat(confirmed).Distinct().Order()],
            [.. found.Where(tick => !wanted.Contains(tick)).Order()]);
    }

    private static ShortMap Build(PhraseMap phrases, MeterMap meter, IReadOnlyList<ShortBoundary> found, PhraseEdits edits)
    {
        var removed = edits.Removed.ToHashSet();
        var cuts = new SortedDictionary<Tick, ShortBoundary>();
        foreach (ShortBoundary boundary in found.Where(boundary => !removed.Contains(boundary.At)))
        {
            cuts[boundary.At] = boundary;
        }

        var phraseBoundaries = phrases.Boundaries.Select(static boundary => boundary.At).ToHashSet();
        foreach (Tick at in edits.Added.Where(tick => tick > Tick.Zero && tick < phrases.End && !phraseBoundaries.Contains(tick)))
        {
            cuts[at] = cuts.TryGetValue(at, out ShortBoundary? suggested)
                ? suggested with { Attribution = Attribution.UserConfirmed }
                : new ShortBoundary(at, ShortEvidence.Length, Attribution.UserEdited);
        }

        var units = new List<ShortUnit>();
        foreach (Phrase phrase in phrases.Phrases)
        {
            var starts = new List<ShortBoundary> { new(phrase.Span.Onset, ShortEvidence.PhraseStart, phrase.Start?.Attribution ?? Attribution.Imported) };
            starts.AddRange(cuts.Values.Where(cut => cut.At > phrase.Span.Onset && cut.At < phrase.Span.End));
            for (int i = 0; i < starts.Count; i++)
            {
                Tick from = starts[i].At;
                Tick to = i + 1 < starts.Count ? starts[i + 1].At : phrase.Span.End;
                units.Add(new ShortUnit(units.Count + 1, TickSpan.FromBounds(from, to), meter.BarAt(from).Number, meter.BarAt(new Tick(to.Value - 1)).Number, phrase, starts[i]));
            }
        }

        return new ShortMap(phrases, meter, found, edits, units);
    }

    /// <summary>The cuts that divide one phrase into units of at most <see cref="MaxBars"/> bars, in time order.</summary>
    private static IEnumerable<ShortBoundary> Divide(Phrase phrase, Note[] sorted, MeterMap meter)
    {
        if (phrase.BarCount <= MaxBars)
        {
            return [];
        }

        Note[] inside = [.. sorted.Where(note => phrase.Span.Contains(note.Span.Onset))];
        if (inside.Length == 0)
        {
            return [];
        }

        Tick[] onsets = [.. inside.Select(static note => note.Span.Onset).Distinct()];
        var candidates = new List<(int Bar, ShortBoundary Cut)>();
        for (int bar = phrase.FirstBar + 1; bar <= phrase.LastBar; bar++)
        {
            Tick line = meter.GetBar(bar).Span.Onset;
            if (FirstOnsetFrom(onsets, line) is not { } at || at >= phrase.Span.End || at <= phrase.Span.Onset)
            {
                continue;
            }

            // A cut only at the first onset of its bar: a bar whose first note arrives late belongs with the one before.
            if (meter.BarAt(at).Number != bar)
            {
                continue;
            }

            ShortEvidence evidence = Evidence(inside, onsets, at, line, meter);
            candidates.Add((bar, new ShortBoundary(at, evidence, Attribution.Inferred(Confidence(evidence)))));
        }

        var cuts = new List<ShortBoundary>();
        Split(phrase.FirstBar, phrase.LastBar, candidates, cuts);
        return cuts.OrderBy(static cut => cut.At);
    }

    /// <summary>Divides bars <paramref name="first"/> to <paramref name="last"/> at the best candidate until every part is short enough.</summary>
    private static void Split(int first, int last, List<(int Bar, ShortBoundary Cut)> candidates, List<ShortBoundary> cuts)
    {
        if (last - first + 1 <= MaxBars)
        {
            return;
        }

        double middle = (first + last + 1) / 2.0;
        (int Bar, ShortBoundary Cut)? best = candidates
            .Where(candidate => candidate.Bar > first && candidate.Bar <= last)
            .OrderBy(candidate => Rank(candidate.Cut.Evidence))
            .ThenBy(candidate => Math.Abs(candidate.Bar - middle))
            .ThenBy(candidate => candidate.Bar)
            .Cast<(int Bar, ShortBoundary Cut)?>()
            .FirstOrDefault();
        if (best is not { } chosen)
        {
            return;
        }

        cuts.Add(chosen.Cut);
        Split(first, chosen.Bar - 1, candidates, cuts);
        Split(chosen.Bar, last, candidates, cuts);
    }

    private static int Rank(ShortEvidence evidence) => evidence switch
    {
        ShortEvidence.Rest => 0,
        ShortEvidence.Return => 1,
        ShortEvidence.HeldNote => 2,
        _ => 3,
    };

    private static double Confidence(ShortEvidence evidence) => evidence switch
    {
        ShortEvidence.Rest => RestConfidence,
        ShortEvidence.Return => ReturnConfidence,
        ShortEvidence.HeldNote => HeldNoteConfidence,
        _ => LengthConfidence,
    };

    /// <summary>What the notes say about cutting at <paramref name="at"/>, the first onset at or after the bar line <paramref name="line"/>.</summary>
    private static ShortEvidence Evidence(Note[] inside, Tick[] onsets, Tick at, Tick line, MeterMap meter)
    {
        Note[] before = [.. inside.Where(note => note.Span.Onset < at)];
        if (before.Length == 0)
        {
            return ShortEvidence.Length;
        }

        long beat = meter.SignatureAt(line).BeatTicks;
        long soundingUntil = before.Max(note => note.Span.End.Value);
        if (at.Value - soundingUntil >= beat / 2)
        {
            return ShortEvidence.Rest;
        }

        if (Returns(inside, onsets, at))
        {
            return ShortEvidence.Return;
        }

        long lastOnset = before.Max(note => note.Span.Onset.Value);
        return soundingUntil == line.Value && at == line && line.Value - lastOnset > beat ? ShortEvidence.HeldNote : ShortEvidence.Length;
    }

    /// <summary>Whether the top line's first <see cref="MinReturnNotes"/> onsets of the phrase come back at <paramref name="at"/> with the same pitches and spacing.</summary>
    private static bool Returns(Note[] inside, Tick[] onsets, Tick at)
    {
        int start = Array.IndexOf(onsets, at);
        if (start < 0 || start + MinReturnNotes > onsets.Length || MinReturnNotes > start)
        {
            return false;
        }

        int Top(Tick onset) => inside.Where(note => note.Span.Onset == onset).Max(note => note.Pitch.MidiNumber);
        for (int i = 0; i < MinReturnNotes; i++)
        {
            if (Top(onsets[i]) != Top(onsets[start + i])
                || (i > 0 && onsets[i].Value - onsets[i - 1].Value != onsets[start + i].Value - onsets[start + i - 1].Value))
            {
                return false;
            }
        }

        return true;
    }

    private static Tick? FirstOnsetFrom(Tick[] onsets, Tick position)
    {
        int index = Array.BinarySearch(onsets, position);
        if (index < 0)
        {
            index = ~index;
        }

        return index < onsets.Length ? onsets[index] : null;
    }
}