using System.Globalization;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The structure facts as sentences in both languages (S1-A4): the recurrences, the phrases and the new material,
/// written only from the <see cref="Measures"/>. No notes, meter or search belong here; what a sentence needs that the
/// measures do not hold is measured in <c>StructureAnalyzer.cs</c>.
/// </summary>
internal static partial class StructureAnalyzer
{
    /// <summary>A list in a fact names at most this many places, then says how many more there are.</summary>
    private const int MaxListed = 6;

    /// <summary>The facts, in this order: the recurrences; the phrases, when the searched notes have any; the new material, when a bar has notes.</summary>
    private static List<MotifFact> Facts(Measures measures, Words w)
    {
        bool inferred = measures.Chords || measures.HandInferred;
        string lineBasis = (measures.HandInferred ? w.T("The hand is inferred, so which notes are searched is inferred too. ", "Рука предположена, поэтому и то, среди каких нот идёт поиск, предположено. ") : "")
            + (measures.Chords
                ? w.T("Notes sound together, so lines are taken as the highest note at each onset.", "Ноты звучат вместе, поэтому линией считается самая высокая нота каждой атаки.")
                : w.T("Lines are read from the notes, one at a time.", "Линии прочитаны по нотам, одна за другой."));
        var facts = new List<MotifFact>
        {
            RecurrenceFact(measures, inferred, lineBasis, w),
        };
        if (measures.Phrases.Count > 0)
        {
            facts.Add(PhraseFact(measures.Phrases, measures.MotifStart, w));
        }

        if (measures.BarsWithNotes > 0)
        {
            facts.Add(NewMaterialFact(measures, inferred, lineBasis, w));
        }

        return facts;
    }

    private static MotifFact RecurrenceFact(Measures measures, bool inferred, string lineBasis, Words w)
    {
        IReadOnlyList<Recurrence> recurrences = measures.Recurrences;
        string rule = w.T(
            " A recurrence has the same onsets from its first note and every pitch moved by the same amount; note lengths are not compared.",
            " У повторения те же атаки от первой ноты, и все высоты сдвинуты на одно и то же расстояние; длины нот не сравниваются.");
        FactCertainty certainty = inferred ? FactCertainty.Inferred : FactCertainty.Detected;
        if (measures.MotifNotes < MinNotesForRecurrence)
        {
            return new MotifFact(
                "structure.recurrences",
                FactLens.Structure,
                certainty,
                w.IsRussian
                    ? $"В линии мотива {w.Count(measures.MotifNotes, "", "", "нота", "ноты", "нот")}: слишком мало для поиска повторений, нужно {MinNotesForRecurrence}."
                    : $"With {Words.EnglishCount(measures.MotifNotes, "note", "notes")} in its line, the motif is too short to search for recurrences; it takes {MinNotesForRecurrence}.",
                lineBasis + rule);
        }

        if (recurrences.Count == 0)
        {
            return new MotifFact("structure.recurrences", FactLens.Structure, certainty, w.T(
                "The motif does not recur elsewhere, exactly or transposed.",
                "Мотив больше нигде не повторяется, ни точно, ни с транспозицией."), lineBasis + rule);
        }

        string[] places = [.. recurrences.Select(recurrence => w.IsRussian
            ? recurrence.IsExact
                ? $"точно {w.InBars(recurrence.FirstBar, recurrence.LastBar)}"
                : $"на {w.IntervalBy(recurrence.Transposition)} {(recurrence.Transposition > 0 ? "выше" : "ниже")} {w.InBars(recurrence.FirstBar, recurrence.LastBar)}"
            : recurrence.IsExact
                ? $"exactly {w.InBars(recurrence.FirstBar, recurrence.LastBar)}"
                : $"{w.IntervalBy(recurrence.Transposition)} {(recurrence.Transposition > 0 ? "higher" : "lower")} {w.InBars(recurrence.FirstBar, recurrence.LastBar)}")];
        string list = places.Length <= MaxListed
            ? w.Joined(places)
            : w.T($"{string.Join(", ", places.Take(MaxListed))} and {places.Length - MaxListed} more times", $"{string.Join(", ", places.Take(MaxListed))} и ещё {w.Times(places.Length - MaxListed)}");
        int exactCount = recurrences.Count(recurrence => recurrence.IsExact);
        string text = w.IsRussian
            ? $"Мотив повторяется {w.Times(recurrences.Count)} (точно: {exactCount}, с транспозицией: {recurrences.Count - exactCount}): {list}."
            : $"The motif recurs {w.Times(recurrences.Count)} ({exactCount} exact, {recurrences.Count - exactCount} transposed): {list}.";

        Recurrence? earlier = recurrences.FirstOrDefault(recurrence => recurrence.Span.Onset < measures.MotifStart);
        if (earlier is not null)
        {
            text += w.T($" It is first heard {w.InBars(earlier.FirstBar, earlier.LastBar)}.", $" Впервые он звучит {w.InBars(earlier.FirstBar, earlier.LastBar)}.");
        }

        // Every recurrence is a place, the text's list cut or not; the motif itself is the selection.
        return new MotifFact("structure.recurrences", FactLens.Structure, certainty, text, lineBasis + rule)
        {
            Brief = w.IsRussian
                ? $"Мотив повторяется {w.Times(recurrences.Count)} (точно: {exactCount}, с транспозицией: {recurrences.Count - exactCount})."
                : $"The motif recurs {w.Times(recurrences.Count)} ({exactCount} exact, {recurrences.Count - exactCount} transposed).",
            Places = [.. recurrences.Select((recurrence, i) => new FactPlace(places[i], recurrence.Span, recurrence.NoteIds))],
        };
    }

