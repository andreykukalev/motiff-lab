using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assembly;

/// <summary>What a line of the carry sheet says: a choice to make (?), something around will change (!), or nothing changes (✓).</summary>
public enum SheetMark
{
    Choice,
    Changes,
    Same,
}

/// <summary>
/// One line of the carry sheet, always in the same order: where, length, seam, key, meter and tempo, notes; from a
/// recording, how to get the notes comes first.
/// </summary>
/// <param name="Name">"how", "where", "length", "seam", "key", "meter", "notes".</param>
/// <param name="Label">«Как получить ноты», «Куда», «Длина», «Стык», «Тональность», «Размер и темп», «Ноты».</param>
/// <param name="Text">The line in musical words, after the label.</param>
public sealed record SheetLine(string Name, SheetMark Mark, string Label, string Text);

/// <summary>
/// The carry sheet («В ноты…», decision record 0024, section 5): what a carry will do to the music around it, said
/// before it is made, in the same six lines every time, with the ghost (the player's notes as they will be).
/// </summary>
/// <param name="Title">«9 нот из Кавера».</param>
/// <param name="Route">«такты 15–16 Кавера → такт 15 нот».</param>
/// <param name="ReplaceText">The «Куда» choice for replacing: «заменить 7 нот такта 15»; null when nothing is selected in the notes.</param>
/// <param name="InsertText">The «Куда» choice for inserting at the playhead: «вставить в такт 15, долю 2».</param>
/// <param name="KeyText">The key line's choice for a transposition: «на тон вниз»; null when the keys agree or are not known.</param>
/// <param name="SuggestedTranspose">The semitones that bring the fragment into the notes' key, 0 when they agree.</param>
/// <param name="KeepText">From a recording, replacing: the «Как получить ноты» choice that keeps the notes and adds what the
/// performer plays otherwise; null otherwise.</param>
/// <param name="HeardText">The other choice: the notes as heard in the recording; null when <paramref name="KeepText"/> is.</param>
/// <param name="Ghost">The notes as they will be after the carry.</param>
/// <param name="Outcome">What the carry does in the ghost.</param>
public sealed record CarrySheet(
    string Title,
    string Route,
    string? ReplaceText,
    string InsertText,
    string? KeyText,
    int SuggestedTranspose,
    string? KeepText,
    string? HeardText,
    IReadOnlyList<SheetLine> Lines,
    Assembled Ghost,
    StepOutcome Outcome)
{
    /// <summary>
    /// The sheet for <paramref name="carry"/> made on top of <paramref name="steps"/>. <paramref name="names"/> gives a
    /// source's name in the genitive as the lines use it («Кавера»), and <paramref name="replaceable"/> and
    /// <paramref name="playhead"/> say what the «Куда» line can offer. A fragment of a recording (<paramref name="heard"/>)
    /// that replaces notes gets a first line on how to get its notes.
    /// </summary>
    public static CarrySheet Of(
        SourceMusic baseMusic,
        Func<string, SourceMusic?> sources,
        IReadOnlyList<AssemblyStep> steps,
        Assembled current,
        CarryStep carry,
        AssemblyNames names,
        AssemblyPlace? replaceable,
        Tick playhead,
        Words words,
        bool heard = false)
    {
        ArgumentNullException.ThrowIfNull(baseMusic);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(carry);
        ArgumentNullException.ThrowIfNull(names);
        SourceMusic source = sources(carry.SourceId) ?? throw new ArgumentException("The carry's source is not there.", nameof(carry));
        Assembled ghost = Assembled.Build(baseMusic, sources, [.. steps, carry]);
        StepOutcome outcome = ghost.OutcomeOf(carry.Number) ?? throw new InvalidOperationException("The carry has no outcome.");
        string sourceName = names.Of(carry.SourceId);
        string sourceGenitive = names.GenitiveOf(carry.SourceId);
        SourceMusic notes = current.Music;

        IReadOnlyList<Events.Note> carried = source.NotesIn(carry.From.Onset, carry.From.End);
        string title = words.T(
            $"{words.Count(carried.Count, "note", "notes", "нота", "ноты", "нот")} from {sourceName}",
            $"{words.Count(carried.Count, "note", "notes", "нота", "ноты", "нот")} из {sourceGenitive}");

        Tick t0 = outcome.Span?.Onset ?? Tick.Zero;
        string fromBars = BarsText(source, carry.From.Onset, carry.From.End, words);
        string route = words.T(
            $"{fromBars} of {sourceName} → {BarText(notes, t0, words)} of the notes",
            $"{fromBars} {sourceGenitive} → {BarText(notes, t0, words)} нот");

        string? replaceText = null;
        int replacedCount = 0;
        if (replaceable is not null && Place(notes, replaceable) is { } replaced)
        {
            replacedCount = notes.NotesIn(replaced.Start, replaced.End).Count;
            replaceText = words.T(
                $"replace {words.Count(replacedCount, "note", "notes", "нота", "ноты", "нот")} of {BarsText(notes, replaced.Start, replaced.End, words)}",
                $"заменить {words.Count(replacedCount, "note", "notes", "нота", "ноты", "нот")} {BarsGenitive(notes, replaced.Start, replaced.End, words)}");
        }

        (int playBar, int playBeat, _) = notes.PositionOf(playhead);
        string insertText = words.T($"insert at bar {playBar}, beat {playBeat}", $"вставить в такт {playBar}, долю {playBeat}");

        string? keepText = null, heardText = null;
        List<SheetLine> lines = [];
        if (heard && !carry.To.Inserts)
        {
            keepText = words.T("keep your notes and add what the performer plays otherwise", "оставить ваши ноты, добавив то, что исполнитель играет иначе");
            heardText = words.T("take the notes as the recording was heard", "взять ноты так, как они распознаны в записи");
            lines.Add(new("how", SheetMark.Choice, words.T("How to get the notes", "Как получить ноты"), carry.Keep ? keepText : heardText));
        }

        lines.AddRange(
        [
            new("where", SheetMark.Choice, words.T("Where", "Куда"), carry.To.Inserts ? insertText : replaceText ?? insertText),
            LengthLine(notes, outcome, t0, words),
            SeamLine(ghost, source, carry, outcome, words),
        ]);

        (SheetLine keyLine, string? keyText, int transpose) = KeyLine(source, notes, carry, outcome, sourceName, words);
        lines.Add(keyLine);
        lines.Add(MeterLine(source, notes, carry, outcome, words));
        lines.Add(NotesLine(outcome, names, words));
        return new CarrySheet(title, route, replaceText, insertText, keyText, transpose, keepText, heardText, lines, ghost, outcome);
    }

    /// <summary>Where a place lies in the notes now; null when a note it holds on to is gone.</summary>
    public static (Tick Start, Tick End)? Place(SourceMusic music, AssemblyPlace place)
    {
        ArgumentNullException.ThrowIfNull(music);
        ArgumentNullException.ThrowIfNull(place);
        if (Resolve(music, place.Start) is not { } start)
        {
            return null;
        }

        if (place.End is not { } anchor)
        {
            return (start, start);
        }

        return Resolve(music, anchor) is { } end ? (start, end < start ? start : end) : null;
    }

    private static Tick? Resolve(SourceMusic music, TimeAnchor anchor)
    {
        if (anchor.NoteId is null)
        {
            return new Tick(Math.Max(0, anchor.Offset));
        }

        Events.Note? note = music.Notes.FirstOrDefault(note => note.Id == anchor.NoteId);
        return note is null ? null : new Tick(Math.Max(0, note.Span.Onset.Value + anchor.Offset));
    }

    private static SheetLine LengthLine(SourceMusic notes, StepOutcome outcome, Tick t0, Words words)
    {
        string label = words.T("Length", "Длина");
        if (outcome.Delta == 0)
        {
            return new("length", SheetMark.Same, label, words.T("as long as what it replaces; the bars after it stay where they are.", "столько же, сколько заменяемое; такты после него не сдвинутся."));
        }

        (int bar, AssemblyBar at) = notes.BarAt(t0);
        string amount = AssemblyWords.Length(Math.Abs(outcome.Delta), at.Signature, words);
        bool longer = outcome.Delta > 0;
        int after = bar + 1;
        string text = words.T(
            $"the fragment is {amount} {(longer ? "longer" : "shorter")}. The notes become {amount} {(longer ? "longer" : "shorter")}, the bars after {bar} move and the numbering from bar {after} changes.",
            $"фрагмент на {amount} {(longer ? "длиннее" : "короче")}. Ноты станут на {amount} {(longer ? "длиннее" : "короче")}, такты после {bar} сдвинутся, нумерация с {after}-го изменится.");
        return new("length", SheetMark.Changes, label, text);
    }

    private static SheetLine SeamLine(Assembled ghost, SourceMusic source, CarryStep carry, StepOutcome outcome, Words words)
    {
        string label = words.T("Seam", "Стык");
        List<string> parts = [];
        if (outcome.Span is { } span)
        {
            IReadOnlyList<AssemblyBar> bars = ghost.Music.Bars;
            for (int i = 1; i < bars.Count - 1; i++)
            {
                AssemblyBar bar = bars[i];
                bool atSeam = bar.Span.Contains(span.Onset) || bar.Span.Contains(new Tick(Math.Max(0, span.End.Value - 1))) || bar.Span.Onset == span.End;
                if (bar.IsShort && atSeam)
                {
                    int number = ghost.Music.NumberOf(i);
                    parts.Add(words.T(
                        $"bar {number} becomes a short bar of {bar.Signature}",
                        $"такт {number} станет неполным, в {bar.Signature}"));
                }
            }
        }

        (_, int endBeat, long endOffset) = source.PositionOf(new Tick(Math.Max(0, carry.From.End.Value)));
        if (outcome.Shortened.Count > 0)
        {
            string ends = endOffset == 0 && endBeat > 1
                ? words.T($"the fragment ends on beat {endBeat}; ", $"фрагмент кончается на {endBeat}-й доле; ")
                : "";
            parts.Insert(0, ends + words.T(
                $"{words.Count(outcome.Shortened.Count, "note", "notes", "нота", "ноты", "нот")} of the notes at the seam will be shortened",
                outcome.Shortened.Count == 1 ? "нота на стыке будет укорочена" : $"{words.Count(outcome.Shortened.Count, "note", "notes", "нота", "ноты", "нот")} на стыке будут укорочены"));
        }

        return parts.Count == 0
            ? new("seam", SheetMark.Same, label, words.T("the fragment fits the beats on both sides.", "фрагмент ложится по долям с обеих сторон."))
            : new("seam", SheetMark.Changes, label, Words.Capitalize(string.Join("; ", parts)) + ".");
    }

    private static (SheetLine Line, string? Choice, int Transpose) KeyLine(SourceMusic source, SourceMusic notes, CarryStep carry, StepOutcome outcome, string sourceName, Words words)
    {
        string label = words.T("Key", "Тональность");
        MusicalKey? from = source.KeyAround(carry.From.Onset, carry.From.End);
        Tick t0 = outcome.Span?.Onset ?? Tick.Zero;
        MusicalKey? to = notes.KeyAround(t0, t0.Offset(Math.Max(1, carry.From.Duration)));
        if (from is not { } a || to is not { } b)
        {
            return (new("key", SheetMark.Same, label, words.T("not enough notes to tell.", "по нотам не определить.")), null, 0);
        }

        if (a.Fifths == b.Fifths)
        {
            string same = a == b
                ? words.T($"the same, {a.NameIn(words)}.", $"та же, {a.NameIn(words)}.")
                : words.T($"{a.NameIn(words)} and {b.NameIn(words)}, the same key signature.", $"{a.NameIn(words)} и {b.NameIn(words)}, ключевые знаки те же.");
            return (new("key", SheetMark.Same, label, same), null, 0);
        }

        int transpose = (((b.Fifths - a.Fifths) * 7 % 12) + 12) % 12;
        if (transpose > 6)
        {
            transpose -= 12;
        }

        string text = words.T(
            $"{sourceName} is in {a.NameIn(words)}, the notes in {b.NameIn(words)}.",
            $"{sourceName} в {AssemblyWords.KeyPrepositional(a, words)}, ноты в {AssemblyWords.KeyPrepositional(b, words)}.");
        return (new("key", SheetMark.Changes, label, text), AssemblyWords.Interval(transpose, words), transpose);
    }

    private static SheetLine MeterLine(SourceMusic source, SourceMusic notes, CarryStep carry, StepOutcome outcome, Words words)
    {
        string label = words.T("Meter and tempo", "Размер и темп");
        Tick t0 = outcome.Span?.Onset ?? Tick.Zero;
        TimeSignature a = source.BarAt(carry.From.Onset).Bar.Signature;
        TimeSignature b = notes.BarAt(t0).Bar.Signature;
        int ta = source.TempoAt(carry.From.Onset);
        int tb = notes.TempoAt(t0);
        List<string> parts = [];
        if (a != b)
        {
            parts.Add(words.T(
                $"the fragment is in {a}, the notes in {b}: the bars inside the fragment will be barred in {a} and the time signature will appear in the notes",
                $"фрагмент в {a}, ноты в {b}: такты внутри фрагмента будут размечены по {a}, подпись размера появится в нотах"));
        }

        if (Math.Abs(ta - tb) > 3)
        {
            parts.Add(words.T(
                $"the fragment goes at {ta} a minute, the notes at {tb}: it will play at the notes' tempo",
                $"фрагмент в темпе {ta}, ноты в {tb}: он прозвучит в темпе нот"));
        }

        return parts.Count == 0
            ? new("meter", SheetMark.Same, label, words.T("the same.", "совпадают."))
            : new("meter", SheetMark.Changes, label, Words.Capitalize(string.Join("; ", parts)) + ".");
    }

    private static SheetLine NotesLine(StepOutcome outcome, AssemblyNames names, Words words)
    {
        string label = words.T("Notes", "Ноты");
        int put = outcome.Put.Count;
        int removed = outcome.Removed.Count;
        string text = words.T(
            $"{put} go in, {(removed == 0 ? "none leave" : $"{removed} leave")}",
            $"{put} {(Words.RussianForm(put, "войдёт", "войдут", "войдут"))}, {(removed == 0 ? "ни одна не уйдёт" : $"{removed} {Words.RussianForm(removed, "уйдёт", "уйдут", "уйдут")}")}");
        if (removed > 0)
        {
            List<string> from = [];
            foreach (IGrouping<int, (string Id, int From)> group in outcome.Removed.GroupBy(note => note.From).OrderBy(group => group.Key))
            {
                int count = group.Count();
                from.Add(group.Key == 0
                    ? words.T($"{count} from the notes before the assembly", $"{count} из прежних нот")
                    : words.T($"{count} from {names.OfStep(group.Key)} (carry No. {group.Key})", $"{count} из {names.GenitiveOfStep(group.Key)} (перенос №{group.Key})"));
            }

            text += words.T($"; of them {words.Joined(from)}", $"; из них {words.Joined(from)}");
        }

        return new("notes", SheetMark.Same, label, text + ".");
    }

    /// <summary>«такт 15» or «такты 15–16» for <c>[start, end)</c>.</summary>
    public static string BarsText(SourceMusic music, Tick start, Tick end, Words words)
    {
        ArgumentNullException.ThrowIfNull(music);
        int first = music.BarAt(start).Number;
        int last = music.BarAt(new Tick(Math.Max(start.Value, end.Value - 1))).Number;
        return first == last ? words.T($"bar {first}", $"такт {first}") : words.T($"bars {first}–{last}", $"такты {first}–{last}");
    }

    /// <summary>«такта 15» or «тактов 15–16».</summary>
    public static string BarsGenitive(SourceMusic music, Tick start, Tick end, Words words)
    {
        ArgumentNullException.ThrowIfNull(music);
        int first = music.BarAt(start).Number;
        int last = music.BarAt(new Tick(Math.Max(start.Value, end.Value - 1))).Number;
        return first == last ? words.T($"bar {first}", $"такта {first}") : words.T($"bars {first}–{last}", $"тактов {first}–{last}");
    }

    private static string BarText(SourceMusic music, Tick at, Words words) => words.T($"bar {music.BarAt(at).Number}", $"такт {music.BarAt(at).Number}");
}