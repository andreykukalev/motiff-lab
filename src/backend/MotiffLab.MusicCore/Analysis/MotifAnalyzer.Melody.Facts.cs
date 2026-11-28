using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The melody facts as sentences in both languages (S1-A2), written from the measures of <c>MotifAnalyzer.Melody.cs</c>.
/// Nothing is measured here; the notes are read only to name the places a measure points at.
/// </summary>
internal static partial class MotifAnalyzer
{
    /// <summary>A list of intervals names at most this many, then says how many more there are.</summary>
    private const int MaxNamedIntervals = 12;

    /// <summary>How the line was read, the start of each melody fact's basis.</summary>
    private static string LineBasis(LineMeasure line, Words w) => line.TopNotes
        ? w.T(
            "Notes sound together, so the melody is taken as the top line: the highest note at each onset, among right-hand notes when there are any, leaving out notes under a melody note still held.",
            "Ноты звучат вместе, поэтому мелодией считается верхний голос: самая высокая нота каждой атаки, среди нот правой руки, если они есть, без нот под ещё звучащей нотой мелодии.")
        : w.T("Read from the notes, one at a time.", "Прочитано по нотам, одна за другой.");

    private static MotifFact LineFact(LineMeasure measure, MusicalKey key, Words w)
    {
        Note[] line = measure.Line;
        string keyName = key.NameIn(w);
        string lineName = measure.TopNotes ? w.T("top line", "верхнем голосе") : w.T("melody", "мелодии");
        string count = w.IsRussian
            ? $"{w.Count(line.Length, "", "", "нота", "ноты", "нот")} в {lineName}"
            : $"{Words.EnglishCount(line.Length, "note", "notes")} in the {lineName}";
        return new MotifFact(
            "melody.line",
            FactLens.Melody,
            measure.Certainty,
            $"{count}: {string.Join(" ", line.Select(note => key.Spell(note.Pitch.MidiNumber)))}.",
            w.IsRussian ? $"{LineBasis(measure, w)} Ноты названы для тональности {keyName}." : $"{LineBasis(measure, w)} Spelled for {keyName}.")
        {
            Places = [FactPlace.Of(measure.TopNotes ? w.T("the top line", "верхний голос") : w.T("the melody", "мелодия"), line)],
            Brief = $"{count}.",
        };
    }

    private static MotifFact IntervalsFact(LineMeasure line, StepMeasure steps, MusicalKey key, Words w)
    {
        string list = string.Join(" ", line.Intervals.Select(step => step > 0 ? $"+{step}" : step < 0 ? $"−{-step}" : "0"));
        string summary = IntervalsSummary(steps, w);
        return new MotifFact(
            "melody.intervals",
            FactLens.Melody,
            line.Certainty,
            $"{w.T("Intervals in semitones", "Интервалы в полутонах")}: {list}. {summary}",
            $"{LineBasis(line, w)} {w.T("Intervals are named by their size in semitones.", "Интервалы названы по размеру в полутонах.")}")
        {
            Places = LeapPlaces(line, steps, key, w),
            Brief = summary,
        };
    }

    /// <summary>The intervals counted, without the list: "7 steps, 10 leaps. Largest leap: a major 6th down."</summary>
    private static string IntervalsSummary(StepMeasure steps, Words w)
    {
        var summary = new List<string>();
        if (steps.Steps > 0)
        {
            summary.Add(w.Count(steps.Steps, "step", "steps", "шаг", "шага", "шагов"));
        }

        if (steps.Leaps > 0)
        {
            summary.Add(w.Count(steps.Leaps, "leap", "leaps", "скачок", "скачка", "скачков"));
        }

        if (steps.Repeats > 0)
        {
            summary.Add(w.Count(steps.Repeats, "repeated note", "repeated notes", "повтор звука", "повтора звука", "повторов звука"));
        }

        string text = $"{Words.Capitalize(string.Join(", ", summary))}.";
        if (steps.Leaps > 0)
        {
            int largest = steps.Largest;
            text += w.IsRussian
                ? $" Самый большой скачок: {w.IntervalName(largest)} {(largest > 0 ? "вверх" : "вниз")}."
                : $" Largest leap: {w.IntervalBy(largest)} {(largest > 0 ? "up" : "down")}.";
        }

        return text;
    }

