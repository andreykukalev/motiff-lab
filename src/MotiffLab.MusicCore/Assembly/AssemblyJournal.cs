using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assembly;

/// <summary>A step of the journal as the journal sheet lists it: «№1 · 9 нот из Кавера → такт 15» and what it did.</summary>
/// <param name="SourceId">The source a carry came from; null for a note change or a transposition.</param>
/// <param name="Title">«№1 · 9 нот из Кавера → такт 15».</param>
/// <param name="Detail">«положил 9, убрал 7», or why it is not applied.</param>
/// <param name="Span">Where the step's music lies in the notes, to show it; null when it is not applied.</param>
public sealed record JournalRow(int Number, string? SourceId, string Title, string Detail, bool Applied, TickSpan? Span);

/// <summary>One line of the apply dialog or of a carry's card: ✓ for what stays as said, ! for what changes with it.</summary>
public sealed record JournalLine(SheetMark Mark, string Label, string Text);

/// <summary>
/// The journal said in musical words (decision record 0024, sections 2, 6 and 7): its rows, the card of a carry with what
/// it put in, took out and what leans on it, and what «Сделать нотами» will change.
/// </summary>
public static class AssemblyJournal
{
    /// <summary>The journal's rows, in order.</summary>
    public static IReadOnlyList<JournalRow> Rows(AssemblyWork work, Words words, NoteNaming naming)
    {
        ArgumentNullException.ThrowIfNull(work);
        AssemblyNames names = work.NamesIn(words);
        Assembled result = work.Result;
        SourceMusic music = result.Music;
        List<JournalRow> rows = [];
        foreach (AssemblyStep step in work.Steps)
        {
            StepOutcome? outcome = result.OutcomeOf(step.Number);
            bool applied = outcome?.Applied ?? false;
            TickSpan? span = applied ? outcome!.Span : null;
            string where = span is { } at ? " → " + CarrySheet.BarsText(music, at.Onset, at.End, words) : "";
            string number = words.T($"No. {step.Number}", $"№{step.Number}");
            switch (step)
            {
                case CarryStep carry:
                    int put = outcome?.Put.Count ?? 0;
                    int removed = outcome?.Removed.Count ?? 0;
                    rows.Add(new JournalRow(
                        step.Number,
                        carry.SourceId,
                        $"{number} · {NotesFrom(work, carry, names, words)}{where}",
                        applied
                            ? words.T($"put in {put}, took out {removed}", $"положил {put}, убрал {removed}")
                            : Lost(words),
                        applied,
                        span));
                    break;
                case NoteStep note:
                    rows.Add(new JournalRow(
                        step.Number,
                        null,
                        $"{number} · {words.T("Note change", "Правка ноты")}",
                        applied ? NoteChange(work, note, words, naming) : Lost(words),
                        applied,
                        span));
                    break;
                case TransposeStep transpose:
                    rows.Add(new JournalRow(
                        step.Number,
                        null,
                        $"{number} · {words.T("Transposition", "Транспонирование")}{where}",
                        applied ? AssemblyWords.Interval(transpose.Semitones, words) : Lost(words),
                        applied,
                        span));
                    break;
            }
        }

        return rows;
    }

    /// <summary>
    /// The card of a carry, from the same lines as the carry sheet: what it put in and took out (✓), and what would go
    /// with it if it were returned (!).
    /// </summary>
    public static IReadOnlyList<JournalLine> Card(AssemblyWork work, int number, Words words)
    {
        ArgumentNullException.ThrowIfNull(work);
        StepOutcome? outcome = work.Result.OutcomeOf(number);
        AssemblyNames names = work.NamesIn(words);
        List<JournalLine> lines = [];
        if (outcome is { Applied: true })
        {
            string put = words.Count(outcome.Put.Count, "note", "notes", "ноту", "ноты", "нот");
            int fromBase = outcome.Removed.Count(removed => removed.From == 0);
            int fromSteps = outcome.Removed.Count - fromBase;
            string took = outcome.Removed.Count == 0
                ? words.T("nothing", "ничего")
                : fromSteps == 0
                    ? words.Count(fromBase, "earlier note", "earlier notes", "прежнюю ноту", "прежние ноты", "прежних нот")
                    : words.Count(outcome.Removed.Count, "note", "notes", "ноту", "ноты", "нот");
            lines.Add(new JournalLine(SheetMark.Same, words.T("Put in", "Положил"), words.T($"{put}. Took out: {took}.", $"{put}. Убрал: {took}.")));
        }

        RevertPlan plan = work.PlanRevert(number);
        if (plan.Dependents.Count == 0)
        {
            lines.Add(new JournalLine(SheetMark.Same, words.T("Leans on it", "Зависит"), words.T("nothing else changes if it is returned.", "ничего, вернуть можно отдельно.")));
        }
        else
        {
            lines.Add(new JournalLine(SheetMark.Changes, words.T("Leans on it", "Зависит"), words.T(
                $"{Steps(plan.Dependents, work, names, words)} would go with it.",
                $"вместе с ним уйдёт {Steps(plan.Dependents, work, names, words)}.")));
        }

        return lines;
    }

