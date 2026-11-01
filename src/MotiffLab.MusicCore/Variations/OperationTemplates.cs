using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>
/// The opening sentence of a step's explanation, one template per operation (S1-E4). Each template
/// reads only the diff, the key and the meter, never the parameters the step was asked for, so it
/// states what happened to the notes; when the diff does not have the template's shape it says
/// nothing and the shared sentences of <see cref="VariationFacts"/> stand alone.
/// </summary>
public static class OperationTemplates
{
    /// <summary>The template's sentence for <paramref name="operation"/>, or null when it has none or the diff does not fit it.</summary>
    /// <param name="operation">A catalogue id; a chain of steps (ids joined by " › ") has no template.</param>
    public static string? Lead(
        string operation,
        IReadOnlyList<NoteChange> changes,
        MotifTexture before,
        MotifTexture after,
        MusicalKey? key,
        MeterMap meter,
        Words words = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(meter);
        return operation switch
        {
            RhythmReshape.OperationId => Anticipated(changes, meter, words) ?? Dotted(changes, meter, words),
            IntervalReshape.OperationId => Reshaped(before, after, key, words),
            ChangeDirection.OperationId => Turned(before, after, key, words),
            HandEdit.OperationId => Edited(changes, before, after, meter, words),
            Articulation.OperationId => Articulated(changes, words),
            AccompanimentPattern.OperationId => Repatterned(before, after, meter, words),
            DensityAndRests.OperationId => Densified(changes, before, after, words),
            BassPattern.OperationId => Rebassed(before, after, meter, key, words),
            Ornament.OperationId => Ornamented(before, after, key, words),
            AccentShift.OperationId => Accented(changes, words),
            VoicingAndRegister.OperationId => Revoiced(changes, before, after, key, meter, words),
            Simplify.OperationId => Simplified(changes, before, after, words),
            PassingNotes.OperationId => Filled(before, after, key, words),
            Reharmonise.OperationId => Reharmonised(changes, before, after, key, meter, words),
            _ => null,
        };
    }

    /// <summary>
    /// "By hand, E5 at beat 2 of bar 1 is removed.", "By hand, F5 is added at beat 3 of bar 1, lasting
    /// 1 eighth.", or "By hand, E5 at beat 2 of bar 1 changes: pitch to F5, velocity from 80 to 90.",
    /// when exactly one note was removed, added or changed.
    /// </summary>
    private static string? Edited(IReadOnlyList<NoteChange> changes, MotifTexture before, MotifTexture after, MeterMap meter, Words w)
    {
        Note[] removed = [.. before.Notes.Where(note => !after.Notes.Any(other => other.Id == note.Id))];
        Note[] added = [.. after.Notes.Where(note => !before.Notes.Any(other => other.Id == note.Id))];
        NoteChange[] changed = [.. changes.Where(static change => NoteValues.Of(change.Before) != NoteValues.Of(change.After))];
        return (removed.Length, added.Length, changed.Length) switch
        {
            (1, 0, 0) => w.T(
                $"By hand, {removed[0].Pitch} at {Place(removed[0].Span.Onset, meter, w)} is removed.",
                $"Вручную удалена нота {removed[0].Pitch} ({Place(removed[0].Span.Onset, meter, w)})."),
            (0, 1, 0) => w.T(
                $"By hand, {added[0].Pitch} is added at {Place(added[0].Span.Onset, meter, w)}, lasting {VariationFacts.Length(added[0].Span.Duration)}.",
                $"Вручную добавлена нота {added[0].Pitch} ({Place(added[0].Span.Onset, meter, w)}), длина: {VariationFacts.Length(added[0].Span.Duration, w)}."),
            (0, 0, 1) => w.T(
                $"By hand, {changed[0].Before.Pitch} at {Place(changed[0].Before.Span.Onset, meter, w)} changes: {string.Join(", ", Edits(changed[0], meter, w))}.",
                $"Вручную изменена нота {changed[0].Before.Pitch} ({Place(changed[0].Before.Span.Onset, meter, w)}): {string.Join(", ", Edits(changed[0], meter, w))}."),
            _ => null,
        };
    }

    private static IEnumerable<string> Edits(NoteChange change, MeterMap meter, Words w)
    {
        if (change.PitchChanged)
        {
            yield return w.T($"pitch to {change.After.Pitch}", $"высота: {change.After.Pitch}");
        }

        if (change.OnsetChanged)
        {
            yield return w.T($"start to {Place(change.After.Span.Onset, meter, w)}", $"начало: {Place(change.After.Span.Onset, meter, w)}");
        }

        if (change.DurationChanged)
        {
            yield return w.T(
                $"length from {VariationFacts.Length(change.Before.Span.Duration)} to {VariationFacts.Length(change.After.Span.Duration)}",
                $"длина: {VariationFacts.Length(change.Before.Span.Duration, w)} → {VariationFacts.Length(change.After.Span.Duration, w)}");
        }

        if (change.Before.Velocity != change.After.Velocity)
        {
            yield return w.T($"velocity from {change.Before.Velocity} to {change.After.Velocity}", $"громкость: {change.Before.Velocity} → {change.After.Velocity}");
        }
    }

    /// <summary>
    /// "Only note lengths change: 9 notes sound shorter.", or "… 4 notes sound longer.", when every
    /// note that changed kept its pitch and onset and all of them changed length the same way.
    /// </summary>
    private static string? Articulated(IReadOnlyList<NoteChange> changes, Words w)
    {
        NoteChange[] changed = [.. changes.Where(static change => change.DurationChanged)];
        if (changed.Length == 0 || changes.Any(static change => change.PitchChanged || change.OnsetChanged))
        {
            return null;
        }

        bool shorter = changed.All(static change => change.After.Span.Duration < change.Before.Span.Duration);
        bool longer = changed.All(static change => change.After.Span.Duration > change.Before.Span.Duration);
        if (!shorter && !longer)
        {
            return null;
        }

        if (w.IsRussian)
        {
            string sound = Words.RussianForm(changed.Length, "звучит", "звучат", "звучат");
            return $"Меняются только длины нот: {(shorter ? "короче" : "дольше")} {sound} {w.Count(changed.Length, "", "", "нота", "ноты", "нот")}.";
        }

        string notes = changed.Length == 1 ? "1 note sounds" : $"{changed.Length} notes sound";
        return $"Only note lengths change: {notes} {(shorter ? "shorter" : "longer")}.";
    }

