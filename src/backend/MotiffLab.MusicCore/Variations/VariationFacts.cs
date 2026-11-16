using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>
/// What every operation and every chain of steps shares: the motif's notes, the diff, the
/// kept-property checks (S1-E9) and the explanation, which states only facts read from the diff (S1-E4).
/// </summary>
public static class VariationFacts
{
    /// <summary>The selected notes, in the selection's order.</summary>
    public static IReadOnlyList<Note> MotifNotes(IEnumerable<Note> notes, MotifSelection selection)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(selection);
        Dictionary<string, Note> byId = notes.ToDictionary(static note => note.Id, StringComparer.Ordinal);
        return selection.NoteIds.Select(id => byId[id]).ToArray();
    }

    /// <summary>
    /// Builds the variation from the notes before and after: the diff by note id, ordered notes,
    /// checks of the kept properties against <paramref name="before"/>, and the explanation.
    /// </summary>
    /// <param name="key">The key the step counted scale steps in, or null; intervals then also count as kept when their sizes in scale steps are (the Keep table's footnote 2).</param>
    /// <param name="meter">The meter the explanation names beats in; null reads <see cref="MeterMap.Default"/>.</param>
    /// <param name="words">The language the explanation is written in; English by default.</param>
    public static Variation Build(
        string operation,
        TickSpan span,
        IReadOnlyList<Note> before,
        IEnumerable<Note> after,
        IReadOnlySet<KeepProperty> keep,
        IReadOnlyList<string> flags,
        MusicalKey? key = null,
        MeterMap? meter = null,
        Words words = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(keep);
        ArgumentNullException.ThrowIfNull(flags);
        Note[] ordered = Ordered(after);
        Dictionary<string, Note> afterById = ordered.ToDictionary(static note => note.Id, StringComparer.Ordinal);
        var beforeIds = before.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        NoteChange[] changes = before
            .Where(note => afterById.ContainsKey(note.Id))
            .Select(note => new NoteChange(note, afterById[note.Id]))
            .ToArray();
        Note[] added = ordered.Where(note => !beforeIds.Contains(note.Id)).ToArray();
        Note[] removed = before.Where(note => !afterById.ContainsKey(note.Id)).ToArray();
        Note[] source = [.. before];
        var textures = (Before: MotifTexture.Read(source), After: MotifTexture.Read(ordered));
        Note[][]? copies = Copies(source, ordered, span);
        bool melodyPitchClasses = KeepTable.Qualifies(operation.Split(VariationChain.StepSeparator), KeepProperty.Melody, KeepTable.MelodyPitchClasses);
        KeepCheck[] properties = Enum.GetValues<KeepProperty>()
            .Select(property => new KeepCheck(property, Check(property, source, ordered, copies, span, key, melodyPitchClasses, textures.Before, textures.After, meter ?? MeterMap.Default)))
            .ToArray();
        KeepCheck[] checks = properties.Where(check => keep.Contains(check.Property)).ToArray();
        string explanation = Explain(changes, added, removed, source, ordered, textures.Before, textures.After, Sequence(source, copies, span, key, words), words);
        if (OperationTemplates.Lead(operation, changes, textures.Before, textures.After, key, meter ?? MeterMap.Default, words) is { } lead)
        {
            explanation = $"{lead} {explanation}";
        }

        bool sameMelody = Pitches(textures.Before.Melody).SequenceEqual(Pitches(textures.After.Melody));
        string unchanged = Unchanged(properties, textures.Before, sameMelody, words);
        if (changes.Length + added.Length + removed.Length > 0 && unchanged.Length > 0)
        {
            explanation = $"{explanation} {unchanged}";
        }

        return new Variation(operation, ordered, changes, added, removed, checks, properties, flags, explanation);
    }

    /// <summary>
    /// "Unchanged: pitches, pitch order and register.", naming every property the diff shows the step
    /// left as it was; the bass is named only when the source has one apart from its melody, and a
    /// melody kept only by its pitch classes (footnote 3) is named as such.
    /// </summary>
    private static string Unchanged(IReadOnlyList<KeepCheck> properties, MotifTexture before, bool sameMelody, Words w)
    {
        string[] names = [.. properties
            .Where(check => check.Status == KeepStatus.Kept && (check.Property != KeepProperty.Bass || !before.IsSingleLine))
            .Select(check => check.Property switch
            {
                KeepProperty.Pitches => w.T("pitches", "высоты"),
                KeepProperty.PitchOrder => w.T("pitch order", "порядок высот"),
                KeepProperty.Contour => w.T("contour", "контур"),
                KeepProperty.Intervals => w.T("intervals", "интервалы"),
                KeepProperty.Rhythm => w.T("rhythm", "ритм"),
                KeepProperty.Melody => sameMelody ? w.T("melody pitches", "высоты мелодии") : w.T("melody pitch classes", "звуковысотные классы мелодии"),
                KeepProperty.Bass => w.T("bass pitches", "высоты баса"),
                KeepProperty.Harmony => w.T("harmony", "гармония"),
                KeepProperty.Register => w.T("register", "регистр"),
                _ => check.Property.ToString().ToLowerInvariant(),
            })];
        return names.Length == 0 ? "" : $"{w.T("Unchanged", "Не изменились")}: {w.Joined(names)}.";
    }

    /// <summary>"3 quarter notes", "1 eighth", "5 sixteenths", or ticks when the length is finer than that.</summary>
    public static string Length(long ticks) => Length(ticks, Words.English);

    /// <summary>
    /// The length in a language: "1 eighth"; "1 восьмая", or after a preposition that takes the
    /// accusative ("на 1 восьмую позже") when <paramref name="accusative"/> is set.
    /// </summary>
    public static string Length(long ticks, Words words, bool accusative = false)
    {
        (long unit, string name, string one, string oneBy, string few, string many) = ticks switch
        {
            _ when ticks % CanonicalTime.TicksPerQuarter == 0 => (CanonicalTime.TicksPerQuarter, "quarter note", "четверть", "четверть", "четверти", "четвертей"),
            _ when ticks % (CanonicalTime.TicksPerQuarter / 2) == 0 => (CanonicalTime.TicksPerQuarter / 2, "eighth", "восьмая", "восьмую", "восьмые", "восьмых"),
            _ when ticks % (CanonicalTime.TicksPerQuarter / 4) == 0 => (CanonicalTime.TicksPerQuarter / 4, "sixteenth", "шестнадцатая", "шестнадцатую", "шестнадцатые", "шестнадцатых"),
            _ when ticks % (CanonicalTime.TicksPerQuarter / 8) == 0 => (CanonicalTime.TicksPerQuarter / 8, "thirty-second", "тридцать вторая", "тридцать вторую", "тридцать вторые", "тридцать вторых"),
            _ => (1L, "tick", "тик", "тик", "тика", "тиков"),
        };
        long count = ticks / unit;
        if (words.IsRussian)
        {
            int form = (int)(count % 100);
            return $"{count} {Words.RussianForm(form, accusative ? oneBy : one, few, many)}";
        }

        return count == 1 ? $"1 {name}" : $"{count} {name}s";
    }

    /// <summary>
    /// How far apart copies of these notes sit when a step repeats them: the span, or the smallest
    /// whole number of spans that holds every note to its end.
    /// </summary>
    public static long Period(IReadOnlyList<Note> notes, TickSpan span)
    {
        ArgumentNullException.ThrowIfNull(notes);
        long extent = notes.Count == 0 ? 0 : notes.Max(static note => note.Span.End.Value) - span.Onset.Value;
        return Math.Max(1, (extent + span.Duration - 1) / span.Duration) * span.Duration;
    }

    /// <summary>
    /// The result cut into back-to-back windows of the source's <see cref="Period"/>, when it fills
    /// two or more of them in a row from the span's start, as a sequence does; null otherwise.
    /// </summary>
    private static Note[][]? Copies(Note[] before, Note[] after, TickSpan span)
    {
        long period = Period(before, span);
        long start = span.Onset.Value;
        if (after.Length == 0 || after.Any(note => note.Span.Onset.Value < start))
        {
            return null;
        }

        IGrouping<long, Note>[] windows = [.. after.GroupBy(note => (note.Span.Onset.Value - start) / period).OrderBy(static window => window.Key)];
        return windows.Length < 2 || windows.Where((window, i) => window.Key != i).Any()
            ? null
            : [.. windows.Select(static window => window.ToArray())];
    }

    private static Note[] Ordered(IEnumerable<Note> notes) => notes
        .OrderBy(static note => note.Span.Onset)
        .ThenBy(static note => note.Pitch.MidiNumber)
        .ThenBy(static note => note.Id, StringComparer.Ordinal)
        .ToArray();

    /// <remarks>
    /// Pitches, pitch order and register read every note. Contour and intervals read the melody, as
    /// the melody facts do (S1-A2), so the notes of a chord under it are not melodic steps. Melody
    /// and bass are kept when their lines keep their pitches in order; their rhythm may change
    /// (the Keep table's footnote 1). Contour, intervals and rhythm are also kept when the result is
    /// copies of the motif, one after the other (<see cref="Copies"/>), and each copy keeps them, as
    /// in a sequence; the moves between copies are not the motif's own. With a key, intervals are
    /// also kept when their sizes in scale steps are, so a diatonic copy keeps them (footnote 2).
    /// When <paramref name="melodyPitchClasses"/> is set, a step was V3, and the melody is also kept
    /// when its pitch classes stay in order and only octaves change (footnote 3).
    /// </remarks>
    private static KeepStatus Check(
        KeepProperty property,
        Note[] before,
        Note[] after,
        Note[][]? copies,
        TickSpan span,
        MusicalKey? key,
        bool melodyPitchClasses,
        MotifTexture textureBefore,
        MotifTexture textureAfter,
        MeterMap meter)
    {
        if (property is KeepProperty.Harmony)
        {
            return SameHarmony(before, after, span, key, meter);
        }

        int[] pitchesBefore = before.Select(static note => note.Pitch.MidiNumber).ToArray();
        int[] pitchesAfter = after.Select(static note => note.Pitch.MidiNumber).ToArray();
        int[] melodyBefore = Pitches(textureBefore.Melody);
        int[] melodyAfter = Pitches(textureAfter.Melody);
        bool kept = property switch
        {
            KeepProperty.Pitches => pitchesBefore.Order().SequenceEqual(pitchesAfter.Order()),
            KeepProperty.PitchOrder => pitchesBefore.SequenceEqual(pitchesAfter),
            KeepProperty.Contour => Each(melodyAfter, after, copies, melody => SameContour(melodyBefore, melody)),
            KeepProperty.Intervals => Each(melodyAfter, after, copies, melody => SameIntervals(melodyBefore, melody, key)),
            KeepProperty.Melody => melodyBefore.SequenceEqual(melodyAfter)
                || (melodyPitchClasses && melodyBefore.Select(PitchClass).SequenceEqual(melodyAfter.Select(PitchClass))),
            KeepProperty.Bass => Pitches(textureBefore.Bass).SequenceEqual(Pitches(textureAfter.Bass)),
            KeepProperty.Rhythm => Rhythm(before, span.Onset.Value).SequenceEqual(Rhythm(after, span.Onset.Value))
                || (copies is not null && copies.Select((copy, i) => Rhythm(before, span.Onset.Value).Order().SequenceEqual(Rhythm(copy, span.Onset.Value + (i * Period(before, span))).Order())).All(static same => same)),
            KeepProperty.Register => pitchesBefore.Length == pitchesAfter.Length
                && (pitchesBefore.Length == 0 || (pitchesBefore.Min() == pitchesAfter.Min() && pitchesBefore.Max() == pitchesAfter.Max())),
            _ => throw new ArgumentOutOfRangeException(nameof(property), property, "Unknown property."),
        };
        return kept ? KeepStatus.Kept : KeepStatus.NoLongerKept;
    }

    /// <summary>
    /// Harmony is kept when the chords read from the notes (<see cref="HarmonyAnalyzer.FindChords"/>) are the same at
    /// every place a chord starts, before or after; not checked when the source has no chords, as on a single line.
    /// </summary>
    private static KeepStatus SameHarmony(Note[] before, Note[] after, TickSpan span, MusicalKey? key, MeterMap meter)
    {
        IReadOnlyList<ChordSpan> was = HarmonyAnalyzer.FindChords(before, meter, span, key);
        if (was.Count == 0)
        {
            return KeepStatus.NotChecked;
        }

        IReadOnlyList<ChordSpan> now = HarmonyAnalyzer.FindChords(after, meter, span, key);
        static ChordLabel? At(IReadOnlyList<ChordSpan> chords, Tick at) => chords.FirstOrDefault(chord => chord.Span.Contains(at))?.Chord;
        bool same = was.Concat(now).Select(static chord => chord.Span.Onset).All(at => At(was, at) == At(now, at));
        return same ? KeepStatus.Kept : KeepStatus.NoLongerKept;
    }

    /// <summary>Whether the whole result's melody, or else the melody of every copy, passes <paramref name="same"/>.</summary>
    private static bool Each(int[] melodyAfter, Note[] after, Note[][]? copies, Func<int[], bool> same) =>
        same(melodyAfter) || (after.Length > 0 && copies is not null && copies.All(copy => same(Pitches(MotifTexture.Read(copy).Melody))));

    private static bool SameContour(int[] before, int[] after) => Steps(before).Select(Math.Sign).SequenceEqual(Steps(after).Select(Math.Sign));

    private static bool SameIntervals(int[] before, int[] after, MusicalKey? key)
    {
        if (Steps(before).SequenceEqual(Steps(after)))
        {
            return true;
        }

        if (key is not { } scale || before.Length != after.Length)
        {
            return false;
        }

        ScalePosition[] from = [.. before.Select(pitch => ScaleSteps.Locate(scale, pitch))];
        ScalePosition[] to = [.. after.Select(pitch => ScaleSteps.Locate(scale, pitch))];
        return from.Select(static position => position.Alteration).SequenceEqual(to.Select(static position => position.Alteration))
            && from.Zip(from.Skip(1), static (a, b) => b.Step - a.Step).SequenceEqual(to.Zip(to.Skip(1), static (a, b) => b.Step - a.Step));
    }

    private static int PitchClass(int pitch) => pitch % 12;

    private static int[] Pitches(IReadOnlyList<Note> line) => line.Select(static note => note.Pitch.MidiNumber).ToArray();

    private static IEnumerable<int> Steps(int[] pitches) => pitches.Zip(pitches.Skip(1), static (a, b) => b - a);

    private static IEnumerable<(long Offset, long Duration)> Rhythm(Note[] notes, long start) =>
        notes.Select(note => (note.Span.Onset.Value - start, note.Span.Duration));

    private static string Explain(
        IReadOnlyList<NoteChange> changes,
        Note[] added,
        Note[] removed,
        Note[] before,
        Note[] after,
        MotifTexture textureBefore,
        MotifTexture textureAfter,
        string? sequence,
        Words w)
    {
        int count = changes.Count;
        if (count == 0 && added.Length == 0 && removed.Length == 0)
        {
            return w.T("The selection has no notes, so nothing changed.", "В выделении нет нот, поэтому ничего не изменилось.");
        }

        var sentences = new List<string>();
        if (added.Length > 0)
        {
            sentences.Add(w.IsRussian ? $"Добавлено нот: {added.Length}." : added.Length == 1 ? "1 note is added." : $"{added.Length} notes are added.");
        }

        if (sequence is not null)
        {
            sentences.Add(sequence);
        }

        if (removed.Length > 0)
        {
            sentences.Add(w.IsRussian ? $"Удалено нот: {removed.Length}." : removed.Length == 1 ? "1 note is removed." : $"{removed.Length} notes are removed.");
        }

        if (count == 0)
        {
            return string.Join(" ", sentences);
        }

        int pitchChanges = changes.Count(static change => change.PitchChanged);
        sentences.Add(pitchChanges == 0
            ? w.T("The pitch sequence is unchanged.", "Последовательность высот не изменилась.")
            : w.IsRussian ? $"Высоту меняют {pitchChanges} {NotesOf(count, w)}." : $"{pitchChanges} of {count} notes change pitch.");
        if (pitchChanges > 0 && MelodyIntervals(textureBefore, textureAfter, w) is { } melody)
        {
            sentences.Add(melody);
        }


        int lengthChanges = changes.Count(static change => change.DurationChanged);
        if (changes.All(static change => change.After.Span.Duration == change.Before.Span.Duration * 2))
        {
            sentences.Add($"{Every(count, "lasts twice as long", "last twice as long", "звучит вдвое дольше", "звучат вдвое дольше", w)}.");
        }
        else if (changes.All(static change => change.After.Span.Duration * 2 == change.Before.Span.Duration))
        {
            sentences.Add($"{Every(count, "lasts half as long", "last half as long", "звучит вдвое короче", "звучат вдвое короче", w)}.");
        }
        else
        {
            sentences.Add(lengthChanges == 0
                ? w.T("Every note keeps its length.", "Каждая нота сохраняет длину.")
                : w.IsRussian ? $"Длину меняют {lengthChanges} {NotesOf(count, w)}." : $"{lengthChanges} of {count} notes change length.");
        }

        int louder = changes.Count(static change => change.After.Velocity > change.Before.Velocity);
        int softer = changes.Count(static change => change.After.Velocity < change.Before.Velocity);
        if (louder + softer > 0)
        {
            sentences.Add(w.IsRussian
                ? $"Громкость меняют {louder + softer} {NotesOf(count, w)} (громче: {louder}, тише: {softer})."
                : $"{louder + softer} of {count} notes change velocity ({louder} louder, {softer} softer).");
        }

        long[] shifts = changes.Select(static change => change.After.Span.Onset.Value - change.Before.Span.Onset.Value).ToArray();
        int moved = shifts.Count(static shift => shift != 0);
        long[] gapsBefore = Gaps(changes.Select(static change => change.Before));
        long[] gapsAfter = Gaps(changes.Select(static change => change.After));
        if (moved == 0)
        {
            sentences.Add(w.T("Every note starts where it did.", "Каждая нота начинается там же, где раньше."));
        }
        else if (shifts.Distinct().Count() == 1)
        {
            long shift = shifts[0];
            sentences.Add(w.IsRussian
                ? $"{Every(count, "", "", "начинается", "начинаются", w)} на {Length(Math.Abs(shift), w, accusative: true)} {(shift > 0 ? "позже" : "раньше")}."
                : $"{Every(count, "starts", "start", "", "", w)} {Length(Math.Abs(shift))} {(shift > 0 ? "later" : "earlier")}.");
        }
        else if (gapsBefore.Length > 0 && gapsBefore.Zip(gapsAfter).All(static gap => gap.Second == gap.First * 2))
        {
            sentences.Add(w.T($"The time between onsets doubles, so {Moved(moved, count, w)}.", $"Время между атаками удваивается, поэтому {Moved(moved, count, w)}."));
        }
        else if (gapsBefore.Length > 0 && gapsBefore.Zip(gapsAfter).All(static gap => gap.Second * 2 == gap.First))
        {
            sentences.Add(w.T($"The time between onsets halves, so {Moved(moved, count, w)}.", $"Время между атаками сокращается вдвое, поэтому {Moved(moved, count, w)}."));
        }
        else
        {
            sentences.Add(w.IsRussian ? $"На новом месте начинаются {moved} {NotesOf(count, w)}." : $"{moved} of {count} notes start at a new place.");
        }

        long lengthBefore = Extent(before);
        long lengthAfter = Extent(after);
        if (lengthBefore != lengthAfter)
        {
            sentences.Add(w.IsRussian
                ? $"От первой атаки до конца последней ноты мотив теперь занимает {Length(lengthAfter, w, accusative: true)}; раньше — {Length(lengthBefore, w)}."
                : $"From its first onset to its last note's end, the motif now takes {Length(lengthAfter)} instead of {Length(lengthBefore)}.");
        }

        return string.Join(" ", sentences);
    }

    /// <summary>
    /// "The melody's intervals in semitones go from +3 +5 −1 to −3 −5 +1.", when the melody has the
    /// same notes by id before and after and its intervals changed; null otherwise.
    /// </summary>
    private static string? MelodyIntervals(MotifTexture before, MotifTexture after, Words w)
    {
        if (before.Melody.Count < 2
            || !before.Melody.Select(static note => note.Id).SequenceEqual(after.Melody.Select(static note => note.Id), StringComparer.Ordinal))
        {
            return null;
        }

        int[] from = [.. Steps(Pitches(before.Melody))];
        int[] to = [.. Steps(Pitches(after.Melody))];
        return from.SequenceEqual(to)
            ? null
            : w.T($"The melody's intervals in semitones go from {Signed(from)} to {Signed(to)}.", $"Интервалы мелодии в полутонах: были {Signed(from)}, стали {Signed(to)}.");
    }

    /// <summary>
    /// "The motif repeats 2 times, each copy a 2nd higher in the key's scale than the one before,
    /// keeping its rhythm.", when the first copy is the motif as it was and every later copy has its
    /// rhythm and moves every note the same distance again, in semitones or else in scale steps;
    /// null otherwise.
    /// </summary>
    private static string? Sequence(Note[] before, Note[][]? copies, TickSpan span, MusicalKey? key, Words w)
    {
        if (copies is null)
        {
            return null;
        }

        long period = Period(before, span);
        Note[] source = Shape(before, span.Onset.Value);
        Note[][] shaped = [.. copies.Select((copy, i) => Shape(copy, span.Onset.Value + (i * period)))];
        if (!shaped[0].Select(Sound).SequenceEqual(source.Select(Sound))
            || shaped.Skip(1).Any(copy => !Rhythm(copy, 0).SequenceEqual(Rhythm(source, 0))))
        {
            return null;
        }

        string repeats = w.IsRussian ? w.Times(shaped.Length - 1) : shaped.Length == 2 ? "once" : $"{shaped.Length - 1} times";
        int semitones = shaped[1][0].Pitch.MidiNumber - source[0].Pitch.MidiNumber;
        if (Moves(shaped, source, (pitch, copy) => pitch + (copy * semitones)))
        {
            return semitones == 0
                ? w.T($"The motif repeats {repeats} at the same pitch, keeping its rhythm.", $"Мотив повторяется {repeats} на той же высоте, сохраняя ритм.")
                : w.IsRussian
                    ? Repeats(repeats, $"на {MotifAnalyzer.IntervalBy(semitones, w)} {(semitones > 0 ? "выше" : "ниже")}", w)
                    : Repeats(repeats, $"{(Math.Abs(semitones) >= 12 ? "an" : "a")} {MotifAnalyzer.IntervalName(semitones)} {(semitones > 0 ? "higher" : "lower")}", w);
        }

        if (key is not { } scale)
        {
            return null;
        }

        int steps = ScaleSteps.Locate(scale, shaped[1][0].Pitch.MidiNumber).Step - ScaleSteps.Locate(scale, source[0].Pitch.MidiNumber).Step;
        return steps != 0 && Moves(shaped, source, (pitch, copy) => ScaleSteps.Move(scale, pitch, copy * steps))
            ? w.IsRussian
                ? Repeats(repeats, $"на {RussianScaleDistance(Math.Abs(steps))} {(steps > 0 ? "выше" : "ниже")} по гамме тональности", w)
                : Repeats(repeats, $"a {Ordinal(Math.Abs(steps) + 1)} {(steps > 0 ? "higher" : "lower")} in the key's scale", w)
            : null;
    }

    private static string Repeats(string repeats, string distance, Words w) => w.T(
        $"The motif repeats {repeats}, each copy {distance} than the one before, keeping its rhythm.",
        $"Мотив повторяется {repeats}, каждый раз {distance}, чем предыдущий, сохраняя ритм.");

    // A distance in scale steps, after "на": "на секунду", "на терцию"; past a 10th, in steps.
    private static readonly string[] s_russianScaleDistances = ["приму", "секунду", "терцию", "кварту", "квинту", "сексту", "септиму", "октаву", "нону", "дециму"];

    private static string RussianScaleDistance(int steps) => steps < s_russianScaleDistances.Length
        ? s_russianScaleDistances[steps]
        : $"{steps} {Words.RussianForm(steps, "ступень", "ступени", "ступеней")}";

    /// <summary>Whether every later copy has the source's pitches moved as <paramref name="moved"/> says, given a pitch and the copy's number.</summary>
    private static bool Moves(Note[][] copies, Note[] source, Func<int, int, int> moved) =>
        copies.Skip(1).Select((copy, i) => copy.Length == source.Length
            && copy.Zip(source).All(pair => pair.First.Pitch.MidiNumber == moved(pair.Second.Pitch.MidiNumber, i + 1))).All(static same => same);

    /// <summary>Notes moved to start from <paramref name="start"/>, ordered by onset, then pitch.</summary>
    private static Note[] Shape(Note[] notes, long start) => [.. notes
        .Select(note => note with { Span = new TickSpan(new Tick(note.Span.Onset.Value - start), note.Span.Duration) })
        .OrderBy(static note => note.Span.Onset)
        .ThenBy(static note => note.Pitch.MidiNumber)];

    private static (TickSpan Span, Pitch Pitch) Sound(Note note) => (note.Span, note.Pitch);

    private static string Ordinal(int number) => (number % 100, number % 10) switch
    {
        (11 or 12 or 13, _) => $"{number}th",
        (_, 1) => $"{number}st",
        (_, 2) => $"{number}nd",
        (_, 3) => $"{number}rd",
        _ => $"{number}th",
    };

    private static string Signed(int[] steps) => string.Join(" ", steps.Select(static step => step switch
    {
        > 0 => $"+{step}",
        < 0 => $"−{-step}",
        _ => "0",
    }));

    private static string Every(int count, string one, string many, string russianOne, string russianMany, Words w) => w.IsRussian
        ? count == 1 ? $"Нота {russianOne}" : $"Все {count} {Words.RussianForm(count, "нота", "ноты", "нот")} {russianMany}"
        : count == 1 ? $"The note {one}" : $"All {count} notes {many}";

    private static string Moved(int moved, int count, Words w) => w.IsRussian
        ? $"{Words.RussianForm(moved, "сдвигается", "сдвигаются", "сдвигаются")} {moved} {NotesOf(count, w)}"
        : moved == 1 ? $"1 of {count} notes moves" : $"{moved} of {count} notes move";

    /// <summary>"из 5 нот", "из 1 ноты".</summary>
    private static string NotesOf(int count, Words w) => w.OutOf(count, "note", "notes", "ноты", "нот");

    private static long[] Gaps(IEnumerable<Note> notes)
    {
        long[] onsets = notes.Select(static note => note.Span.Onset.Value).ToArray();
        return onsets.Zip(onsets.Skip(1), static (a, b) => b - a).ToArray();
    }

    private static long Extent(Note[] notes) =>
        notes.Length == 0 ? 0 : notes.Max(static note => note.Span.End.Value) - notes.Min(static note => note.Span.Onset.Value);
}