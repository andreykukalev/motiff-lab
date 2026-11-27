using System.Globalization;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The rhythm facts as sentences in both languages (S1-A1), written from the measures of <c>MotifAnalyzer.Rhythm.cs</c>.
/// Nothing is measured here; the notes are read only to name the places a measure points at.
/// </summary>
internal static partial class MotifAnalyzer
{
    /// <summary>A list in a fact names at most this many places, then says how many more there are.</summary>
    private const int MaxListed = 6;

    private static string PerformanceBasis(Words w) => w.T(
        " Onsets fall between grid lines, as in a played performance, so this may not match the written rhythm.",
        " Атаки попадают между линиями сетки, как в живом исполнении, поэтому это может не совпадать с записанным ритмом.");

    private static string SyncopationBasis(Words w) => w.T(
        "An onset is syncopated when it starts off the beat and still sounds on the next beat, or starts on a beat other than the first and sounds across the bar line. Tied notes count as one note.",
        "Атака синкопирована, если она начинается не на долю и ещё звучит на следующей доле, или начинается не на первой доле и звучит через тактовую черту. Ноты с лигой считаются одной нотой.");

    private static MotifFact DurationsFact(DurationMeasure measure, bool offGrid, Words w)
    {
        // Each length the text names is a place: the notes of that length.
        var parts = measure.Values.Select(value => value.Value.Counted(value.Notes.Length, w)).ToList();
        var places = measure.Values.Select(value => FactPlace.Of(w.IsRussian ? value.Value.RussianFew : value.Notes.Length == 1 ? value.Value.One : value.Value.Many, value.Notes)).ToList();
        int other = measure.Other.Count;
        if (other > 0)
        {
            parts.Add(w.IsRussian
                ? $"{other} {Words.RussianForm(other, "длина, не равная одной длительности", "длины, не равные одной длительности", "длин, не равных одной длительности")}"
                : Words.EnglishCount(other, "length that is no single note value", "lengths that are no single note value"));
            places.Add(FactPlace.Of(w.T("no single note value", "не одна длительность"), measure.Other));
        }

        string text = $"{w.T("Lengths", "Длительности")}: {string.Join(", ", parts)}.";
        MotifFact fact = offGrid
            ? new MotifFact("rhythm.durations", FactLens.Rhythm, FactCertainty.Uncertain, text, w.T(
                "Onsets fall between grid lines, as in a played performance, so lengths may not be the written note values.",
                "Атаки попадают между линиями сетки, как в живом исполнении, поэтому длины могут не совпадать с записанными длительностями."))
            : new MotifFact("rhythm.durations", FactLens.Rhythm, FactCertainty.Detected, text, w.T(
                "Read from the notes' lengths; a tied note counts as one length.",
                "Прочитано по длинам нот; нота с лигой считается одной длиной."));
        return fact with { Places = places };
    }

