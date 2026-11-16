using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Assistant.Repairs;

/// <summary>The repairs of a bar's length: a bar read too long, and two hands that do not add up to the same length.</summary>
public static partial class CheckRepairs
{
    /// <summary>
    /// A bar too long (0028 section 6: drop a doubled note, read a triplet, shorten the last note): for each hand whose notes
    /// run past the bar's end by an excess, a repeated note as long as the excess removed, three equal notes read as a
    /// triplet that saves the excess, or the last notes shortened to the bar's end; the hand's later notes in the bar move
    /// earlier with it. When both hands run over, each candidate repairs both, the first way of each paired.
    /// </summary>
    private static (IReadOnlyList<RepairCandidate>, string?) TooLong(RepairContext context)
    {
        Words w = context.Piece.Words;
        long end = context.Bar.Span.End.Value;
        Note[][] over = [.. context.Lines.Where(line => line.Max(static note => note.Span.End.Value) > end)];
        if (over.Length == 0)
        {
            return ([], w.T(
                "The notes already end within the bar; the extra length is in rests, which are not notes to correct. Compare the bar with the page.",
                "Ноты и так заканчиваются в пределах такта; лишняя длина — в паузах, а их правками не исправить. Сверьте такт со страницей."));
        }

        List<LineFix>[] fixes = [.. over.Select(line => LineFixes(context, line, line.Max(static note => note.Span.End.Value) - end))];
        if (fixes.Any(static line => line.Count == 0))
        {
            return ([], w.T(
                "No note of the bar is repeated, part of three equal notes or long enough to shorten by exactly the extra length, so no repair is safe. Compare the bar with the page.",
                "В такте нет повторённой ноты, трёх равных нот или ноты, которую можно укоротить ровно на лишнюю длину, поэтому надёжной правки нет. Сверьте такт со страницей."));
        }

        // The first hand's ways, each paired with the first way of every other hand that runs over.
        IEnumerable<LineFix[]> combined = fixes[0].Select(first => new[] { first }.Concat(fixes.Skip(1).Select(static other => other[0])).ToArray());
        List<RepairCandidate> candidates = [];
        foreach (LineFix[] together in combined)
        {
            var after = context.Notes.ToDictionary(static note => note.Id, StringComparer.Ordinal);
            foreach (LineFix fix in together)
            {
                fix.Apply(after);
            }

            string title = string.Join(w.T("; ", "; "), together.Select(static fix => fix.Title));
            string id = string.Join("+", together.Select(static fix => fix.Id));
            if (Candidate(context, id, title, context.Notes, after.Values) is { } candidate)
            {
                candidates.Add(candidate);
            }
        }

        return (candidates, w.T("No repair keeps the notes within the piano's range and the grid.", "Ни одна правка не оставляет ноты в диапазоне и на сетке."));
    }

    /// <summary>The ways to take <paramref name="excess"/> ticks out of one hand's notes in the bar, best first.</summary>
    private static List<LineFix> LineFixes(RepairContext context, Note[] line, long excess) =>
        [.. Doubled(context, line, excess), .. Triplets(context, line, excess), .. ShortenedLast(context, line, excess), .. new[] { Before(context, line, excess) }.OfType<LineFix>()];

    /// <summary>A repeated note exactly as long as the excess: the scan read it twice.</summary>
    private static IEnumerable<LineFix> Doubled(RepairContext context, Note[] line, long excess)
    {
        Words w = context.Piece.Words;
        var fixes = new List<LineFix>();
        for (int i = 1; i < line.Length; i++)
        {
            Note note = line[i], previous = line[i - 1];
            if (note.Pitch == previous.Pitch && note.Span.Duration == excess && note.Span.Onset == previous.Span.End && Single(line, note) && Single(line, previous))
            {
                Note[] later = [.. line.Where(other => other.Span.Onset > note.Span.Onset)];
                fixes.Add(new LineFix(
                    $"doubled.{context.Address(note)}",
                    w.T($"Remove the repeated {note.Pitch} ({context.Address(note)})", $"Убрать повторную {note.Pitch} ({context.Address(note)})"),
                    notes =>
                    {
                        notes.Remove(note.Id);
                        foreach (Note moved in later)
                        {
                            notes[moved.Id] = Earlier(moved, excess);
                        }
                    }));
            }
        }

        return fixes;
    }

