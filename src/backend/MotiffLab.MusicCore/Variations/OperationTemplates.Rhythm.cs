using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

internal static partial class OperationTemplates
{
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

        Note[] added = Added(before, after);
        Note[] removed = Added(after, before);
        NoteChange[] shorter = [.. changes.Where(static change => change.After.Span.Duration < change.Before.Span.Duration)];
        NoteChange[] longer = [.. changes.Where(static change => change.After.Span.Duration > change.Before.Span.Duration)];
        return added.Length > 0 || removed.Length > 0
            ? Split(added, removed, shorter, longer, w) ?? Merged(added, removed, shorter, longer, w)
            : Shortened(shorter, longer, after, w) ?? Held(shorter, longer, after, w);
    }

    /// <summary>V7, notes split into repeats: every note added is a repeat inside a shortened note at its pitch.</summary>
    private static string? Split(Note[] added, Note[] removed, NoteChange[] shorter, NoteChange[] longer, Words w)
    {
        if (added.Length == 0 || removed.Length > 0 || longer.Length > 0 || shorter.Length == 0
            || !added.All(note => shorter.Any(change => change.Before.Pitch == note.Pitch && change.Before.Span.Contains(note.Span.Onset))))
        {
            return null;
        }

        int total = shorter.Length + added.Length;
        return w.IsRussian
            ? $"{NotesIn(shorter.Length, w)} {Words.RussianForm(shorter.Length, "разбита", "разбиты", "разбиты")} на повторы той же высоты: теперь {Words.RussianForm(total, "звучит", "звучат", "звучат")} {NotesIn(total, w)} вместо {shorter.Length}."
            : $"{(shorter.Length == 1 ? "1 note is" : $"{shorter.Length} notes are")} split into repeated notes at {(shorter.Length == 1 ? "its pitch" : "their pitches")}: {total} notes now sound where {shorter.Length} did.";
    }

    /// <summary>V7, repeats merged: every note removed is a repeat inside a lengthened note at its pitch.</summary>
    private static string? Merged(Note[] added, Note[] removed, NoteChange[] shorter, NoteChange[] longer, Words w)
    {
        if (removed.Length == 0 || added.Length > 0 || shorter.Length > 0 || longer.Length == 0
            || !removed.All(note => longer.Any(change => change.After.Pitch == note.Pitch && change.After.Span.Contains(note.Span.Onset))))
        {
            return null;
        }

        int total = removed.Length + longer.Length;
        return w.IsRussian
            ? $"{NotesIn(total, w)}, {Words.RussianForm(total, "повторяющая", "повторяющие", "повторяющих")} одну высоту, {Words.RussianForm(total, "слита", "слиты", "слиты")} в {longer.Length} {Words.RussianForm(longer.Length, "ноту", "ноты", "нот")} подлиннее."
            : $"{total} repeated notes are merged into {(longer.Length == 1 ? "1 longer note" : $"{longer.Length} longer notes")} at the same {(longer.Length == 1 ? "pitch" : "pitches")}.";
    }

    /// <summary>V7, more air: only shortened notes, each now ending before the next melody note.</summary>
    private static string? Shortened(NoteChange[] shorter, NoteChange[] longer, MotifTexture after, Words w)
    {
        if (shorter.Length == 0 || longer.Length > 0)
        {
            return null;
        }

        long?[] gaps = [.. shorter.Select(change => Gap(change, after))];
        if (gaps.Any(static gap => gap is not > 0))
        {
            return null;
        }

        string rest = gaps.Distinct().Count() == 1
            ? w.T($"{VariationFacts.Length(gaps[0]!.Value)} before the next note, leaving a rest", $"за {VariationFacts.Length(gaps[0]!.Value, w, accusative: true)} до следующей ноты, оставляя паузу")
            : w.T("before the next note, leaving a rest", "раньше следующей ноты, оставляя паузу");
        return w.IsRussian
            ? $"{NotesIn(shorter.Length, w)} теперь {Words.RussianForm(shorter.Length, "заканчивается", "заканчиваются", "заканчиваются")} {rest}; каждая нота начинается там же, где раньше."
            : $"{(shorter.Length == 1 ? "1 note now ends" : $"{shorter.Length} notes now end")} {rest}; every note starts where it did.";
    }

    /// <summary>V7, rests filled: only lengthened notes, each now held to the next melody note.</summary>
    private static string? Held(NoteChange[] shorter, NoteChange[] longer, MotifTexture after, Words w)
    {
        if (longer.Length == 0 || shorter.Length > 0 || !longer.All(change => Gap(change, after) == 0))
        {
            return null;
        }

        return w.IsRussian
            ? $"{NotesIn(longer.Length, w)} теперь {Words.RussianForm(longer.Length, "тянется", "тянутся", "тянутся")} до следующей ноты, и паузы перед ними исчезают; каждая нота начинается там же, где раньше."
            : $"{(longer.Length == 1 ? "1 note is" : $"{longer.Length} notes are")} now held to the next note, so the rests go; every note starts where it did.";
    }

    /// <summary>How long after a changed note's end the next melody note starts; null when no melody note starts after it.</summary>
    private static long? Gap(NoteChange change, MotifTexture after) =>
        after.Melody.Where(note => note.Span.Onset > change.After.Span.Onset).Select(note => (long?)(note.Span.Onset.Value - change.After.Span.End.Value)).FirstOrDefault();

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
}