    private static MotifFact GridFact(GridMeasure measure, Note[] notes, Words w)
    {
        MetricPosition start = measure.Start;
        string startText = w.IsRussian
            ? start.BeatOffset == 0
                ? $"Начинается на доле {start.Beat} такта {start.Bar}."
                : $"Начинается после доли {start.Beat} такта {start.Bar}, не на долю."
            : start.BeatOffset == 0
                ? $"It starts on beat {start.Beat} of bar {start.Bar}."
                : $"It starts after beat {start.Beat} of bar {start.Bar}, off the beat.";
        int onBeat = measure.OnBeat;
        string beats = w.IsRussian
            ? $"{onBeat} {w.OutOf(measure.Onsets, "", "", "атаки", "атак")} {Words.RussianForm(onBeat, "приходится", "приходятся", "приходятся")} на долю."
            : $"{onBeat} of {Words.EnglishCount(measure.Onsets, "onset", "onsets")} {(onBeat == 1 ? "falls" : "fall")} on a beat.";
        FactPlace startPlace = FactPlace.Of($"{w.T("start", "начало")}, {Place(start, w)}", notes.Where(note => note.Span.Onset == measure.First));

        int offGrid = measure.OffGrid;
        if (offGrid > 0)
        {
            return new MotifFact(
                "rhythm.grid",
                FactLens.Rhythm,
                FactCertainty.Uncertain,
                w.IsRussian
                    ? $"{offGrid} {w.OutOf(measure.Onsets, "", "", "атаки", "атак")} {Words.RussianForm(offGrid, "попадает", "попадают", "попадают")} между линиями сетки тридцать вторых и триолей. {beats} {startText}"
                    : $"{offGrid} of {Words.EnglishCount(measure.Onsets, "onset", "onsets")} {(offGrid == 1 ? "falls" : "fall")} between thirty-second and triplet grid lines. {beats} {startText}",
                w.T(
                    "Measured from the bar lines. Onsets off every grid usually mean a played performance whose timing was not quantised.",
                    "Измерено от тактовых черт. Атаки вне любой сетки обычно означают живое исполнение без квантования."))
            {
                Places = [FactPlace.Of(w.T("between grid lines", "между линиями сетки"), measure.OffGridNotes), startPlace],
            };
        }

        string grid = w.IsRussian
            ? measure.Triplets is { } russianTriplets
                ? $"сетку {s_grids[measure.Grid].RussianGrid}, с {s_grids[russianTriplets].RussianWith}"
                : $"сетку {s_grids[measure.Grid].RussianGrid}"
            : measure.Triplets is { } triplets
                ? $"the {s_grids[measure.Grid].Name} grid, with {s_grids[triplets].Name}s"
                : $"the {s_grids[measure.Grid].Name} grid";
        return new MotifFact("rhythm.grid", FactLens.Rhythm, FactCertainty.Detected, w.T($"Onsets fit {grid}. {beats} {startText}", $"Атаки укладываются в {grid}. {beats} {startText}"), w.T("Measured from the bar lines.", "Измерено от тактовых черт."))
        {
            Places = [startPlace],
            Brief = w.T($"Onsets fit {grid}.", $"Атаки укладываются в {grid}."),
        };
    }

    private static MotifFact SilenceFact(SilenceMeasure measure, Words w)
    {
        IReadOnlyList<TickSpan> gaps = measure.Gaps;
        var places = gaps.Select(gap => new FactPlace($"{w.T("silence", "пауза")}, {LengthName(gap.Duration, w)}", gap, [])).ToList();
        string text = gaps.Count == 0
            ? w.T("No silence between notes", "Между нотами нет пауз")
            : w.IsRussian
                ? $"{w.Count(gaps.Count, "", "", "пауза", "паузы", "пауз")} между нотами: {string.Join(", ", gaps.Select(gap => LengthName(gap.Duration, w)))}"
                : $"{Words.EnglishCount(gaps.Count, "silence", "silences")} between notes: {string.Join(", ", gaps.Select(gap => LengthName(gap.Duration, w)))}";
        if (measure.Opening is { } opening)
        {
            text += w.IsRussian
                ? $"; выделение начинается с паузы: {LengthName(opening.Duration, w)}"
                : $"; the selection opens with {LengthName(opening.Duration, w)} of silence";
            places.Add(new FactPlace($"{w.T("opening silence", "начальная пауза")}, {LengthName(opening.Duration, w)}", opening, []));
        }

        return new MotifFact("rhythm.silences", FactLens.Rhythm, FactCertainty.Detected, $"{text}.", w.T(
            "Measured where no note of the motif sounds; rests in other voices or hands are not counted.",
            "Измерено там, где не звучит ни одна нота мотива; паузы в других голосах или руках не считаются."))
        {
            Places = places,
        };
    }

    /// <summary>"a quarter", or "a length that is no single note value"; "четверть".</summary>
    private static string LengthName(long ticks, Words w)
    {
        int index = Array.FindIndex(s_values, value => value.Ticks == ticks);
        return index < 0
            ? w.T("a length that is no single note value", "длина, не равная одной длительности")
            : w.IsRussian ? s_values[index].RussianOne : Words.WithArticle(s_values[index].One);
    }

