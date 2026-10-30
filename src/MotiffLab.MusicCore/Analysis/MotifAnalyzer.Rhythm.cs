using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// Rhythm facts beyond lengths, grid and silences (S1-A1): how beats are subdivided, syncopations,
/// accents where the file marks them, and rhythms that repeat inside the motif.
/// </summary>
/// <remarks>
/// An onset is where one or more notes of the motif start; notes that start together count once,
/// and the longest of them decides how long the onset is held.
/// </remarks>
public static partial class MotifAnalyzer
{
    /// <summary>A list in a fact names at most this many places, then says how many more there are.</summary>
    private const int MaxListed = 6;

    private static string PerformanceBasis(Words w) => w.T(
        " Onsets fall between grid lines, as in a played performance, so this may not match the written rhythm.",
        " Атаки попадают между линиями сетки, как в живом исполнении, поэтому это может не совпадать с записанным ритмом.");

    /// <summary>One onset of the motif and how long its longest note sounds.</summary>
    private readonly record struct Hit(Tick At, Tick End);

    /// <summary>A beat of the selection: where it starts and ends, and the bar it is in.</summary>
    private readonly record struct BeatSlot(int Bar, Tick Start, Tick End);

    private static Hit[] Hits(IEnumerable<IGrouping<Tick, Note>> onsets) =>
        [.. onsets.Select(group => new Hit(group.Key, group.Max(note => note.Span.End))).OrderBy(hit => hit.At)];

    /// <summary>The beats a beat-aligned span covers, each cut at its bar line.</summary>
    private static List<BeatSlot> Beats(MeterMap meter, TickSpan span)
    {
        var beats = new List<BeatSlot>();
        Tick start = span.Onset;
        while (start < span.End)
        {
            Bar bar = meter.BarAt(start);
            MetricPosition position = meter.ToPosition(start);
            long end = Math.Min(start.Value + bar.Signature.BeatTicks - position.BeatOffset, Math.Min(bar.Span.End.Value, span.End.Value));
            beats.Add(new BeatSlot(bar.Number, start, new Tick(end)));
            start = new Tick(end);
        }

        return beats;
    }

    private static MotifFact GroupingFact(Hit[] hits, MeterMap meter, TickSpan span, bool offGrid, Words w)
    {
        List<BeatSlot> beats = Beats(meter, span);
        int[] counts = [.. beats.Select(beat => hits.Count(hit => hit.At >= beat.Start && hit.At < beat.End))];
        string perBar = string.Join(" | ", beats
            .Select((beat, i) => (beat.Bar, Count: counts[i]))
            .GroupBy(beat => beat.Bar)
            .Select(bar => string.Join(" ", bar.Select(beat => beat.Count))));

        string text = $"{w.T("Onsets per beat, bar by bar", "Атак на долю, по тактам")}: {perBar}.";
        IGrouping<int, int> common = counts.GroupBy(count => count).OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key).First();
        if (common.Count() == counts.Length && counts.Length > 1)
        {
            text += w.IsRussian ? $" В каждой доле {BeatSplitRussian(common.Key)}." : $" Every beat {BeatSplit(common.Key, plural: false)}.";
        }
        else if (common.Count() * 2 > counts.Length)
        {
            text += w.IsRussian
                ? $" В большинстве долей {BeatSplitRussian(common.Key)} ({common.Count()} из {counts.Length})."
                : $" Most beats {BeatSplit(common.Key, plural: true)} ({common.Count()} of {counts.Length}).";
        }

