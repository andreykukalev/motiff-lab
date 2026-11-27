using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The harmony facts as sentences in both languages (S1-A3): the motion, the chords, the bass, the harmonic rhythm, the
/// voicing and the notes neighbouring chords share, written from the <see cref="Measures"/>. No reading of the notes
/// belongs here; the meter only names where a chord starts.
/// </summary>
internal static partial class HarmonyAnalyzer
{
    /// <summary>The steps of a progression a fact names, then "…".</summary>
    private const int MaxNamedSteps = 8;

    /// <summary>The chords a brief names by symbol, then "…".</summary>
    private const int MaxBriefChords = 6;

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

    internal static string FunctionName(HarmonicFunction function, Words w) => function switch
    {
        HarmonicFunction.Tonic => w.T("tonic", "тоника"),
        HarmonicFunction.Subdominant => w.T("subdominant", "субдоминанта"),
        HarmonicFunction.Dominant => w.T("dominant", "доминанта"),
        _ => "",
    };

    /// <summary>The facts, in this order: the motion when the key is known, the chords, the bass when there is one, the rhythm, the voicing, and the shared notes when the chord changes.</summary>
    private static List<MotifFact> Facts(Measures measures, MeterMap meter, MusicalKey? key, Words w)
    {
        MusicalKey spelling = key ?? new MusicalKey(0, KeyMode.Major);
        var facts = new List<MotifFact>();
        if (key is { } inKey && measures.Cadence is { } cadence)
        {
            facts.Add(MotionFact(measures.Steps, cadence, meter, inKey, w));
        }

        facts.Add(ChordsFact(measures.Chords, meter, spelling, w));
        if (measures.Bass.Count > 0)
        {
            facts.Add(BassFact(measures.Bass, measures.Moves, measures.Pedal, spelling, w));
        }

        facts.AddRange(
        [
            RhythmFact(measures, meter, spelling, w),
            VoicingFact(measures.Spreads, meter, spelling, w),
        ]);
        if (measures.Changes.Count > 0)
        {
            facts.Add(SharedFact(measures.Changes, meter, spelling, w));
        }

        return facts;
    }

