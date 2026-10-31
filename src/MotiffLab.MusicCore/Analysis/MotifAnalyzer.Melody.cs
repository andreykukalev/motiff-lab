using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// Melody facts beyond line, intervals in semitones, contour, range and degrees (S1-A2): interval
/// names, repeated notes, melody notes that sound with lower notes, and the phrase boundaries the
/// source marks or the player set.
/// </summary>
public static partial class MotifAnalyzer
{
    /// <summary>A list of intervals names at most this many, then says how many more there are.</summary>
    private const int MaxNamedIntervals = 12;

    private static MotifFact IntervalNamesFact(Note[] line, int[] intervals, FactCertainty certainty, string basis, Words w)
    {
        string[] names = [.. intervals.Select(step => step == 0
            ? w.T("unison", "прима")
            : $"{IntervalName(step, w)} {(step > 0 ? w.T("up", "вверх") : w.T("down", "вниз"))}")];
        string list = names.Length <= MaxNamedIntervals
            ? string.Join(", ", names)
            : $"{string.Join(", ", names.Take(MaxNamedIntervals))} {w.T("and", "и ещё")} {names.Length - MaxNamedIntervals}{w.T(" more", "")}";
        string text = $"{w.T("Intervals by name", "Интервалы по названию")}: {list}.";

        // The most common size, up or down, when one size clearly leads; its place is the notes of every interval of that size.
        IGrouping<int, int>[] sizes = [.. intervals.GroupBy(Math.Abs).OrderByDescending(group => group.Count()).ThenBy(group => group.Key)];
        FactPlace[] places = [];
        if (sizes[0].Count() > 1 && (sizes.Length == 1 || sizes[0].Count() > sizes[1].Count()))
        {
            string name = sizes[0].Key == 0 ? w.T("repeated note", "повтор звука") : IntervalName(sizes[0].Key, w);
            text += w.IsRussian
                ? $" Чаще всего: {name} ({sizes[0].Count()} из {intervals.Length})."
                : $" Most common: {(sizes[0].Key == 0 ? "the repeated note" : WithArticle(name))} ({sizes[0].Count()} of {intervals.Length}).";
            Note[] notes = [.. Enumerable.Range(0, intervals.Length)
                .Where(i => Math.Abs(intervals[i]) == sizes[0].Key)
                .SelectMany(i => new[] { line[i], line[i + 1] })
                .Distinct()];
            places = [FactPlace.Of($"{w.T("most common", "чаще всего")}, {name}", notes)];
        }

        return new MotifFact("melody.interval-names", FactLens.Melody, certainty, text, $"{basis} {w.T("Names go by size in semitones, not by spelling.", "Названия даны по размеру в полутонах, а не по написанию.")}")
        {
            Places = places,
        };
    }

    private static MotifFact RepeatedNotesFact(Note[] line, MeterMap meter, MusicalKey key, FactCertainty certainty, string basis, Words w)
    {
        var runs = new List<string>();
        var places = new List<FactPlace>();
        for (int start = 0; start < line.Length;)
        {
            int end = start + 1;
            while (end < line.Length && line[end].Pitch.MidiNumber == line[start].Pitch.MidiNumber)
            {
                end++;
            }

            if (end - start > 1)
            {
                runs.Add(w.IsRussian
                    ? $"{key.Spell(line[start].Pitch.MidiNumber)} {w.Times(end - start)} ({Place(meter.ToPosition(line[start].Span.Onset), w)})"
                    : $"{key.Spell(line[start].Pitch.MidiNumber)} {(end - start == 2 ? "twice" : $"{end - start} times")} from {Place(meter.ToPosition(line[start].Span.Onset), w)}");
                places.Add(FactPlace.Of(runs[^1], line[start..end]));
            }

            start = end;
        }

        string text = runs.Count == 0
            ? w.T("No note is repeated straight away.", "Ни одна нота не повторяется сразу.")
            : $"{w.T("Repeated straight away", "Сразу повторяются")}: {string.Join("; ", runs)}.";

        IGrouping<int, Note>[] pitches = [.. line.GroupBy(note => note.Pitch.MidiNumber).OrderByDescending(group => group.Count()).ThenBy(group => group.Key)];
        if (pitches[0].Count() > 1 && (pitches.Length == 1 || pitches[0].Count() > pitches[1].Count()))
        {
            text += w.IsRussian
                ? $" Чаще всего звучит {key.Spell(pitches[0].Key)}: {pitches[0].Count()} {w.OutOf(line.Length, "", "", "ноты", "нот")}."
                : $" {key.Spell(pitches[0].Key)} sounds most often, {pitches[0].Count()} of {Count(line.Length, "note", "notes")}.";
            places.Add(FactPlace.Of($"{w.T("most often", "чаще всего")}, {key.Spell(pitches[0].Key)}", pitches[0]));
        }

        return new MotifFact("melody.repeated-notes", FactLens.Melody, certainty, text, basis)
        {
            Places = places,
        };
    }

