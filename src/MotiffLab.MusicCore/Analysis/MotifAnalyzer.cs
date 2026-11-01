using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>The direction from one melody note to the next.</summary>
public enum ContourStep
{
    Up,
    Down,
    Same,
}

/// <summary>The key a motif was read in, how, and the key that came closest.</summary>
/// <param name="RunnerUp">The next best key when the key was inferred from the notes; null when the file states it.</param>
public sealed record KeyReading(MusicalKey Key, FactCertainty Certainty, MusicalKey? RunnerUp);

/// <summary>What the engine measured in a motif, as values and as plain facts.</summary>
/// <param name="Line">The melody: the notes in order, or the top line (<see cref="MotifTexture.Melody"/>) when notes sound together.</param>
/// <param name="LineIsTopNotes">True when notes sound together, so <paramref name="Line"/> is inferred as the top line.</param>
/// <param name="Intervals">Semitones from each melody note to the next; positive is up.</param>
/// <param name="Contour">The direction of each interval.</param>
/// <param name="Key">Null when the motif has no notes.</param>
/// <param name="Facts">The facts in display order: melody, rhythm, then key.</param>
public sealed record MotifAnalysis(
    IReadOnlyList<Note> Line,
    bool LineIsTopNotes,
    IReadOnlyList<int> Intervals,
    IReadOnlyList<ContourStep> Contour,
    KeyReading? Key,
    IReadOnlyList<MotifFact> Facts);

/// <summary>
/// Plain facts about a selected motif: melody line, intervals, contour, range, scale degrees
/// (S1-A2), durations, onset grid, silences, beat grouping, syncopation, accents where marked and
/// repeated rhythms (S1-A1), and key. No AI: every fact is measured
/// from the notes or read from the file, and labelled detected, inferred or uncertain.
/// </summary>
/// <remarks>
/// Analysis is pure: the same notes, meter, selection and key signature give the same facts.
/// Interval names go by size in semitones, not by spelling, so three semitones is a minor 3rd
/// even where the score writes an augmented 2nd.
/// </remarks>
public static partial class MotifAnalyzer
{
    /// <summary>A key read from fewer notes or pitch classes than this is uncertain.</summary>
    public const int MinNotesForKey = 6;

    public const int MinPitchClassesForKey = 5;

    /// <summary>A key inferred from the notes is uncertain when the next key scores within this margin.</summary>
    public const double ClearKeyMargin = 0.05;

    private static readonly string[] s_intervalNames =
        ["unison", "minor 2nd", "major 2nd", "minor 3rd", "major 3rd", "perfect 4th", "tritone", "perfect 5th", "minor 6th", "major 6th", "minor 7th", "major 7th"];

    // The same intervals in Russian, as named (nominative) and after "на" (accusative): "на малую терцию выше".
    private static readonly string[] s_russianIntervalNames =
        ["чистая прима", "малая секунда", "большая секунда", "малая терция", "большая терция", "чистая кварта", "тритон", "чистая квинта", "малая секста", "большая секста", "малая септима", "большая септима"];

    private static readonly string[] s_russianIntervalAccusative =
        ["чистую приму", "малую секунду", "большую секунду", "малую терцию", "большую терцию", "чистую кварту", "тритон", "чистую квинту", "малую сексту", "большую сексту", "малую септиму", "большую септиму"];

    private static readonly string[] s_majorDegrees = ["1", "♯1", "2", "♯2", "3", "4", "♯4", "5", "♯5", "6", "♭7", "7"];

    private static readonly string[] s_minorDegrees = ["1", "♭2", "2", "3", "♯3", "4", "♯4", "5", "6", "♯6", "7", "♯7"];

    /// <summary>A note value by its length in ticks: English singular and plural, Russian for one, few and many.</summary>
    private readonly record struct NoteValue(long Ticks, string One, string Many, string RussianOne, string RussianFew, string RussianMany)
    {
        public string Name(Words words) => words.T(One, RussianOne);

        public string Counted(int count, Words words) => words.Count(count, One, Many, RussianOne, RussianFew, RussianMany);

        public string Plural(int count, Words words) => words.Noun(count, One, Many, RussianOne, RussianFew, RussianMany);
    }