    /// <summary>Each place where the line makes its largest leap, the two notes of the leap; none when the line has no leap.</summary>
    private static FactPlace[] LeapPlaces(LineMeasure measure, StepMeasure steps, MusicalKey key, Words w)
    {
        if (steps.Leaps == 0)
        {
            return [];
        }

        Note[] line = measure.Line;
        return [.. Enumerable.Range(0, measure.Intervals.Length)
            .Where(i => measure.Intervals[i] == steps.Largest)
            .Select(i => FactPlace.Of(
                w.IsRussian
                    ? $"самый большой скачок, {key.Spell(line[i].Pitch.MidiNumber)} – {key.Spell(line[i + 1].Pitch.MidiNumber)}"
                    : $"largest leap, {key.Spell(line[i].Pitch.MidiNumber)} to {key.Spell(line[i + 1].Pitch.MidiNumber)}",
                [line[i], line[i + 1]]))];
    }

    private static MotifFact ContourFact(LineMeasure line, ContourMeasure contour, MusicalKey key, Words w) =>
        new("melody.contour", FactLens.Melody, line.Certainty, ContourText(contour, w), LineBasis(line, w))
        {
            Places = ContourPlaces(line.Line, line.Contour, contour, key, w),
        };

    private static string ContourText(ContourMeasure contour, Words w)
    {
        IReadOnlyList<ContourStep> runs = contour.Runs;
        string shape = runs.Count switch
        {
            0 => w.T("Stays on one pitch", "Остаётся на одной высоте"),
            1 => runs[0] == ContourStep.Up ? w.T("Rises", "Поднимается") : w.T("Falls", "Опускается"),
            <= 3 => w.T("Moves ", "Движется ") + string.Join(w.T(", then ", ", затем "), runs.Select(step => step == ContourStep.Up ? w.T("up", "вверх") : w.T("down", "вниз"))),
            _ => w.IsRussian ? $"Идёт зигзагом, меняя направление {w.Times(runs.Count - 1)}" : $"Zigzags, turning {runs.Count - 1} times",
        };

        if (runs.Count == 0)
        {
            return $"{shape}.";
        }

        int net = contour.Net;
        string end = net == 0
            ? w.T("ends where it starts", "заканчивается там же, где начинается")
            : w.IsRussian
                ? $"заканчивается на {w.IntervalBy(net)} {(net > 0 ? "выше" : "ниже")}, чем начинается"
                : $"ends {w.IntervalBy(net)} {(net > 0 ? "higher" : "lower")} than it starts";
        return $"{shape}; {end}.";
    }

    /// <summary>
    /// Where the contour starts, turns and ends: a turn is the note the line leaves in a new
    /// direction, a peak when it turns down and a low point when it turns up. None for one pitch.
    /// </summary>
    private static FactPlace[] ContourPlaces(Note[] line, ContourStep[] steps, ContourMeasure contour, MusicalKey key, Words w)
    {
        if (contour.Runs.Count == 0)
        {
            return [];
        }

        return
        [
            FactPlace.Of($"{w.T("start", "начало")}, {key.Spell(line[0].Pitch.MidiNumber)}", [line[0]]),
            .. contour.Turns.Select(i => FactPlace.Of(
                $"{(steps[i] == ContourStep.Down ? w.T("peak", "вершина") : w.T("low point", "нижняя точка"))}, {key.Spell(line[i].Pitch.MidiNumber)}",
                [line[i]])),
            FactPlace.Of($"{w.T("end", "конец")}, {key.Spell(line[^1].Pitch.MidiNumber)}", [line[^1]]),
        ];
    }

    private static MotifFact RangeFact(RegisterMeasure range, Note[] notes, MusicalKey key, Words w)
    {
        int span = range.High - range.Low;
        string text = span == 0
            ? w.T($"Range: one pitch, {key.Spell(range.Low)}.", $"Диапазон: одна высота, {key.Spell(range.Low)}.")
            : w.IsRussian
                ? $"Диапазон: от {key.Spell(range.Low)} до {key.Spell(range.High)}, {w.IntervalName(span)} ({w.Count(span, "", "", "полутон", "полутона", "полутонов")})."
                : $"Range: {key.Spell(range.Low)} to {key.Spell(range.High)}, {w.IntervalBy(span)} ({Words.EnglishCount(span, "semitone", "semitones")}).";
        return new MotifFact("melody.range", FactLens.Melody, FactCertainty.Detected, text, w.T("Measured over every note of the motif.", "Измерено по всем нотам мотива."))
        {
            Places = ExtremePlaces(notes, range, key, w),
        };
    }