    /// <summary>
    /// "In bars 2–3 the accompaniment keeps its pitches and plays them in a new rhythm or order; the
    /// melody is unchanged.", when the melody is note for note the same and every bar's accompaniment
    /// sounds the same pitches as before, at other times.
    /// </summary>
    private static string? Repatterned(MotifTexture before, MotifTexture after, MeterMap meter, Words w)
    {
        static (long, long, int) Shape(Note note) => (note.Span.Onset.Value, note.Span.Duration, note.Pitch.MidiNumber);
        if (!before.Melody.Select(Shape).SequenceEqual(after.Melody.Select(Shape)))
        {
            return null;
        }

        HashSet<string> melody = after.Melody.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        Dictionary<int, int[]> Pitches(MotifTexture texture) => texture.Notes
            .Where(note => !melody.Contains(note.Id))
            .GroupBy(note => meter.BarAt(note.Span.Onset).Number)
            .ToDictionary(static bar => bar.Key, static bar => bar.Select(static note => note.Pitch.MidiNumber).Distinct().Order().ToArray());
        Dictionary<int, int[]> was = Pitches(before);
        Dictionary<int, int[]> now = Pitches(after);
        bool samePitches = was.Count > 0 && was.Keys.Order().SequenceEqual(now.Keys.Order()) && was.All(bar => bar.Value.SequenceEqual(now[bar.Key]));
        bool moved = !before.Notes.Where(note => !melody.Contains(note.Id)).Select(Shape).Order()
            .SequenceEqual(after.Notes.Where(note => !melody.Contains(note.Id)).Select(Shape).Order());
        if (!samePitches || !moved)
        {
            return null;
        }

        int first = was.Keys.Min();
        int last = was.Keys.Max();
        if (w.IsRussian)
        {
            string inBars = first == last ? $"В такте {first}" : $"В тактах {first}–{last}";
            return $"{inBars} аккомпанемент сохраняет высоты и играет их в новом ритме или порядке; мелодия не изменилась.";
        }

        string bars = first == last ? $"bar {first}" : $"bars {first}–{last}";
        return $"In {bars} the accompaniment keeps its pitches and plays them in a new rhythm or order; the melody is unchanged.";
    }

    /// <summary>
    /// V7: "3 notes are split into repeated notes at their pitches: 7 notes now sound where 3 did.", "4 repeated notes
    /// are merged into 2 longer notes at the same pitches.", "5 notes now end 1 eighth before the next note, leaving a
    /// rest; every note starts where it did.", or "3 notes are now held to the next note, so the rests between them
    /// go; every note starts where it did.", when the diff has that shape and no pitch or onset changed.
    /// </summary>
    private static string? Densified(IReadOnlyList<NoteChange> changes, MotifTexture before, MotifTexture after, Words w)
    {
        if (changes.Any(static change => change.PitchChanged || change.OnsetChanged))
        {
            return null;
        }

        Note[] added = [.. after.Notes.Where(note => !before.Notes.Any(other => other.Id == note.Id))];
        Note[] removed = [.. before.Notes.Where(note => !after.Notes.Any(other => other.Id == note.Id))];
        NoteChange[] shorter = [.. changes.Where(static change => change.After.Span.Duration < change.Before.Span.Duration)];
        NoteChange[] longer = [.. changes.Where(static change => change.After.Span.Duration > change.Before.Span.Duration)];
        string Notes(int count) => w.Count(count, "note", "notes", "нота", "ноты", "нот");
        if (added.Length > 0 && removed.Length == 0 && longer.Length == 0 && shorter.Length > 0
            && added.All(note => shorter.Any(change => change.Before.Pitch == note.Pitch && change.Before.Span.Contains(note.Span.Onset))))
        {
            int total = shorter.Length + added.Length;
            return w.IsRussian
                ? $"{Notes(shorter.Length)} {Words.RussianForm(shorter.Length, "разбита", "разбиты", "разбиты")} на повторы той же высоты: теперь {Words.RussianForm(total, "звучит", "звучат", "звучат")} {Notes(total)} вместо {shorter.Length}."
                : $"{(shorter.Length == 1 ? "1 note is" : $"{shorter.Length} notes are")} split into repeated notes at {(shorter.Length == 1 ? "its pitch" : "their pitches")}: {total} notes now sound where {shorter.Length} did.";
        }

        if (removed.Length > 0 && added.Length == 0 && shorter.Length == 0 && longer.Length > 0
            && removed.All(note => longer.Any(change => change.After.Pitch == note.Pitch && change.After.Span.Contains(note.Span.Onset))))
        {
            int total = removed.Length + longer.Length;
            return w.IsRussian
                ? $"{Notes(total)}, {Words.RussianForm(total, "повторяющая", "повторяющие", "повторяющих")} одну высоту, {Words.RussianForm(total, "слита", "слиты", "слиты")} в {longer.Length} {Words.RussianForm(longer.Length, "ноту", "ноты", "нот")} подлиннее."
                : $"{total} repeated notes are merged into {(longer.Length == 1 ? "1 longer note" : $"{longer.Length} longer notes")} at the same {(longer.Length == 1 ? "pitch" : "pitches")}.";
        }

        if (added.Length > 0 || removed.Length > 0)
        {
            return null;
        }

        long? Gap(NoteChange change) => after.Melody.Where(note => note.Span.Onset > change.After.Span.Onset).Select(note => (long?)(note.Span.Onset.Value - change.After.Span.End.Value)).FirstOrDefault();
        if (shorter.Length > 0 && longer.Length == 0)
        {
            long?[] gaps = [.. shorter.Select(Gap)];
            if (gaps.Any(static gap => gap is not > 0))
            {
                return null;
            }

            string rest = gaps.Distinct().Count() == 1
                ? w.T($"{VariationFacts.Length(gaps[0]!.Value)} before the next note, leaving a rest", $"за {VariationFacts.Length(gaps[0]!.Value, w, accusative: true)} до следующей ноты, оставляя паузу")
                : w.T("before the next note, leaving a rest", "раньше следующей ноты, оставляя паузу");
            return w.IsRussian
                ? $"{Notes(shorter.Length)} теперь {Words.RussianForm(shorter.Length, "заканчивается", "заканчиваются", "заканчиваются")} {rest}; каждая нота начинается там же, где раньше."
                : $"{(shorter.Length == 1 ? "1 note now ends" : $"{shorter.Length} notes now end")} {rest}; every note starts where it did.";
        }

        if (longer.Length > 0 && shorter.Length == 0 && longer.All(change => Gap(change) == 0))
        {
            return w.IsRussian
                ? $"{Notes(longer.Length)} теперь {Words.RussianForm(longer.Length, "тянется", "тянутся", "тянутся")} до следующей ноты, и паузы перед ними исчезают; каждая нота начинается там же, где раньше."
                : $"{(longer.Length == 1 ? "1 note is" : $"{longer.Length} notes are")} now held to the next note, so the rests go; every note starts where it did.";
        }

        return null;
    }

