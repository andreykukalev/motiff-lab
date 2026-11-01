using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>The three levels the piece is read at (decision record 0018, section 3), finest first.</summary>
public enum PhraseLevel
{
    /// <summary>Motifs and half-phrases of 1–4 bars (<see cref="ShortMap"/>).</summary>
    Short,

    /// <summary>Phrases of 4–8 bars, the default (<see cref="PhraseMap"/>).</summary>
    Phrase,

    /// <summary>Sentences of 8–16 or more bars (<see cref="SentenceMap"/>).</summary>
    Sentence,
}

/// <summary>The player's edits at every level, saved with the project; each level's edits stick to the piece until Reset to detected.</summary>
public sealed record LevelEdits(PhraseEdits Short, PhraseEdits Phrase, PhraseEdits Sentence)
{
    public static LevelEdits None { get; } = new(PhraseEdits.None, PhraseEdits.None, PhraseEdits.None);

    public bool IsEmpty => Short.IsEmpty && Phrase.IsEmpty && Sentence.IsEmpty;

    public PhraseEdits this[PhraseLevel level] => level switch
    {
        PhraseLevel.Short => Short,
        PhraseLevel.Phrase => Phrase,
        PhraseLevel.Sentence => Sentence,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown level."),
    };

    /// <summary>These edits with one level's replaced.</summary>
    public LevelEdits With(PhraseLevel level, PhraseEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        return level switch
        {
            PhraseLevel.Short => this with { Short = edits },
            PhraseLevel.Phrase => this with { Phrase = edits },
            PhraseLevel.Sentence => this with { Sentence = edits },
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown level."),
        };
    }
}

/// <summary>
/// One unit of any level, as the brackets and the facade show it: a short unit, a phrase or a sentence with
/// its number at its level, its place and how its start was found. The maps keep the richer records
/// (<see cref="ShortUnit"/>, <see cref="Phrase"/>, <see cref="Sentence"/>); this is the view every level shares.
/// </summary>
/// <param name="Number">1 for the first unit of the piece at this level, then in order.</param>
/// <param name="Start">How the unit's start was found: imported for the start of the piece, otherwise the boundary's attribution.</param>
/// <param name="Evidence">The evidence names behind the start, in lower camel case; empty for the start of the piece or a sentence.</param>
/// <param name="Description">How the start was found, in the language asked for, as <see cref="PhraseMap.Describe(PhraseBoundary?, Words)"/> words it; a sentence's basis.</param>
public sealed record PhraseUnit(PhraseLevel Level, int Number, TickSpan Span, int FirstBar, int LastBar, Attribution Start, IReadOnlyList<string> Evidence, string Description)
{
    /// <summary>The number of bars the unit touches, counting partly covered bars.</summary>
    public int BarCount => LastBar - FirstBar + 1;

    /// <summary>Whether the unit runs longer than a phrase usually does (<see cref="PhraseMap.LongPhraseBars"/>).</summary>
    public bool IsLong => BarCount > PhraseMap.LongPhraseBars;

    /// <summary>Whether <paramref name="span"/> lies within the unit.</summary>
    public bool Contains(TickSpan span) => Span.Onset <= span.Onset && span.End <= Span.End;

    public bool Equals(PhraseUnit? other) =>
        other is not null && Level == other.Level && Number == other.Number && Span == other.Span && FirstBar == other.FirstBar && LastBar == other.LastBar
        && Start == other.Start && Evidence.SequenceEqual(other.Evidence, StringComparer.Ordinal) && Description == other.Description;

    public override int GetHashCode() => HashCode.Combine(Level, Number, Span);
}

/// <summary>
/// The piece in three levels from one analysis (decision record 0018, section 3; preparation item 7): the
/// phrases <see cref="PhraseMap"/> finds, the sentences <see cref="SentenceMap"/> groups them into, and the
/// short units <see cref="ShortMap"/> divides them into. The levels nest: every sentence boundary is a phrase
/// boundary and every phrase boundary a short boundary, so a unit of a coarser level is made of whole units
/// of the finer one, and changing the level moves a selection to the enclosing or the contained unit.
/// </summary>
/// <remarks>
/// <para>
/// Edits are kept per level (<see cref="LevelEdits"/>) and cascade only where nesting demands it: making a unit
/// at a coarse level puts boundaries at its edges at the finer levels too, and joining at a fine level across
/// a boundary of a coarser level takes that boundary away there as well. Reset at a level forgets that
/// level's edits only.
/// </para>
/// <para>Pure: the same notes, meter, marks, key, language and edits give the same units at every level.</para>
/// </remarks>
public sealed class PhraseLevels
{
    private readonly IReadOnlyList<Note> _notes;
    private readonly MeterMap _meter;
    private readonly KeySignature? _signature;
    private readonly Words _words;