    /// <summary>The lowest and the highest notes, each pitch as one place with every note on it; one place when every note has one pitch.</summary>
    private static FactPlace[] ExtremePlaces(Note[] notes, RegisterMeasure range, MusicalKey key, Words w) => range.Low == range.High
        ? [FactPlace.Of($"{w.T("one pitch", "одна высота")}, {key.Spell(range.Low)}", notes)]
        :
        [
            FactPlace.Of($"{w.T("lowest", "самая низкая")}, {key.Spell(range.Low)}", notes.Where(note => note.Pitch.MidiNumber == range.Low)),
            FactPlace.Of($"{w.T("highest", "самая высокая")}, {key.Spell(range.High)}", notes.Where(note => note.Pitch.MidiNumber == range.High)),
        ];

    /// <param name="degrees">The scale degree of each note of the line, as <see cref="Degree"/> names it.</param>
    private static MotifFact DegreesFact(LineMeasure line, string[] degrees, KeyReading key, Words w)
    {
        string keyName = key.Key.NameIn(w);
        string basis = w.IsRussian
            ? $"Отсчитано от тоники ({keyName}); ♯ и ♭ отмечают ноты вне {(key.Key.Mode == KeyMode.Minor ? "натурального минора" : "мажорной гаммы")}. {(key.Certainty == FactCertainty.Detected ? "Тональность указана в файле." : "Тональность прочитана по нотам, поэтому ступени тоже.")}"
            : $"Counted from the tonic of {keyName}; ♯ and ♭ mark notes outside its {(key.Key.Mode == KeyMode.Minor ? "natural minor" : "major")} scale. {(key.Certainty == FactCertainty.Detected ? "The key is stated by the file." : "The key is a reading of the notes, so the degrees are too.")}";

        // The notes outside the scale, which the degrees mark with ♯ or ♭.
        int[] outside = [.. Enumerable.Range(0, degrees.Length).Where(i => degrees[i].Length > 1)];
        string brief = outside.Length == 0
            ? w.T($"Every note of the line is in the scale of {keyName}.", $"Все ноты линии — в звукоряде тональности {keyName}.")
            : w.IsRussian
                ? $"{Words.Capitalize(w.Count(outside.Length, "", "", "нота", "ноты", "нот"))} вне звукоряда тональности {keyName}."
                : $"{Words.EnglishCount(outside.Length, "note", "notes")} outside the scale of {keyName}.";
        return new MotifFact(
            "melody.degrees",
            FactLens.Melody,
            Weaker(line.Certainty, key.Certainty),
            w.T($"Scale degrees in {keyName}: {string.Join(" ", degrees)}.", $"Ступени в тональности {keyName}: {string.Join(" ", degrees)}."),
            basis)
        {
            Places = [.. outside.Select(i => FactPlace.Of($"{key.Key.Spell(line.Line[i].Pitch.MidiNumber)}, {w.T("degree", "ступень")} {degrees[i]}", [line.Line[i]]))],
            Brief = brief,
        };
    }