    /// <summary>What returning a step will do, in the sheet's lines: what goes with it (!), what stays (✓) and the length.</summary>
    public static IReadOnlyList<JournalLine> Revert(AssemblyWork work, RevertPlan plan, Words words)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(plan);
        AssemblyNames names = work.NamesIn(words);
        List<JournalLine> lines = [];
        if (plan.Dependents.Count > 0)
        {
            lines.Add(new JournalLine(SheetMark.Changes, words.T("Goes with it", "Уйдёт вместе с ним"), words.T(
                $"{Steps(plan.Dependents, work, names, words)}. They can be made again after: the notes before come back.",
                $"{Steps(plan.Dependents, work, names, words)}. Их можно сделать снова: прежние ноты вернутся на место.")));
        }

        if (plan.Staying.Count > 0)
        {
            lines.Add(new JournalLine(SheetMark.Same, words.T("Stays", "Останется"), Steps(plan.Staying, work, names, words) + "."));
        }

        if (plan.Delta != 0)
        {
            TimeSignature signature = work.Result.Music.Bars.Count > 0 ? work.Result.Music.Bars[0].Signature : TimeSignature.CommonTime;
            string length = AssemblyWords.Length(Math.Abs(plan.Delta), signature, words);
            lines.Add(new JournalLine(SheetMark.Changes, words.T("Length", "Длина"), plan.Delta < 0
                ? words.T($"the notes become {length} shorter.", $"ноты станут на {length} короче.")
                : words.T($"the notes become {length} longer.", $"ноты станут на {length} длиннее.")));
        }

