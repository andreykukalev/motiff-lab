using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>The kinds of chord the chord finder names (S1-A3): triads, a suspended fourth, and the common sevenths.</summary>
public enum ChordQuality
{
    Major,
    Minor,
    Diminished,
    Augmented,
    Suspended4,
    Dominant7,
    Major7,
    Minor7,
    HalfDiminished7,
    Diminished7,
}

/// <summary>A chord by its root and kind, without a voicing: "Am", "E7".</summary>
/// <param name="Root">The root's pitch class, 0 (C) to 11 (B).</param>
public readonly record struct ChordLabel(int Root, ChordQuality Quality)
{
    /// <summary>The chord's tones above the root in semitones, root first, then third (or fourth), fifth and seventh.</summary>
    public IReadOnlyList<int> Intervals => Quality switch
    {
        ChordQuality.Major => [0, 4, 7],
        ChordQuality.Minor => [0, 3, 7],
        ChordQuality.Diminished => [0, 3, 6],
        ChordQuality.Augmented => [0, 4, 8],
        ChordQuality.Suspended4 => [0, 5, 7],
        ChordQuality.Dominant7 => [0, 4, 7, 10],
        ChordQuality.Major7 => [0, 4, 7, 11],
        ChordQuality.Minor7 => [0, 3, 7, 10],
        ChordQuality.HalfDiminished7 => [0, 3, 6, 10],
        ChordQuality.Diminished7 => [0, 3, 6, 9],
        _ => throw new ArgumentOutOfRangeException(nameof(Quality), Quality, "Unknown chord quality."),
    };

    /// <summary>The chord's pitch classes in the order of <see cref="Intervals"/>.</summary>
    public IReadOnlyList<int> PitchClasses
    {
        get
        {
            int root = Root;
            return [.. Intervals.Select(interval => (root + interval) % 12)];
        }
    }

    /// <summary>Whether a seventh is part of the chord.</summary>
    public bool IsSeventh => Intervals.Count == 4;

    public bool Contains(int pitchClass) => PitchClasses.Contains(((pitchClass % 12) + 12) % 12);

    /// <summary>Which chord tone a pitch class is: 0 the root, 1 the third (or fourth), 2 the fifth, 3 the seventh; −1 when it is not in the chord.</summary>
    public int Position(int pitchClass)
    {
        int index = 0;
        foreach (int tone in PitchClasses)
        {
            if (tone == ((pitchClass % 12) + 12) % 12)
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    /// <summary>The chord symbol, spelled for a key, with the bass after a slash when it is not the root: "Am", "E/G♯", "Bdim7".</summary>
    public string Symbol(MusicalKey spelling, int? bassPitchClass = null)
    {
        string suffix = Quality switch
        {
            ChordQuality.Major => "",
            ChordQuality.Minor => "m",
            ChordQuality.Diminished => "dim",
            ChordQuality.Augmented => "+",
            ChordQuality.Suspended4 => "sus4",
            ChordQuality.Dominant7 => "7",
            ChordQuality.Major7 => "maj7",
            ChordQuality.Minor7 => "m7",
            ChordQuality.HalfDiminished7 => "m7♭5",
            ChordQuality.Diminished7 => "dim7",
            _ => throw new ArgumentOutOfRangeException(nameof(Quality), Quality, "Unknown chord quality."),
        };
        string symbol = $"{spelling.SpellClass(Root)}{suffix}";
        return bassPitchClass is int bass && ((bass % 12) + 12) % 12 != Root ? $"{symbol}/{spelling.SpellClass(bass)}" : symbol;
    }
}

/// <summary>One chord the finder read: where it sounds, which chord, its bass, how sure the reading is and what else it could be.</summary>
/// <param name="Bass">The lowest note sounding where the chord starts, which gives its inversion.</param>
/// <param name="Confidence">How far the reading is ahead of the next one, from 0 to 99; below <see cref="HarmonyAnalyzer.ClearConfidence"/> it is uncertain.</param>
/// <param name="Alternatives">Up to two other readings that fit nearly as well, best first (FR-A8).</param>
/// <param name="NoteIds">The notes that start in the chord's span, in time order, then by pitch.</param>
public sealed record ChordSpan(TickSpan Span, ChordLabel Chord, Pitch Bass, int Confidence, IReadOnlyList<ChordLabel> Alternatives, IReadOnlyList<string> NoteIds)
{
    /// <summary>0 for root position, 1–3 for the first to third inversion; −1 when the bass is not a chord tone.</summary>
    public int Inversion => Chord.Position(Bass.PitchClass);

    /// <summary>The symbol with the bass when it is not the root: "E/G♯".</summary>
    public string Symbol(MusicalKey spelling) => Chord.Symbol(spelling, Bass.PitchClass);

    public bool Equals(ChordSpan? other) =>
        other is not null && Span == other.Span && Chord == other.Chord && Bass == other.Bass && Confidence == other.Confidence
        && Alternatives.SequenceEqual(other.Alternatives) && NoteIds.SequenceEqual(other.NoteIds, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Span, Chord, Bass, Confidence);
}

/// <summary>The harmony of a selection (S1-A3): its chords, its bass line and the facts about them, every one inferred.</summary>
public sealed record HarmonyAnalysis(IReadOnlyList<ChordSpan> Chords, IReadOnlyList<Note> BassLine, IReadOnlyList<MotifFact> Facts)
{
    /// <summary>A selection without harmony: a single line, or notes the finder cannot read as chords.</summary>
    public static HarmonyAnalysis None { get; } = new([], [], []);
}

/// <summary>
/// Chords and bass where two or more notes sound together (S1-A3, #46): chord labels with inversion and alternatives,
/// the bass line, harmonic rhythm, voicing spread and the notes neighbouring chords share. No AI and no floating
/// point: every reading is a rule applied to the notes, and every fact is marked inferred, or uncertain where the
/// reading is close.
/// </summary>
/// <remarks>
/// <para>
/// The finder cuts the passage into beats (a dotted quarter in 6/8, 9/8 and 12/8) and weighs each pitch class by
/// how long it sounds in the beat. It scores every root and <see cref="ChordQuality"/> against those weights: the
/// chord's tones count for it, other tones against it, a missing tone and above all a missing root against it,
/// and the lowest note as the root for it; sevenths and the rarer triads pay a little, so a plain triad wins a
/// near tie. A beat whose notes all fit the chord before it joins that chord, so an arpeggio or a melody over a
/// held chord is one chord, not one per note.
/// </para>
/// <para>
/// Pure and deterministic: the same notes, meter and span give the same chords, ties broken by quality and then
/// root. Variation operations read the same finder (V12, V15) and the Keep check of harmony compares its chords.
/// </para>
/// </remarks>
public static class HarmonyAnalyzer
{
    /// <summary>A reading at least this confident is inferred; a closer one is uncertain (FR-D5).</summary>
    public const int ClearConfidence = 70;

    /// <summary>Another reading within this many points of the best is an alternative, so every uncertain chord names at least one.</summary>
    public const int AlternativeMargin = ClearConfidence - 50;

    /// <summary>The most bass notes the bass fact names one by one.</summary>
    public const int MaxNamedBassNotes = 16;

    private static readonly ChordQuality[] s_qualities = Enum.GetValues<ChordQuality>();

    /// <summary>The chords of the notes in a span, in time order; empty when fewer than two pitch classes ever sound together.</summary>
    /// <param name="key">The key, whose scale tips a near tie towards the chord in it (A minor rather than A major over A and E in A minor); null for none.</param>
    public static IReadOnlyList<ChordSpan> FindChords(IReadOnlyList<Note> notes, MeterMap meter, TickSpan span, MusicalKey? key = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        Note[] inside = [.. notes
            .Where(note => note.Span.Onset < span.End && note.Span.End > span.Onset)
            .OrderBy(static note => note.Span.Onset)
            .ThenBy(static note => note.Pitch.MidiNumber)
            .ThenBy(static note => note.Id, StringComparer.Ordinal)];
        Motifs.MotifTexture texture = Motifs.MotifTexture.Read(inside);
        if (texture.IsSingleLine || inside.Select(static note => note.Pitch.PitchClass).Distinct().Count() < 2)
        {
            return [];
        }

        // A stretch where only the melody sounds, one note after another, holds no chord of its own (S1-A3).
        var bass = texture.Bass.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        var lineOnly = texture.Melody.Where(note => !bass.Contains(note.Id)).Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        int[] scale = key is { } inKey ? Scale(inKey) : [];
        var chords = new List<ChordSpan>();
        Segment? current = null;
        foreach ((long from, long to) in Windows(meter, span))
        {
            Segment window = Segment.Of(inside, from, to, lineOnly, scale);
            if (window.Total == 0)
            {
                continue;
            }

            Reading? own = window.Distinct >= 2 && window.Together ? Best(window) : null;
            if (current is null)
            {
                current = own is null ? null : window;
                continue;
            }

            ChordLabel chord = Best(current)!.Value.Chord;
            bool fits = window.PitchClasses.All(chord.Contains);
            Segment merged = current.With(window);
            if ((own is null && fits) || own?.Chord == chord || (fits && Best(merged)?.Chord == chord))
            {
                current = merged;
                continue;
            }

            chords.Add(Close(current, inside));
            current = own is null ? null : window;
        }

        if (current is not null)
        {
            chords.Add(Close(current, inside));
        }

        return chords;
    }

    /// <summary>The chords and bass of a selection, with their facts; <see cref="HarmonyAnalysis.None"/> for a single line.</summary>
    /// <param name="key">The key the motif was read in, for spelling; C major when null.</param>
    public static HarmonyAnalysis Analyze(IEnumerable<Note> notes, MeterMap meter, Motifs.MotifSelection selection, MusicalKey? key, Words words = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(selection);
        var ids = selection.NoteIds.ToHashSet(StringComparer.Ordinal);
        Note[] chosen = [.. notes.Where(note => ids.Contains(note.Id))
            .OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Pitch.MidiNumber).ThenBy(static note => note.Id, StringComparer.Ordinal)];
        Motifs.MotifTexture texture = Motifs.MotifTexture.Read(chosen);
        if (chosen.Length == 0 || texture.IsSingleLine)
        {
            return HarmonyAnalysis.None;
        }

        IReadOnlyList<ChordSpan> chords = FindChords(chosen, meter, selection.Span, key);
        if (chords.Count == 0)
        {
            return HarmonyAnalysis.None;
        }

        MusicalKey spelling = key ?? new MusicalKey(0, KeyMode.Major);
        Words w = words;
        var facts = new List<MotifFact>
        {
            ChordsFact(chords, meter, spelling, w),
        };
        if (texture.Bass.Count > 0)
        {
            facts.Add(BassFact(texture.Bass, meter, spelling, w));
        }

        facts.AddRange(
        [
            RhythmFact(chords, meter, selection.Span, spelling, w),
            VoicingFact(chords, chosen, meter, spelling, w),
        ]);
        if (chords.Skip(1).Where((chord, i) => chord.Chord != chords[i].Chord).Any())
        {
            facts.Add(SharedFact(chords, chosen, meter, spelling, w));
        }

        return new HarmonyAnalysis(chords, texture.Bass, facts);
    }

    /// <summary>"Am from bar 1", "E from bar 2, beat 3"; "Am с такта 1", "E с доли 3 такта 2".</summary>
    public static string From(Tick onset, MeterMap meter, Words words)
    {
        ArgumentNullException.ThrowIfNull(meter);
        MetricPosition position = meter.ToPosition(onset);
        return (position.Beat, position.BeatOffset) switch
        {
            (1, 0) => words.T($"from bar {position.Bar}", $"с такта {position.Bar}"),
            (_, 0) => words.T($"from bar {position.Bar}, beat {position.Beat}", $"с доли {position.Beat} такта {position.Bar}"),
            _ => words.T($"from bar {position.Bar}, after beat {position.Beat}", $"после доли {position.Beat} такта {position.Bar}"),
        };
    }

    /// <summary>"first inversion"; "секстаккорд", by the chord's size; null in root position.</summary>
    public static string? InversionName(ChordSpan chord, Words words)
    {
        ArgumentNullException.ThrowIfNull(chord);
        return (chord.Inversion, chord.Chord.IsSeventh) switch
        {
            (0, _) => null,
            (-1, _) => words.T("over a bass outside the chord", "на басу вне аккорда"),
            (1, false) => words.T("first inversion", "секстаккорд"),
            (2, false) => words.T("second inversion", "квартсекстаккорд"),
            (1, true) => words.T("first inversion", "квинтсекстаккорд"),
            (2, true) => words.T("second inversion", "терцквартаккорд"),
            _ => words.T("third inversion", "секундаккорд"),
        };
    }

    private static MotifFact ChordsFact(IReadOnlyList<ChordSpan> chords, MeterMap meter, MusicalKey spelling, Words w)
    {
        string[] named = [.. chords.Select(chord =>
        {
            string text = $"{chord.Symbol(spelling)} {From(chord.Span.Onset, meter, w)}";
            return InversionName(chord, w) is { } inversion ? $"{text} ({inversion})" : text;
        })];
        string list = string.Join(", ", named);
        string text = w.IsRussian
            ? $"{w.Count(chords.Count, "", "", "аккорд", "аккорда", "аккордов")} по нотам: {list}."
            : $"{(chords.Count == 1 ? "1 chord" : $"{chords.Count} chords")} read from the notes: {list}.";
        ChordSpan[] doubtful = [.. chords.Where(static chord => chord.Alternatives.Count > 0)];
        string basis = w.T(
            "Inferred: the pitches sounding in each beat, weighed by how long they sound and with the lowest note counting most, are matched against major, minor, diminished, augmented, suspended and seventh chords; a beat whose notes fit the chord before it joins that chord.",
            "Предположено: высоты, звучащие на каждой доле, с весом по длительности и с наибольшим весом самой низкой ноты, сравниваются с мажорными, минорными, уменьшёнными, увеличенными, задержанными и септаккордами; доля, ноты которой входят в предыдущий аккорд, присоединяется к нему.");
        if (doubtful.Length > 0)
        {
            string others = string.Join("; ", doubtful.Select(chord =>
                $"{chord.Symbol(spelling)} {From(chord.Span.Onset, meter, w)}: {string.Join(w.T(" or ", " или "), chord.Alternatives.Select(alternative => alternative.Symbol(spelling, chord.Bass.PitchClass)))}"));
            basis += w.T($" Other readings that fit nearly as well: {others}.", $" Почти так же подходят: {others}.");
        }

        FactCertainty certainty = chords.Any(static chord => chord.Confidence < ClearConfidence) ? FactCertainty.Uncertain : FactCertainty.Inferred;
        return new MotifFact("harmony.chords", FactLens.Harmony, certainty, text, basis)
        {
            Places = [.. chords.Select(chord => new FactPlace($"{chord.Symbol(spelling)}, {From(chord.Span.Onset, meter, w)}", chord.Span, chord.NoteIds))],
        };
    }

    private static MotifFact BassFact(IReadOnlyList<Note> bass, MeterMap meter, MusicalKey spelling, Words w)
    {
        string[] names = [.. bass.Take(MaxNamedBassNotes).Select(note => spelling.Spell(note.Pitch.MidiNumber))];
        string line = string.Join(" ", names) + (bass.Count > MaxNamedBassNotes ? " …" : "");
        int[] moves = [.. bass.Zip(bass.Skip(1), static (a, b) => Math.Abs(b.Pitch.MidiNumber - a.Pitch.MidiNumber))];
        int steps = moves.Count(static move => move is > 0 and <= 2);
        int leaps = moves.Count(static move => move > 2);
        int repeats = moves.Count(static move => move == 0);
        string text = w.IsRussian
            ? $"Бас: {w.Count(bass.Count, "", "", "нота", "ноты", "нот")} — {line}."
            : $"The bass plays {(bass.Count == 1 ? "1 note" : $"{bass.Count} notes")}: {line}.";
        if (moves.Length > 0)
        {
            string[] kinds = [.. new[]
                {
                    (steps, w.Count(steps, "step", "steps", "шаг", "шага", "шагов")),
                    (leaps, w.Count(leaps, "leap", "leaps", "скачок", "скачка", "скачков")),
                    (repeats, w.Count(repeats, "repeated note", "repeated notes", "повтор", "повтора", "повторов")),
                }
                .Where(static kind => kind.Item1 > 0)
                .Select(static kind => kind.Item2)];
            text += w.IsRussian
                ? $" Из {w.Count(moves.Length, "", "", "хода", "ходов", "ходов")}: {w.Joined(kinds)}."
                : $" Its {(moves.Length == 1 ? "move is" : $"{moves.Length} moves are")} {w.Joined(kinds)}.";
        }

        var places = new List<FactPlace> { FactPlace.Of(w.T("the bass line", "линия баса"), bass) };
        if (Pedal(bass) is { } held)
        {
            int first = meter.BarAt(held[0].Span.Onset).Number;
            int last = meter.BarAt(new Tick(held[^1].Span.End.Value - 1)).Number;
            string pitch = spelling.Spell(held[0].Pitch.MidiNumber);
            string bars = first == last ? w.T($"bar {first}", $"такте {first}") : w.T($"bars {first}–{last}", $"тактах {first}–{last}");
            text += w.T($" It stays on {pitch} in {bars}.", $" В {bars} бас держится на {pitch}.");
            places.Add(FactPlace.Of(w.T($"stays on {pitch}", $"на {pitch}"), held));
        }

        return new MotifFact("harmony.bass", FactLens.Harmony, FactCertainty.Inferred, text, w.T(
            "Inferred: the bass is the lowest note at each onset, among left-hand notes when there are any, leaving out notes over a bass note still held.",
            "Предположено: бас — самая низкая нота каждой атаки, среди нот левой руки, если они есть, без нот над ещё звучащей нотой баса."))
        {
            Places = places,
        };
    }

    /// <summary>The longest run of three or more bass notes on one pitch that lasts at least a bar, or null.</summary>
    private static Note[]? Pedal(IReadOnlyList<Note> bass)
    {
        Note[]? best = null;
        int start = 0;
        for (int i = 1; i <= bass.Count; i++)
        {
            if (i < bass.Count && bass[i].Pitch == bass[start].Pitch)
            {
                continue;
            }

            Note[] run = [.. bass.Skip(start).Take(i - start)];
            long length = run.Length == 0 ? 0 : run[^1].Span.End.Value - run[0].Span.Onset.Value;
            if (run.Length >= 3 && length >= CanonicalTime.TicksPerQuarter * 3 && (best is null || run.Length > best.Length))
            {
                best = run;
            }

            start = i;
        }

        return best;
    }

    private static MotifFact RhythmFact(IReadOnlyList<ChordSpan> chords, MeterMap meter, TickSpan span, MusicalKey spelling, Words w)
    {
        int bars = meter.BarAt(new Tick(span.End.Value - 1)).Number - meter.BarAt(span.Onset).Number + 1;
        ChordSpan[] changed = [.. chords.Skip(1).Where((chord, i) => chord.Chord != chords[i].Chord)];
        int changes = changed.Length;
        int beats = meter.SignatureAt(span.Onset).Numerator;
        string rate = changes == 0
            ? ""
            : changes >= bars * beats ? w.T("on every beat", "на каждой доле")
            : changes * 2 >= bars * 3 ? w.T("more than once a bar", "чаще раза в такт")
            : changes * 4 >= bars * 3 ? w.T("about once a bar", "примерно раз в такт")
            : changes * 8 >= bars * 3 ? w.T("about every 2 bars", "примерно раз в 2 такта")
            : w.T("less often than every 2 bars", "реже, чем раз в 2 такта");
        string barsText = w.Count(bars, "bar", "bars", "такт", "такта", "тактов");
        string text = changes == 0
            ? w.T($"The harmony stays on one chord, {chords[0].Symbol(spelling)}, for {barsText}.", $"Гармония держится на одном аккорде, {chords[0].Symbol(spelling)}, {w.Count(bars, "", "", "такт", "такта", "тактов")}.")
            : w.IsRussian
                ? $"Аккорд меняется {w.Times(changes)} за {w.Count(bars, "", "", "такт", "такта", "тактов")}: {rate}."
                : $"The chord changes {w.Times(changes)} in {barsText}: {rate}.";
        return new MotifFact("harmony.rhythm", FactLens.Harmony, FactCertainty.Inferred, text, w.T(
            "Inferred from the chords read from the notes: each change is where a new chord starts.",
            "Предположено по аккордам, прочитанным из нот: каждая смена — начало нового аккорда."))
        {
            Places = [.. changed.Select(chord => new FactPlace($"{chord.Symbol(spelling)}, {From(chord.Span.Onset, meter, w)}", chord.Span, chord.NoteIds))],
        };
    }

    private static MotifFact VoicingFact(IReadOnlyList<ChordSpan> chords, Note[] notes, MeterMap meter, MusicalKey spelling, Words w)
    {
        (ChordSpan Chord, Note Low, Note High)[] spreads = [.. chords.Select(chord =>
        {
            Note[] sounding = [.. notes.Where(note => note.Span.Onset < chord.Span.End && note.Span.End > chord.Span.Onset)];
            return (chord, sounding.MinBy(static note => note.Pitch.MidiNumber)!, sounding.MaxBy(static note => note.Pitch.MidiNumber)!);
        })];
        static int Size((ChordSpan Chord, Note Low, Note High) spread) => spread.High.Pitch.MidiNumber - spread.Low.Pitch.MidiNumber;
        var widest = spreads.OrderByDescending(Size).First();
        var narrowest = spreads.OrderBy(Size).First();
        int open = spreads.Count(spread => Size(spread) > 12);
        string Named((ChordSpan Chord, Note Low, Note High) spread) => $"{spread.Chord.Symbol(spelling)}, {From(spread.Chord.Span.Onset, meter, w)}";
        string text = spreads.Length == 1 || Size(widest) == Size(narrowest)
            ? w.T(
                $"From bass to top, {(spreads.Length == 1 ? "the chord spans" : "every chord spans")} {Article(MotifAnalyzer.IntervalName(Size(widest), w))}: {(Size(widest) > 12 ? "open" : "close")} voicing.",
                $"От баса до верхнего звука {(spreads.Length == 1 ? "аккорд занимает" : "каждый аккорд занимает")} {MotifAnalyzer.IntervalName(Size(widest), w)}: {(Size(widest) > 12 ? "широкое" : "тесное")} расположение.")
            : w.IsRussian
                ? $"От баса до верхнего звука аккорды занимают от {MotifAnalyzer.IntervalName(Size(narrowest), w)} ({Named(narrowest)}) до {MotifAnalyzer.IntervalName(Size(widest), w)} ({Named(widest)}); в широком расположении, шире октавы, — {open} {w.OutOf(spreads.Length, "", "", "аккорда", "аккордов")}."
                : $"From bass to top, the chords span from {Article(MotifAnalyzer.IntervalName(Size(narrowest), w))} ({Named(narrowest)}) to {Article(MotifAnalyzer.IntervalName(Size(widest), w))} ({Named(widest)}); {open} of {spreads.Length} are open, wider than an octave.";
        return new MotifFact("harmony.voicing", FactLens.Harmony, FactCertainty.Inferred, text, w.T(
            "Measured from the lowest to the highest note sounding in each chord the finder read.",
            "Измерено от самой низкой до самой высокой ноты, звучащей в каждом прочитанном аккорде."))
        {
            Places = spreads.Length == 1 || Size(widest) == Size(narrowest)
                ? [FactPlace.Of(Named(widest), [widest.Low, widest.High])]
                : [FactPlace.Of(Named(widest), [widest.Low, widest.High]), FactPlace.Of(Named(narrowest), [narrowest.Low, narrowest.High])],
        };
    }

    private static MotifFact SharedFact(IReadOnlyList<ChordSpan> chords, Note[] notes, MeterMap meter, MusicalKey spelling, Words w)
    {
        var pairs = new List<(ChordSpan From, ChordSpan To, int[] Shared)>();
        for (int i = 1; i < chords.Count; i++)
        {
            if (chords[i].Chord == chords[i - 1].Chord)
            {
                continue;
            }

            int[] shared = [.. chords[i - 1].Chord.PitchClasses.Where(chords[i].Chord.Contains)];
            pairs.Add((chords[i - 1], chords[i], shared));
        }

        (ChordSpan From, ChordSpan To, int[] Shared)[] sharing = [.. pairs.Where(static pair => pair.Shared.Length > 0)];
        string Pair((ChordSpan From, ChordSpan To, int[] Shared) pair) =>
            $"{pair.From.Symbol(spelling)}–{pair.To.Symbol(spelling)}: {w.Joined([.. pair.Shared.Select(spelling.SpellClass)])}";
        string text = sharing.Length == 0
            ? w.T(
                $"Neighbouring chords share no notes at any of the {(pairs.Count == 1 ? "1 change" : $"{pairs.Count} changes")}.",
                $"Ни при одной из смен аккордов ({pairs.Count}) соседние аккорды не имеют общих звуков.")
            : w.IsRussian
                ? $"Общие звуки у соседних аккордов — в {sharing.Length} {w.OutOf(pairs.Count, "", "", "смены", "смен")}: {string.Join("; ", sharing.Select(Pair))}."
                : $"Neighbouring chords share notes at {sharing.Length} of {(pairs.Count == 1 ? "1 change" : $"{pairs.Count} changes")}: {string.Join("; ", sharing.Select(Pair))}.";
        return new MotifFact("harmony.shared", FactLens.Harmony, FactCertainty.Inferred, text, w.T(
            "Inferred from the chords read from the notes: a shared note is a pitch class in both chords.",
            "Предположено по аккордам, прочитанным из нот: общий звук — звуковысотный класс, входящий в оба аккорда."))
        {
            Places = [.. sharing.Select(pair => new FactPlace(
                $"{Pair(pair)}, {From(pair.To.Span.Onset, meter, w)}",
                TickSpan.FromBounds(pair.From.Span.Onset, pair.To.Span.End),
                [.. notes
                    .Where(note => note.Span.Onset >= pair.From.Span.Onset && note.Span.Onset < pair.To.Span.End && pair.Shared.Contains(note.Pitch.PitchClass))
                    .Select(static note => note.Id)]))],
        };
    }

    /// <summary>"a minor 3rd", "an octave", "2 octaves".</summary>
    private static string Article(string interval) =>
        char.IsDigit(interval[0]) ? interval : interval.StartsWith("octave", StringComparison.Ordinal) ? $"an {interval}" : $"a {interval}";

    /// <summary>The key's scale as pitch classes: major, or natural minor with its leading tone.</summary>
    private static int[] Scale(MusicalKey key)
    {
        int[] steps = key.Mode == KeyMode.Major ? [0, 2, 4, 5, 7, 9, 11] : [0, 2, 3, 5, 7, 8, 10, 11];
        return [.. steps.Select(step => (key.TonicPitchClass + step) % 12)];
    }

    /// <summary>The beats of the span, each cut to it: a dotted quarter in compound meters, else the meter's beat.</summary>
    private static IEnumerable<(long From, long To)> Windows(MeterMap meter, TickSpan span)
    {
        long at = span.Onset.Value;
        while (at < span.End.Value)
        {
            Bar bar = meter.BarAt(new Tick(at));
            TimeSignature signature = bar.Signature;
            bool compound = signature.Denominator == 8 && signature.Numerator % 3 == 0 && signature.Numerator > 3;
            long unit = signature.BeatTicks * (compound ? 3 : 1);
            long fromBar = at - bar.Span.Onset.Value;
            long end = Math.Min(Math.Min(bar.Span.Onset.Value + (((fromBar / unit) + 1) * unit), bar.Span.End.Value), span.End.Value);
            yield return (at, end);
            at = end;
        }
    }

    private static ChordSpan Close(Segment segment, Note[] notes)
    {
        (ChordLabel Chord, long Score)[] ranked = Rank(segment);
        long best = ranked[0].Score;
        long second = ranked.Length > 1 ? ranked[1].Score : best - (100 * segment.Total);
        int confidence = (int)Math.Clamp(50 + ((best - second) / Math.Max(1, segment.Total)), 0, 99);
        ChordLabel[] alternatives = [.. ranked.Skip(1).TakeWhile(entry => best - entry.Score <= AlternativeMargin * segment.Total).Take(2).Select(static entry => entry.Chord)];
        var span = TickSpan.FromBounds(new Tick(segment.From), new Tick(segment.To));
        string[] ids = [.. notes.Where(note => note.Span.Onset >= span.Onset && note.Span.Onset < span.End).Select(static note => note.Id)];
        return new ChordSpan(span, ranked[0].Chord, new Pitch(segment.Bass), confidence, alternatives, ids);
    }

    private readonly record struct Reading(ChordLabel Chord, long Score);

    private static Reading? Best(Segment segment)
    {
        if (segment.Distinct < 2)
        {
            return null;
        }

        (ChordLabel chord, long score) = Rank(segment)[0];
        return new Reading(chord, score);
    }

    /// <summary>Every root and quality, best first; ties go to the earlier quality, then the lower root.</summary>
    private static (ChordLabel Chord, long Score)[] Rank(Segment segment) =>
        [.. s_qualities
            .SelectMany(quality => Enumerable.Range(0, 12).Select(root => new ChordLabel(root, quality)))
            .Select(chord => (Chord: chord, Score: Score(chord, segment)))
            .OrderByDescending(static entry => entry.Score)
            .ThenBy(static entry => entry.Chord.Quality)
            .ThenBy(static entry => entry.Chord.Root)];

    /// <summary>A chord's fit to the weights, in hundredths of a tick.</summary>
    private static long Score(ChordLabel chord, Segment segment)
    {
        long total = segment.Total;
        long inside = 0;
        int missing = 0;
        foreach (int tone in chord.PitchClasses)
        {
            inside += segment.Weights[tone];
            missing += segment.Weights[tone] == 0 ? 1 : 0;
        }

        long outside = total - inside;
        long cost = chord.Quality switch
        {
            ChordQuality.Major or ChordQuality.Minor => 0,
            ChordQuality.Dominant7 => 4,
            ChordQuality.Diminished => 5,
            ChordQuality.Minor7 => 6,
            _ => 8,
        };
        return (100 * inside) - (60 * outside) - (20 * total * missing)
            - (segment.Weights[chord.Root] == 0 ? 25 * total : 0)
            + (segment.Bass % 12 == chord.Root ? 25 * total : 0)
            + (segment.Scale.Length > 0 && chord.PitchClasses.All(segment.Scale.Contains) ? 6 * total : 0)
            - (cost * total);
    }

    /// <summary>The pitch classes sounding in a stretch, weighed by ticks, and the lowest note where it starts.</summary>
    private sealed class Segment
    {
        private Segment(long from, long to, long[] weights, int bass, bool together, int[] scale)
        {
            From = from;
            To = to;
            Weights = weights;
            Bass = bass;
            Together = together;
            Scale = scale;
        }

        public long From { get; }

        public long To { get; }

        public long[] Weights { get; }

        /// <summary>The MIDI number of the lowest note sounding where the stretch starts, or else the lowest in it.</summary>
        public int Bass { get; }

        /// <summary>Whether notes sound together here, or a note other than the melody's sounds: only then is there a chord to read.</summary>
        public bool Together { get; }

        /// <summary>The key's scale, or empty.</summary>
        public int[] Scale { get; }

        public long Total => Weights.Sum();

        public int Distinct => Weights.Count(static weight => weight > 0);

        public IEnumerable<int> PitchClasses => Enumerable.Range(0, 12).Where(pc => Weights[pc] > 0);

        public static Segment Of(Note[] notes, long from, long to, IReadOnlySet<string> lineOnly, int[] scale)
        {
            var weights = new long[12];
            int bass = int.MaxValue;
            int lowestAtStart = int.MaxValue;
            bool together = false;
            var sounding = new List<Note>();
            foreach (Note note in notes)
            {
                long overlap = Math.Min(note.Span.End.Value, to) - Math.Max(note.Span.Onset.Value, from);
                if (overlap <= 0)
                {
                    continue;
                }

                together |= !lineOnly.Contains(note.Id) || sounding.Any(other => other.Span.End > note.Span.Onset && other.Span.Onset < note.Span.End);
                sounding.Add(note);

                weights[note.Pitch.PitchClass] += overlap;
                bass = Math.Min(bass, note.Pitch.MidiNumber);
                if (note.Span.Onset.Value <= from)
                {
                    lowestAtStart = Math.Min(lowestAtStart, note.Pitch.MidiNumber);
                }
            }

            return new Segment(from, to, weights, lowestAtStart != int.MaxValue ? lowestAtStart : bass == int.MaxValue ? 0 : bass, together, scale);
        }

        /// <summary>This stretch and the next one after it, with this one's bass.</summary>
        public Segment With(Segment next) => new(From, next.To, [.. Weights.Zip(next.Weights, static (a, b) => a + b)], Bass, Together || next.Together, Scale);
    }
}