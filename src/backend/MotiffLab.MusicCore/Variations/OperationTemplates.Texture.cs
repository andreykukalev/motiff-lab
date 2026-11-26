using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

internal static partial class OperationTemplates
{
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
            return Handed(handed, w);
        }

        if (handed.Length > 0 || moved.Any(static change => (change.After.Pitch.MidiNumber - change.Before.Pitch.MidiNumber) % 12 != 0))
        {
            return null;
        }

        int up = moved.Count(static change => change.After.Pitch.MidiNumber > change.Before.Pitch.MidiNumber);
        static (long, long, int) Shape(Note note) => (note.Span.Onset.Value, note.Span.Duration, note.Pitch.MidiNumber);
        return Transposed(moved, up, before.Notes.Count, w)
            ?? (before.Melody.Select(Shape).SequenceEqual(after.Melody.Select(Shape)) ? Rechorded(moved.Length, up, before, after, key, meter, w) : null);
    }

    /// <summary>V3, the whole passage an octave higher or lower: all <paramref name="count"/> notes moved by the same octave.</summary>
    private static string? Transposed(NoteChange[] moved, int up, int count, Words w)
    {
        if (moved.Length != count || !moved.All(change => change.After.Pitch.MidiNumber - change.Before.Pitch.MidiNumber == (up > 0 ? 12 : -12)))
        {
            return null;
        }

        return w.IsRussian
            ? $"{NotesIn(moved.Length, w)} {Words.RussianForm(moved.Length, "звучит", "звучат", "звучат")} на октаву {(up > 0 ? "выше" : "ниже")}; названия звуков и ритм не изменились."
            : $"{moved.Length} {(moved.Length == 1 ? "note moves" : "notes move")} an octave {(up > 0 ? "higher" : "lower")}; their note names and the rhythm are unchanged.";
    }

    /// <summary>V3, notes to the other hand: every note that changed hand went to the same one.</summary>
    private static string? Handed(NoteChange[] handed, Words w)
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

    /// <summary>V3, chord notes moved by octaves under an unchanged melody: <paramref name="moved"/> of them, <paramref name="up"/> up and the rest down.</summary>
    private static string Rechorded(int moved, int up, MotifTexture before, MotifTexture after, MusicalKey? key, MeterMap meter, Words w)
    {
        int down = moved - up;
        MusicalKey spelling = key ?? new MusicalKey(0, KeyMode.Major);
        var span = TickSpan.FromBounds(before.Notes.Min(static note => note.Span.Onset), before.Notes.Max(static note => note.Span.End));
        string[] was = [.. HarmonyAnalyzer.FindChords(before.Notes, meter, span, key).Select(chord => chord.Symbol(spelling))];
        string[] now = [.. HarmonyAnalyzer.FindChords(after.Notes, meter, span, key).Select(chord => chord.Symbol(spelling))];
        string how = up > 0 && down > 0
            ? w.T($" ({up} up, {down} down)", $" ({up} вверх, {down} вниз)")
            : w.T(up > 0 ? " up" : " down", up > 0 ? " вверх" : " вниз");
        string octaves = w.IsRussian
            ? $"{NotesIn(moved, w)} {Words.RussianForm(moved, "переходит", "переходят", "переходят")} на октаву{how}"
            : $"{moved} chord {(moved == 1 ? "note moves" : "notes move")} by an octave{how}";
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
        Note[] removed = Added(after, before);
        Note[] added = Added(before, after);
        if (removed.Length == 0 && changes.All(static change => !change.PitchChanged && !change.OnsetChanged && !change.DurationChanged))
        {
            return null;
        }

        int roots = added.Count(static note => note.Id.EndsWith("~b1", StringComparison.Ordinal));
        int rounded = changes.Count(static change => change.OnsetChanged || change.DurationChanged);
        int folded = changes.Count(static change => change.PitchChanged);
        string list = string.Join("; ", SimplifiedParts(removed.Length, roots, rounded, folded, w));
        return $"{char.ToUpperInvariant(list[0])}{list[1..]}.";
    }

    /// <summary>V6's clauses: the notes left out, the roots the bass plays, the notes moved to the nearest eighth and the melody notes folded by an octave.</summary>
    private static List<string> SimplifiedParts(int removed, int roots, int rounded, int folded, Words w)
    {
        var parts = new List<string>();
        if (removed > 0)
        {
            parts.Add(w.IsRussian
                ? $"{NotesIn(removed, w)} {Words.RussianForm(removed, "убрана", "убраны", "убраны")}"
                : $"{removed} {(removed == 1 ? "note is" : "notes are")} left out");
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
                ? $"{NotesIn(rounded, w)} {Words.RussianForm(rounded, "встала", "встали", "встали")} на ближайшую восьмую"
                : $"{rounded} {(rounded == 1 ? "note moves" : "notes move")} to the nearest eighth");
        }

        if (folded > 0)
        {
            parts.Add(w.IsRussian
                ? $"{NotesIn(folded, w)} мелодии {Words.RussianForm(folded, "перенесена", "перенесены", "перенесены")} на октаву, чтобы скачок был не шире октавы"
                : $"{folded} melody {(folded == 1 ? "note moves" : "notes move")} by an octave, so no leap is wider than an octave");
        }

        return parts;
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
}