    private static MotifFact IntervalNamesFact(LineMeasure line, CommonSize? common, Words w)
    {
        int[] intervals = line.Intervals;
        string[] names = [.. intervals.Select(step => step == 0
            ? w.T("unison", "прима")
            : $"{w.IntervalName(step)} {(step > 0 ? w.T("up", "вверх") : w.T("down", "вниз"))}")];
        string text = $"{w.T("Intervals by name", "Интервалы по названию")}: {w.Listed(names, MaxNamedIntervals, ", ", " ")}.";
        string brief = w.IsRussian
            ? $"{w.Count(intervals.Length, "", "", "интервал", "интервала", "интервалов")}, ни один размер не преобладает."
            : $"{Words.EnglishCount(intervals.Length, "interval", "intervals")}; no size clearly leads.";

        // The most common size, up or down, when one size clearly leads; its place is the notes of every interval of that size.
        FactPlace[] places = [];
        if (common is not null)
        {
            string name = common.Size == 0 ? w.T("repeated note", "повтор звука") : w.IntervalName(common.Size);
            brief = w.IsRussian
                ? $"Чаще всего: {name} ({common.Count} из {intervals.Length})."
                : $"Most common: {(common.Size == 0 ? "the repeated note" : Words.WithArticle(name))} ({common.Count} of {intervals.Length}).";
            text += " " + brief;
            places = [FactPlace.Of($"{w.T("most common", "чаще всего")}, {name}", common.Notes)];
        }

        return new MotifFact("melody.interval-names", FactLens.Melody, line.Certainty, text, $"{LineBasis(line, w)} {w.T("Names go by size in semitones, not by spelling.", "Названия даны по размеру в полутонах, а не по написанию.")}")
        {
            Places = places,
            Brief = brief,
        };
    }

    private static MotifFact RepeatedNotesFact(LineMeasure measure, RepeatMeasure repeats, MeterMap meter, MusicalKey key, Words w)
    {
        Note[] line = measure.Line;
        var places = new List<FactPlace>();
        foreach ((int start, int end) in repeats.Runs)
        {
            string run = w.IsRussian
                ? $"{key.Spell(line[start].Pitch.MidiNumber)} {w.Times(end - start)} ({Place(meter.ToPosition(line[start].Span.Onset), w)})"
                : $"{key.Spell(line[start].Pitch.MidiNumber)} {(end - start == 2 ? "twice" : $"{end - start} times")} from {Place(meter.ToPosition(line[start].Span.Onset), w)}";
            places.Add(FactPlace.Of(run, line[start..end]));
        }

        int runs = places.Count;
        string text = runs == 0
            ? w.T("No note is repeated straight away.", "Ни одна нота не повторяется сразу.")
            : $"{w.T("Repeated straight away", "Сразу повторяются")}: {string.Join("; ", places.Select(place => place.Label))}.";
        string? brief = runs == 0
            ? null
            : w.IsRussian
                ? $"Сразу {Words.RussianForm(runs, "повторяется", "повторяются", "повторяются")} {w.Count(runs, "", "", "нота", "ноты", "нот")}."
                : $"{Words.EnglishCount(runs, "note", "notes")} {(runs == 1 ? "is" : "are")} repeated straight away.";

        if (repeats.MostOften is { } most)
        {
            text += w.IsRussian
                ? $" Чаще всего звучит {key.Spell(most.Key)}: {most.Count()} {w.OutOf(line.Length, "", "", "ноты", "нот")}."
                : $" {key.Spell(most.Key)} sounds most often, {most.Count()} of {Words.EnglishCount(line.Length, "note", "notes")}.";
            places.Add(FactPlace.Of($"{w.T("most often", "чаще всего")}, {key.Spell(most.Key)}", most));
        }

        return new MotifFact("melody.repeated-notes", FactLens.Melody, measure.Certainty, text, LineBasis(measure, w))
        {
            Places = places,
            Brief = brief,
        };
    }

    /// <summary>
    /// Melody notes that sound over lower notes, struck with them or over notes still held, and those
    /// whose pitch class a lower note shares: such a note is both a melody note and a note of the chord
    /// under it, so the ear can take it either way.
    /// </summary>
    private static MotifFact SoundingWithFact(OverMeasure measure, MusicalKey key, Words w)
    {
        // The melody notes the text names, each with the lower notes that share its pitch class.
        FactPlace[] places = [.. measure.Shared.Select(entry => FactPlace.Of(
            $"{key.Spell(entry.Note.Pitch.MidiNumber)} ({w.T("with", "с")} {string.Join(", ", entry.Doubled.Select(other => key.Spell(other.Pitch.MidiNumber)))})",
            [entry.Note, .. entry.Doubled]))];
        int over = measure.Struck + measure.Held;
        string text = w.T(
            "Every melody note sounds on its own; the other notes fall between melody notes.",
            "Каждая нота мелодии звучит одна; остальные ноты попадают между нотами мелодии.");
        string? brief = null;
        if (over > 0)
        {
            (text, brief) = OverText(measure, [.. places.Select(place => place.Label)], w);
        }

        return new MotifFact(
            "melody.over-chords",
            FactLens.Melody,
            FactCertainty.Inferred,
            text,
            w.T(
                "The melody is the inferred top line. A lower note sounds with a melody note when it starts with it or is still held when it starts. A shared pitch class means the melody note is also a note of the chord under it.",
                "Мелодия — предположенный верхний голос. Нижняя нота звучит с нотой мелодии, если начинается вместе с ней или ещё звучит, когда та начинается. Общий звуковысотный класс значит, что нота мелодии входит и в аккорд под ней."))
        {
            Places = places,
            Brief = brief,
        };
    }