    /// <summary>
    /// V12: "The bass line changes from A2 E2 A2 to A2 A2 E2 E2; the melody and the notes over the bass are unchanged.",
    /// or "The bass now holds A2 as a pedal, struck once in each of bars 1–4; …", when only the bass changed.
    /// </summary>
    private static string? Rebassed(MotifTexture before, MotifTexture after, MeterMap meter, MusicalKey? key, Words w)
    {
        static (long, long, int) Shape(Note note) => (note.Span.Onset.Value, note.Span.Duration, note.Pitch.MidiNumber);
        if (!before.Melody.Select(Shape).SequenceEqual(after.Melody.Select(Shape)))
        {
            return null;
        }

        static Note[] Line(MotifTexture texture) => [.. texture.Bass.Where(note => !texture.Melody.Contains(note))];
        static Note[] Upper(MotifTexture texture) => [.. texture.Notes.Where(note => !texture.Bass.Contains(note) || texture.Melody.Contains(note))];
        Note[] was = Line(before);
        Note[] now = Line(after);
        if (now.Length == 0 || !Upper(before).Select(Shape).Order().SequenceEqual(Upper(after).Select(Shape).Order()) || was.Select(Shape).SequenceEqual(now.Select(Shape)))
        {
            return null;
        }

        MusicalKey spelling = key ?? new MusicalKey(0, KeyMode.Major);
        string Names(Note[] line) => string.Join(" ", line.Take(HarmonyAnalyzer.MaxNamedBassNotes).Select(note => spelling.Spell(note.Pitch.MidiNumber))) + (line.Length > HarmonyAnalyzer.MaxNamedBassNotes ? " …" : "");
        string unchanged = w.T("the melody and the notes over the bass are unchanged", "мелодия и голоса над басом не изменились");
        if (now.All(note => note.Pitch == now[0].Pitch))
        {
            int[] bars = [.. now.Select(note => meter.BarAt(note.Span.Onset).Number)];
            string pitch = spelling.Spell(now[0].Pitch.MidiNumber);
            if (bars.Distinct().Count() == bars.Length && bars.Length > 1 && bars[^1] - bars[0] == bars.Length - 1)
            {
                return w.T(
                    $"The bass now holds {pitch} as a pedal, struck once in each of bars {bars[0]}–{bars[^1]}; {unchanged}.",
                    $"Бас теперь держит {pitch} педалью и берёт его по разу в каждом такте {bars[0]}–{bars[^1]}; {unchanged}.");
            }

            return w.T(
                $"The bass now stays on {pitch}: {(now.Length == 1 ? "1 note" : $"{now.Length} notes")}; {unchanged}.",
                $"Бас теперь остаётся на {pitch}: {w.Count(now.Length, "", "", "нота", "ноты", "нот")}; {unchanged}.");
        }

        return w.T(
            $"The bass line changes from {Names(was)} to {Names(now)}; {unchanged}.",
            $"Линия баса меняется: была {Names(was)}, стала {Names(now)}; {unchanged}.");
    }

