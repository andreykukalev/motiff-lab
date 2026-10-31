using System.Globalization;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>Another place in the piece where the motif's line is heard with the same rhythm.</summary>
/// <param name="Span">From the first onset of the recurrence to the end of its last note.</param>
/// <param name="Transposition">Semitones from the motif to the recurrence: 0 for an exact recurrence, positive when higher.</param>
/// <param name="NoteIds">The line notes that make up the recurrence, in order.</param>
public sealed record Recurrence(TickSpan Span, int FirstBar, int LastBar, int Transposition, IReadOnlyList<string> NoteIds)
{
    public bool IsExact => Transposition == 0;
}

/// <summary>A phrase: from one boundary, or the first note, to the next boundary, or the end of the last note.</summary>
/// <param name="Start">How the phrase's start is known; null for the first phrase, which starts with the first note.</param>
public sealed record PhraseSpan(TickSpan Span, int FirstBar, int LastBar, Attribution? Start)
{
    /// <summary>Bars the phrase touches, counting a bar it starts or ends inside.</summary>
    public int BarCount => LastBar - FirstBar + 1;
}

/// <summary>Where the motif recurs, how the piece is phrased, and which bars bring new material.</summary>
/// <param name="Recurrences">In time order; the motif itself is not listed.</param>
/// <param name="Phrases">In time order, covering the notes the motif was picked from.</param>
/// <param name="NewMaterialBars">Bars with notes that do not repeat an earlier bar, exactly or transposed.</param>
/// <param name="Facts">The facts, all in the structure lens: recurrences, phrases, new material.</param>
public sealed record StructureAnalysis(
    IReadOnlyList<Recurrence> Recurrences,
    IReadOnlyList<PhraseSpan> Phrases,
    IReadOnlyList<int> NewMaterialBars,
    IReadOnlyList<MotifFact> Facts);

/// <summary>
/// Repetition and phrasing around a motif (S1-A4): where the motif recurs in the piece, exactly or
/// transposed; the phrase spans and their lengths; and which bars are new material. No AI: every
/// fact is measured from the notes and the phrase boundaries.
/// </summary>
/// <remarks>
/// <para>
/// The search runs on the notes that pass the motif's filter, for example the right hand, and
/// compares melody lines: the highest note at each onset. A recurrence has the same onsets
/// relative to its first note, with nothing starting in between, and every pitch moved by the same
/// number of semitones. Note lengths are not compared, so a recurrence played shorter or longer
/// still counts. Recurrences do not overlap the motif or each other.
/// </para>
/// <para>
/// A bar repeats an earlier bar when its line has the same onsets from the bar line and the same
/// pitches, or the same intervals for a transposed repeat (two or more onsets). Bars with no onset
/// are neither new nor repeated.
/// </para>
/// <para>
/// Analysis is pure: the same notes, meter, selection and boundaries give the same result.
/// </para>
/// </remarks>
public static class StructureAnalyzer
{
    /// <summary>A line of fewer notes matches too much of any piece to be worth searching for.</summary>
    public const int MinNotesForRecurrence = 3;

    /// <summary>A list in a fact names at most this many places, then says how many more there are.</summary>
    private const int MaxListed = 6;

    /// <param name="notes">The notes of the piece.</param>
    /// <param name="phrases">
    /// The phrase boundaries of the notes that pass the selection's filter, from
    /// <see cref="PhraseBoundaryFinder"/>, including any the player set.
    /// </param>
    /// <param name="words">The language of the facts' text, bases and place labels; English by default.</param>
    public static StructureAnalysis Analyze(IEnumerable<Note> notes, MeterMap meter, MotifSelection selection, IEnumerable<PhraseBoundary> phrases, Words words = default)
    {
        Words w = words;
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(phrases);

        Note[] all = [.. notes];
        var chosenIds = selection.NoteIds.ToHashSet(StringComparer.Ordinal);
        Note[] motif = [.. all.Where(note => chosenIds.Contains(note.Id))];
        if (motif.Length == 0)
        {
            return new StructureAnalysis([], [], [], []);
        }

        Note[] scope = [.. all.Where(selection.Filter.Matches)];
        Note[] scopeLine = Line(scope);
        Note[] motifLine = Line(motif);
        bool chords = scope.GroupBy(note => note.Span.Onset).Any(group => group.Count() > 1);
        bool inferred = chords || selection.HandInferred;
        string lineBasis = (selection.HandInferred ? w.T("The hand is inferred, so which notes are searched is inferred too. ", "Рука предположена, поэтому и то, среди каких нот идёт поиск, предположено. ") : "")
            + (chords
                ? w.T("Notes sound together, so lines are taken as the highest note at each onset.", "Ноты звучат вместе, поэтому линией считается самая высокая нота каждой атаки.")
                : w.T("Lines are read from the notes, one at a time.", "Линии прочитаны по нотам, одна за другой."));

        IReadOnlyList<Recurrence> recurrences = motifLine.Length < MinNotesForRecurrence ? [] : FindRecurrences(scopeLine, motifLine, meter);
        IReadOnlyList<PhraseSpan> spans = PhraseSpans(scope, phrases, meter);
        IReadOnlyList<int> newBars = NewMaterial(scopeLine, meter);

        var facts = new List<MotifFact>
        {
            RecurrenceFact(recurrences, motifLine, inferred, lineBasis, w),
        };
        if (spans.Count > 0)
        {
            facts.Add(PhraseFact(spans, motifLine[0].Span.Onset, w));
        }

        if (scopeLine.Length > 0)
        {
            facts.Add(NewMaterialFact(newBars, scopeLine, meter, inferred, lineBasis, w));
        }

        return new StructureAnalysis(recurrences, spans, newBars, facts);
    }