    /// <summary>The text and brief of the over-chords fact when some melody note sounds over lower notes.</summary>
    /// <param name="shared">The label of each melody note whose pitch class a lower note shares.</param>
    private static (string Text, string Brief) OverText(OverMeasure measure, string[] shared, Words w)
    {
        int melody = measure.Melody;
        int over = measure.Struck + measure.Held;
        string[] how = [.. new[] { (measure.Struck, w.T("struck together with them", "взяты вместе с ними")), (measure.Held, w.T("over notes still held", "над ещё звучащими нотами")) }
            .Where(part => part.Item1 > 0)
            .Select(part => w.IsRussian ? $"{part.Item2}: {part.Item1}" : $"{part.Item1} {part.Item2}")];
        string list = w.Listed(shared, MaxNamedIntervals, ", ", " ");
        if (w.IsRussian)
        {
            string brief = $"{over} {w.OutOf(melody, "", "", "ноты мелодии", "нот мелодии")} {Words.RussianForm(over, "звучит", "звучат", "звучат")} над нижними нотами.";
            string text = $"{over} {w.OutOf(melody, "", "", "ноты мелодии", "нот мелодии")} {Words.RussianForm(over, "звучит", "звучат", "звучат")} над нижними нотами: {string.Join(", ", how)}."
                + (shared.Length == 0
                    ? " Ни одна из них не совпадает по звуковысотному классу с нижней нотой."
                    : $" {shared.Length} {Words.RussianForm(shared.Length, "совпадает", "совпадают", "совпадают")} по звуковысотному классу с нижней нотой, поэтому {(shared.Length == 1 ? "она входит" : "они входят")} и в аккорд под ней: {list}.");
            return (text, brief);
        }

        string englishBrief = $"{over} of {Words.EnglishCount(melody, "melody note", "melody notes")} sound over lower notes.";
        string englishText = $"{over} of {Words.EnglishCount(melody, "melody note", "melody notes")} sound over lower notes: {string.Join(", ", how)}."
            + (shared.Length == 0
                ? " None shares its pitch class with a lower note."
                : $" {Words.EnglishCount(shared.Length, "shares its pitch class", "share their pitch class")} with a lower note, so {(shared.Length == 1 ? "it is" : "they are")} also part of the chord below: {list}.");
        return (englishText, englishBrief);
    }