    /// <summary>Three equal notes in a row read as a triplet: they take two thirds of their time, saving one note's length.</summary>
    private static IEnumerable<LineFix> Triplets(RepairContext context, Note[] line, long excess)
    {
        Words w = context.Piece.Words;
        var fixes = new List<LineFix>();
        for (int i = 0; i + 2 < line.Length; i++)
        {
            Note[] three = line[i..(i + 3)];
            long length = three[0].Span.Duration;
            bool even = three.All(note => note.Span.Duration == length && Single(line, note)) && three[1].Span.Onset == three[0].Span.End && three[2].Span.Onset == three[1].Span.End;
            if (even && length == excess && length % 3 == 0)
            {
                Note[] later = [.. line.Where(other => other.Span.Onset > three[2].Span.Onset)];
                long part = length * 2 / 3;
                fixes.Add(new LineFix(
                    $"triplet.{context.Address(three[0])}",
                    w.T($"Read {string.Join(" ", three.Select(static note => note.Pitch))} as a triplet", $"Считать {string.Join(" ", three.Select(static note => note.Pitch))} триолью"),
                    notes =>
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            notes[three[k].Id] = three[k] with { Span = new(new(three[0].Span.Onset.Value + (k * part)), part), Attribution = Attribution.UserEdited };
                        }

                        foreach (Note moved in later)
                        {
                            notes[moved.Id] = Earlier(moved, excess);
                        }
                    }));
            }
        }

        return fixes;
    }

    /// <summary>The last notes shortened to the bar's end, when every note of the hand starts within the bar.</summary>
    private static IEnumerable<LineFix> ShortenedLast(RepairContext context, Note[] line, long excess)
    {
        Words w = context.Piece.Words;
        long end = context.Bar.Span.End.Value;
        Note[] last = [.. line.Where(note => note.Span.End.Value > end)];
        if (line.All(note => note.Span.Onset.Value < end) && last.All(note => note.Span.End.Value - excess > note.Span.Onset.Value))
        {
            yield return new LineFix(
                $"shorten.{context.Address(last[0])}",
                w.T($"Shorten {string.Join(" ", last.Select(static note => note.Pitch))} to the bar's end", $"Укоротить {string.Join(" ", last.Select(static note => note.Pitch))} до конца такта"),
                notes =>
                {
                    foreach (Note note in last)
                    {
                        notes[note.Id] = Lasting(note, end - note.Span.Onset.Value);
                    }
                });
        }
    }

    /// <summary>
    /// The note before the overflow read a step too long (a dot where there is none): the last single note that starts
    /// within the bar, is longer than the excess and has notes after it, shortened by the excess, the notes after it moved
    /// earlier.
    /// </summary>
    private static LineFix? Before(RepairContext context, Note[] line, long excess)
    {
        Words w = context.Piece.Words;
        long end = context.Bar.Span.End.Value;
        Note? note = line.LastOrDefault(note => note.Span.Onset.Value < end && note.Span.Duration > excess && Single(line, note) && line.Any(other => other.Span.Onset > note.Span.Onset));
        if (note is null)
        {
            return null;
        }

        Note[] later = [.. line.Where(other => other.Span.Onset > note.Span.Onset)];
        string by = NoteAddressBook.Fraction(excess);
        return new LineFix(
            $"before.{context.Address(note)}",
            w.T($"Shorten {note.Pitch} ({context.Address(note)}) by {by} and move what follows earlier", $"Укоротить {note.Pitch} ({context.Address(note)}) на {by} и сдвинуть следующие ноты раньше"),
            notes =>
            {
                notes[note.Id] = Lasting(note, note.Span.Duration - excess);
                foreach (Note moved in later)
                {
                    notes[moved.Id] = Earlier(moved, excess);
                }
            });
    }

    /// <summary>
    /// Two hands of different length (0028 section 6): the shorter hand's last notes lengthened to where the other hand
    /// ends, as a dot read wrong when the gap is half their length. A gap that is a rest needs no correction.
    /// </summary>
    private static (IReadOnlyList<RepairCandidate>, string?) StavesDiffer(RepairContext context)
    {
        Words w = context.Piece.Words;
        if (context.Lines.Count < 2)
        {
            return ([], w.T("The bar has notes in one hand only, so there is no other hand to match.", "В такте ноты только одной руки, сравнивать не с чем."));
        }

        long reach = Math.Min(context.Lines.Max(static line => line.Max(static note => note.Span.End.Value)), context.Measure.End.Value);
        var candidates = new List<RepairCandidate>();
        foreach (Note[] line in context.Lines.Where(line => line.Max(static note => note.Span.End.Value) < reach))
        {
            long start = line.Max(static note => note.Span.Onset.Value);
            Note[] last = [.. line.Where(note => note.Span.Onset.Value == start)];
            long length = last.Max(static note => note.Span.Duration), gap = reach - (start + length);
            bool dot = gap * 2 == length;
            string pitches = string.Join(" ", last.Select(static note => note.Pitch));
            string title = dot
                ? w.T($"Dot {pitches} ({context.Address(last[0])})", $"Поставить точку у {pitches} ({context.Address(last[0])})")
                : w.T($"Hold {pitches} to the end of the other hand", $"Держать {pitches} до конца другой руки");
            if (Candidate(context, $"{(dot ? "dot" : "hold")}.{context.Address(last[0])}", title, context.Notes, context.Notes.Select(note => last.Contains(note) ? Lasting(note, reach - start) : note)) is { } candidate)
            {
                candidates.Add(candidate);
            }
        }

        return (candidates, w.T(
            "Every hand already reaches as far as its notes allow; the gap is a rest, which needs no correction.",
            "Каждая рука и так доходит до конца своих нот; разница — пауза, её исправлять не нужно."));
    }

    /// <summary>Whether no other note of the line starts with this one: a chord is not read as a doubled note or a triplet.</summary>
    private static bool Single(Note[] line, Note note) => line.Count(other => other.Span.Onset == note.Span.Onset) == 1;

    /// <summary>One way to repair one hand: its id part, its title, and what it does to the bar's notes by id.</summary>
    private sealed record LineFix(string Id, string Title, Action<Dictionary<string, Note>> Apply);
}