    private static readonly NoteValue[] s_values =
    [
        new(1920, "whole note", "whole notes", "целая", "целые", "целых"),
        new(1440, "dotted half", "dotted halves", "половинная с точкой", "половинные с точкой", "половинных с точкой"),
        new(960, "half", "halves", "половинная", "половинные", "половинных"),
        new(720, "dotted quarter", "dotted quarters", "четверть с точкой", "четверти с точкой", "четвертей с точкой"),
        new(480, "quarter", "quarters", "четверть", "четверти", "четвертей"),
        new(360, "dotted eighth", "dotted eighths", "восьмая с точкой", "восьмые с точкой", "восьмых с точкой"),
        new(320, "quarter-note triplet", "quarter-note triplets", "триольная четверть", "триольные четверти", "триольных четвертей"),
        new(240, "eighth", "eighths", "восьмая", "восьмые", "восьмых"),
        new(180, "dotted sixteenth", "dotted sixteenths", "шестнадцатая с точкой", "шестнадцатые с точкой", "шестнадцатых с точкой"),
        new(160, "eighth-note triplet", "eighth-note triplets", "триольная восьмая", "триольные восьмые", "триольных восьмых"),
        new(120, "sixteenth", "sixteenths", "шестнадцатая", "шестнадцатые", "шестнадцатых"),
        new(80, "sixteenth-note triplet", "sixteenth-note triplets", "триольная шестнадцатая", "триольные шестнадцатые", "триольных шестнадцатых"),
        new(60, "thirty-second", "thirty-seconds", "тридцать вторая", "тридцать вторые", "тридцать вторых"),
    ];

    // Onset grids from coarse to fine, measured from the bar line; the Russian names the grid ("сетка восьмых") and goes after "с" ("с триольными восьмыми").
    private static readonly (long Ticks, string Name, bool Triplet, string RussianGrid, string RussianWith)[] s_grids =
    [
        (480, "quarter-note", false, "четвертей", "четвертями"),
        (240, "eighth-note", false, "восьмых", "восьмыми"),
        (160, "eighth-note triplet", true, "триольных восьмых", "триольными восьмыми"),
        (120, "sixteenth-note", false, "шестнадцатых", "шестнадцатыми"),
        (80, "sixteenth-note triplet", true, "триольных шестнадцатых", "триольными шестнадцатыми"),
        (60, "thirty-second-note", false, "тридцать вторых", "тридцать вторыми"),
    ];

    /// <param name="notes">The notes of the piece; the selection's notes are picked by id.</param>
    /// <param name="signature">The key signature in force where the selection starts, or null when the file has none.</param>
    /// <param name="accents">The accents the file writes, or null when the format cannot mark accents (MIDI), which leaves out the accent fact.</param>
    /// <param name="phrases">
    /// The phrase boundaries of the notes the motif was picked from, found in the source and set by
    /// the player (<see cref="PhraseBoundaryFinder"/>), or null to leave out the phrase fact.
    /// </param>
    /// <param name="words">The language of the facts' text, bases and place labels; English by default.</param>
    public static MotifAnalysis Analyze(
        IEnumerable<Note> notes,
        MeterMap meter,
        MotifSelection selection,
        KeySignature? signature,
        IEnumerable<AccentMark>? accents = null,
        IEnumerable<PhraseBoundary>? phrases = null,
        Words words = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(selection);

        var byId = new Dictionary<string, Note>(StringComparer.Ordinal);
        foreach (Note note in notes)
        {
            byId.TryAdd(note.Id, note);
        }

        Note[] chosen = [.. selection.NoteIds.Where(byId.ContainsKey).Select(id => byId[id])
            .OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal)];
        if (chosen.Length == 0)
        {
            return new MotifAnalysis([], false, [], [], null, []);
        }

        Words w = words;
        IGrouping<Tick, Note>[] onsets = [.. chosen.GroupBy(note => note.Span.Onset)];
        MotifTexture texture = MotifTexture.Read(chosen);
        Note[] line = [.. texture.Melody];
        bool topNotes = !texture.IsSingleLine;
        int[] intervals = [.. line.Zip(line.Skip(1), (from, to) => to.Pitch.MidiNumber - from.Pitch.MidiNumber)];
        ContourStep[] contour = [.. intervals.Select(step => step > 0 ? ContourStep.Up : step < 0 ? ContourStep.Down : ContourStep.Same)];
        KeyReading key = ReadKey(chosen, signature);
        string keyName = key.Key.NameIn(w);

        FactCertainty lineCertainty = topNotes ? FactCertainty.Inferred : FactCertainty.Detected;
        string lineBasis = topNotes
            ? w.T(
                "Notes sound together, so the melody is taken as the top line: the highest note at each onset, among right-hand notes when there are any, leaving out notes under a melody note still held.",
                "Ноты звучат вместе, поэтому мелодией считается верхний голос: самая высокая нота каждой атаки, среди нот правой руки, если они есть, без нот под ещё звучащей нотой мелодии.")
            : w.T("Read from the notes, one at a time.", "Прочитано по нотам, одна за другой.");
        string lineName = topNotes ? w.T("top line", "верхнем голосе") : w.T("melody", "мелодии");
        string degreesBasis = w.IsRussian
            ? $"Отсчитано от тоники ({keyName}); ♯ и ♭ отмечают ноты вне {(key.Key.Mode == KeyMode.Minor ? "натурального минора" : "мажорной гаммы")}. {(key.Certainty == FactCertainty.Detected ? "Тональность указана в файле." : "Тональность прочитана по нотам, поэтому ступени тоже.")}"
            : $"Counted from the tonic of {keyName}; ♯ and ♭ mark notes outside its {(key.Key.Mode == KeyMode.Minor ? "natural minor" : "major")} scale. {(key.Certainty == FactCertainty.Detected ? "The key is stated by the file." : "The key is a reading of the notes, so the degrees are too.")}";