    /// <summary>
    /// V14: "A mordent on 2 long notes (E5 and A4): the note, the note above and the note again, 1 thirty-second each,
    /// then the note held; every other note is unchanged.", when every ornamented note has the same figure.
    /// </summary>
    private static string? Ornamented(MotifTexture before, MotifTexture after, MusicalKey? key, Words w)
    {
        Note[] added = [.. after.Notes.Where(note => !before.Notes.Any(other => other.Id == note.Id))];
        if (added.Length == 0 || before.Notes.Any(note => !after.Notes.Any(other => other.Id == note.Id)))
        {
            return null;
        }

        var figures = new List<(Note Main, int[] Shape, long Step)>();
        foreach (Note main in before.Notes)
        {
            Note[] parts = [.. after.Notes.Where(note => note.Id == main.Id || note.Id.StartsWith($"{main.Id}~o", StringComparison.Ordinal)).OrderBy(static note => note.Span.Onset)];
            if (parts.Length == 1)
            {
                continue;
            }

            figures.Add((main, [.. parts.Select(part => Math.Sign(part.Pitch.MidiNumber - main.Pitch.MidiNumber))], parts[0].Span.Duration));
        }

        if (figures.Count == 0 || figures.Sum(static figure => figure.Shape.Length - 1) != added.Length || figures.Select(static figure => figure.Step).Distinct().Count() != 1)
        {
            return null;
        }

        static string? Kind(int[] shape) => shape switch
        {
            [0, 1, 0] => "mordent",
            [0, -1, 0] => "lower",
            [1, 0, -1, 0] => "turn",
            [1, 0] => "graceAbove",
            [-1, 0] => "graceBelow",
            _ when shape.Length >= 5 && shape[^1] == 0 && shape[..^1].Select((sign, i) => sign == (i % 2 == 0 ? 0 : 1)).All(static same => same) => "trill",
            _ => null,
        };
        string?[] kinds = [.. figures.Select(figure => Kind(figure.Shape)).Distinct()];
        if (kinds.Length != 1 || kinds[0] is not { } kind)
        {
            return null;
        }

        MusicalKey spelling = key ?? new MusicalKey(0, KeyMode.Major);
        string names = w.Joined([.. figures.Select(figure => spelling.Spell(figure.Main.Pitch.MidiNumber))]);
        int count = figures.Count;
        string step = VariationFacts.Length(figures[0].Step, w);
        if (w.IsRussian)
        {
            (string name, string figure) = kind switch
            {
                "mordent" => ("Мордент", "нота, верхняя соседняя и снова нота"),
                "lower" => ("Нижний мордент", "нота, нижняя соседняя и снова нота"),
                "turn" => ("Группетто", "верхняя соседняя, нота, нижняя соседняя и снова нота"),
                "graceAbove" => ("Форшлаг сверху", "верхняя соседняя на месте ноты"),
                "graceBelow" => ("Форшлаг снизу", "нижняя соседняя на месте ноты"),
                _ => ("Трель", "нота и верхняя соседняя по очереди"),
            };
            return $"{name} на {count} {Words.RussianForm(count, "долгой ноте", "долгих нотах", "долгих нотах")} ({names}): {figure}, каждая — {step}, затем нота тянется; остальные ноты не изменились.";
        }

        (string english, string shape) = kind switch
        {
            "mordent" => ("A mordent", "the note, the note above and the note again"),
            "lower" => ("A lower mordent", "the note, the note below and the note again"),
            "turn" => ("A turn", "the note above, the note, the note below and the note again"),
            "graceAbove" => ("A grace note from above", "the note above on the note's beat"),
            "graceBelow" => ("A grace note from below", "the note below on the note's beat"),
            _ => ("A trill", "the note and the note above, in turn"),
        };
        return $"{english} on {(count == 1 ? "1 long note" : $"{count} long notes")} ({names}): {shape}, {step} each, then the note held; every other note is unchanged.";
    }

    /// <summary>
    /// V3: "4 notes move an octave higher; their note names and the rhythm are unchanged.", "The chords keep their notes
    /// and change position: C F G7 C become C/E F/A G7/D C/E; 6 notes move by an octave, and the melody and the rhythm
    /// are unchanged.", "5 chord notes move by an octave (3 up, 2 down); …", or "3 notes move to the right hand; every
    /// pitch, onset and length is unchanged.", when only octaves or hands changed.
    /// </summary>
    private static string? Revoiced(IReadOnlyList<NoteChange> changes, MotifTexture before, MotifTexture after, MusicalKey? key, MeterMap meter, Words w)
    {
        if (before.Notes.Count != after.Notes.Count || changes.Any(static change => change.OnsetChanged || change.DurationChanged || change.Before.Velocity != change.After.Velocity))
        {
            return null;
        }

        NoteChange[] moved = [.. changes.Where(static change => change.PitchChanged)];
        NoteChange[] handed = [.. changes.Where(static change => change.Before.Hand?.Value != change.After.Hand?.Value)];
        if (moved.Length == 0)
        {
            if (handed.Length == 0 || handed.Select(static change => change.After.Hand?.Value).Distinct().Count() != 1)
            {
                return null;
            }

            bool right = handed[0].After.Hand?.Value == Hand.Right;
            return w.IsRussian
                ? $"{w.Count(handed.Length, "", "", "нота", "ноты", "нот")} {Words.RussianForm(handed.Length, "переходит", "переходят", "переходят")} в {(right ? "правую" : "левую")} руку; высоты, начала и длительности не изменились."
                : $"{(handed.Length == 1 ? "1 note moves" : $"{handed.Length} notes move")} to the {(right ? "right" : "left")} hand; every pitch, onset and length is unchanged.";
        }

        if (handed.Length > 0 || moved.Any(static change => (change.After.Pitch.MidiNumber - change.Before.Pitch.MidiNumber) % 12 != 0))
        {
            return null;
        }

        int up = moved.Count(static change => change.After.Pitch.MidiNumber > change.Before.Pitch.MidiNumber);
        int down = moved.Length - up;
        string Notes(int count) => w.Count(count, "note", "notes", "нота", "ноты", "нот");
        if (moved.Length == before.Notes.Count && moved.All(change => change.After.Pitch.MidiNumber - change.Before.Pitch.MidiNumber == (up > 0 ? 12 : -12)))
        {
            return w.IsRussian
                ? $"{Notes(moved.Length)} {Words.RussianForm(moved.Length, "звучит", "звучат", "звучат")} на октаву {(up > 0 ? "выше" : "ниже")}; названия звуков и ритм не изменились."
                : $"{moved.Length} {(moved.Length == 1 ? "note moves" : "notes move")} an octave {(up > 0 ? "higher" : "lower")}; their note names and the rhythm are unchanged.";
        }

        static (long, long, int) Shape(Note note) => (note.Span.Onset.Value, note.Span.Duration, note.Pitch.MidiNumber);
        if (!before.Melody.Select(Shape).SequenceEqual(after.Melody.Select(Shape)))
        {
            return null;
        }

        MusicalKey spelling = key ?? new MusicalKey(0, KeyMode.Major);
        var span = TickSpan.FromBounds(before.Notes.Min(static note => note.Span.Onset), before.Notes.Max(static note => note.Span.End));
        string[] was = [.. HarmonyAnalyzer.FindChords(before.Notes, meter, span, key).Select(chord => chord.Symbol(spelling))];
        string[] now = [.. HarmonyAnalyzer.FindChords(after.Notes, meter, span, key).Select(chord => chord.Symbol(spelling))];
        string how = up > 0 && down > 0
            ? w.T($" ({up} up, {down} down)", $" ({up} вверх, {down} вниз)")
            : w.T(up > 0 ? " up" : " down", up > 0 ? " вверх" : " вниз");
        string octaves = w.IsRussian
            ? $"{Notes(moved.Length)} {Words.RussianForm(moved.Length, "переходит", "переходят", "переходят")} на октаву{how}"
            : $"{moved.Length} chord {(moved.Length == 1 ? "note moves" : "notes move")} by an octave{how}";
        if (was.Length > 0 && was.Length == now.Length && !was.SequenceEqual(now))
        {
            return w.IsRussian
                ? $"Аккорды сохраняют звуки и меняют расположение: {string.Join(" ", was)} → {string.Join(" ", now)}; {octaves}, а мелодия и ритм не изменились."
                : $"The chords keep their notes and change position: {string.Join(" ", was)} become {string.Join(" ", now)}; {octaves}, and the melody and the rhythm are unchanged.";
        }

        return w.IsRussian
            ? $"{octaves}: аккорды сохраняют звуки, а мелодия и ритм не изменились."
            : $"{char.ToUpperInvariant(octaves[0])}{octaves[1..]}; the chords keep their notes, and the melody and the rhythm are unchanged.";
    }