    /// <summary>
    /// Melody notes that sound over lower notes, struck with them or over notes still held, and those
    /// whose pitch class a lower note shares: such a note is both a melody note and a note of the chord
    /// under it, so the ear can take it either way.
    /// </summary>
    private static MotifFact SoundingWithFact(MotifTexture texture, MusicalKey key, Words w)
    {
        int struck = 0;
        int held = 0;
        var shared = new List<string>();
        var places = new List<FactPlace>();
        foreach (Note note in texture.Melody)
        {
            Note[] lower = [.. texture.SoundingWith(note).Where(other => other.Pitch < note.Pitch)];
            if (lower.Length == 0)
            {
                continue;
            }

            if (lower.Any(other => other.Span.Onset == note.Span.Onset))
            {
                struck++;
            }
            else
            {
                held++;
            }

            Note[] doubled = [.. lower.Where(other => other.Pitch.PitchClass == note.Pitch.PitchClass).OrderByDescending(other => other.Pitch.MidiNumber)];
            if (doubled.Length > 0)
            {
                shared.Add($"{key.Spell(note.Pitch.MidiNumber)} ({w.T("with", "с")} {string.Join(", ", doubled.Select(other => key.Spell(other.Pitch.MidiNumber)))})");
                places.Add(FactPlace.Of(shared[^1], [note, .. doubled]));
            }
        }

        int over = struck + held;
        string text;
        if (over == 0)
        {
            text = w.T(
                "Every melody note sounds on its own; the other notes fall between melody notes.",
                "Каждая нота мелодии звучит одна; остальные ноты попадают между нотами мелодии.");
        }
        else
        {
            string[] how = [.. new[] { (struck, w.T("struck together with them", "взяты вместе с ними")), (held, w.T("over notes still held", "над ещё звучащими нотами")) }
                .Where(part => part.Item1 > 0)
                .Select(part => w.IsRussian ? $"{part.Item2}: {part.Item1}" : $"{part.Item1} {part.Item2}")];
            string list = shared.Count <= MaxNamedIntervals
                ? string.Join(", ", shared)
                : $"{string.Join(", ", shared.Take(MaxNamedIntervals))} {w.T("and", "и ещё")} {shared.Count - MaxNamedIntervals}{w.T(" more", "")}";
            if (w.IsRussian)
            {
                text = $"{over} {w.OutOf(texture.Melody.Count, "", "", "ноты мелодии", "нот мелодии")} {Words.RussianForm(over, "звучит", "звучат", "звучат")} над нижними нотами: {string.Join(", ", how)}.";
                text += shared.Count == 0
                    ? " Ни одна из них не совпадает по звуковысотному классу с нижней нотой."
                    : $" {shared.Count} {Words.RussianForm(shared.Count, "совпадает", "совпадают", "совпадают")} по звуковысотному классу с нижней нотой, поэтому {(shared.Count == 1 ? "она входит" : "они входят")} и в аккорд под ней: {list}.";
            }
            else
            {
                text = $"{over} of {Count(texture.Melody.Count, "melody note", "melody notes")} sound over lower notes: {string.Join(", ", how)}.";
                text += shared.Count == 0
                    ? " None shares its pitch class with a lower note."
                    : $" {Count(shared.Count, "shares its pitch class", "share their pitch class")} with a lower note, so {(shared.Count == 1 ? "it is" : "they are")} also part of the chord below: {list}.";
            }
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
            // The melody notes the text names, each with the lower notes that share its pitch class.
            Places = places,
        };
    }

