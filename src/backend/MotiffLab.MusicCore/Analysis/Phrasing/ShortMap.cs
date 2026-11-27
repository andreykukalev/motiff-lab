using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis.Phrasing;

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
public sealed partial class ShortMap
{
    /// <summary>The most bars a short unit runs when the notes alone decide (decision record 0018: 1–4 bars).</summary>
    public const int MaxBars = 4;

    /// <summary>Half a beat of silence across every part before a bar line often ends a figure.</summary>
    public const double RestConfidence = 0.7;

    /// <summary>The opening figure coming back often starts the figure again, but it can also continue one.</summary>
    internal const double ReturnConfidence = 0.6;

    /// <summary>A long note ending at the bar line often closes a figure, but many figures run on across one.</summary>
    internal const double HeldNoteConfidence = 0.5;

    /// <summary>A cut for length alone is a guess at the middle.</summary>
    internal const double LengthConfidence = 0.4;

    /// <summary>The fewest onsets of the top line that must come back for a return to count.</summary>
    internal const int MinReturnNotes = 3;

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
        LevelEditor editor = Editor();
        return editor.Make(editor.Snap(span.Onset, _meter), editor.Snap(span.End > Phrases.End ? Phrases.End : span.End, _meter));
    }

    /// <summary>
    /// The edits that join a unit with the one after it by taking its cut away. Unchanged for the last unit of a
    /// phrase: joining across a phrase boundary is a phrase edit, which <see cref="PhraseLevels"/> makes.
    /// </summary>
    public PhraseEdits JoinWithNext(ShortUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return Editor().Join(unit.Span.End);
    }

    /// <summary>The edits that cut at <paramref name="at"/>, snapped like an edge of <see cref="MakeUnit"/>, and change nothing else.</summary>
    public PhraseEdits SplitAt(Tick at)
    {
        LevelEditor editor = Editor();
        return editor.Split(editor.Snap(at, _meter));
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

    /// <summary>The short edits at this map's cuts and phrase boundaries: the player may cut anywhere inside the piece but at a phrase boundary.</summary>
    private LevelEditor Editor() =>
        new(Edits, [.. Boundaries.Select(static boundary => boundary.At)], CutsAllowed(Phrases), Found.Select(static boundary => boundary.At).ToHashSet());

    /// <summary>Whether a tick can hold a cut of <paramref name="phrases"/>: inside the piece and not at a phrase boundary, which is a phrase edit.</summary>
    private static Func<Tick, bool> CutsAllowed(PhraseMap phrases)
    {
        var phraseBoundaries = phrases.Boundaries.Select(static boundary => boundary.At).ToHashSet();
        return tick => PhraseMap.Inside(tick, phrases.End) && !phraseBoundaries.Contains(tick);
    }

    private static ShortMap Build(PhraseMap phrases, MeterMap meter, IReadOnlyList<ShortBoundary> found, PhraseEdits edits)
    {
        IReadOnlyList<ShortBoundary> cuts = edits.AppliedTo(
            found,
            static boundary => boundary.At,
            CutsAllowed(phrases),
            static suggested => suggested with { Attribution = Attribution.UserConfirmed },
            static at => new ShortBoundary(at, ShortEvidence.Length, Attribution.UserEdited));
        var units = new List<ShortUnit>();
        foreach (Phrase phrase in phrases.Phrases)
        {
            var starts = new List<ShortBoundary> { new(phrase.Span.Onset, ShortEvidence.PhraseStart, phrase.Start?.Attribution ?? Attribution.Imported) };
            starts.AddRange(cuts.Where(cut => cut.At > phrase.Span.Onset && cut.At < phrase.Span.End));
            for (int i = 0; i < starts.Count; i++)
            {
                Tick from = starts[i].At;
                Tick to = i + 1 < starts.Count ? starts[i + 1].At : phrase.Span.End;
                units.Add(new ShortUnit(units.Count + 1, TickSpan.FromBounds(from, to), meter.BarAt(from).Number, meter.BarAt(new Tick(to.Value - 1)).Number, phrase, starts[i]));
            }
        }

        return new ShortMap(phrases, meter, found, edits, units);
    }
}