    /// <summary>
    /// V6: "6 inner notes are left out; the bass plays one root per chord; 12 notes move to the nearest eighth; the
    /// melody's line stays.", from what the diff removed, added and moved.
    /// </summary>
    private static string? Simplified(IReadOnlyList<NoteChange> changes, MotifTexture before, MotifTexture after, Words w)
    {
        Note[] removed = [.. before.Notes.Where(note => !after.Notes.Any(other => other.Id == note.Id))];
        Note[] added = [.. after.Notes.Where(note => !before.Notes.Any(other => other.Id == note.Id))];
        if (removed.Length == 0 && changes.All(static change => !change.PitchChanged && !change.OnsetChanged && !change.DurationChanged))
        {
            return null;
        }

        int roots = added.Count(static note => note.Id.EndsWith("~b1", StringComparison.Ordinal));
        int rounded = changes.Count(static change => change.OnsetChanged || change.DurationChanged);
        int folded = changes.Count(static change => change.PitchChanged);
        string Notes(int count) => w.Count(count, "note", "notes", "нота", "ноты", "нот");
        var parts = new List<string>();
        if (removed.Length > 0)
        {
            parts.Add(w.IsRussian
                ? $"{Notes(removed.Length)} {Words.RussianForm(removed.Length, "убрана", "убраны", "убраны")}"
                : $"{removed.Length} {(removed.Length == 1 ? "note is" : "notes are")} left out");
        }

        if (roots > 0)
        {
            parts.Add(w.T(
                $"the bass plays {(roots == 1 ? "1 root" : $"{roots} roots")}, one per chord",
                $"в басу {w.Count(roots, "", "", "основной тон", "основных тона", "основных тонов")}, по одному на аккорд"));
        }

        if (rounded > 0)
        {
            parts.Add(w.IsRussian
                ? $"{Notes(rounded)} {Words.RussianForm(rounded, "встала", "встали", "встали")} на ближайшую восьмую"
                : $"{rounded} {(rounded == 1 ? "note moves" : "notes move")} to the nearest eighth");
        }

        if (folded > 0)
        {
            parts.Add(w.IsRussian
                ? $"{Notes(folded)} мелодии {Words.RussianForm(folded, "перенесена", "перенесены", "перенесены")} на октаву, чтобы скачок был не шире октавы"
                : $"{folded} melody {(folded == 1 ? "note moves" : "notes move")} by an octave, so no leap is wider than an octave");
        }

        string list = string.Join("; ", parts);
        return $"{char.ToUpperInvariant(list[0])}{list[1..]}.";
    }

    /// <summary>
    /// V8: "The accents move: 4 notes now play louder, at velocity 100, and 12 softer, at velocity 70; every pitch,
    /// onset and length is unchanged.", when only velocities changed.
    /// </summary>
    private static string? Accented(IReadOnlyList<NoteChange> changes, Words w)
    {
        NoteChange[] changed = [.. changes.Where(static change => change.Before.Velocity != change.After.Velocity)];
        if (changed.Length == 0 || changes.Any(static change => change.PitchChanged || change.OnsetChanged || change.DurationChanged))
        {
            return null;
        }

        NoteChange[] louder = [.. changed.Where(static change => change.After.Velocity > change.Before.Velocity)];
        NoteChange[] softer = [.. changed.Where(static change => change.After.Velocity < change.Before.Velocity)];
        string At(NoteChange[] group) => group.Select(static change => change.After.Velocity).Distinct().Count() == 1
            ? w.T($", at velocity {group[0].After.Velocity}", $" (громкость {group[0].After.Velocity})")
            : string.Empty;
        string Part(NoteChange[] group, string english, string russian) => group.Length == 0
            ? null!
            : w.IsRussian
                ? $"{w.Count(group.Length, "", "", "нота", "ноты", "нот")} {Words.RussianForm(group.Length, "звучит", "звучат", "звучат")} {russian}{At(group)}"
                : $"{group.Length} {(group.Length == 1 ? "note" : "notes")} now {(group.Length == 1 ? "plays" : "play")} {english}{At(group)}";
        string[] parts = [.. new[] { Part(louder, "louder", "громче"), Part(softer, "softer", "тише") }.Where(static part => part is not null)];
        return w.IsRussian
            ? $"Акценты сместились: {string.Join(", а ", parts)}; высоты, начала и длительности нот не изменились."
            : $"The accents move: {string.Join(", and ", parts)}; every pitch, onset and length is unchanged.";
    }