    private PhraseLevels(IReadOnlyList<Note> notes, MeterMap meter, KeySignature? signature, Words words, PhraseMap phrases, SentenceMap sentences, ShortMap shorts, LevelEdits edits)
    {
        _notes = notes;
        _meter = meter;
        _signature = signature;
        _words = words;
        Phrases = phrases;
        Sentences = sentences;
        Shorts = shorts;
        Edits = edits;
    }

    public PhraseMap Phrases { get; }

    public SentenceMap Sentences { get; }

    public ShortMap Shorts { get; }

    public LevelEdits Edits { get; }

    /// <summary>The levels, finest first.</summary>
    public static IReadOnlyList<PhraseLevel> All { get; } = [PhraseLevel.Short, PhraseLevel.Phrase, PhraseLevel.Sentence];

    /// <summary>
    /// The three levels of a piece ending at <paramref name="end"/>: its phrases from <paramref name="notes"/>
    /// and <paramref name="marks"/>, its sentences read in the key <paramref name="signature"/> states, if
    /// any, and its short units, with the player's <paramref name="edits"/>, written in <paramref name="words"/>.
    /// </summary>
    public static PhraseLevels Suggest(IReadOnlyList<Note> notes, MeterMap meter, PhraseMarks marks, Tick end, KeySignature? signature, Words words = default, LevelEdits? edits = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        LevelEdits all = edits ?? LevelEdits.None;
        PhraseMap phrases = PhraseMap.Suggest(notes, meter, marks, end, all.Phrase);
        return Build(notes, meter, signature, words, phrases, all);
    }

    /// <summary>The same piece with other edits.</summary>
    public PhraseLevels With(LevelEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        return Build(_notes, _meter, _signature, _words, edits.Phrase == Edits.Phrase ? Phrases : Phrases.With(edits.Phrase), edits);
    }

    /// <summary>The units of a level, in time order; at least one.</summary>
    public IReadOnlyList<PhraseUnit> Units(PhraseLevel level) => level switch
    {
        PhraseLevel.Short => [.. Shorts.Units.Select(unit => new PhraseUnit(
            PhraseLevel.Short, unit.Number, unit.Span, unit.FirstBar, unit.LastBar, unit.Start.Attribution,
            unit.Start.Evidence == ShortEvidence.PhraseStart ? PhraseEvidenceNames(unit.Phrase.Start) : [Name(unit.Start.Evidence)],
            unit.Start.Evidence == ShortEvidence.PhraseStart ? PhraseMap.Describe(unit.Phrase.Start, _words) : ShortMap.Describe(unit.Start, _words)))],
        PhraseLevel.Phrase => [.. Phrases.Phrases.Select(phrase => new PhraseUnit(
            PhraseLevel.Phrase, phrase.Number, phrase.Span, phrase.FirstBar, phrase.LastBar, phrase.Start?.Attribution ?? Attribution.Imported,
            PhraseEvidenceNames(phrase.Start), PhraseMap.Describe(phrase.Start, _words)))],
        PhraseLevel.Sentence => [.. Sentences.Sentences.Select(sentence => new PhraseUnit(
            PhraseLevel.Sentence, sentence.Number, sentence.Span, sentence.FirstBar, sentence.LastBar, sentence.Found, [], sentence.Basis))],
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown level."),
    };

    /// <summary>The unit of a level that holds a position; the last unit for the end of the piece or later.</summary>
    public PhraseUnit At(PhraseLevel level, Tick position)
    {
        IReadOnlyList<PhraseUnit> units = Units(level);
        return units.LastOrDefault(unit => unit.Span.Onset <= position) ?? units[0];
    }

    /// <summary>The unit of a level whose span is exactly <paramref name="span"/>, or null.</summary>
    public PhraseUnit? Matching(PhraseLevel level, TickSpan span) => Units(level).FirstOrDefault(unit => unit.Span == span);

