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
    public static StructureAnalysis Analyze(IEnumerable<Note> notes, MeterMap meter, MotifSelection selection, IEnumerable<PhraseBoundary> phrases)
    {
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
        string lineBasis = (selection.HandInferred ? "The hand is inferred, so which notes are searched is inferred too. " : "")
            + (chords ? "Notes sound together, so lines are taken as the highest note at each onset." : "Lines are read from the notes, one at a time.");

        IReadOnlyList<Recurrence> recurrences = motifLine.Length < MinNotesForRecurrence ? [] : FindRecurrences(scopeLine, motifLine, meter);
        IReadOnlyList<PhraseSpan> spans = PhraseSpans(scope, phrases, meter);
        IReadOnlyList<int> newBars = NewMaterial(scopeLine, meter);

        var facts = new List<MotifFact>
        {
            RecurrenceFact(recurrences, motifLine, inferred, lineBasis),
        };
        if (spans.Count > 0)
        {
            facts.Add(PhraseFact(spans, motifLine[0].Span.Onset));
        }

        if (scopeLine.Length > 0)
        {
            facts.Add(NewMaterialFact(newBars, scopeLine, meter, inferred, lineBasis));
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

    private static MotifFact RecurrenceFact(IReadOnlyList<Recurrence> recurrences, Note[] motifLine, bool inferred, string lineBasis)
    {
        const string Rule = " A recurrence has the same onsets from its first note and every pitch moved by the same amount; note lengths are not compared.";
        FactCertainty certainty = inferred ? FactCertainty.Inferred : FactCertainty.Detected;
        if (motifLine.Length < MinNotesForRecurrence)
        {
            return new MotifFact(
                "structure.recurrences",
                FactLens.Structure,
                certainty,
                $"With {Count(motifLine.Length, "note", "notes")} in its line, the motif is too short to search for recurrences; it takes {MinNotesForRecurrence}.",
                lineBasis + Rule);
        }

        if (recurrences.Count == 0)
        {
            return new MotifFact("structure.recurrences", FactLens.Structure, certainty, "The motif does not recur elsewhere, exactly or transposed.", lineBasis + Rule);
        }

        string[] places = [.. recurrences.Select(recurrence => recurrence.IsExact
            ? $"exactly in {Bars(recurrence.FirstBar, recurrence.LastBar)}"
            : $"{WithArticle(MotifAnalyzer.IntervalName(recurrence.Transposition))} {(recurrence.Transposition > 0 ? "higher" : "lower")} in {Bars(recurrence.FirstBar, recurrence.LastBar)}")];
        string list = places.Length <= MaxListed
            ? Joined(places)
            : $"{string.Join(", ", places.Take(MaxListed))} and {places.Length - MaxListed} more times";
        int exactCount = recurrences.Count(recurrence => recurrence.IsExact);
        string text = $"The motif recurs {Times(recurrences.Count)} ({exactCount} exact, {recurrences.Count - exactCount} transposed): {list}.";

        Recurrence? earlier = recurrences.FirstOrDefault(recurrence => recurrence.Span.Onset < motifLine[0].Span.Onset);
        if (earlier is not null)
        {
            text += $" It is first heard in {Bars(earlier.FirstBar, earlier.LastBar)}.";
        }

        return new MotifFact("structure.recurrences", FactLens.Structure, certainty, text, lineBasis + Rule);
    }

    private static MotifFact PhraseFact(IReadOnlyList<PhraseSpan> spans, Tick motifStart)
    {
        const string Basis = "Phrases run between the boundaries that slurs, rests of a beat or more, repeat signs or the player mark; a bar a phrase starts or ends inside counts for that phrase.";
        if (spans.Count == 1)
        {
            return new MotifFact(
                "structure.phrases",
                FactLens.Structure,
                FactCertainty.Detected,
                $"No phrase boundary is marked, so the notes read as one span, {Bars(spans[0].FirstBar, spans[0].LastBar)}.",
                Basis + " Where nothing marks a boundary, none is invented.");
        }

        int containing = spans.Select((span, index) => (span, index)).Last(entry => entry.span.Span.Onset <= motifStart).index;
        string text = $"{Count(spans.Count, "phrase", "phrases")}: {Listed(spans.Select(span => Bars(span.FirstBar, span.LastBar)).ToList())} "
            + $"({Joined(spans.Select(span => span.BarCount.ToString(CultureInfo.InvariantCulture)).ToList())} bars). "
            + $"The motif starts in phrase {containing + 1}.";
        bool inferred = spans.Any(span => span.Start?.Provenance is Provenance.Inferred or Provenance.Detected);
        return inferred
            ? new MotifFact("structure.phrases", FactLens.Structure, FactCertainty.Inferred, text, Basis + " Some boundaries rest on a rest alone, which is a reading of the notes, not a mark in the score.")
            : new MotifFact("structure.phrases", FactLens.Structure, FactCertainty.Detected, text, Basis);
    }

    private static MotifFact NewMaterialFact(IReadOnlyList<int> newBars, Note[] line, MeterMap meter, bool inferred, string lineBasis)
    {
        int barsWithNotes = line.Select(note => meter.BarAt(note.Span.Onset).Number).Distinct().Count();
        int repeated = barsWithNotes - newBars.Count;
        string text = repeated == 0
            ? $"Every bar with notes brings new material ({Count(barsWithNotes, "bar", "bars")}); none repeats an earlier bar."
            : $"New material in {BarList(newBars)}; the other {Count(repeated, "bar repeats", "bars repeat")} an earlier bar, exactly or transposed.";
        return new MotifFact(
            "structure.new-material",
            FactLens.Structure,
            inferred ? FactCertainty.Inferred : FactCertainty.Detected,
            text,
            lineBasis + " Bars are compared by their line's onsets from the bar line and its pitches, or its intervals for a transposed repeat.");
    }

    /// <summary>"bar 5", "bars 5–6".</summary>
    private static string Bars(int first, int last) => first == last ? $"bar {first}" : $"bars {first}–{last}";

    /// <summary>"bars 1–2, 5 and 9": runs of consecutive bars joined.</summary>
    private static string BarList(IReadOnlyList<int> bars)
    {
        var runs = new List<string>();
        for (int i = 0; i < bars.Count;)
        {
            int j = i;
            while (j + 1 < bars.Count && bars[j + 1] == bars[j] + 1)
            {
                j++;
            }

            runs.Add(j == i ? $"{bars[i]}" : $"{bars[i]}–{bars[j]}");
            i = j + 1;
        }

        return (bars.Count == 1 ? "bar " : "bars ") + Joined(runs);
    }

    private static string Times(int count) => count switch
    {
        1 => "once",
        2 => "twice",
        _ => $"{count} times",
    };

    private static string Listed(IReadOnlyList<string> items) =>
        items.Count <= MaxListed ? string.Join(", ", items) : $"{string.Join(", ", items.Take(MaxListed))} and {items.Count - MaxListed} more";

    /// <summary>"a", "a and b", "a, b and c".</summary>
    private static string Joined(IReadOnlyList<string> items) =>
        items.Count == 1 ? items[0] : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";

    private static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    private static string WithArticle(string noun) => noun[0] switch
    {
        >= '0' and <= '9' => noun,
        'a' or 'e' or 'i' or 'o' or 'u' => "an " + noun,
        _ => "a " + noun,
    };
}