    private static MotifFact PhraseFact(Note[] notes, IEnumerable<PhraseBoundary> phrases, MeterMap meter, Words w)
    {
        Tick first = notes[0].Span.Onset;
        Tick last = notes.Max(note => note.Span.Onset);
        PhraseBoundary[] all = [.. phrases.OrderBy(boundary => boundary.At)];
        PhraseBoundary[] inside = [.. all.Where(boundary => boundary.At > first && boundary.At <= last)];
        PhraseBoundary? atStart = all.FirstOrDefault(boundary => boundary.At == first);

        string text = inside.Length == 0
            ? w.T("No phrase boundary falls inside the motif.", "Внутри мотива нет границы фразы.")
            : w.IsRussian
                ? $"{w.Count(inside.Length, "", "", "граница фразы", "границы фраз", "границ фраз")} внутри мотива: {string.Join("; ", inside.Select(boundary => $"{Place(meter.ToPosition(boundary.At), w)}, {EvidenceText(boundary, w)}"))}."
                : $"{Count(inside.Length, "phrase boundary", "phrase boundaries")} inside the motif: {string.Join("; ", inside.Select(boundary => $"at {Place(meter.ToPosition(boundary.At), w)}, {EvidenceText(boundary, w)}"))}.";
        if (atStart is not null)
        {
            text += w.T($" It starts where a phrase starts ({EvidenceText(atStart, w)}).", $" Мотив начинается там же, где фраза ({EvidenceText(atStart, w)}).");
        }

        PhraseBoundary[] cited = atStart is null ? inside : [atStart, .. inside];
        bool inferred = cited.Any(boundary => boundary.Attribution.Provenance is Provenance.Inferred or Provenance.Detected);
        string basis = w.T(
            "Boundaries come from phrase slurs, rests of a beat or more and repeat signs in the source, from a passage that comes back note for note, and from boundaries the player set; where none of these marks one, none is invented.",
            "Границы берутся из фразировочных лиг, пауз длиной в долю и больше и знаков повторения в источнике, из отрывка, который возвращается нота в ноту, и из границ, поставленных исполнителем; там, где ничего из этого границу не отмечает, она не придумывается.");
        MotifFact fact = inferred
            ? new MotifFact("melody.phrases", FactLens.Melody, FactCertainty.Inferred, text, $"{basis} {w.T("A boundary from a rest or a return alone is a reading of the notes, not a mark in the score.", "Граница только по паузе или возврату — это прочтение нот, а не знак в нотах.")}")
            : new MotifFact("melody.phrases", FactLens.Melody, FactCertainty.Detected, text, basis);
        FactPlace[] places = [.. inside.Select(boundary => BoundaryPlace($"{w.T("phrase boundary", "граница фразы")}, {Place(meter.ToPosition(boundary.At), w)}", boundary.At, notes))];
        return fact with { Places = atStart is null ? places : [.. places, BoundaryPlace(w.T("phrase start", "начало фразы"), first, notes)] };
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

    /// <summary>"from a slur and a rest of a beat or more", "set by the player", or "from a slur, confirmed by the player".</summary>
    private static string EvidenceText(PhraseBoundary boundary, Words w)
    {
        // Russian names the evidence after "по" ("по лиге и паузе"), in the dative.
        string[] evidence = [.. boundary.Evidence.Select(mark => mark switch
        {
            PhraseEvidence.Slur => w.T("a slur", "лиге"),
            PhraseEvidence.Rest => w.T("a rest of a beat or more", "паузе в долю и больше"),
            PhraseEvidence.RepeatSign => w.T("a repeat sign", "знаку повторения"),
            PhraseEvidence.Repetition => w.T("a passage that comes back note for note", "отрывку, который возвращается нота в ноту"),
            _ => mark.ToString(),
        })];
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