        var facts = new List<MotifFact>
        {
            new(
                "melody.line",
                FactLens.Melody,
                lineCertainty,
                w.IsRussian
                    ? $"{w.Count(line.Length, "", "", "нота", "ноты", "нот")} в {lineName}: {string.Join(" ", line.Select(note => key.Key.Spell(note.Pitch.MidiNumber)))}."
                    : $"{Count(line.Length, "note", "notes")} in the {lineName}: {string.Join(" ", line.Select(note => key.Key.Spell(note.Pitch.MidiNumber)))}.",
                w.IsRussian ? $"{lineBasis} Ноты названы для тональности {keyName}." : $"{lineBasis} Spelled for {keyName}.")
            {
                Places = [FactPlace.Of(topNotes ? w.T("the top line", "верхний голос") : w.T("the melody", "мелодия"), line)],
            },
        };

        if (intervals.Length > 0)
        {
            facts.Add(new MotifFact("melody.intervals", FactLens.Melody, lineCertainty, IntervalsText(intervals, w), $"{lineBasis} {w.T("Intervals are named by their size in semitones.", "Интервалы названы по размеру в полутонах.")}")
            {
                Places = LeapPlaces(line, intervals, key.Key, w),
            });
            facts.Add(new MotifFact("melody.contour", FactLens.Melody, lineCertainty, ContourText(contour, intervals.Sum(), w), lineBasis)
            {
                Places = ContourPlaces(line, contour, key.Key, w),
            });
        }

        facts.Add(RangeFact(chosen, key.Key, w));
        facts.Add(new MotifFact(
            "melody.degrees",
            FactLens.Melody,
            Weaker(lineCertainty, key.Certainty),
            w.IsRussian
                ? $"Ступени в тональности {keyName}: {string.Join(" ", line.Select(note => Degree(note.Pitch, key.Key)))}."
                : $"Scale degrees in {keyName}: {string.Join(" ", line.Select(note => Degree(note.Pitch, key.Key)))}.",
            degreesBasis)
        {
            // The notes outside the scale, which the degrees mark with ♯ or ♭.
            Places = [.. line
                .Select(note => (Note: note, Degree: Degree(note.Pitch, key.Key)))
                .Where(entry => entry.Degree.Length > 1)
                .Select(entry => FactPlace.Of($"{key.Key.Spell(entry.Note.Pitch.MidiNumber)}, {w.T("degree", "ступень")} {entry.Degree}", [entry.Note]))],
        });

        if (intervals.Length > 0)
        {
            facts.Add(IntervalNamesFact(line, intervals, lineCertainty, lineBasis, w));
        }

        facts.Add(RepeatedNotesFact(line, meter, key.Key, lineCertainty, lineBasis, w));
        if (topNotes)
        {
            facts.Add(SoundingWithFact(texture, key.Key, w));
        }

        if (phrases is not null)
        {
            facts.Add(PhraseFact(chosen, phrases, meter, w));
        }

        bool offGrid = OffGridCount(chosen, meter) > 0;
        facts.Add(DurationsFact(chosen, offGrid, w));
        facts.Add(GridFact(chosen, onsets.Select(group => group.Key).ToArray(), meter, w));
        facts.Add(SilenceFact(chosen, selection.Span, w));
        Hit[] hits = Hits(onsets);
        facts.Add(GroupingFact(hits, meter, selection.Span, offGrid, w));
        facts.Add(SyncopationFact(chosen, hits, meter, offGrid, w));
        if (accents is not null)
        {
            facts.Add(AccentFact(chosen, accents, meter, key.Key, w));
        }

        facts.Add(PatternFact(chosen, hits, meter, selection.Span, offGrid, w));
        facts.Add(KeyFact(key, signature, chosen.Length, PitchClassCount(chosen), w));