    private static MotifFact PhraseFact(IReadOnlyList<PhraseSpan> spans, Tick motifStart, Words w)
    {
        string basis = w.T(
            "Phrases run between the boundaries that slurs, rests of a beat or more, repeat signs or the player mark; a bar a phrase starts or ends inside counts for that phrase.",
            "Фразы идут между границами, которые отмечают лиги, паузы в долю и больше, знаки повторения или исполнитель; такт, внутри которого фраза начинается или кончается, относится к этой фразе.");
        if (spans.Count == 1)
        {
            return new MotifFact(
                "structure.phrases",
                FactLens.Structure,
                FactCertainty.Detected,
                w.T(
                    $"No phrase boundary is marked, so the notes read as one span, {w.Bars(spans[0].FirstBar, spans[0].LastBar)}.",
                    $"Границы фраз не отмечены, поэтому ноты читаются одним отрезком: {w.Bars(spans[0].FirstBar, spans[0].LastBar)}."),
                basis + w.T(" Where nothing marks a boundary, none is invented.", " Где границу ничто не отмечает, она не придумывается."))
            {
                Places = [new FactPlace(w.Bars(spans[0].FirstBar, spans[0].LastBar), spans[0].Span, [])],
            };
        }

        int containing = spans.Select((span, index) => (span, index)).Last(entry => entry.span.Span.Onset <= motifStart).index;
        string barCounts = w.Joined(spans.Select(span => span.BarCount.ToString(CultureInfo.InvariantCulture)).ToList());
        string text = w.IsRussian
            ? $"{w.Count(spans.Count, "", "", "фраза", "фразы", "фраз")}: {w.Listed(spans.Select(span => w.Bars(span.FirstBar, span.LastBar)).ToList(), MaxListed, ", ", " ")} "
                + $"({barCounts} {Words.RussianForm(spans[^1].BarCount, "такт", "такта", "тактов")}). "
                + $"Мотив начинается во фразе {containing + 1}."
            : $"{Words.EnglishCount(spans.Count, "phrase", "phrases")}: {w.Listed(spans.Select(span => w.Bars(span.FirstBar, span.LastBar)).ToList(), MaxListed, ", ", " ")} "
                + $"({barCounts} bars). "
                + $"The motif starts in phrase {containing + 1}.";
        bool inferred = spans.Any(span => span.Start?.Provenance is Provenance.Inferred or Provenance.Detected);
        MotifFact fact = inferred
            ? new MotifFact("structure.phrases", FactLens.Structure, FactCertainty.Inferred, text, basis + w.T(
                " Some boundaries rest on a rest alone, which is a reading of the notes, not a mark in the score.",
                " Некоторые границы стоят только по паузе: это прочтение нот, а не знак в нотах."))
            : new MotifFact("structure.phrases", FactLens.Structure, FactCertainty.Detected, text, basis);

        // Every phrase is a place, a stretch of time from its boundary to the next.
        return fact with
        {
            Brief = w.IsRussian
                ? $"{w.Count(spans.Count, "", "", "фраза", "фразы", "фраз")}; мотив начинается во фразе {containing + 1}."
                : $"{Words.EnglishCount(spans.Count, "phrase", "phrases")}; the motif starts in phrase {containing + 1}.",
            Places = [.. spans.Select((span, i) => new FactPlace($"{w.T("phrase", "фраза")} {i + 1}, {w.Bars(span.FirstBar, span.LastBar)}", span.Span, []))],
        };
    }

    private static MotifFact NewMaterialFact(Measures measures, bool inferred, string lineBasis, Words w)
    {
        IReadOnlyList<int> newBars = measures.NewBars;
        int barsWithNotes = measures.BarsWithNotes;
        int repeated = barsWithNotes - newBars.Count;
        string text = w.IsRussian
            ? repeated == 0
                ? $"Каждый такт с нотами приносит новый материал ({w.Count(barsWithNotes, "", "", "такт", "такта", "тактов")}); ни один не повторяет предыдущий."
                : $"Новый материал {InBarList(newBars, w)}; остальные {w.Count(repeated, "", "", "такт повторяет", "такта повторяют", "тактов повторяют")} более ранний такт, точно или с транспозицией."
            : repeated == 0
                ? $"Every bar with notes brings new material ({Words.EnglishCount(barsWithNotes, "bar", "bars")}); none repeats an earlier bar."
                : $"New material {InBarList(newBars, w)}; the other {Words.EnglishCount(repeated, "bar repeats", "bars repeat")} an earlier bar, exactly or transposed.";

        // Every run of new bars is a place: the bars, with the line notes that were compared.
        FactPlace[] places = [.. measures.NewRuns.Select(run => new FactPlace(w.Bars(run.First, run.Last), run.Span, run.NoteIds))];
        return new MotifFact(
            "structure.new-material",
            FactLens.Structure,
            inferred ? FactCertainty.Inferred : FactCertainty.Detected,
            text,
            lineBasis + w.T(
                " Bars are compared by their line's onsets from the bar line and its pitches, or its intervals for a transposed repeat.",
                " Такты сравниваются по атакам линии от тактовой черты и её высотам, а для транспонированного повтора — по интервалам."))
        {
            Places = places,
        };
    }

    /// <summary>"in bars 1–2, 5 and 9": runs of consecutive bars joined; "в тактах 1–2, 5 и 9".</summary>
    private static string InBarList(IReadOnlyList<int> bars, Words w) =>
        w.InBars(w.Joined([.. Words.Runs(bars).Select(run => Words.Range(run.First, run.Last))]), bars.Count == 1);
}