    /// <summary>The highest note at each onset, in time order.</summary>
    private static Note[] Line(IEnumerable<Note> notes) =>
        [.. notes
            .GroupBy(note => note.Span.Onset)
            .OrderBy(group => group.Key)
            .Select(group => group.OrderByDescending(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal).First())];

    private static List<Recurrence> FindRecurrences(Note[] line, Note[] motif, MeterMap meter)
    {
        Tick motifStart = motif[0].Span.Onset;
        Tick motifLast = motif[^1].Span.Onset;
        long[] offsets = [.. motif.Select(note => note.Span.Onset.Value - motifStart.Value)];

        var found = new List<Recurrence>();
        Tick? previousLast = null;
        for (int i = 0; i + motif.Length <= line.Length; i++)
        {
            Tick start = line[i].Span.Onset;
            int shift = line[i].Pitch.MidiNumber - motif[0].Pitch.MidiNumber;
            bool matches = true;
            for (int k = 1; k < motif.Length && matches; k++)
            {
                matches = line[i + k].Span.Onset.Value - start.Value == offsets[k]
                    && line[i + k].Pitch.MidiNumber - motif[k].Pitch.MidiNumber == shift;
            }

            if (!matches)
            {
                continue;
            }

            Note[] notes = line[i..(i + motif.Length)];
            Tick end = notes.Max(note => note.Span.End);

            // Onsets decide overlap, so a recurrence may start while the motif's last note still rings.
            Tick lastOnset = notes[^1].Span.Onset;
            if ((start <= motifLast && lastOnset >= motifStart) || start <= previousLast)
            {
                continue;
            }

            found.Add(new Recurrence(
                TickSpan.FromBounds(start, end),
                meter.BarAt(start).Number,
                meter.BarAt(lastOnset).Number,
                shift,
                [.. notes.Select(note => note.Id)]));
            previousLast = lastOnset;
        }

        return found;
    }

    private static List<PhraseSpan> PhraseSpans(Note[] scope, IEnumerable<PhraseBoundary> phrases, MeterMap meter)
    {
        if (scope.Length == 0)
        {
            return [];
        }

        Tick first = scope.Min(note => note.Span.Onset);
        Tick end = scope.Max(note => note.Span.End);
        PhraseBoundary[] boundaries = [.. phrases
            .Where(boundary => boundary.At > first && boundary.At < end)
            .GroupBy(boundary => boundary.At)
            .Select(group => group.First())
            .OrderBy(boundary => boundary.At)];

        var spans = new List<PhraseSpan>();
        Tick start = first;
        Attribution? attribution = null;
        foreach (PhraseBoundary? boundary in boundaries.Append<PhraseBoundary?>(null))
        {
            Tick stop = boundary?.At ?? end;
            spans.Add(new PhraseSpan(TickSpan.FromBounds(start, stop), meter.BarAt(start).Number, meter.BarAt(new Tick(stop.Value - 1)).Number, attribution));
            if (boundary is not null)
            {
                start = boundary.At;
                attribution = boundary.Attribution;
            }
        }

        return spans;
    }