    /// <summary>
    /// V11: "Passing notes are added after 3 melody notes (D5 after C5, …), in the second half of each; every main note
    /// keeps its pitch and onset.", when notes were only added after shortened melody notes.
    /// </summary>
    private static string? Filled(MotifTexture before, MotifTexture after, MusicalKey? key, Words w)
    {
        Note[] added = [.. after.Notes.Where(note => !before.Notes.Any(other => other.Id == note.Id))];
        if (added.Length == 0 || before.Notes.Any(note => !after.Notes.Any(other => other.Id == note.Id)))
        {
            return null;
        }

        var figures = new List<(Note Main, Note? Next, Note[] Added)>();
        for (int i = 0; i < before.Melody.Count; i++)
        {
            Note main = before.Melody[i];
            Note[] parts = [.. added.Where(note => note.Id.StartsWith($"{main.Id}~p", StringComparison.Ordinal)).OrderBy(static note => note.Span.Onset)];
            if (parts.Length > 0)
            {
                figures.Add((main, i + 1 < before.Melody.Count ? before.Melody[i + 1] : null, parts));
            }
        }

        if (figures.Sum(static figure => figure.Added.Length) != added.Length || figures.Any(static figure => figure.Next is null))
        {
            return null;
        }

        bool Between(Note note, Note main, Note next) => Math.Min(main.Pitch.MidiNumber, next.Pitch.MidiNumber) < note.Pitch.MidiNumber
            && note.Pitch.MidiNumber < Math.Max(main.Pitch.MidiNumber, next.Pitch.MidiNumber);
        bool passing = figures.All(figure => figure.Added.All(note => Between(note, figure.Main, figure.Next!)));
        MusicalKey spelling = key ?? new MusicalKey(0, KeyMode.Major);
        string Figure((Note Main, Note? Next, Note[] Added) figure) => w.T(
            $"{w.Joined([.. figure.Added.Select(note => spelling.Spell(note.Pitch.MidiNumber))])} after {spelling.Spell(figure.Main.Pitch.MidiNumber)}",
            $"{w.Joined([.. figure.Added.Select(note => spelling.Spell(note.Pitch.MidiNumber))])} после {spelling.Spell(figure.Main.Pitch.MidiNumber)}");
        string list = string.Join(", ", figures.Take(4).Select(Figure)) + (figures.Count > 4 ? ", …" : string.Empty);
        int count = figures.Count;
        return w.IsRussian
            ? $"{(passing ? "Проходящие" : "Вспомогательные")} ноты добавлены после {count} {Words.RussianForm(count, "ноты", "нот", "нот")} мелодии ({list}), во второй половине каждой; основные ноты сохраняют высоту и начало."
            : $"{(passing ? "Passing" : "Neighbour")} notes are added after {(count == 1 ? "1 melody note" : $"{count} melody notes")} ({list}), in the second half of {(count == 1 ? "it" : "each")}; every main note keeps its pitch and onset.";
    }

    /// <summary>
    /// V15: "Read from the notes, the chords change from C F C to Am Dm Am; the melody and the rhythm are unchanged, and
    /// 4 accompaniment notes move to tones of the new chords.", when only accompaniment pitches changed.
    /// </summary>
    private static string? Reharmonised(IReadOnlyList<NoteChange> changes, MotifTexture before, MotifTexture after, MusicalKey? key, MeterMap meter, Words w)
    {
        static (long, long, int) Shape(Note note) => (note.Span.Onset.Value, note.Span.Duration, note.Pitch.MidiNumber);
        NoteChange[] moved = [.. changes.Where(static change => change.PitchChanged)];
        if (moved.Length == 0 || changes.Count != before.Notes.Count || changes.Any(static change => change.OnsetChanged || change.DurationChanged)
            || !before.Melody.Select(Shape).SequenceEqual(after.Melody.Select(Shape)))
        {
            return null;
        }

        var span = TickSpan.FromBounds(before.Notes.Min(static note => note.Span.Onset), before.Notes.Max(static note => note.Span.End));
        IReadOnlyList<ChordSpan> was = HarmonyAnalyzer.FindChords(before.Notes, meter, span, key);
        IReadOnlyList<ChordSpan> now = HarmonyAnalyzer.FindChords(after.Notes, meter, span, key);
        if (was.Count == 0 || now.Count == 0)
        {
            return null;
        }

        MusicalKey spelling = key ?? new MusicalKey(0, KeyMode.Major);
        string Symbols(IReadOnlyList<ChordSpan> chords) => string.Join(" ", chords.Select(chord => chord.Symbol(spelling)));
        return w.IsRussian
            ? $"По нотам аккорды меняются: были {Symbols(was)}, стали {Symbols(now)}; мелодия и ритм не изменились, а {w.Count(moved.Length, "", "", "нота", "ноты", "нот")} аккомпанемента {Words.RussianForm(moved.Length, "переходит", "переходят", "переходят")} в звуки новых аккордов."
            : $"Read from the notes, the chords change from {Symbols(was)} to {Symbols(now)}; the melody and the rhythm are unchanged, and {(moved.Length == 1 ? "1 accompaniment note moves" : $"{moved.Length} accompaniment notes move")} to tones of the new chords.";
    }

    /// <summary>"beat 2 of bar 1", or "1 sixteenth after beat 2 of bar 1" off the beat; "доля 2 такта 1", "1 шестнадцатая после доли 2 такта 1".</summary>
    private static string Place(Tick onset, MeterMap meter, Words w)
    {
        MetricPosition position = meter.ToPosition(onset);
        if (w.IsRussian)
        {
            return position.BeatOffset == 0
                ? $"доля {position.Beat} такта {position.Bar}"
                : $"{VariationFacts.Length(position.BeatOffset, w)} после доли {position.Beat} такта {position.Bar}";
        }

        string beat = $"beat {position.Beat} of bar {position.Bar}";
        return position.BeatOffset == 0 ? beat : $"{VariationFacts.Length(position.BeatOffset)} after {beat}";
    }

    /// <summary>
    /// "3 notes that started on a beat now start 1 eighth ahead of it: beats 2, 3 and 4 of bar 1.",
    /// when every note that moved started on a beat and now starts half that beat earlier.
    /// </summary>
    private static string? Anticipated(IReadOnlyList<NoteChange> changes, MeterMap meter, Words w)
    {
        NoteChange[] moved = [.. changes.Where(static change => change.OnsetChanged)];
        if (moved.Length == 0 || !moved.All(change => AheadOfBeat(change, meter)))
        {
            return null;
        }

        long[] early = [.. moved.Select(static change => change.Before.Span.Onset.Value - change.After.Span.Onset.Value).Distinct()];
        string beats = Beats(moved.Select(static change => change.Before.Span.Onset), meter, w);
        if (w.IsRussian)
        {
            string earlier = early.Length == 1 ? $"на {VariationFacts.Length(early[0], w, accusative: true)} раньше" : "на полдоли раньше";
            string notes = moved.Length == 1
                ? "1 нота, которая начиналась на доле, теперь начинается"
                : $"{w.Count(moved.Length, "", "", "нота", "ноты", "нот")}, которые начинались на доле, теперь начинаются";
            return $"{notes} {earlier}: {beats}.";
        }

        string ahead = early.Length == 1 ? $"{VariationFacts.Length(early[0])} ahead of it" : "half a beat ahead of it";
        string english = moved.Length == 1 ? "1 note that started on a beat now starts" : $"{moved.Length} notes that started on a beat now start";
        return $"{english} {ahead}: {beats}.";
    }

