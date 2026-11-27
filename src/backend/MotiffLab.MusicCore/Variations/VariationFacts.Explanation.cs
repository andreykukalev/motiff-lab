using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>
/// The explanation's sentences in both languages (S1-E4): what the diff changed, the copies of a sequence, what stayed
/// as it was, and the lengths they name, written from <see cref="DiffMeasures"/>, <see cref="CopyMove"/> and the Keep
/// checks. No notes are compared here; that is <c>VariationFacts.cs</c>.
/// </summary>
internal static partial class VariationFacts
{
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

    private static string Explain(DiffMeasures m, CopyMove? sequence, Words w)
    {
        if (m.Changed == 0 && m.Added == 0 && m.Removed == 0)
        {
            return w.T("The selection has no notes, so nothing changed.", "В выделении нет нот, поэтому ничего не изменилось.");
        }

        var sentences = new List<string>();
        if (m.Added > 0)
        {
            sentences.Add(w.IsRussian ? $"Добавлено нот: {m.Added}." : m.Added == 1 ? "1 note is added." : $"{m.Added} notes are added.");
        }

        if (sequence is { } copies)
        {
            sentences.Add(SequenceText(copies, w));
        }

        if (m.Removed > 0)
        {
            sentences.Add(w.IsRussian ? $"Удалено нот: {m.Removed}." : m.Removed == 1 ? "1 note is removed." : $"{m.Removed} notes are removed.");
        }

        if (m.Changed > 0)
        {
            sentences.AddRange(KeptNotes(m, w));
        }

        return string.Join(" ", sentences);
    }

    /// <summary>What happened to the notes kept by id: their pitches, the melody's intervals, lengths, velocities, onsets and the motif's extent.</summary>
    private static IEnumerable<string> KeptNotes(DiffMeasures m, Words w)
    {
        int count = m.Changed;
        int pitchChanges = m.PitchChanges;
        yield return pitchChanges == 0
            ? w.T("The pitch sequence is unchanged.", "Последовательность высот не изменилась.")
            : w.IsRussian ? $"Высоту меняют {pitchChanges} {NotesOf(count, w)}." : $"{pitchChanges} of {count} notes change pitch.";
        if (pitchChanges > 0 && m.MelodySteps is { } melody)
        {
            yield return w.T($"The melody's intervals in semitones go from {Signed(melody.From)} to {Signed(melody.To)}.", $"Интервалы мелодии в полутонах: были {Signed(melody.From)}, стали {Signed(melody.To)}.");
        }

        yield return Lengths(m, w);
        int louder = m.Louder;
        int softer = m.Softer;
        if (louder + softer > 0)
        {
            yield return w.IsRussian
                ? $"Громкость меняют {louder + softer} {NotesOf(count, w)} (громче: {louder}, тише: {softer})."
                : $"{louder + softer} of {count} notes change velocity ({louder} louder, {softer} softer).";
        }

        yield return Onsets(m, w);
        long lengthBefore = m.ExtentBefore;
        long lengthAfter = m.ExtentAfter;
        if (lengthBefore != lengthAfter)
        {
            yield return w.IsRussian
                ? $"От первой атаки до конца последней ноты мотив теперь занимает {Length(lengthAfter, w, accusative: true)}; раньше — {Length(lengthBefore, w)}."
                : $"From its first onset to its last note's end, the motif now takes {Length(lengthAfter)} instead of {Length(lengthBefore)}.";
        }
    }

    private static string Lengths(DiffMeasures m, Words w)
    {
        int count = m.Changed;
        int lengthChanges = m.LengthChanges;
        return m.Doubled ? $"{Every(count, "lasts twice as long", "last twice as long", "звучит вдвое дольше", "звучат вдвое дольше", w)}."
            : m.Halved ? $"{Every(count, "lasts half as long", "last half as long", "звучит вдвое короче", "звучат вдвое короче", w)}."
            : lengthChanges == 0 ? w.T("Every note keeps its length.", "Каждая нота сохраняет длину.")
            : w.IsRussian ? $"Длину меняют {lengthChanges} {NotesOf(count, w)}." : $"{lengthChanges} of {count} notes change length.";
    }

    private static string Onsets(DiffMeasures m, Words w)
    {
        int count = m.Changed;
        int moved = m.Moved;
        if (moved == 0)
        {
            return w.T("Every note starts where it did.", "Каждая нота начинается там же, где раньше.");
        }

        if (m.Shift is { } shift)
        {
            return w.IsRussian
                ? $"{Every(count, "", "", "начинается", "начинаются", w)} на {Length(Math.Abs(shift), w, accusative: true)} {(shift > 0 ? "позже" : "раньше")}."
                : $"{Every(count, "starts", "start", "", "", w)} {Length(Math.Abs(shift))} {(shift > 0 ? "later" : "earlier")}.";
        }

        return m.GapsDouble ? w.T($"The time between onsets doubles, so {Moved(moved, count, w)}.", $"Время между атаками удваивается, поэтому {Moved(moved, count, w)}.")
            : m.GapsHalve ? w.T($"The time between onsets halves, so {Moved(moved, count, w)}.", $"Время между атаками сокращается вдвое, поэтому {Moved(moved, count, w)}.")
            : w.IsRussian ? $"На новом месте начинаются {moved} {NotesOf(count, w)}." : $"{moved} of {count} notes start at a new place.";
    }

    /// <summary>
    /// "The motif repeats 2 times, each copy a 2nd higher in the key's scale than the one before, keeping its rhythm."
    /// </summary>
    private static string SequenceText(CopyMove move, Words w)
    {
        string repeats = w.IsRussian ? w.Times(move.Repeats) : move.Repeats == 1 ? "once" : $"{move.Repeats} times";
        int distance = move.Distance;
        if (move.InScale)
        {
            return w.IsRussian
                ? Repeats(repeats, $"на {RussianScaleDistance(Math.Abs(distance))} {(distance > 0 ? "выше" : "ниже")} по гамме тональности", w)
                : Repeats(repeats, $"a {Ordinal(Math.Abs(distance) + 1)} {(distance > 0 ? "higher" : "lower")} in the key's scale", w);
        }

        return distance == 0
            ? w.T($"The motif repeats {repeats} at the same pitch, keeping its rhythm.", $"Мотив повторяется {repeats} на той же высоте, сохраняя ритм.")
            : w.IsRussian
                ? Repeats(repeats, $"на {w.IntervalBy(distance)} {(distance > 0 ? "выше" : "ниже")}", w)
                : Repeats(repeats, $"{w.IntervalBy(distance)} {(distance > 0 ? "higher" : "lower")}", w);
    }

    private static string Repeats(string repeats, string distance, Words w) => w.T(
        $"The motif repeats {repeats}, each copy {distance} than the one before, keeping its rhythm.",
        $"Мотив повторяется {repeats}, каждый раз {distance}, чем предыдущий, сохраняя ритм.");

    // A distance in scale steps, after "на": "на секунду", "на терцию"; past a 10th, in steps.
    private static readonly string[] s_russianScaleDistances = ["приму", "секунду", "терцию", "кварту", "квинту", "сексту", "септиму", "октаву", "нону", "дециму"];

    private static string RussianScaleDistance(int steps) => steps < s_russianScaleDistances.Length
        ? s_russianScaleDistances[steps]
        : $"{steps} {Words.RussianForm(steps, "ступень", "ступени", "ступеней")}";

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
}