    private static List<int> NewMaterial(Note[] line, MeterMap meter)
    {
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var shapes = new HashSet<string>(StringComparer.Ordinal);
        var newBars = new List<int>();
        foreach (IGrouping<int, Note> bar in line.GroupBy(note => meter.BarAt(note.Span.Onset).Number))
        {
            Bar span = meter.GetBar(bar.Key);
            Note[] notes = [.. bar];
            string onsets = $"{span.Span.Duration}:{string.Join(",", notes.Select(note => note.Span.Onset.Value - span.Span.Onset.Value))}";
            string pitches = $"{onsets}/{string.Join(",", notes.Select(note => note.Pitch.MidiNumber))}";
            string shape = $"{onsets}/{string.Join(",", notes.Zip(notes.Skip(1), (from, to) => to.Pitch.MidiNumber - from.Pitch.MidiNumber))}";
            bool repeats = exact.Contains(pitches) || (notes.Length > 1 && shapes.Contains(shape));
            if (!repeats)
            {
                newBars.Add(bar.Key);
            }

            exact.Add(pitches);
            if (notes.Length > 1)
            {
                shapes.Add(shape);
            }
        }

        return newBars;
    }

    private static MotifFact RecurrenceFact(IReadOnlyList<Recurrence> recurrences, Note[] motifLine, bool inferred, string lineBasis, Words w)
    {
        string rule = w.T(
            " A recurrence has the same onsets from its first note and every pitch moved by the same amount; note lengths are not compared.",
            " У повторения те же атаки от первой ноты, и все высоты сдвинуты на одно и то же расстояние; длины нот не сравниваются.");
        FactCertainty certainty = inferred ? FactCertainty.Inferred : FactCertainty.Detected;
        if (motifLine.Length < MinNotesForRecurrence)
        {
            return new MotifFact(
                "structure.recurrences",
                FactLens.Structure,
                certainty,
                w.IsRussian
                    ? $"В линии мотива {w.Count(motifLine.Length, "", "", "нота", "ноты", "нот")}: слишком мало для поиска повторений, нужно {MinNotesForRecurrence}."
                    : $"With {Count(motifLine.Length, "note", "notes")} in its line, the motif is too short to search for recurrences; it takes {MinNotesForRecurrence}.",
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
                ? $"точно {InBars(recurrence.FirstBar, recurrence.LastBar, w)}"
                : $"на {MotifAnalyzer.IntervalBy(recurrence.Transposition, w)} {(recurrence.Transposition > 0 ? "выше" : "ниже")} {InBars(recurrence.FirstBar, recurrence.LastBar, w)}"
            : recurrence.IsExact
                ? $"exactly {InBars(recurrence.FirstBar, recurrence.LastBar, w)}"
                : $"{WithArticle(MotifAnalyzer.IntervalName(recurrence.Transposition))} {(recurrence.Transposition > 0 ? "higher" : "lower")} {InBars(recurrence.FirstBar, recurrence.LastBar, w)}")];
        string list = places.Length <= MaxListed
            ? w.Joined(places)
            : w.T($"{string.Join(", ", places.Take(MaxListed))} and {places.Length - MaxListed} more times", $"{string.Join(", ", places.Take(MaxListed))} и ещё {w.Times(places.Length - MaxListed)}");
        int exactCount = recurrences.Count(recurrence => recurrence.IsExact);
        string text = w.IsRussian
            ? $"Мотив повторяется {w.Times(recurrences.Count)} (точно: {exactCount}, с транспозицией: {recurrences.Count - exactCount}): {list}."
            : $"The motif recurs {w.Times(recurrences.Count)} ({exactCount} exact, {recurrences.Count - exactCount} transposed): {list}.";

        Recurrence? earlier = recurrences.FirstOrDefault(recurrence => recurrence.Span.Onset < motifLine[0].Span.Onset);
        if (earlier is not null)
        {
            text += w.T($" It is first heard {InBars(earlier.FirstBar, earlier.LastBar, w)}.", $" Впервые он звучит {InBars(earlier.FirstBar, earlier.LastBar, w)}.");
        }

        // Every recurrence is a place, the text's list cut or not; the motif itself is the selection.
        return new MotifFact("structure.recurrences", FactLens.Structure, certainty, text, lineBasis + rule)
        {
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
                    $"No phrase boundary is marked, so the notes read as one span, {Bars(spans[0].FirstBar, spans[0].LastBar, w)}.",
                    $"Границы фраз не отмечены, поэтому ноты читаются одним отрезком: {Bars(spans[0].FirstBar, spans[0].LastBar, w)}."),
                basis + w.T(" Where nothing marks a boundary, none is invented.", " Где границу ничто не отмечает, она не придумывается."))
            {
                Places = [new FactPlace(Bars(spans[0].FirstBar, spans[0].LastBar, w), spans[0].Span, [])],
            };
        }

        int containing = spans.Select((span, index) => (span, index)).Last(entry => entry.span.Span.Onset <= motifStart).index;
        string barCounts = w.Joined(spans.Select(span => span.BarCount.ToString(CultureInfo.InvariantCulture)).ToList());
        string text = w.IsRussian
            ? $"{w.Count(spans.Count, "", "", "фраза", "фразы", "фраз")}: {Listed(spans.Select(span => Bars(span.FirstBar, span.LastBar, w)).ToList(), w)} "
                + $"({barCounts} {Words.RussianForm(spans[^1].BarCount, "такт", "такта", "тактов")}). "
                + $"Мотив начинается во фразе {containing + 1}."
            : $"{Count(spans.Count, "phrase", "phrases")}: {Listed(spans.Select(span => Bars(span.FirstBar, span.LastBar, w)).ToList(), w)} "
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
            Places = [.. spans.Select((span, i) => new FactPlace($"{w.T("phrase", "фраза")} {i + 1}, {Bars(span.FirstBar, span.LastBar, w)}", span.Span, []))],
        };
    }

    private static MotifFact NewMaterialFact(IReadOnlyList<int> newBars, Note[] line, MeterMap meter, bool inferred, string lineBasis, Words w)
    {
        int barsWithNotes = line.Select(note => meter.BarAt(note.Span.Onset).Number).Distinct().Count();
        int repeated = barsWithNotes - newBars.Count;
        string text = w.IsRussian
            ? repeated == 0
                ? $"Каждый такт с нотами приносит новый материал ({w.Count(barsWithNotes, "", "", "такт", "такта", "тактов")}); ни один не повторяет предыдущий."
                : $"Новый материал {InBarList(newBars, w)}; остальные {w.Count(repeated, "", "", "такт повторяет", "такта повторяют", "тактов повторяют")} более ранний такт, точно или с транспозицией."
            : repeated == 0
                ? $"Every bar with notes brings new material ({Count(barsWithNotes, "bar", "bars")}); none repeats an earlier bar."
                : $"New material {InBarList(newBars, w)}; the other {Count(repeated, "bar repeats", "bars repeat")} an earlier bar, exactly or transposed.";

        // Every run of new bars is a place: the bars, with the line notes that were compared.
        FactPlace[] places = [.. Runs(newBars).Select(run =>
        {
            TickSpan bars = TickSpan.FromBounds(meter.GetBar(run.First).Span.Onset, meter.GetBar(run.Last).Span.End);
            return new FactPlace(Bars(run.First, run.Last, w), bars, [.. line.Where(note => bars.Contains(note.Span.Onset)).Select(note => note.Id)]);
        })];
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

    /// <summary>"bar 5", "bars 5–6"; "такт 5", "такты 5–6".</summary>
    private static string Bars(int first, int last, Words w) => first == last
        ? $"{w.T("bar", "такт")} {first}"
        : $"{w.T("bars", "такты")} {first}–{last}";

    /// <summary>"in bar 5", "in bars 5–6"; "в такте 5", "в тактах 5–6".</summary>
    private static string InBars(int first, int last, Words w) => first == last
        ? $"{w.T("in bar", "в такте")} {first}"
        : $"{w.T("in bars", "в тактах")} {first}–{last}";

    /// <summary>"in bars 1–2, 5 and 9": runs of consecutive bars joined; "в тактах 1–2, 5 и 9".</summary>
    private static string InBarList(IReadOnlyList<int> bars, Words w) =>
        (bars.Count == 1 ? w.T("in bar ", "в такте ") : w.T("in bars ", "в тактах ")) + w.Joined([.. Runs(bars).Select(run => run.First == run.Last ? $"{run.First}" : $"{run.First}–{run.Last}")]);

    /// <summary>Runs of consecutive bars, in order.</summary>
    private static List<(int First, int Last)> Runs(IReadOnlyList<int> bars)
    {
        var runs = new List<(int First, int Last)>();
        for (int i = 0; i < bars.Count;)
        {
            int j = i;
            while (j + 1 < bars.Count && bars[j + 1] == bars[j] + 1)
            {
                j++;
            }

            runs.Add((bars[i], bars[j]));
            i = j + 1;
        }

        return runs;
    }

    private static string Listed(IReadOnlyList<string> items, Words w) =>
        items.Count <= MaxListed
            ? string.Join(", ", items)
            : w.T($"{string.Join(", ", items.Take(MaxListed))} and {items.Count - MaxListed} more", $"{string.Join(", ", items.Take(MaxListed))} и ещё {items.Count - MaxListed}");

    private static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    private static string WithArticle(string noun) => noun[0] switch
    {
        >= '0' and <= '9' => noun,
        'a' or 'e' or 'i' or 'o' or 'u' => "an " + noun,
        _ => "a " + noun,
    };
}