        return new MotifAnalysis(line, topNotes, intervals, contour, key, WithHand(facts, selection, w));
    }

    /// <summary>"minor 3rd", "octave", "octave and a major 2nd", "2 octaves".</summary>
    public static string IntervalName(int semitones) => IntervalName(semitones, Words.English);

    /// <summary>
    /// The interval's name in a language: "minor 3rd", "octave and a major 2nd"; "малая терция",
    /// "октава и большая секунда", "2 октавы".
    /// </summary>
    public static string IntervalName(int semitones, Words words)
    {
        int size = Math.Abs(semitones);
        int octaves = size / 12;
        int rest = size % 12;
        if (words.IsRussian)
        {
            return RussianInterval(octaves, rest, s_russianIntervalNames, "октава");
        }

        if (octaves == 0)
        {
            return s_intervalNames[rest];
        }

        string octaveText = octaves == 1 ? "octave" : $"{octaves} octaves";
        return rest == 0 ? octaveText : $"{octaveText} and {WithArticle(s_intervalNames[rest])}";
    }

    /// <summary>
    /// The interval as English says it inside a sentence, with its article ("a minor 3rd"), or as
    /// Russian says it after "на" ("на малую терцию выше"), without the "на".
    /// </summary>
    public static string IntervalBy(int semitones, Words words)
    {
        if (!words.IsRussian)
        {
            return WithArticle(IntervalName(semitones));
        }

        int size = Math.Abs(semitones);
        return RussianInterval(size / 12, size % 12, s_russianIntervalAccusative, "октаву");
    }

    private static string RussianInterval(int octaves, int rest, string[] names, string octave)
    {
        if (octaves == 0)
        {
            return names[rest];
        }

        string octaveText = octaves == 1 ? octave : $"{octaves} {Words.RussianForm(octaves, "октава", "октавы", "октав")}";
        return rest == 0 ? octaveText : $"{octaveText} и {names[rest]}";
    }

    /// <summary>The scale degree of a pitch in a key: "5", or "♯7" for a note outside the scale.</summary>
    public static string Degree(Pitch pitch, MusicalKey key)
    {
        int offset = ((pitch.PitchClass - key.TonicPitchClass) % 12 + 12) % 12;
        return key.Mode == KeyMode.Major ? s_majorDegrees[offset] : s_minorDegrees[offset];
    }

    /// <summary>Detected facts become inferred when the hand that picked the motif's notes is inferred.</summary>
    private static List<MotifFact> WithHand(List<MotifFact> facts, MotifSelection selection, Words w)
    {
        if (!selection.HandInferred)
        {
            return facts;
        }

        string handBasis = w.T(
            "The hand is inferred, so which notes the motif holds is inferred too. ",
            "Рука предположена, поэтому и состав нот мотива предположен. ");
        return [.. facts.Select(fact => fact.Certainty == FactCertainty.Detected
            ? fact with { Certainty = FactCertainty.Inferred, Basis = handBasis + fact.Basis }
            : fact)];
    }

    /// <summary>The key the notes are in: stated by the file, or read from the pitches with the signature's help when it gives only the accidentals.</summary>
    internal static KeyReading ReadKey(Note[] notes, KeySignature? signature)
    {
        var weights = new double[12];
        foreach (Note note in notes)
        {
            weights[note.Pitch.PitchClass] += note.Span.Duration;
        }

        bool thin = notes.Length < MinNotesForKey || PitchClassCount(notes) < MinPitchClassesForKey;
        if (signature is { Mode: { } mode })
        {
            return new KeyReading(new MusicalKey(signature.Fifths, mode), FactCertainty.Detected, null);
        }

        if (signature is not null)
        {
            var major = new MusicalKey(signature.Fifths, KeyMode.Major);
            double majorScore = KeyFinder.Score(weights, major);
            double minorScore = KeyFinder.Score(weights, major.Relative);
            (MusicalKey best, MusicalKey other) = minorScore > majorScore ? (major.Relative, major) : (major, major.Relative);
            bool close = Math.Abs(majorScore - minorScore) < ClearKeyMargin;
            return new KeyReading(best, thin || close ? FactCertainty.Uncertain : FactCertainty.Inferred, other);
        }

        IReadOnlyList<(MusicalKey Key, double Score)> ranked = KeyFinder.Rank(weights);
        bool unclear = ranked[0].Score - ranked[1].Score < ClearKeyMargin;
        return new KeyReading(ranked[0].Key, thin || unclear ? FactCertainty.Uncertain : FactCertainty.Inferred, ranked[1].Key);
    }

    private static MotifFact KeyFact(KeyReading key, KeySignature? signature, int noteCount, int pitchClasses, Words w)
    {
        string name = key.Key.NameIn(w);
        string? runnerUp = key.RunnerUp?.NameIn(w);
        string thin = noteCount < MinNotesForKey || pitchClasses < MinPitchClassesForKey
            ? w.IsRussian
                ? $" При {w.Count(noteCount, "", "", "ноте", "нотах", "нотах")} и {w.Count(pitchClasses, "", "", "звуковысотном классе", "звуковысотных классах", "звуковысотных классах")} это слабая догадка."
                : $" With {Count(noteCount, "note", "notes")} and {Count(pitchClasses, "pitch class", "pitch classes")}, that is a weak guess."
            : key.Certainty == FactCertainty.Uncertain ? w.T($" {runnerUp} fits almost as well.", $" {Words.Capitalize(runnerUp ?? "")} подходит почти так же.") : "";

        string basis = w.IsRussian
            ? signature switch
            {
                { Mode: not null } => $"В файле указаны ключевые знаки ({Accidentals(signature.Fifths, w)}) и лад.",
                not null => $"Ключевые знаки: {Accidentals(signature.Fifths, w)}, но лад не указан; ноты подходят к тональности {name} лучше, чем к тональности {runnerUp}.{thin}",
                null => $"В файле нет ключевых знаков; ноты лучше всего подходят к тональности {name}, затем к тональности {runnerUp}.{thin}",
            }
            : signature switch
            {
                { Mode: not null } => $"The file states the key signature ({Accidentals(signature.Fifths, w)}) and the mode.",
                not null => $"The key signature has {Accidentals(signature.Fifths, w)} but no mode; the notes fit {name} better than {runnerUp}.{thin}",
                null => $"The file has no key signature; the notes fit {name} best, then {runnerUp}.{thin}",
            };

        return new MotifFact("key", FactLens.Key, key.Certainty, w.T($"Key: {name}.", $"Тональность: {name}."), basis);
    }

    private static string IntervalsText(int[] intervals, Words w)
    {
        int steps = intervals.Count(step => Math.Abs(step) is 1 or 2);
        int leaps = intervals.Count(step => Math.Abs(step) > 2);
        int repeats = intervals.Count(step => step == 0);
        string list = string.Join(" ", intervals.Select(step => step > 0 ? $"+{step}" : step < 0 ? $"−{-step}" : "0"));

        var summary = new List<string>();
        if (steps > 0)
        {
            summary.Add(w.Count(steps, "step", "steps", "шаг", "шага", "шагов"));
        }

        if (leaps > 0)
        {
            summary.Add(w.Count(leaps, "leap", "leaps", "скачок", "скачка", "скачков"));
        }

        if (repeats > 0)
        {
            summary.Add(w.Count(repeats, "repeated note", "repeated notes", "повтор звука", "повтора звука", "повторов звука"));
        }

        string text = $"{w.T("Intervals in semitones", "Интервалы в полутонах")}: {list}. {Words.Capitalize(string.Join(", ", summary))}.";
        if (leaps > 0)
        {
            int largest = intervals.OrderByDescending(Math.Abs).ThenByDescending(step => step).First();
            text += w.IsRussian
                ? $" Самый большой скачок: {IntervalName(largest, w)} {(largest > 0 ? "вверх" : "вниз")}."
                : $" Largest leap: {WithArticle(IntervalName(largest))} {(largest > 0 ? "up" : "down")}.";
        }

        return text;
    }

    private static string ContourText(ContourStep[] contour, int netSemitones, Words w)
    {
        var runs = new List<ContourStep>();
        foreach (ContourStep step in contour.Where(step => step != ContourStep.Same))
        {
            if (runs.Count == 0 || runs[^1] != step)
            {
                runs.Add(step);
            }
        }

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

        string end = netSemitones == 0
            ? w.T("ends where it starts", "заканчивается там же, где начинается")
            : w.IsRussian
                ? $"заканчивается на {IntervalBy(netSemitones, w)} {(netSemitones > 0 ? "выше" : "ниже")}, чем начинается"
                : $"ends {WithArticle(IntervalName(netSemitones))} {(netSemitones > 0 ? "higher" : "lower")} than it starts";
        return $"{shape}; {end}.";
    }

    private static MotifFact RangeFact(Note[] notes, MusicalKey key, Words w)
    {
        Note low = notes.MinBy(note => note.Pitch.MidiNumber)!;
        Note high = notes.MaxBy(note => note.Pitch.MidiNumber)!;
        int span = high.Pitch.MidiNumber - low.Pitch.MidiNumber;
        string text = span == 0
            ? w.T($"Range: one pitch, {key.Spell(low.Pitch.MidiNumber)}.", $"Диапазон: одна высота, {key.Spell(low.Pitch.MidiNumber)}.")
            : w.IsRussian
                ? $"Диапазон: от {key.Spell(low.Pitch.MidiNumber)} до {key.Spell(high.Pitch.MidiNumber)}, {IntervalName(span, w)} ({w.Count(span, "", "", "полутон", "полутона", "полутонов")})."
                : $"Range: {key.Spell(low.Pitch.MidiNumber)} to {key.Spell(high.Pitch.MidiNumber)}, {WithArticle(IntervalName(span))} ({Count(span, "semitone", "semitones")}).";
        return new MotifFact("melody.range", FactLens.Melody, FactCertainty.Detected, text, w.T("Measured over every note of the motif.", "Измерено по всем нотам мотива."))
        {
            Places = ExtremePlaces(notes, key, w),
        };
    }

    /// <summary>The lowest and the highest notes, each pitch as one place with every note on it; one place when every note has one pitch.</summary>
    private static FactPlace[] ExtremePlaces(Note[] notes, MusicalKey key, Words w)
    {
        int low = notes.Min(note => note.Pitch.MidiNumber);
        int high = notes.Max(note => note.Pitch.MidiNumber);
        return low == high
            ? [FactPlace.Of($"{w.T("one pitch", "одна высота")}, {key.Spell(low)}", notes)]
            :
            [
                FactPlace.Of($"{w.T("lowest", "самая низкая")}, {key.Spell(low)}", notes.Where(note => note.Pitch.MidiNumber == low)),
                FactPlace.Of($"{w.T("highest", "самая высокая")}, {key.Spell(high)}", notes.Where(note => note.Pitch.MidiNumber == high)),
            ];
    }

    /// <summary>Each place where the line makes its largest leap, the two notes of the leap; none when the line has no leap.</summary>
    private static FactPlace[] LeapPlaces(Note[] line, int[] intervals, MusicalKey key, Words w)
    {
        if (!intervals.Any(step => Math.Abs(step) > 2))
        {
            return [];
        }

        int largest = intervals.OrderByDescending(Math.Abs).ThenByDescending(step => step).First();
        return [.. Enumerable.Range(0, intervals.Length)
            .Where(i => intervals[i] == largest)
            .Select(i => FactPlace.Of(
                w.IsRussian
                    ? $"самый большой скачок, {key.Spell(line[i].Pitch.MidiNumber)} – {key.Spell(line[i + 1].Pitch.MidiNumber)}"
                    : $"largest leap, {key.Spell(line[i].Pitch.MidiNumber)} to {key.Spell(line[i + 1].Pitch.MidiNumber)}",
                [line[i], line[i + 1]]))];
    }

    /// <summary>
    /// Where the contour starts, turns and ends: a turn is the note the line leaves in a new
    /// direction, a peak when it turns down and a low point when it turns up. None for one pitch.
    /// </summary>
    private static FactPlace[] ContourPlaces(Note[] line, ContourStep[] contour, MusicalKey key, Words w)
    {
        if (contour.All(step => step == ContourStep.Same))
        {
            return [];
        }

        var places = new List<FactPlace> { FactPlace.Of($"{w.T("start", "начало")}, {key.Spell(line[0].Pitch.MidiNumber)}", [line[0]]) };
        ContourStep? direction = null;
        for (int i = 0; i < contour.Length; i++)
        {
            if (contour[i] == ContourStep.Same)
            {
                continue;
            }

            if (direction is not null && contour[i] != direction)
            {
                string turn = contour[i] == ContourStep.Down ? w.T("peak", "вершина") : w.T("low point", "нижняя точка");
                places.Add(FactPlace.Of($"{turn}, {key.Spell(line[i].Pitch.MidiNumber)}", [line[i]]));
            }

            direction = contour[i];
        }

        places.Add(FactPlace.Of($"{w.T("end", "конец")}, {key.Spell(line[^1].Pitch.MidiNumber)}", [line[^1]]));
        return [.. places];
    }

    private static MotifFact DurationsFact(Note[] notes, bool offGrid, Words w)
    {
        var counts = new List<(NoteValue Value, Note[] Notes)>();
        var other = new List<Note>();
        foreach (IGrouping<long, Note> group in notes.GroupBy(note => note.Span.Duration))
        {
            int index = Array.FindIndex(s_values, value => value.Ticks == group.Key);
            if (index < 0)
            {
                other.AddRange(group);
            }
            else
            {
                counts.Add((s_values[index], [.. group]));
            }
        }

        // Each length the text names is a place: the notes of that length.
        var ordered = counts
            .OrderByDescending(value => value.Notes.Length)
            .ThenByDescending(value => value.Value.Ticks)
            .ToList();
        var parts = ordered.Select(value => value.Value.Counted(value.Notes.Length, w)).ToList();
        var places = ordered.Select(value => FactPlace.Of(w.IsRussian ? value.Value.RussianFew : value.Notes.Length == 1 ? value.Value.One : value.Value.Many, value.Notes)).ToList();
        if (other.Count > 0)
        {
            parts.Add(w.IsRussian
                ? $"{other.Count} {Words.RussianForm(other.Count, "длина, не равная одной длительности", "длины, не равные одной длительности", "длин, не равных одной длительности")}"
                : Count(other.Count, "length that is no single note value", "lengths that are no single note value"));
            places.Add(FactPlace.Of(w.T("no single note value", "не одна длительность"), other));
        }

        string text = $"{w.T("Lengths", "Длительности")}: {string.Join(", ", parts)}.";
        MotifFact fact = offGrid
            ? new MotifFact("rhythm.durations", FactLens.Rhythm, FactCertainty.Uncertain, text, w.T(
                "Onsets fall between grid lines, as in a played performance, so lengths may not be the written note values.",
                "Атаки попадают между линиями сетки, как в живом исполнении, поэтому длины могут не совпадать с записанными длительностями."))
            : new MotifFact("rhythm.durations", FactLens.Rhythm, FactCertainty.Detected, text, w.T(
                "Read from the notes' lengths; a tied note counts as one length.",
                "Прочитано по длинам нот; нота с лигой считается одной длиной."));
        return fact with { Places = places };
    }

    private static MotifFact GridFact(Note[] notes, Tick[] onsets, MeterMap meter, Words w)
    {
        int onBeat = onsets.Count(onset => OffsetInBar(meter, onset) % meter.SignatureAt(onset).BeatTicks == 0);
        int offGrid = 0;
        int finestStraight = -1;
        int finestTriplet = -1;
        foreach (Tick onset in onsets)
        {
            long offset = OffsetInBar(meter, onset);
            int index = Array.FindIndex(s_grids, grid => offset % grid.Ticks == 0);
            if (index < 0)
            {
                offGrid++;
            }
            else if (s_grids[index].Triplet)
            {
                finestTriplet = Math.Max(finestTriplet, index);
            }
            else
            {
                finestStraight = Math.Max(finestStraight, index);
            }
        }

        MetricPosition start = meter.ToPosition(onsets[0]);
        string startText = w.IsRussian
            ? start.BeatOffset == 0
                ? $"Начинается на доле {start.Beat} такта {start.Bar}."
                : $"Начинается после доли {start.Beat} такта {start.Bar}, не на долю."
            : start.BeatOffset == 0
                ? $"It starts on beat {start.Beat} of bar {start.Bar}."
                : $"It starts after beat {start.Beat} of bar {start.Bar}, off the beat.";
        string beats = w.IsRussian
            ? $"{onBeat} {w.OutOf(onsets.Length, "", "", "атаки", "атак")} {Words.RussianForm(onBeat, "приходится", "приходятся", "приходятся")} на долю."
            : $"{onBeat} of {Count(onsets.Length, "onset", "onsets")} {(onBeat == 1 ? "falls" : "fall")} on a beat.";
        FactPlace startPlace = FactPlace.Of($"{w.T("start", "начало")}, {Place(start, w)}", notes.Where(note => note.Span.Onset == onsets[0]));

        if (offGrid > 0)
        {
            return new MotifFact(
                "rhythm.grid",
                FactLens.Rhythm,
                FactCertainty.Uncertain,
                w.IsRussian
                    ? $"{offGrid} {w.OutOf(onsets.Length, "", "", "атаки", "атак")} {Words.RussianForm(offGrid, "попадает", "попадают", "попадают")} между линиями сетки тридцать вторых и триолей. {beats} {startText}"
                    : $"{offGrid} of {Count(onsets.Length, "onset", "onsets")} {(offGrid == 1 ? "falls" : "fall")} between thirty-second and triplet grid lines. {beats} {startText}",
                w.T(
                    "Measured from the bar lines. Onsets off every grid usually mean a played performance whose timing was not quantised.",
                    "Измерено от тактовых черт. Атаки вне любой сетки обычно означают живое исполнение без квантования."))
            {
                Places = [FactPlace.Of(w.T("between grid lines", "между линиями сетки"), notes.Where(note => !s_grids.Any(grid => OffsetInBar(meter, note.Span.Onset) % grid.Ticks == 0))), startPlace],
            };
        }

        // A triplet grid covers straight onsets that fall on its lines, such as quarters on the eighth-note triplet grid.
        string grid = w.IsRussian
            ? (finestStraight, finestTriplet) switch
            {
                ( < 0, _) => $"сетку {s_grids[finestTriplet].RussianGrid}",
                (_, < 0) => $"сетку {s_grids[finestStraight].RussianGrid}",
                _ when s_grids[finestStraight].Ticks % s_grids[finestTriplet].Ticks == 0 => $"сетку {s_grids[finestTriplet].RussianGrid}",
                _ => $"сетку {s_grids[finestStraight].RussianGrid}, с {s_grids[finestTriplet].RussianWith}",
            }
            : (finestStraight, finestTriplet) switch
            {
                ( < 0, _) => $"the {s_grids[finestTriplet].Name} grid",
                (_, < 0) => $"the {s_grids[finestStraight].Name} grid",
                _ when s_grids[finestStraight].Ticks % s_grids[finestTriplet].Ticks == 0 => $"the {s_grids[finestTriplet].Name} grid",
                _ => $"the {s_grids[finestStraight].Name} grid, with {s_grids[finestTriplet].Name}s",
            };
        return new MotifFact("rhythm.grid", FactLens.Rhythm, FactCertainty.Detected, w.T($"Onsets fit {grid}. {beats} {startText}", $"Атаки укладываются в {grid}. {beats} {startText}"), w.T("Measured from the bar lines.", "Измерено от тактовых черт."))
        {
            Places = [startPlace],
        };
    }

    private static MotifFact SilenceFact(Note[] notes, TickSpan span, Words w)
    {
        var gaps = new List<long>();
        var places = new List<FactPlace>();
        Tick soundingUntil = notes[0].Span.End;
        foreach (Note note in notes.Skip(1))
        {
            if (note.Span.Onset > soundingUntil)
            {
                gaps.Add(note.Span.Onset.Value - soundingUntil.Value);
                places.Add(new FactPlace($"{w.T("silence", "пауза")}, {LengthName(gaps[^1], w)}", TickSpan.FromBounds(soundingUntil, note.Span.Onset), []));
            }

            if (note.Span.End > soundingUntil)
            {
                soundingUntil = note.Span.End;
            }
        }

        string text = gaps.Count == 0
            ? w.T("No silence between notes", "Между нотами нет пауз")
            : w.IsRussian
                ? $"{w.Count(gaps.Count, "", "", "пауза", "паузы", "пауз")} между нотами: {string.Join(", ", gaps.Select(gap => LengthName(gap, w)))}"
                : $"{Count(gaps.Count, "silence", "silences")} between notes: {string.Join(", ", gaps.Select(gap => LengthName(gap, w)))}";
        long before = notes[0].Span.Onset.Value - span.Onset.Value;
        if (before > 0)
        {
            text += w.IsRussian
                ? $"; выделение начинается с паузы: {LengthName(before, w)}"
                : $"; the selection opens with {LengthName(before, w)} of silence";
            places.Add(new FactPlace($"{w.T("opening silence", "начальная пауза")}, {LengthName(before, w)}", TickSpan.FromBounds(span.Onset, notes[0].Span.Onset), []));
        }

        return new MotifFact("rhythm.silences", FactLens.Rhythm, FactCertainty.Detected, $"{text}.", w.T(
            "Measured where no note of the motif sounds; rests in other voices or hands are not counted.",
            "Измерено там, где не звучит ни одна нота мотива; паузы в других голосах или руках не считаются."))
        {
            Places = places,
        };
    }

    private static int OffGridCount(Note[] notes, MeterMap meter) =>
        notes.Count(note => !s_grids.Any(grid => OffsetInBar(meter, note.Span.Onset) % grid.Ticks == 0));

    /// <summary>Ticks from the bar line, counting a pickup from where a full bar would start.</summary>
    private static long OffsetInBar(MeterMap meter, Tick position)
    {
        Bar bar = meter.BarAt(position);
        return position.Value - bar.Span.Onset.Value + bar.LeadIn;
    }

    /// <summary>"a quarter", or "a length that is no single note value"; "четверть".</summary>
    private static string LengthName(long ticks, Words w)
    {
        int index = Array.FindIndex(s_values, value => value.Ticks == ticks);
        return index < 0
            ? w.T("a length that is no single note value", "длина, не равная одной длительности")
            : w.IsRussian ? s_values[index].RussianOne : WithArticle(s_values[index].One);
    }

    private static int PitchClassCount(Note[] notes) => notes.Select(note => note.Pitch.PitchClass).Distinct().Count();

    private static string Accidentals(int fifths, Words w) => fifths switch
    {
        0 => w.T("no sharps or flats", "без диезов и бемолей"),
        > 0 => w.Count(fifths, "sharp", "sharps", "диез", "диеза", "диезов"),
        _ => w.Count(-fifths, "flat", "flats", "бемоль", "бемоля", "бемолей"),
    };

    private static FactCertainty Weaker(FactCertainty a, FactCertainty b) => (FactCertainty)Math.Max((int)a, (int)b);

    private static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    private static string WithArticle(string noun) => noun[0] switch
    {
        >= '0' and <= '9' => noun,
        'a' or 'e' or 'i' or 'o' or 'u' => "an " + noun,
        _ => "a " + noun,
    };
}