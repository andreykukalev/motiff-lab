using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;

namespace MotiffLab.MusicCore.Variations;

public static partial class OperationTemplates
{
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

        return ReshapedText(changed.Length, intervals.Length, growth[0], w);
    }

    /// <summary>V9's sentence: <paramref name="changed"/> of <paramref name="intervals"/> melody intervals, each <paramref name="growth"/> scale steps wider (narrower when negative).</summary>
    private static string ReshapedText(int changed, int intervals, int growth, Words w)
    {
        if (w.IsRussian)
        {
            string russianWhich = changed == intervals
                ? intervals == 1 ? "Интервал мелодии стал" : $"Все {intervals} {Words.RussianForm(intervals, "интервал", "интервала", "интервалов")} мелодии стали"
                : $"{changed} {w.OutOf(intervals, "", "", "интервала мелодии", "интервалов мелодии")} {Words.RussianForm(changed, "стал", "стали", "стали")}";
            return $"{russianWhich} {(growth > 0 ? "шире" : "уже")} на {w.Count(Math.Abs(growth), "", "", "ступень", "ступени", "ступеней")} гаммы тональности, сохранив направление.";
        }

        string steps = Math.Abs(growth) == 1 ? "1 scale step" : $"{Math.Abs(growth)} scale steps";
        string which = changed == intervals
            ? intervals == 1 ? "The melody interval is" : $"All {intervals} melody intervals are"
            : $"{changed} of {intervals} melody intervals {(changed == 1 ? "is" : "are")}";
        return $"{which} {steps} {(growth > 0 ? "wider" : "narrower")} in the key's scale, keeping {(changed == 1 ? "its" : "their")} direction.";
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
    /// V11: "Passing notes are added after 3 melody notes (D5 after C5, …), in the second half of each; every main note
    /// keeps its pitch and onset.", when notes were only added after shortened melody notes.
    /// </summary>
    private static string? Filled(MotifTexture before, MotifTexture after, MusicalKey? key, Words w)
    {
        Note[] added = Added(before, after);
        if (added.Length == 0 || Added(after, before).Length > 0)
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

        bool passing = figures.All(figure => figure.Added.All(note => Between(note, figure.Main, figure.Next!)));
        MusicalKey spelling = key ?? new MusicalKey(0, KeyMode.Major);
        string Figure((Note Main, Note? Next, Note[] Added) figure) => w.T(
            $"{w.Joined([.. figure.Added.Select(note => spelling.Spell(note.Pitch.MidiNumber))])} after {spelling.Spell(figure.Main.Pitch.MidiNumber)}",
            $"{w.Joined([.. figure.Added.Select(note => spelling.Spell(note.Pitch.MidiNumber))])} после {spelling.Spell(figure.Main.Pitch.MidiNumber)}");
        string list = string.Join(", ", figures.Take(4).Select(Figure)) + (figures.Count > 4 ? ", …" : string.Empty);
        return FilledText(passing, figures.Count, list, w);
    }

    /// <summary>Whether <paramref name="note"/> lies strictly between the pitches of <paramref name="main"/> and <paramref name="next"/>.</summary>
    private static bool Between(Note note, Note main, Note next) => Math.Min(main.Pitch.MidiNumber, next.Pitch.MidiNumber) < note.Pitch.MidiNumber
        && note.Pitch.MidiNumber < Math.Max(main.Pitch.MidiNumber, next.Pitch.MidiNumber);

    /// <summary>V11's sentence: passing or neighbour notes after <paramref name="count"/> melody notes, listed in <paramref name="list"/>.</summary>
    private static string FilledText(bool passing, int count, string list, Words w) => w.IsRussian
        ? $"{(passing ? "Проходящие" : "Вспомогательные")} ноты добавлены после {count} {Words.RussianForm(count, "ноты", "нот", "нот")} мелодии ({list}), во второй половине каждой; основные ноты сохраняют высоту и начало."
        : $"{(passing ? "Passing" : "Neighbour")} notes are added after {(count == 1 ? "1 melody note" : $"{count} melody notes")} ({list}), in the second half of {(count == 1 ? "it" : "each")}; every main note keeps its pitch and onset.";

    /// <summary>
    /// V14: "A mordent on 2 long notes (E5 and A4): the note, the note above and the note again, 1 thirty-second each,
    /// then the note held; every other note is unchanged.", when every ornamented note has the same figure.
    /// </summary>
    private static string? Ornamented(MotifTexture before, MotifTexture after, MusicalKey? key, Words w)
    {
        Note[] added = Added(before, after);
        if (added.Length == 0 || Added(after, before).Length > 0)
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

        string?[] kinds = [.. figures.Select(figure => OrnamentKind(figure.Shape)).Distinct()];
        if (kinds.Length != 1 || kinds[0] is not { } kind)
        {
            return null;
        }

        MusicalKey spelling = key ?? new MusicalKey(0, KeyMode.Major);
        string names = w.Joined([.. figures.Select(figure => spelling.Spell(figure.Main.Pitch.MidiNumber))]);
        return OrnamentText(kind, figures.Count, names, VariationFacts.Length(figures[0].Step, w), w);
    }

    /// <summary>The ornament a figure's shape is, by the sign of each of its notes from the main note; null for none.</summary>
    private static string? OrnamentKind(int[] shape) => shape switch
    {
        [0, 1, 0] => "mordent",
        [0, -1, 0] => "lower",
        [1, 0, -1, 0] => "turn",
        [1, 0] => "graceAbove",
        [-1, 0] => "graceBelow",
        _ when shape.Length >= 5 && shape[^1] == 0 && shape[..^1].Select((sign, i) => sign == (i % 2 == 0 ? 0 : 1)).All(static same => same) => "trill",
        _ => null,
    };

    /// <summary>V14's sentence for <paramref name="count"/> notes ornamented alike.</summary>
    private static string OrnamentText(string kind, int count, string names, string step, Words w)
    {
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
}