    /// <summary>The unit of a level that <paramref name="span"/> lies within, or null when it crosses units.</summary>
    public PhraseUnit? Containing(PhraseLevel level, TickSpan span) => Units(level).FirstOrDefault(unit => unit.Contains(span));

    /// <summary>The unit of the next coarser level that holds <paramref name="unit"/>; null for a sentence.</summary>
    public PhraseUnit? Parent(PhraseUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return unit.Level == PhraseLevel.Sentence ? null : At(unit.Level + 1, unit.Span.Onset);
    }

    /// <summary>
    /// The span to select for a unit: the whole unit, since a structure the maps know is selected whole
    /// (<see cref="Motifs.MotifSelection.MaxStructureBars"/>); or, when it runs even longer than that, its first bars up to that many.
    /// </summary>
    public TickSpan Selectable(PhraseUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.BarCount <= Motifs.MotifSelection.MaxStructureBars)
        {
            return unit.Span;
        }

        Tick stop = _meter.GetBar(unit.FirstBar + Motifs.MotifSelection.MaxStructureBars - 1).Span.End;
        return TickSpan.FromBounds(unit.Span.Onset, stop < unit.Span.End ? stop : unit.Span.End);
    }

    /// <summary>The level whose units <paramref name="span"/> is one of, finest first; null when it is none.</summary>
    public PhraseLevel? LevelOf(TickSpan span) => All.Cast<PhraseLevel?>().FirstOrDefault(level => Matching(level!.Value, span) is not null);

    /// <summary>
    /// Where a selection goes when the level changes to <paramref name="to"/> (decision record 0018, section 3):
    /// the unit that is exactly the selection, else the unit that encloses it, else, when the selection spans
    /// several units of the finer level, the one it starts in.
    /// </summary>
    public PhraseUnit Map(TickSpan span, PhraseLevel to) => Matching(to, span) ?? Containing(to, span) ?? At(to, span.Onset);

    /// <summary>
    /// The edits that make <paramref name="span"/> one unit at <paramref name="level"/> (Make phrase, moving a
    /// handle, or Split, which is a span of one edge): boundaries at its edges at this and every finer level, and
    /// none inside it at this and every coarser level.
    /// </summary>
    public LevelEdits MakeUnit(PhraseLevel level, TickSpan span)
    {
        switch (level)
        {
            case PhraseLevel.Phrase:
                {
                    PhraseEdits phrase = Phrases.MakePhrase(span);
                    return Edits.With(PhraseLevel.Phrase, phrase);
                }

            case PhraseLevel.Sentence:
                {
                    // The edges become phrase boundaries, which the finer levels inherit; the sentence then spans them.
                    PhraseMap phrases = Phrases.With(Phrases.SplitAt(span.Onset));
                    phrases = phrases.With(phrases.SplitAt(span.End));
                    PhraseLevels withPhrases = With(Edits.With(PhraseLevel.Phrase, phrases.Edits));
                    TickSpan snapped = TickSpan.FromBounds(withPhrases.Phrases.At(span.Onset).Span.Onset, withPhrases.Phrases.At(new Tick(Math.Max(span.End.Value - 1, 0))).Span.End);
                    return withPhrases.Edits.With(PhraseLevel.Sentence, withPhrases.Sentences.MakeSentence(snapped));
                }

            default:
                {
                    // Phrase boundaries inside the unit go, since a short unit never crosses one.
                    PhraseLevels joined = Phrases.Boundaries.Any(boundary => boundary.At > span.Onset && boundary.At < span.End)
                        ? With(Edits.With(PhraseLevel.Phrase, Phrases.JoinAcross(span)))
                        : this;
                    return joined.Edits.With(PhraseLevel.Short, joined.Shorts.MakeUnit(span));
                }
        }
    }

    /// <summary>
    /// The edits that join a unit with the one after it at <paramref name="level"/>: the boundary between them
    /// goes at this level and every coarser level that has it. Unchanged for the last unit of the piece.
    /// </summary>
    public LevelEdits JoinWithNext(PhraseLevel level, PhraseUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.Level != level)
        {
            throw new ArgumentException("The unit is not of that level.", nameof(unit));
        }

        if (unit.Span.End >= Phrases.End)
        {
            return Edits;
        }

        switch (level)
        {
            case PhraseLevel.Sentence:
                return Edits.With(PhraseLevel.Sentence, Sentences.JoinWithNext(Sentences.Sentences[unit.Number - 1]));
            case PhraseLevel.Phrase:
                return Edits.With(PhraseLevel.Phrase, Phrases.JoinWithNext(Phrases.Phrases[unit.Number - 1]));
            default:
                ShortUnit shortUnit = Shorts.Units[unit.Number - 1];
                return Phrases.Boundaries.Any(boundary => boundary.At == unit.Span.End)
                    ? Edits.With(PhraseLevel.Phrase, Phrases.JoinWithNext(shortUnit.Phrase))
                    : Edits.With(PhraseLevel.Short, Shorts.JoinWithNext(shortUnit));
        }
    }

    /// <summary>The edits that put a boundary at <paramref name="at"/> at <paramref name="level"/> and every finer level: Split here.</summary>
    public LevelEdits SplitAt(PhraseLevel level, Tick at)
    {
        switch (level)
        {
            case PhraseLevel.Phrase:
                return Edits.With(PhraseLevel.Phrase, Phrases.SplitAt(at));
            case PhraseLevel.Sentence:
                {
                    PhraseLevels withPhrase = With(Edits.With(PhraseLevel.Phrase, Phrases.SplitAt(at)));
                    Tick edge = withPhrase.Phrases.At(at).Span.Onset;
                    return withPhrase.Edits.With(PhraseLevel.Sentence, withPhrase.Sentences.SplitAt(edge));
                }

            default:
                return Edits.With(PhraseLevel.Short, Shorts.SplitAt(at));
        }
    }

    /// <summary>The edits with one level back to what was detected.</summary>
    public LevelEdits Reset(PhraseLevel level) => Edits.With(level, PhraseEdits.None);

    /// <summary>The level's name as the facade and the settings write it: "short", "phrase", "sentence".</summary>
    public static string Name(PhraseLevel level) => level switch
    {
        PhraseLevel.Short => "short",
        PhraseLevel.Phrase => "phrase",
        PhraseLevel.Sentence => "sentence",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown level."),
    };

    /// <summary>The level a name stands for: the facade's "short" / "phrase" / "sentence" or the setting's "short" / "phrases" / "sentences"; null for another text.</summary>
    public static PhraseLevel? Parse(string? name) => name switch
    {
        "short" => PhraseLevel.Short,
        "phrase" or "phrases" => PhraseLevel.Phrase,
        "sentence" or "sentences" => PhraseLevel.Sentence,
        _ => null,
    };

    /// <summary>The value of the setting "phrases.granularity" for a level: "short", "phrases", "sentences".</summary>
    public static string SettingValue(PhraseLevel level) => level switch
    {
        PhraseLevel.Short => "short",
        PhraseLevel.Phrase => "phrases",
        PhraseLevel.Sentence => "sentences",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown level."),
    };

    /// <summary>"short unit", "phrase", "sentence" in the player's language, in the nominative singular.</summary>
    public static string Describe(PhraseLevel level, Words words) => level switch
    {
        PhraseLevel.Short => words.T("short unit", "короткое построение"),
        PhraseLevel.Phrase => words.T("phrase", "фраза"),
        PhraseLevel.Sentence => words.T("sentence", "предложение"),
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown level."),
    };

    private static PhraseLevels Build(IReadOnlyList<Note> notes, MeterMap meter, KeySignature? signature, Words words, PhraseMap phrases, LevelEdits edits)
    {
        SentenceMap sentences = SentenceMap.Suggest(phrases, notes, signature, edits.Sentence, words);
        ShortMap shorts = ShortMap.Suggest(phrases, notes, meter, edits.Short);
        return new PhraseLevels(notes, meter, signature, words, phrases, sentences, shorts, edits);
    }

    private static IReadOnlyList<string> PhraseEvidenceNames(PhraseBoundary? start) =>
        start is null ? [] : [.. start.Evidence.Select(static evidence => Name(evidence.ToString()))];

    private static string Name(ShortEvidence evidence) => Name(evidence.ToString());

    private static string Name(string name) => name.Length > 0 ? char.ToLowerInvariant(name[0]) + name[1..] : name;
}