    private static MotifFact GroupingFact(GroupingMeasure measure, bool offGrid, Words w)
    {
        int[] counts = [.. measure.Beats.Select(beat => beat.Count)];
        string perBar = string.Join(" | ", measure.Beats
            .GroupBy(beat => beat.Bar)
            .Select(bar => string.Join(" ", bar.Select(beat => beat.Count))));

        string text = $"{w.T("Onsets per beat, bar by bar", "Атак на долю, по тактам")}: {perBar}.";
        string brief = w.IsRussian ? $"Атак на долю: от {counts.Min()} до {counts.Max()}." : $"From {counts.Min()} to {counts.Max()} onsets per beat.";
        if (measure.CommonBeats == counts.Length && counts.Length > 1)
        {
            brief = w.IsRussian ? $"В каждой доле {BeatSplitRussian(measure.Common)}." : $"Every beat {BeatSplit(measure.Common, plural: false)}.";
            text += " " + brief;
        }
        else if (measure.CommonBeats * 2 > counts.Length)
        {
            brief = w.IsRussian
                ? $"В большинстве долей {BeatSplitRussian(measure.Common)} ({measure.CommonBeats} из {counts.Length})."
                : $"Most beats {BeatSplit(measure.Common, plural: true)} ({measure.CommonBeats} of {counts.Length}).";
            text += " " + brief;
        }

        string basis = w.T(
            "Counted per beat of the time signature, from the bar lines; notes that start together count as one onset.",
            "Посчитано по долям размера, от тактовых черт; ноты, начинающиеся вместе, считаются одной атакой.");
        MotifFact fact = offGrid
            ? new MotifFact("rhythm.grouping", FactLens.Rhythm, FactCertainty.Uncertain, text, basis + PerformanceBasis(w))
            : new MotifFact("rhythm.grouping", FactLens.Rhythm, FactCertainty.Detected, text, basis);
        return counts.Length == 1 ? fact : fact with { Brief = brief };
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

    private static MotifFact SyncopationFact(List<Syncopation> syncopations, bool offGrid, Words w)
    {
        List<FactPlace> found = SyncopationPlaces(syncopations, w);
        string text = found.Count == 0
            ? w.T(
                "No syncopation: no onset off the beat is held into the next beat, and no note is held across the bar line from a weak beat.",
                "Синкоп нет: ни одна атака не на долю не тянется в следующую долю, и ни одна нота со слабой доли не тянется через тактовую черту.")
            : $"{w.Count(found.Count, "syncopation", "syncopations", "синкопа", "синкопы", "синкоп")}: {w.Listed([.. found.Select(place => place.Label)], MaxListed, "; ", "; ")}.";
        MotifFact fact = offGrid
            ? new MotifFact("rhythm.syncopation", FactLens.Rhythm, FactCertainty.Uncertain, text, SyncopationBasis(w) + PerformanceBasis(w))
            : new MotifFact("rhythm.syncopation", FactLens.Rhythm, FactCertainty.Detected, text, SyncopationBasis(w));
        return fact with
        {
            Places = found,
            Brief = found.Count == 0
                ? w.T("No syncopation.", "Синкоп нет.")
                : $"{Words.Capitalize(w.Count(found.Count, "syncopation", "syncopations", "синкопа", "синкопы", "синкоп"))}.",
        };
    }

    /// <summary>Each syncopation as a place, named where it is, with the notes that sound past the beat or the bar line; shared with the syncopation share (S1-A5).</summary>
    private static List<FactPlace> SyncopationPlaces(List<Syncopation> syncopations, Words w) =>
        [.. syncopations.Select(syncopation => FactPlace.Of(SyncopationLabel(syncopation, w), syncopation.Notes))];

    /// <summary>"bar 2 after beat 1, held into beat 2", "bar 2 after beat 3, held across the bar line", "bar 2 beat 3, held across the bar line".</summary>
    private static string SyncopationLabel(Syncopation syncopation, Words w)
    {
        MetricPosition at = syncopation.At;
        if (at.BeatOffset == 0)
        {
            return w.T($"bar {at.Bar} beat {at.Beat}, held across the bar line", $"такт {at.Bar} доля {at.Beat}, через тактовую черту");
        }

        return syncopation.AcrossBar
            ? w.T($"bar {at.Bar} after beat {at.Beat}, held across the bar line", $"такт {at.Bar} после доли {at.Beat}, через тактовую черту")
            : w.T($"bar {at.Bar} after beat {at.Beat}, held into beat {at.Beat + 1}", $"такт {at.Bar} после доли {at.Beat}, до доли {at.Beat + 1}");
    }

    private static MotifFact AccentFact(List<(Note Note, AccentKind Kind)> accented, int noteCount, MeterMap meter, MusicalKey key, Words w)
    {
        FactPlace[] places = [.. accented.Select(entry => FactPlace.Of(
            $"{key.Spell(entry.Note.Pitch.MidiNumber)}{w.T(" at ", ", ")}{Place(meter.ToPosition(entry.Note.Span.Onset), w)}{(entry.Kind == AccentKind.StrongAccent ? w.T(" (strong accent)", " (сильный акцент)") : "")}",
            [entry.Note]))];
        string? brief = accented.Count == 0
            ? null
            : w.IsRussian
                ? $"Акценты отмечены на {accented.Count} {w.OutOf(noteCount, "", "", "ноты", "нот")}"
                : $"Accents marked on {accented.Count} of {Words.EnglishCount(noteCount, "note", "notes")}";
        string text = brief is null
            ? w.T("No accents are marked on the motif's notes.", "На нотах мотива акценты не отмечены.")
            : $"{brief}: " + w.Listed([.. places.Select(place => place.Label)], MaxListed, "; ", "; ") + ".";
        return new MotifFact("rhythm.accents", FactLens.Rhythm, FactCertainty.Detected, text, w.T(
            "Read from the accent marks written in the file (> accent, ^ strong accent); loudness in the notes is not counted.",
            "Прочитано по знакам акцента в файле (> акцент, ^ сильный акцент); громкость нот не учитывается."))
        {
            Places = places,
            Brief = brief is null ? null : $"{brief}.",
        };
    }

    private static MotifFact PatternFact(PatternMeasure measure, Note[] notes, MeterMap meter, bool offGrid, Words w)
    {
        var sentences = new List<string>();
        var places = new List<FactPlace>();
        string[] barGroups = [.. measure.RepeatedBars.Select(group => w.Joined([.. group.Select(bar => bar.ToString(CultureInfo.InvariantCulture))]))];
        if (barGroups.Length > 0)
        {
            sentences.Add(w.IsRussian
                ? $"У тактов {barGroups[0]} одинаковый ритм" + string.Concat(barGroups.Skip(1).Select(group => $"; у тактов {group} тоже")) + "."
                : $"Bars {barGroups[0]} have the same rhythm" + string.Concat(barGroups.Skip(1).Select(group => $"; so do bars {group}")) + ".");

            // Each bar of a group is a place, the bar with the notes that start in it.
            places.AddRange(measure.RepeatedBars.SelectMany(group => group.Select(bar => SlotPlace($"{w.T("bar", "такт")} {bar}", meter.GetBar(bar).Span, notes))));
        }

        if (measure.Figure is { } figure)
        {
            string name = FigureName(figure, w);
            int count = measure.FigureBeats.Count;
            sentences.Add(w.IsRussian
                ? $"Фигура {name} заполняет {count} {Words.RussianForm(count, "долю", "доли", "долей")}."
                : $"The figure {name} fills {count} beats.");

            // Each beat the figure fills is a place, the beat with the notes that start in it.
            places.AddRange(measure.FigureBeats.Select(beat => SlotPlace($"{name}, {Place(meter.ToPosition(beat.Start), w)}", TickSpan.FromBounds(beat.Start, beat.End), notes)));
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
        return fact with { Places = places, Brief = sentences.Count > 1 ? sentences[0] : null };
    }

    /// <summary>A beat figure by its note values: "eighth–eighth", "sixteenth rest–sixteenth–eighth"; «пауза шестнадцатая–восьмая».</summary>
    private static string FigureName(BeatFigure figure, Words w)
    {
        var parts = new List<string>();
        if (figure.Rest != 0)
        {
            parts.Add(w.IsRussian ? $"пауза {ValueName(figure.Rest, w)}" : $"{ValueName(figure.Rest, w)} rest");
        }

        parts.AddRange(figure.Lengths.Select(length => ValueName(length, w)));
        return string.Join("–", parts);
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
}