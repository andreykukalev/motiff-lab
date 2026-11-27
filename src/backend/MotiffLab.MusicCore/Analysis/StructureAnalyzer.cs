using MotiffLab.MusicCore.Analysis.Phrasing;
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
internal static partial class StructureAnalyzer
{
    /// <summary>A line of fewer notes matches too much of any piece to be worth searching for.</summary>
    public const int MinNotesForRecurrence = 3;

    /// <param name="notes">The notes of the piece.</param>
    /// <param name="phrases">
    /// The phrase boundaries of the notes that pass the selection's filter, from
    /// <see cref="PhraseBoundaryFinder"/>, including any the player set.
    /// </param>
    /// <param name="words">The language of the facts' text, bases and place labels; English by default.</param>
    public static StructureAnalysis Analyze(IEnumerable<Note> notes, MeterMap meter, MotifSelection selection, IEnumerable<PhraseBoundary> phrases, Words words = default)
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
        IReadOnlyList<Recurrence> recurrences = motifLine.Length < MinNotesForRecurrence ? [] : FindRecurrences(scopeLine, motifLine, meter);
        IReadOnlyList<PhraseSpan> spans = PhraseSpans(scope, phrases, meter);
        IReadOnlyList<int> newBars = NewMaterial(scopeLine, meter);
        var measures = new Measures(
            recurrences,
            motifLine.Length,
            motifLine[0].Span.Onset,
            spans,
            newBars,
            scopeLine.Select(note => meter.BarAt(note.Span.Onset).Number).Distinct().Count(),
            NewMaterialRuns(newBars, scopeLine, meter),
            scope.GroupBy(note => note.Span.Onset).Any(group => group.Count() > 1),
            selection.HandInferred);
        return new StructureAnalysis(recurrences, spans, newBars, Facts(measures, words));
    }

    /// <summary>
    /// Bars of the whole piece whose line (the highest note at each onset) repeats no earlier bar, exactly or
    /// transposed: the new material lane of Piece DNA.
    /// </summary>
    public static IReadOnlyList<int> NewMaterialBars(IEnumerable<Note> notes, MeterMap meter)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        return NewMaterial(Line(notes), meter);
    }

    /// <summary>The highest note at each onset, in time order.</summary>
    internal static Note[] Line(IEnumerable<Note> notes) =>
        [.. notes
            .GroupBy(note => note.Span.Onset)
            .OrderBy(group => group.Key)
            .Select(group => group.OrderByDescending(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal).First())];

    internal static List<Recurrence> FindRecurrences(Note[] line, Note[] motif, MeterMap meter)
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
            (string pitches, string shape) = BarLine(span, notes);
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

    /// <summary>
    /// The two keys a bar's line is compared by: its onsets from the bar line with its pitches (an exact repeat), and with
    /// its intervals (a transposed repeat).
    /// </summary>
    internal static (string Pitches, string Shape) BarLine(Bar bar, IReadOnlyList<Note> notes)
    {
        string onsets = $"{bar.Span.Duration}:{string.Join(",", notes.Select(note => note.Span.Onset.Value - bar.Span.Onset.Value))}";
        string pitches = $"{onsets}/{string.Join(",", notes.Select(note => note.Pitch.MidiNumber))}";
        string shape = $"{onsets}/{string.Join(",", notes.Zip(notes.Skip(1), (from, to) => to.Pitch.MidiNumber - from.Pitch.MidiNumber))}";
        return (pitches, shape);
    }

    /// <summary>The runs of consecutive new bars, each with its time and the line notes in it that were compared.</summary>
    private static List<(int First, int Last, TickSpan Span, IReadOnlyList<string> NoteIds)> NewMaterialRuns(IReadOnlyList<int> newBars, Note[] line, MeterMap meter) =>
        [.. Words.Runs(newBars).Select(run =>
        {
            TickSpan bars = TickSpan.FromBounds(meter.GetBar(run.First).Span.Onset, meter.GetBar(run.Last).Span.End);
            IReadOnlyList<string> noteIds = [.. line.Where(note => bars.Contains(note.Span.Onset)).Select(note => note.Id)];
            return (run.First, run.Last, bars, noteIds);
        })];

    /// <summary>What the facts are written from (<c>StructureAnalyzer.Facts.cs</c>): the findings and the counts their sentences name.</summary>
    /// <param name="MotifNotes">Notes in the motif's line; at least one.</param>
    /// <param name="MotifStart">The first onset of the motif's line.</param>
    /// <param name="BarsWithNotes">Bars where a note of the searched line starts.</param>
    /// <param name="NewRuns">The runs of <paramref name="NewBars"/>, each with its time and line notes.</param>
    /// <param name="Chords">Notes of the searched scope sound together, so lines are the highest note at each onset.</param>
    /// <param name="HandInferred">The selection's hand is inferred.</param>
    private sealed record Measures(
        IReadOnlyList<Recurrence> Recurrences,
        int MotifNotes,
        Tick MotifStart,
        IReadOnlyList<PhraseSpan> Phrases,
        IReadOnlyList<int> NewBars,
        int BarsWithNotes,
        IReadOnlyList<(int First, int Last, TickSpan Span, IReadOnlyList<string> NoteIds)> NewRuns,
        bool Chords,
        bool HandInferred);
}