        string basis = w.T(
            "Counted per beat of the time signature, from the bar lines; notes that start together count as one onset.",
            "Посчитано по долям размера, от тактовых черт; ноты, начинающиеся вместе, считаются одной атакой.");
        return offGrid
            ? new MotifFact("rhythm.grouping", FactLens.Rhythm, FactCertainty.Uncertain, text, basis + PerformanceBasis(w))
            : new MotifFact("rhythm.grouping", FactLens.Rhythm, FactCertainty.Detected, text, basis);
    }

    private static string BeatSplit(int onsets, bool plural) => (onsets switch
    {
        0 => "{0} no onset",
        1 => "{0} one onset",
        2 => "{1} in two",
        3 => "{1} in three",
        4 => "{1} in four",
        _ => $"{{0}} {onsets} onsets",
    }).Replace("{0}", plural ? "hold" : "holds", StringComparison.Ordinal).Replace("{1}", plural ? "split" : "splits", StringComparison.Ordinal);

    /// <summary>What a beat holds, after "в каждой доле": "одна атака", "две атаки", "нет атак".</summary>
    private static string BeatSplitRussian(int onsets) => onsets switch
    {
        0 => "нет атак",
        1 => "одна атака",
        2 => "две атаки",
        3 => "три атаки",
        4 => "четыре атаки",
        _ => $"{onsets} {Words.RussianForm(onsets, "атака", "атаки", "атак")}",
    };

    private static string SyncopationBasis(Words w) => w.T(
        "An onset is syncopated when it starts off the beat and still sounds on the next beat, or starts on a beat other than the first and sounds across the bar line. Tied notes count as one note.",
        "Атака синкопирована, если она начинается не на долю и ещё звучит на следующей доле, или начинается не на первой доле и звучит через тактовую черту. Ноты с лигой считаются одной нотой.");

    private static MotifFact SyncopationFact(Note[] notes, Hit[] hits, MeterMap meter, bool offGrid, Words w)
    {
        List<FactPlace> found = Syncopations(notes, hits, meter, w);
        string text = found.Count == 0
            ? w.T(
                "No syncopation: no onset off the beat is held into the next beat, and no note is held across the bar line from a weak beat.",
                "Синкоп нет: ни одна атака не на долю не тянется в следующую долю, и ни одна нота со слабой доли не тянется через тактовую черту.")
            : $"{w.Count(found.Count, "syncopation", "syncopations", "синкопа", "синкопы", "синкоп")}: {Listed([.. found.Select(place => place.Label)], w)}.";
        MotifFact fact = offGrid
            ? new MotifFact("rhythm.syncopation", FactLens.Rhythm, FactCertainty.Uncertain, text, SyncopationBasis(w) + PerformanceBasis(w))
            : new MotifFact("rhythm.syncopation", FactLens.Rhythm, FactCertainty.Detected, text, SyncopationBasis(w));
        return fact with { Places = found };
    }

    /// <summary>
    /// Each syncopated onset in time order, named where it is, with the notes of the onset that
    /// sound past the beat or the bar line; shared by the syncopation fact and the syncopation share (S1-A5).
    /// </summary>
    private static List<FactPlace> Syncopations(Note[] notes, Hit[] hits, MeterMap meter, Words w)
    {
        var found = new List<FactPlace>();
        foreach (Hit hit in hits)
        {
            Bar bar = meter.BarAt(hit.At);
            MetricPosition position = meter.ToPosition(hit.At);
            (string Label, Tick Past)? syncopation = null;
            if (position.BeatOffset != 0)
            {
                Tick nextBeat = hit.At.Offset(bar.Signature.BeatTicks - position.BeatOffset);
                if (hit.End > nextBeat)
                {
                    syncopation = nextBeat >= bar.Span.End
                        ? (w.T($"bar {position.Bar} after beat {position.Beat}, held across the bar line", $"такт {position.Bar} после доли {position.Beat}, через тактовую черту"), nextBeat)
                        : (w.T($"bar {position.Bar} after beat {position.Beat}, held into beat {position.Beat + 1}", $"такт {position.Bar} после доли {position.Beat}, до доли {position.Beat + 1}"), nextBeat);
                }
            }
            else if (position.Beat != 1 && hit.End > bar.Span.End)
            {
                syncopation = (w.T($"bar {position.Bar} beat {position.Beat}, held across the bar line", $"такт {position.Bar} доля {position.Beat}, через тактовую черту"), bar.Span.End);
            }

            if (syncopation is { } held)
            {
                found.Add(FactPlace.Of(held.Label, notes.Where(note => note.Span.Onset == hit.At && note.Span.End > held.Past)));
            }
        }

        return found;
    }

    private static MotifFact AccentFact(Note[] notes, IEnumerable<AccentMark> accents, MeterMap meter, MusicalKey key, Words w)
    {
        var marks = new Dictionary<string, AccentKind>(StringComparer.Ordinal);
        foreach (AccentMark mark in accents)
        {
            // A strong accent wins over a plain one written on the same note.
            if (!marks.TryGetValue(mark.NoteId, out AccentKind kind) || mark.Kind > kind)
            {
                marks[mark.NoteId] = mark.Kind;
            }
        }

        var accented = notes.Where(note => marks.ContainsKey(note.Id)).ToList();
        FactPlace[] places = [.. accented.Select(note => FactPlace.Of(
            $"{key.Spell(note.Pitch.MidiNumber)}{w.T(" at ", ", ")}{Place(meter.ToPosition(note.Span.Onset), w)}{(marks[note.Id] == AccentKind.StrongAccent ? w.T(" (strong accent)", " (сильный акцент)") : "")}",
            [note]))];
        string text = accented.Count == 0
            ? w.T("No accents are marked on the motif's notes.", "На нотах мотива акценты не отмечены.")
            : w.IsRussian
                ? $"Акценты отмечены на {accented.Count} {w.OutOf(notes.Length, "", "", "ноты", "нот")}: " + Listed([.. places.Select(place => place.Label)], w) + "."
                : $"Accents marked on {accented.Count} of {Count(notes.Length, "note", "notes")}: " + Listed([.. places.Select(place => place.Label)], w) + ".";
        return new MotifFact("rhythm.accents", FactLens.Rhythm, FactCertainty.Detected, text, w.T(
            "Read from the accent marks written in the file (> accent, ^ strong accent); loudness in the notes is not counted.",
            "Прочитано по знакам акцента в файле (> акцент, ^ сильный акцент); громкость нот не учитывается."))
        {
            Places = places,
        };
    }

    private static MotifFact PatternFact(Note[] notes, Hit[] hits, MeterMap meter, TickSpan span, bool offGrid, Words w)
    {
        var sentences = new List<string>();
        var places = new List<FactPlace>();

        // Bars wholly inside the selection with the same onsets from the bar line.
        var bars = new List<(int Number, string Rhythm)>();
        for (Bar bar = meter.BarAt(span.Onset); bar.Span.Onset < span.End; bar = meter.GetBar(bar.Number + 1))
        {
            Hit[] inBar = [.. hits.Where(hit => hit.At >= bar.Span.Onset && hit.At < bar.Span.End)];
            if (bar.Span.Onset >= span.Onset && bar.Span.End <= span.End && inBar.Length > 0)
            {
                bars.Add((bar.Number, $"{bar.Span.Duration}:{string.Join(",", inBar.Select(hit => hit.At.Value - bar.Span.Onset.Value))}"));
            }
        }

        IGrouping<string, (int Number, string Rhythm)>[] repeatedBars = [.. bars
            .GroupBy(bar => bar.Rhythm)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.First().Number)];
        string[] barGroups = [.. repeatedBars.Select(group => w.Joined(group.Select(bar => bar.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList()))];
        if (barGroups.Length > 0)
        {
            sentences.Add(w.IsRussian
                ? $"У тактов {barGroups[0]} одинаковый ритм" + string.Concat(barGroups.Skip(1).Select(group => $"; у тактов {group} тоже")) + "."
                : $"Bars {barGroups[0]} have the same rhythm" + string.Concat(barGroups.Skip(1).Select(group => $"; so do bars {group}")) + ".");

            // Each bar of a group is a place, the bar with the notes that start in it.
            places.AddRange(repeatedBars.SelectMany(group => group.Select(bar => SlotPlace($"{w.T("bar", "такт")} {bar.Number}", meter.GetBar(bar.Number).Span, notes))));
        }

        // Figures inside a beat: onsets and how long each lasts until the next one or the end of the beat.
        // A beat holding one note on the beat is no figure.
        var figures = new List<string>();
        var figureBeats = new List<BeatSlot>();
        foreach (BeatSlot beat in Beats(meter, span))
        {
            Hit[] inBeat = [.. hits.Where(hit => hit.At >= beat.Start && hit.At < beat.End)];
            if (inBeat.Length == 0 || (inBeat.Length == 1 && inBeat[0].At == beat.Start))
            {
                continue;
            }

            var parts = new List<string>();
            if (inBeat[0].At > beat.Start)
            {
                parts.Add(w.IsRussian ? $"пауза {ValueName(inBeat[0].At.Value - beat.Start.Value, w)}" : $"{ValueName(inBeat[0].At.Value - beat.Start.Value, w)} rest");
            }

            for (int i = 0; i < inBeat.Length; i++)
            {
                Tick until = i + 1 < inBeat.Length ? inBeat[i + 1].At : beat.End;
                parts.Add(ValueName(until.Value - inBeat[i].At.Value, w));
            }

            figures.Add(string.Join("–", parts));
            figureBeats.Add(beat);
        }

        IGrouping<string, string>? figure = figures
            .GroupBy(name => name)
            .Where(group => group.Count() > 1)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => figures.IndexOf(group.Key))
            .FirstOrDefault();
        if (figure is not null)
        {
            sentences.Add(w.IsRussian
                ? $"Фигура {figure.Key} заполняет {figure.Count()} {Words.RussianForm(figure.Count(), "долю", "доли", "долей")}."
                : $"The figure {figure.Key} fills {figure.Count()} beats.");

            // Each beat the figure fills is a place, the beat with the notes that start in it.
            places.AddRange(figureBeats
                .Where((beat, i) => figures[i] == figure.Key)
                .Select(beat => SlotPlace($"{figure.Key}, {Place(meter.ToPosition(beat.Start), w)}", TickSpan.FromBounds(beat.Start, beat.End), notes)));
        }

        string text = sentences.Count == 0
            ? w.T("No bar rhythm or beat figure repeats within the motif.", "Внутри мотива не повторяется ни ритм такта, ни фигура доли.")
            : string.Join(" ", sentences);
        string basis = w.T(
            "Bars are compared by where their onsets fall from the bar line, counting only bars wholly inside the selection; figures by the onsets inside one beat. A beat holding one note on the beat is not a figure.",
            "Такты сравниваются по положению атак от тактовой черты, только такты целиком внутри выделения; фигуры — по атакам внутри одной доли. Доля с одной нотой на долю фигурой не считается.");
        MotifFact fact = offGrid
            ? new MotifFact("rhythm.patterns", FactLens.Rhythm, FactCertainty.Uncertain, text, basis + PerformanceBasis(w))
            : new MotifFact("rhythm.patterns", FactLens.Rhythm, FactCertainty.Detected, text, basis);
        return fact with { Places = places };
    }

    /// <summary>A bar or a beat as a place: its own span, with the notes that start in it.</summary>
    private static FactPlace SlotPlace(string label, TickSpan slot, Note[] notes) =>
        new(label, slot, [.. notes.Where(note => slot.Contains(note.Span.Onset)).Select(note => note.Id)]);

    /// <summary>"sixteenth", or "odd length" for a length that is no single note value; "шестнадцатая".</summary>
    private static string ValueName(long ticks, Words w)
    {
        int index = Array.FindIndex(s_values, value => value.Ticks == ticks);
        return index < 0 ? w.T("odd length", "нестандартная длина") : s_values[index].Name(w);
    }

    /// <summary>"bar 2 beat 1", or "bar 2 after beat 1" off the beat; "такт 2 доля 1", "такт 2 после доли 1".</summary>
    private static string Place(MetricPosition position, Words w) => w.IsRussian
        ? position.BeatOffset == 0 ? $"такт {position.Bar} доля {position.Beat}" : $"такт {position.Bar} после доли {position.Beat}"
        : position.BeatOffset == 0 ? $"bar {position.Bar} beat {position.Beat}" : $"bar {position.Bar} after beat {position.Beat}";

    /// <summary>Items joined with semicolons, the list cut after <see cref="MaxListed"/>.</summary>
    private static string Listed(IReadOnlyList<string> items, Words w) =>
        items.Count <= MaxListed
            ? string.Join("; ", items)
            : w.T($"{string.Join("; ", items.Take(MaxListed))}; and {items.Count - MaxListed} more", $"{string.Join("; ", items.Take(MaxListed))}; и ещё {items.Count - MaxListed}");
}