    private static bool AheadOfBeat(NoteChange change, MeterMap meter)
    {
        Tick onset = change.Before.Span.Onset;
        long beat = meter.SignatureAt(onset).BeatTicks;
        return meter.ToPosition(onset).BeatOffset == 0 && beat % 2 == 0 && change.After.Span.Onset.Value == onset.Value - (beat / 2);
    }

    /// <summary>
    /// "2 pairs of equal notes become long–short, 3 sixteenths then 1 sixteenth, starting on beats 1
    /// and 3 of bar 1.", when every note that changed is in a pair a length apart: the first now
    /// sounds half that length longer, and the second starts half that length later.
    /// </summary>
    private static string? Dotted(IReadOnlyList<NoteChange> changes, MeterMap meter, Words w)
    {
        NoteChange[] changed = [.. changes.Where(static change => change.OnsetChanged || change.DurationChanged)];
        NoteChange[] firsts = [.. changed.Where(static change => !change.OnsetChanged && change.After.Span.Duration > change.Before.Span.Duration)];
        NoteChange[] seconds = [.. changed.Where(static change => change.After.Span.Onset > change.Before.Span.Onset && change.After.Span.Duration <= change.Before.Span.Duration)];
        if (firsts.Length == 0 || firsts.Length + seconds.Length != changed.Length)
        {
            return null;
        }

        static long Grown(NoteChange first) => first.After.Span.Duration - first.Before.Span.Duration;
        static long Shift(NoteChange second) => second.After.Span.Onset.Value - second.Before.Span.Onset.Value;
        bool paired = firsts.All(first => seconds.Any(second => Shift(second) == Grown(first) && second.Before.Span.Onset.Value == first.Before.Span.Onset.Value + (2 * Grown(first))))
            && seconds.All(second => firsts.Any(first => Grown(first) == Shift(second) && first.Before.Span.Onset.Value == second.Before.Span.Onset.Value - (2 * Shift(second))));
        if (!paired)
        {
            return null;
        }

        Tick[] starts = [.. firsts.Select(static change => change.Before.Span.Onset).Distinct().Order()];
        long[] lengths = [.. firsts.Select(static change => 2 * Grown(change)).Distinct()];
        if (w.IsRussian)
        {
            string russianShape = lengths.Length == 1
                ? $"{VariationFacts.Length(lengths[0] * 3 / 2, w)}, затем {VariationFacts.Length(lengths[0] / 2, w)}"
                : "первая нота каждой пары втрое длиннее второй";
            string become = Words.RussianForm(starts.Length, "становится", "становятся", "становятся");
            return $"{w.Count(starts.Length, "", "", "пара", "пары", "пар")} равных нот {become} «долгая–короткая»: {russianShape}; начало: {Beats(starts, meter, w)}.";
        }

        string shape = lengths.Length == 1
            ? $"{VariationFacts.Length(lengths[0] * 3 / 2)} then {VariationFacts.Length(lengths[0] / 2)}"
            : "the first of each pair 3 times as long as the second";
        string pairs = starts.Length == 1 ? "1 pair of equal notes becomes" : $"{starts.Length} pairs of equal notes become";
        return $"{pairs} long–short, {shape}, starting on {Beats(starts, meter, w)}.";
    }

    /// <summary>
    /// "4 of 6 melody intervals are 1 scale step wider in the key's scale, keeping their direction.",
    /// when the melody kept its notes and every interval that changed size in scale steps grew or
    /// shrank by the same number of steps without turning.
    /// </summary>
    private static string? Reshaped(MotifTexture before, MotifTexture after, MusicalKey? key, Words w)
    {
        if (key is not { } scale || ScaleIntervals(before, after, scale) is not { } intervals)
        {
            return null;
        }

        (int From, int To)[] changed = [.. intervals.Where(static interval => interval.From != interval.To)];
        if (changed.Length == 0 || changed.Any(static interval => Math.Sign(interval.From) != Math.Sign(interval.To)))
        {
            return null;
        }

        int[] growth = [.. changed.Select(static interval => Math.Abs(interval.To) - Math.Abs(interval.From)).Distinct()];
        if (growth.Length != 1 || growth[0] == 0)
        {
            return null;
        }

        if (w.IsRussian)
        {
            string russianWhich = changed.Length == intervals.Length
                ? intervals.Length == 1 ? "Интервал мелодии стал" : $"Все {intervals.Length} {Words.RussianForm(intervals.Length, "интервал", "интервала", "интервалов")} мелодии стали"
                : $"{changed.Length} {w.OutOf(intervals.Length, "", "", "интервала мелодии", "интервалов мелодии")} {Words.RussianForm(changed.Length, "стал", "стали", "стали")}";
            return $"{russianWhich} {(growth[0] > 0 ? "шире" : "уже")} на {w.Count(Math.Abs(growth[0]), "", "", "ступень", "ступени", "ступеней")} гаммы тональности, сохранив направление.";
        }

        string steps = Math.Abs(growth[0]) == 1 ? "1 scale step" : $"{Math.Abs(growth[0])} scale steps";
        string which = changed.Length == intervals.Length
            ? intervals.Length == 1 ? "The melody interval is" : $"All {intervals.Length} melody intervals are"
            : $"{changed.Length} of {intervals.Length} melody intervals {(changed.Length == 1 ? "is" : "are")}";
        return $"{which} {steps} {(growth[0] > 0 ? "wider" : "narrower")} in the key's scale, keeping {(changed.Length == 1 ? "its" : "their")} direction.";
    }