    /// <summary>
    /// "Harmonic motion: Am → E → Am, i → V → i (tonic → dominant → tonic). It comes to rest: the dominant resolves to
    /// the tonic, an authentic cadence." The places are the chords, consecutive equal chords joined.
    /// </summary>
    private static MotifFact MotionFact(IReadOnlyList<Step> steps, Cadence cadence, MeterMap meter, MusicalKey key, Words w)
    {
        string more = steps.Count > MaxNamedSteps ? " → …" : "";
        Step[] named = [.. steps.Take(MaxNamedSteps)];
        string symbols = string.Join(" → ", named.Select(step => step.Chord.Symbol(key))) + more;
        string numerals = string.Join(" → ", named.Select(step => Numeral(step.Chord, key))) + more;
        bool anyFunction = named.Any(step => FunctionOf(step.Chord, key) != HarmonicFunction.None);
        string functions = string.Join(" → ", named.Select(step =>
            FunctionOf(step.Chord, key) is var function and not HarmonicFunction.None ? FunctionName(function, w) : Numeral(step.Chord, key))) + more;

        Step last = steps[^1];
        string lastNumeral = Numeral(last.Chord, key);
        string gives = cadence switch
        {
            Cadence.Static => w.T(
                "The harmony stands still: the line and the rhythm carry the motion.",
                "Гармония стоит на месте: движение несут линия и ритм."),
            Cadence.Authentic => w.T(
                "It comes to rest: the dominant pulls to the tonic and resolves into it, an authentic cadence.",
                "Отрывок приходит к устою: доминанта тяготеет к тонике и разрешается в неё — автентический каданс."),
            Cadence.Plagal => w.T(
                "It closes softly: the subdominant goes to the tonic without the dominant's pull, a plagal cadence.",
                "Отрывок закрывается мягко: субдоминанта переходит в тонику без тяготения доминанты — плагальный каданс."),
            Cadence.Half => w.T(
                "It stops on the dominant and stays open, a half cadence: the music asks for an answer.",
                "Отрывок останавливается на доминанте и остаётся открытым — половинный каданс: музыка ждёт ответа."),
            Cadence.Deceptive => w.T(
                $"The dominant promises the tonic, but {lastNumeral} comes instead, a deceptive cadence: the music goes on.",
                $"Доминанта обещает тонику, но приходит {lastNumeral} — прерванный каданс: музыка продолжается."),
            Cadence.Home => w.T(
                "It returns to the tonic, the key's point of rest.",
                "Гармония возвращается к тонике, устою тональности."),
            _ => w.T(
                $"It ends away from the tonic, on {lastNumeral}: the motion leads on to what follows.",
                $"Гармония заканчивается вне тоники, на {lastNumeral}: движение ведёт дальше."),
        };

        string progression = cadence == Cadence.Static
            ? w.T($"one chord throughout, {symbols} ({numerals})", $"один аккорд на всём протяжении, {symbols} ({numerals})")
            : anyFunction ? $"{symbols}, {numerals} ({functions})" : $"{symbols}, {numerals}";
        string text = w.T($"Harmonic motion: {progression}. {gives}", $"Гармоническое движение: {progression}. {gives}");
        string brief = w.T($"Harmonic motion: {numerals}. ", $"Гармоническое движение: {numerals}. ") + gives;
        string basis = w.T(
            $"Inferred from the chords read from the notes, as steps of {key.NameIn(w)}: the first step is the tonic, the fourth and second the subdominant, the fifth and the leading note's chord the dominant; a chord with a note outside the key has no function. The last two chords name the cadence.",
            $"Предположено по аккордам, прочитанным из нот, как ступеням тональности {key.NameIn(w)}: первая ступень — тоника, четвёртая и вторая — субдоминанта, пятая и аккорд вводного тона — доминанта; у аккорда с нотой вне тональности функции нет. Последние два аккорда называют каданс.");
        return new MotifFact("harmony.motion", FactLens.Harmony, steps.Any(static step => step.Uncertain) ? FactCertainty.Uncertain : FactCertainty.Inferred, text, basis)
        {
            Brief = cadence == Cadence.Static ? null : brief,
            Places = [.. steps.Select(step =>
            {
                string function = FunctionName(FunctionOf(step.Chord, key), w);
                string label = $"{step.Chord.Symbol(key)} ({Numeral(step.Chord, key)}{(function.Length > 0 ? $", {function}" : "")}), {From(step.Span.Onset, meter, w)}";
                return new FactPlace(label, step.Span, step.NoteIds);
            })],
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
        string symbols = string.Join(", ", chords.Take(MaxBriefChords).Select(chord => chord.Symbol(spelling))) + (chords.Count > MaxBriefChords ? " …" : "");
        return new MotifFact("harmony.chords", FactLens.Harmony, certainty, text, basis)
        {
            Brief = w.IsRussian
                ? $"{w.Count(chords.Count, "", "", "аккорд", "аккорда", "аккордов")}: {symbols}."
                : $"{(chords.Count == 1 ? "1 chord" : $"{chords.Count} chords")}: {symbols}.",
            Places = [.. chords.Select(chord => new FactPlace($"{chord.Symbol(spelling)}, {From(chord.Span.Onset, meter, w)}", chord.Span, chord.NoteIds))],
        };
    }

    private static MotifFact BassFact(IReadOnlyList<Note> bass, BassMoves moves, HeldBass? pedal, MusicalKey spelling, Words w)
    {
        string[] names = [.. bass.Take(MaxNamedBassNotes).Select(note => spelling.Spell(note.Pitch.MidiNumber))];
        string line = string.Join(" ", names) + (bass.Count > MaxNamedBassNotes ? " …" : "");
        string text = w.IsRussian
            ? $"Бас: {w.Count(bass.Count, "", "", "нота", "ноты", "нот")} — {line}."
            : $"The bass plays {(bass.Count == 1 ? "1 note" : $"{bass.Count} notes")}: {line}.";
        string brief = w.IsRussian
            ? $"Бас: {w.Count(bass.Count, "", "", "нота", "ноты", "нот")}"
            : $"The bass plays {(bass.Count == 1 ? "1 note" : $"{bass.Count} notes")}";
        if (moves.Count > 0)
        {
            string[] kinds = [.. new[]
                {
                    (moves.Steps, w.Count(moves.Steps, "step", "steps", "шаг", "шага", "шагов")),
                    (moves.Leaps, w.Count(moves.Leaps, "leap", "leaps", "скачок", "скачка", "скачков")),
                    (moves.Repeats, w.Count(moves.Repeats, "repeated note", "repeated notes", "повтор", "повтора", "повторов")),
                }
                .Where(static kind => kind.Item1 > 0)
                .Select(static kind => kind.Item2)];
            text += w.IsRussian
                ? $" Из {w.Count(moves.Count, "", "", "хода", "ходов", "ходов")}: {w.Joined(kinds)}."
                : $" Its {(moves.Count == 1 ? "move is" : $"{moves.Count} moves are")} {w.Joined(kinds)}.";
            brief += $", {w.Joined(kinds)}";
        }

        brief += ".";

        var places = new List<FactPlace> { FactPlace.Of(w.T("the bass line", "линия баса"), bass) };
        if (pedal is { } held)
        {
            string pitch = spelling.Spell(held.Notes[0].Pitch.MidiNumber);
            string inBars = w.InBars(held.FirstBar, held.LastBar);
            text += w.T($" It stays on {pitch} {inBars}.", $" {Words.Capitalize(inBars)} бас держится на {pitch}.");
            brief += w.T($" It stays on {pitch} {inBars}.", $" {Words.Capitalize(inBars)} бас держится на {pitch}.");
            places.Add(FactPlace.Of(w.T($"stays on {pitch}", $"на {pitch}"), held.Notes));
        }

        return new MotifFact("harmony.bass", FactLens.Harmony, FactCertainty.Inferred, text, w.T(
            "Inferred: the bass is the lowest note at each onset, among left-hand notes when there are any, leaving out notes over a bass note still held.",
            "Предположено: бас — самая низкая нота каждой атаки, среди нот левой руки, если они есть, без нот над ещё звучащей нотой баса."))
        {
            Places = places,
            Brief = brief,
        };
    }

    private static MotifFact RhythmFact(Measures measures, MeterMap meter, MusicalKey spelling, Words w)
    {
        IReadOnlyList<ChordSpan> chords = measures.Chords;
        IReadOnlyList<ChordSpan> changed = measures.Changed;
        int bars = measures.Bars;
        int changes = changed.Count;
        int beats = measures.Beats;
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

    private static MotifFact VoicingFact(IReadOnlyList<Spread> spreads, MeterMap meter, MusicalKey spelling, Words w)
    {
        var widest = spreads.OrderByDescending(static spread => spread.Size).First();
        var narrowest = spreads.OrderBy(static spread => spread.Size).First();
        int open = spreads.Count(static spread => spread.Size > 12);
        string Named(Spread spread) => $"{spread.Chord.Symbol(spelling)}, {From(spread.Chord.Span.Onset, meter, w)}";
        string text = spreads.Count == 1 || widest.Size == narrowest.Size
            ? w.T(
                $"From bass to top, {(spreads.Count == 1 ? "the chord spans" : "every chord spans")} {w.IntervalBy(widest.Size)}: {(widest.Size > 12 ? "open" : "close")} voicing.",
                $"От баса до верхнего звука {(spreads.Count == 1 ? "аккорд занимает" : "каждый аккорд занимает")} {w.IntervalName(widest.Size)}: {(widest.Size > 12 ? "широкое" : "тесное")} расположение.")
            : w.IsRussian
                ? $"От баса до верхнего звука аккорды занимают от {w.IntervalName(narrowest.Size)} ({Named(narrowest)}) до {w.IntervalName(widest.Size)} ({Named(widest)}); в широком расположении, шире октавы, — {open} {w.OutOf(spreads.Count, "", "", "аккорда", "аккордов")}."
                : $"From bass to top, the chords span from {w.IntervalBy(narrowest.Size)} ({Named(narrowest)}) to {w.IntervalBy(widest.Size)} ({Named(widest)}); {open} of {spreads.Count} are open, wider than an octave.";
        return new MotifFact("harmony.voicing", FactLens.Harmony, FactCertainty.Inferred, text, w.T(
            "Measured from the lowest to the highest note sounding in each chord the finder read.",
            "Измерено от самой низкой до самой высокой ноты, звучащей в каждом прочитанном аккорде."))
        {
            Brief = spreads.Count == 1 || widest.Size == narrowest.Size
                ? null
                : w.IsRussian
                    ? $"В широком расположении, шире октавы, — {open} {w.OutOf(spreads.Count, "", "", "аккорда", "аккордов")}."
                    : $"{open} of {spreads.Count} chords are open, wider than an octave.",
            Places = spreads.Count == 1 || widest.Size == narrowest.Size
                ? [FactPlace.Of(Named(widest), [widest.Low, widest.High])]
                : [FactPlace.Of(Named(widest), [widest.Low, widest.High]), FactPlace.Of(Named(narrowest), [narrowest.Low, narrowest.High])],
        };
    }

    private static MotifFact SharedFact(IReadOnlyList<Change> pairs, MeterMap meter, MusicalKey spelling, Words w)
    {
        Change[] sharing = [.. pairs.Where(static pair => pair.Shared.Length > 0)];
        string Pair(Change pair) =>
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
            Brief = sharing.Length == 0
                ? null
                : w.IsRussian
                    ? $"Общие звуки у соседних аккордов — в {sharing.Length} {w.OutOf(pairs.Count, "", "", "смены", "смен")}."
                    : $"Neighbouring chords share notes at {sharing.Length} of {(pairs.Count == 1 ? "1 change" : $"{pairs.Count} changes")}.",
            Places = [.. sharing.Select(pair => new FactPlace(
                $"{Pair(pair)}, {From(pair.To.Span.Onset, meter, w)}",
                TickSpan.FromBounds(pair.From.Span.Onset, pair.To.Span.End),
                pair.NoteIds))],
        };
    }
}