    private static MotifFact PhraseFact(PhraseMeasure measure, Note[] notes, MeterMap meter, Words w)
    {
        PhraseBoundary[] inside = measure.Inside;
        string text = inside.Length == 0
            ? w.T("No phrase boundary falls inside the motif.", "Внутри мотива нет границы фразы.")
            : w.IsRussian
                ? $"{w.Count(inside.Length, "", "", "граница фразы", "границы фраз", "границ фраз")} внутри мотива: {string.Join("; ", inside.Select(boundary => $"{Place(meter.ToPosition(boundary.At), w)}, {EvidenceText(boundary, w)}"))}."
                : $"{Words.EnglishCount(inside.Length, "phrase boundary", "phrase boundaries")} inside the motif: {string.Join("; ", inside.Select(boundary => $"at {Place(meter.ToPosition(boundary.At), w)}, {EvidenceText(boundary, w)}"))}.";
        if (measure.AtStart is { } atStart)
        {
            text += w.T($" It starts where a phrase starts ({EvidenceText(atStart, w)}).", $" Мотив начинается там же, где фраза ({EvidenceText(atStart, w)}).");
        }

        string basis = w.T(
            "Boundaries come from phrase slurs, rests of a beat or more and repeat signs in the source, from a passage that comes back note for note or starts again at once, and from boundaries the player set; where none of these marks one, none is invented.",
            "Границы берутся из фразировочных лиг, пауз длиной в долю и больше и знаков повторения в источнике, из отрывка, который возвращается нота в ноту или сразу начинается снова, и из границ, поставленных исполнителем; там, где ничего из этого границу не отмечает, она не придумывается.");
        MotifFact fact = measure.Inferred
            ? new MotifFact("melody.phrases", FactLens.Melody, FactCertainty.Inferred, text, $"{basis} {w.T("A boundary from a rest or a return alone is a reading of the notes, not a mark in the score.", "Граница только по паузе или возврату — это прочтение нот, а не знак в нотах.")}")
            : new MotifFact("melody.phrases", FactLens.Melody, FactCertainty.Detected, text, basis);
        FactPlace[] places = [.. inside.Select(boundary => BoundaryPlace($"{w.T("phrase boundary", "граница фразы")}, {Place(meter.ToPosition(boundary.At), w)}", boundary.At, notes))];
        string? brief = inside.Length == 0
            ? null
            : w.IsRussian
                ? $"{w.Count(inside.Length, "", "", "граница фразы", "границы фраз", "границ фраз")} внутри мотива."
                : $"{Words.EnglishCount(inside.Length, "phrase boundary", "phrase boundaries")} inside the motif.";
        return fact with { Places = measure.AtStart is null ? places : [.. places, BoundaryPlace(w.T("phrase start", "начало фразы"), notes[0].Span.Onset, notes)], Brief = brief };
    }

    /// <summary>
    /// A boundary as the notes of the motif that start a phrase there, or, when none of them starts
    /// exactly there, the stretch from the boundary to the motif's next onset.
    /// </summary>
    private static FactPlace BoundaryPlace(string label, Tick at, Note[] notes)
    {
        Note[] starting = [.. notes.Where(note => note.Span.Onset == at)];
        return starting.Length > 0
            ? FactPlace.Of(label, starting)
            : new FactPlace(label, TickSpan.FromBounds(at, notes.Where(note => note.Span.Onset > at).Min(note => note.Span.Onset)), []);
    }

    /// <summary>
    /// What supports a phrase boundary, after "from" or "по": "a slur", "a rest of a beat or more"; Russian in the dative
    /// ("лиге", "паузе в долю и больше", "возврату отрывка нота в ноту"), with no clause a list would leave open. Also the
    /// structure facts' basis says it so.
    /// </summary>
    internal static string EvidenceName(PhraseEvidence mark, Words w) => mark switch
    {
        PhraseEvidence.Slur => w.T("a slur", "лиге"),
        PhraseEvidence.Rest => w.T("a rest of a beat or more", "паузе в долю и больше"),
        PhraseEvidence.RepeatSign => w.T("a repeat sign", "знаку повторения"),
        PhraseEvidence.Repetition => w.T("a passage that comes back note for note", "возврату отрывка нота в ноту"),
        PhraseEvidence.Restatement => w.T("a passage that starts again at once", "немедленному повтору отрывка"),
        _ => mark.ToString(),
    };

    /// <summary>"from a slur and a rest of a beat or more", "set by the player", or "from a slur, confirmed by the player".</summary>
    private static string EvidenceText(PhraseBoundary boundary, Words w)
    {
        string[] evidence = [.. boundary.Evidence.Select(mark => EvidenceName(mark, w))];
        string source = evidence.Length switch
        {
            0 => "",
            1 => $"{w.T("from", "по")} {evidence[0]}",
            _ => $"{w.T("from", "по")} {w.Joined(evidence)}",
        };

        return boundary.Attribution.Provenance switch
        {
            Provenance.UserEdited or Provenance.UserConfirmed when source.Length == 0 => w.T("set by the player", "поставлена исполнителем"),
            Provenance.UserEdited or Provenance.UserConfirmed => $"{source}, {w.T("confirmed by the player", "подтверждена исполнителем")}",
            _ => source,
        };
    }
}