    /// <summary>
    /// "The melody is mirrored around its first note, E5: every interval turns the other way, keeping
    /// its size in steps of the key's scale.", or "2 of 6 melody intervals turn the other way, …",
    /// when the melody kept its notes and every interval that changed is the old one turned over, in
    /// semitones or else in scale steps.
    /// </summary>
    private static string? Turned(MotifTexture before, MotifTexture after, MusicalKey? key, Words w)
    {
        if (SemitoneIntervals(before, after) is not { } semitones)
        {
            return null;
        }

        string? size = Flips(semitones) ? w.T("semitones", "полутонах") : null;
        (int From, int To)[] intervals = semitones;
        if (size is null && key is { } scale && ScaleIntervals(before, after, scale) is { } steps && Flips(steps))
        {
            size = w.T("steps of the key's scale", "ступенях гаммы тональности");
            intervals = steps;
        }

        if (size is null)
        {
            return null;
        }

        int moving = intervals.Count(static interval => interval.From != 0);
        int turned = intervals.Count(static interval => interval.From != 0 && interval.To == -interval.From);
        if (turned == moving && before.Melody[0].Pitch == after.Melody[0].Pitch)
        {
            return w.T(
                $"The melody is mirrored around its first note, {before.Melody[0].Pitch}: every interval turns the other way, keeping its size in {size}.",
                $"Мелодия отражена относительно первой ноты, {before.Melody[0].Pitch}: каждый интервал меняет направление, сохраняя размер в {size}.");
        }

        if (w.IsRussian)
        {
            return $"{turned} {w.OutOf(intervals.Length, "", "", "интервала мелодии", "интервалов мелодии")} {Words.RussianForm(turned, "меняет", "меняют", "меняют")} направление, сохраняя размер в {size}.";
        }

        string which = turned == 1 ? $"1 of {intervals.Length} melody intervals turns" : $"{turned} of {intervals.Length} melody intervals turn";
        return $"{which} the other way, keeping {(turned == 1 ? "its" : "their")} size in {size}.";
    }

    /// <summary>Whether at least one interval turned over and every other interval stayed as it was.</summary>
    private static bool Flips((int From, int To)[] intervals) =>
        intervals.Any(static interval => interval.From != 0 && interval.To == -interval.From)
        && intervals.All(static interval => interval.To == interval.From || interval.To == -interval.From);

    /// <summary>The melody's intervals in semitones before and after, or null when the melody's notes changed or it has fewer than two.</summary>
    private static (int From, int To)[]? SemitoneIntervals(MotifTexture before, MotifTexture after) =>
        SameMelody(before, after) ? Pair(before, after, static note => note.Pitch.MidiNumber) : null;

    /// <summary>The melody's intervals in steps of the key's scale before and after, or null as for <see cref="SemitoneIntervals"/>.</summary>
    private static (int From, int To)[]? ScaleIntervals(MotifTexture before, MotifTexture after, MusicalKey key) =>
        SameMelody(before, after) ? Pair(before, after, note => ScaleSteps.Locate(key, note.Pitch.MidiNumber).Step) : null;

    private static bool SameMelody(MotifTexture before, MotifTexture after) =>
        before.Melody.Count >= 2
        && before.Melody.Select(static note => note.Id).SequenceEqual(after.Melody.Select(static note => note.Id), StringComparer.Ordinal);

    private static (int From, int To)[] Pair(MotifTexture before, MotifTexture after, Func<Note, int> measure)
    {
        int[] from = [.. before.Melody.Select(measure)];
        int[] to = [.. after.Melody.Select(measure)];
        return [.. from.Zip(from.Skip(1), static (a, b) => b - a).Zip(to.Zip(to.Skip(1), static (a, b) => b - a))];
    }

    /// <summary>
    /// "beat 2 of bar 1", "beats 2, 3 and 4 of bar 1 and beat 1 of bar 2", from the meter, in order;
    /// in a bar with a position off the beat, each is named on its own: "beat 1 of bar 1 and 1 eighth
    /// after beat 1 of bar 1".
    /// </summary>
    public static string Beats(IEnumerable<Tick> onsets, MeterMap meter) => Beats(onsets, meter, Words.English);

    /// <summary>The beats in a language: "beats 2 and 3 of bar 1"; "доли 2 и 3 такта 1".</summary>
    public static string Beats(IEnumerable<Tick> onsets, MeterMap meter, Words words)
    {
        ArgumentNullException.ThrowIfNull(onsets);
        ArgumentNullException.ThrowIfNull(meter);
        Words w = words;
        string[] bars = [.. onsets
            .Distinct()
            .Order()
            .Select(meter.ToPosition)
            .GroupBy(static position => position.Bar)
            .SelectMany(bar =>
            {
                if (bar.Any(static position => position.BeatOffset != 0))
                {
                    return bar.Select(position => position.BeatOffset == 0
                        ? w.T($"beat {position.Beat} of bar {position.Bar}", $"доля {position.Beat} такта {position.Bar}")
                        : w.T(
                            $"{VariationFacts.Length(position.BeatOffset)} after beat {position.Beat} of bar {position.Bar}",
                            $"{VariationFacts.Length(position.BeatOffset, w)} после доли {position.Beat} такта {position.Bar}"));
                }

                int[] beats = [.. bar.Select(static position => position.Beat)];
                return [beats.Length == 1
                    ? w.T($"beat {beats[0]} of bar {bar.Key}", $"доля {beats[0]} такта {bar.Key}")
                    : w.T($"beats {List(beats.Select(static beat => $"{beat}"), w)} of bar {bar.Key}", $"доли {List(beats.Select(static beat => $"{beat}"), w)} такта {bar.Key}")];
            })];
        return List(bars, w);
    }

    private static string List(IEnumerable<string> items, Words w)
    {
        string[] all = [.. items];
        return all.Length <= 1 ? string.Concat(all) : w.Joined(all);
    }
}