        return lines;
    }

    /// <summary>The bars «Сделать нотами» will change, in the notes as they will be: «такты 9–10, 15–16».</summary>
    public static string ChangedBars(AssemblyWork work, Words words)
    {
        ArgumentNullException.ThrowIfNull(work);
        SourceMusic music = work.Result.Music;
        List<(int First, int Last)> runs = [];
        foreach (TickSpan span in work.Result.Steps.Where(step => step.Applied && step.Span is not null).Select(step => step.Span!.Value).OrderBy(span => span.Onset))
        {
            int first = music.BarAt(span.Onset).Number;
            int last = music.BarAt(new Tick(Math.Max(span.Onset.Value, span.End.Value - 1))).Number;
            if (runs.Count > 0 && first <= runs[^1].Last + 1)
            {
                runs[^1] = (runs[^1].First, Math.Max(runs[^1].Last, last));
            }
            else
            {
                runs.Add((first, last));
            }
        }

        if (runs.Count == 0)
        {
            return words.T("nothing", "ничего");
        }

        string list = string.Join(", ", runs.Select(run => run.First == run.Last ? $"{run.First}" : $"{run.First}–{run.Last}"));
        return runs.Count == 1 && runs[0].First == runs[0].Last ? words.T($"bar {list}", $"такт {list}") : words.T($"bars {list}", $"такты {list}");
    }

    /// <summary>The apply dialog's lines: one for each carry and change still applied, with where it came from.</summary>
    public static IReadOnlyList<JournalLine> Apply(AssemblyWork work, Words words)
    {
        ArgumentNullException.ThrowIfNull(work);
        AssemblyNames names = work.NamesIn(words);
        Assembled result = work.Result;
        SourceMusic music = result.Music;
        TimeSignature signature = music.Bars.Count > 0 ? music.Bars[0].Signature : TimeSignature.CommonTime;
        List<JournalLine> lines = [];
        foreach (AssemblyStep step in work.Steps)
        {
            if (result.OutcomeOf(step.Number) is not { Applied: true, Span: { } span } outcome)
            {
                continue;
            }

            string bars = Words.Capitalize(CarrySheet.BarsText(music, span.Onset, span.End, words));
            string text = step switch
            {
                CarryStep carry => words.T($"from {names.Of(carry.SourceId)}", $"из {names.GenitiveOf(carry.SourceId)}")
                    + " · " + words.T(
                        $"{words.Count(outcome.Put.Count, "note", "notes", "нота", "ноты", "нот")} in place of {outcome.Removed.Count}",
                        $"{words.Count(outcome.Put.Count, "note", "notes", "нота", "ноты", "нот")} вместо {outcome.Removed.Count}")
                    + (outcome.Delta == 0
                        ? ""
                        : outcome.Delta > 0
                            ? words.T($", the piece {AssemblyWords.Length(outcome.Delta, signature, words)} longer", $", пьеса на {AssemblyWords.Length(outcome.Delta, signature, words)} длиннее")
                            : words.T($", the piece {AssemblyWords.Length(-outcome.Delta, signature, words)} shorter", $", пьеса на {AssemblyWords.Length(-outcome.Delta, signature, words)} короче")),
                NoteStep => words.T("a note change", "правка ноты"),
                TransposeStep transpose => AssemblyWords.Interval(transpose.Semitones, words),
                _ => "",
            };
            lines.Add(new JournalLine(SheetMark.Same, bars, text));
        }

        return lines;
    }

    /// <summary>«9 нот из Кавера», for a carry; for one that keeps the notes, the notes it added once it is in the journal.</summary>
    public static string NotesFrom(AssemblyWork work, CarryStep carry, AssemblyNames names, Words words)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(carry);
        ArgumentNullException.ThrowIfNull(names);
        int count = carry.Keep && work.Steps.Contains(carry) && work.Result.OutcomeOf(carry.Number) is { } outcome
            ? outcome.Put.Count
            : work.Source(carry.SourceId)?.NotesIn(carry.From.Onset, carry.From.End).Count ?? 0;
        return words.T(
            $"{words.Count(count, "note", "notes", "нота", "ноты", "нот")} from {names.Of(carry.SourceId)}",
            $"{words.Count(count, "note", "notes", "нота", "ноты", "нот")} из {names.GenitiveOf(carry.SourceId)}");
    }

    /// <summary>«перенос №1 и 2 правки нот», for a list of step numbers.</summary>
    private static string Steps(IReadOnlyList<int> numbers, AssemblyWork work, AssemblyNames names, Words words)
    {
        List<string> parts = [];
        int[] carries = [.. numbers.Where(number => work.Steps.FirstOrDefault(step => step.Number == number) is CarryStep)];
        foreach (int number in carries)
        {
            CarryStep carry = (CarryStep)work.Steps.First(step => step.Number == number);
            parts.Add(words.T($"carry No. {number} from {names.Of(carry.SourceId)}", $"перенос №{number} из {names.GenitiveOf(carry.SourceId)}"));
        }

        int changes = numbers.Count - carries.Length;
        if (changes > 0)
        {
            parts.Add(words.Count(changes, "note change", "note changes", "правка нот", "правки нот", "правок нот"));
        }

        return words.Joined(parts);
    }

    private static string NoteChange(AssemblyWork work, NoteStep note, Words words, NoteNaming naming)
    {
        if (NoteBefore(work, note.NoteId) is not { } before)
        {
            return note.Removes ? words.T("a note removed", "нота убрана") : AssemblyWords.Interval(note.Semitones ?? 0, words);
        }

        SourceMusic music = work.Result.Music;
        int fifths = music.Keys.LastOrDefault(key => key.At <= before.Span.Onset)?.Fifths ?? 0;
        string name = NoteNames.Name(before.Pitch.MidiNumber, Math.Clamp(fifths, -7, 7), naming);
        Note? now = music.Notes.FirstOrDefault(kept => kept.Id == note.NoteId);
        string bar = now is null ? "" : ", " + CarrySheet.BarsText(music, now.Span.Onset, now.Span.End, words);
        return note.Removes
            ? words.T($"{name} removed{bar}", $"убрана {name}{bar}")
            : $"{name} {AssemblyWords.Interval(note.Semitones ?? 0, words)}{bar}";
    }

    /// <summary>A note as it was before any change: in the base, or in the source a carry took it from.</summary>
    private static Note? NoteBefore(AssemblyWork work, string id)
    {
        if (work.Base.Notes.FirstOrDefault(note => note.Id == id) is { } inBase)
        {
            return inBase;
        }

        int dot = id.IndexOf('.', StringComparison.Ordinal);
        if (id.Length < 3 || id[0] != 'c' || dot < 2 || !int.TryParse(id.AsSpan(1, dot - 1), out int number)
            || work.Steps.FirstOrDefault(step => step.Number == number) is not CarryStep carry
            || work.Source(carry.SourceId)?.Notes.FirstOrDefault(note => note.Id == id[(dot + 1)..]) is not { } inSource)
        {
            return null;
        }

        return inSource with { Pitch = new Pitch(Math.Clamp(inSource.Pitch.MidiNumber + carry.Transpose, 0, Pitch.MaxMidiNumber)) };
    }

    private static string Lost(Words words) => words.T("not applied: a note it held on to is gone", "не применён: нота, за которую он держался, ушла");
}