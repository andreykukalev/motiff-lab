using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>What in the source supports a phrase boundary (S1-I5).</summary>
public enum PhraseEvidence
{
    /// <summary>A phrase slur starts or ends here. Imported.</summary>
    Slur,

    /// <summary>A beat or more of silence comes before this note. Derived from the notes.</summary>
    Rest,

    /// <summary>A repeat sign stands on the bar line here. Imported.</summary>
    RepeatSign,

    /// <summary>
    /// The passage that opens the piece, or that starts at another boundary, comes back here note for
    /// note. Derived from the notes, so inferred.
    /// </summary>
    Repetition,

    /// <summary>
    /// Inside a stretch that runs longer than a phrase with no boundary (<see cref="PhraseMap.LongPhraseBars"/>), a
    /// passage starts again at once, note for note: the start of a phrase answered by one that starts the same, or
    /// either edge of a short figure played twice in a row. Derived from the notes, so inferred.
    /// </summary>
    Restatement,
}

/// <summary>A slur in the source, from the onset of its first note to the onset of its last.</summary>
/// <param name="Track">The track of the notes the slur is drawn over.</param>
public sealed record SlurMark(int Track, Tick Start, Tick Stop);

/// <summary>The phrase marks a source states. MIDI has none; MusicXML has slurs and repeat signs.</summary>
public sealed record PhraseMarks(IReadOnlyList<SlurMark> Slurs, IReadOnlyList<Tick> RepeatSigns)
{
    public static PhraseMarks None { get; } = new([], []);
}

/// <summary>A place where a new phrase starts: the onset of its first note.</summary>
/// <param name="At">The onset of the first note after the boundary.</param>
/// <param name="Evidence">Everything in the source that supports the boundary, in enum order; empty for a boundary only the player set.</param>
/// <param name="Attribution">
/// Imported when a slur or repeat sign in the source marks it; inferred, with
/// <see cref="PhraseBoundaryFinder.RestConfidence"/>, when only a rest does; user-confirmed or
/// user-edited when the player set it (<see cref="PhraseBoundaryFinder.WithPlayerBoundaries"/>).
/// </param>
public sealed record PhraseBoundary(Tick At, IReadOnlyList<PhraseEvidence> Evidence, Attribution Attribution);

/// <summary>
/// Marks phrase boundaries only where the source supports them (S1-I5): phrase slurs, rests of a
/// beat or more, repeat signs, and a passage that comes back note for note. Where the source gives
/// no evidence, no boundary is invented.
/// </summary>
/// <remarks>
/// <para>
/// Only outermost slurs count as phrase marks; a slur inside another (a legato slur within a
/// phrase) does not. A slur marks a boundary at its first note and after its last. A beat is the
/// beat of the time signature where the silence starts. A repeat sign marks the first note at or
/// after its bar line. Boundaries before or at the first note are left out, since a piece's start
/// is not a boundary.
/// </para>
/// <para>
/// A repetition is read on the top line, the highest note at each onset: when the
/// <see cref="MinRepeatNotes"/> onsets from the start of the piece, or from a boundary found
/// another way, come back later with the same pitches and the same time between them, the place
/// they come back is a boundary, unless one is already less than a beat away. Boundaries found
/// this way are searched in turn, so a passage that comes back twice marks both returns.
/// </para>
/// <para>
/// A stretch between boundaries that runs longer than a phrase (<see cref="PhraseMap.LongPhraseBars"/> bars) is read
/// again at its bar lines (#347). A passage from one of its bar lines that starts again at once, note for note, from a
/// later bar line of the stretch makes a restatement: when it starts again after <see cref="MinPhraseBars"/> to
/// <see cref="PhraseMap.LongPhraseBars"/> bars, the two are phrases of their own, the first marked as a restatement and
/// the second as a repetition; when a shorter figure is played twice in a row, whole, the two make one phrase, marked at
/// both edges as a restatement. A bar line of the stretch where a passage from an earlier bar line comes back is a
/// repetition. Only the first bar line of a passage that comes back counts, not the ones inside it: the onsets just before the two
/// differ.
/// </para>
/// <para>
/// Last, a phrase between two others that would lie within one bar (a second ending, a bar of chords between rests)
/// joins the phrase after it: the boundary between them is left out, unless a slur marks it.
/// </para>
/// <para>
/// Pass the notes the phrases are wanted for, for example one hand or track: silences are
/// measured across those notes only, and only slurs on their tracks count.
/// The result is pure: the same notes, meter and marks give the same boundaries.
/// </para>
/// </remarks>
public static class PhraseBoundaryFinder
{
    /// <summary>A beat of rest often ends a phrase, but not always, so a boundary from a rest alone is not certain.</summary>
    public const double RestConfidence = 0.7;

    /// <summary>A passage that comes back often starts a phrase again, but a figure can also repeat inside one, so it is less certain than a rest.</summary>
    public const double RepetitionConfidence = 0.6;

    /// <summary>The fewest onsets that must come back note for note, so a short figure repeating inside a phrase marks nothing.</summary>
    public const int MinRepeatNotes = 8;

    /// <summary>A passage that starts again at once is less sure than one that brings back an opening: a figure can also run on.</summary>
    public const double RestatementConfidence = 0.5;

    /// <summary>The fewest bars a phrase usually runs (decision record 0018, section 3: phrases are 4–8 bars).</summary>
    public const int MinPhraseBars = 4;

    public static IReadOnlyList<PhraseBoundary> Find(IEnumerable<Note> notes, MeterMap meter, PhraseMarks marks)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(marks);

        Note[] sorted = [.. notes.OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Id, StringComparer.Ordinal)];
        if (sorted.Length == 0)
        {
            return [];
        }

        Tick[] onsets = [.. sorted.Select(static note => note.Span.Onset).Distinct()];
        var found = new SortedDictionary<Tick, SortedSet<PhraseEvidence>>();

        void Add(Tick? at, PhraseEvidence evidence)
        {
            if (at is not { } tick || tick <= onsets[0])
            {
                return;
            }

            if (!found.TryGetValue(tick, out SortedSet<PhraseEvidence>? set))
            {
                found[tick] = set = [];
            }

            set.Add(evidence);
        }

        foreach (Tick? at in SlurEdges(sorted, onsets, marks))
        {
            Add(at, PhraseEvidence.Slur);
        }

        foreach (Tick at in Rests(sorted, meter))
        {
            Add(at, PhraseEvidence.Rest);
        }

        // Repeat signs.
        foreach (Tick sign in marks.RepeatSigns)
        {
            Add(FirstOnsetFrom(onsets, sign), PhraseEvidence.RepeatSign);
        }

        // Repetitions: what starts the piece or a boundary, coming back note for note.
        (Tick At, int Pitch)[] line = TopLine(sorted);
        foreach (Tick at in Repetitions(line, meter, found.Keys))
        {
            Add(at, PhraseEvidence.Repetition);
        }

        // Stretches longer than a phrase, read again at their bar lines.
        Tick end = sorted.Max(static note => note.Span.End);
        foreach ((Tick at, PhraseEvidence evidence) in InsideLongStretches(line, meter, [.. found.Keys], end))
        {
            Add(at, evidence);
        }

        JoinOneBarPhrases(found, meter, end);
        return [.. found.Select(static entry => new PhraseBoundary(
            entry.Key,
            [.. entry.Value],
            Attributed(entry.Value)))];
    }

    /// <summary>The first onset at the start of each outermost slur of a track and the first onset after its end.</summary>
    private static IEnumerable<Tick?> SlurEdges(Note[] sorted, Tick[] onsets, PhraseMarks marks)
    {
        var tracks = sorted.Select(static note => note.Track).ToHashSet();
        SlurMark[] slurs = [.. marks.Slurs.Where(slur => tracks.Contains(slur.Track)).Distinct()];
        foreach (SlurMark slur in slurs)
        {
            bool nested = slurs.Any(other => other != slur && other.Track == slur.Track && other.Start <= slur.Start && slur.Stop <= other.Stop);
            if (!nested)
            {
                yield return FirstOnsetFrom(onsets, slur.Start);
                yield return FirstOnsetFrom(onsets, slur.Stop.Offset(1));
            }
        }
    }

    /// <summary>The onsets after a beat or more where none of the notes sounds.</summary>
    private static IEnumerable<Tick> Rests(Note[] sorted, MeterMap meter)
    {
        Tick soundingUntil = sorted[0].Span.End;
        foreach (Note note in sorted.Skip(1))
        {
            if (note.Span.Onset.Value - soundingUntil.Value >= meter.SignatureAt(soundingUntil).BeatTicks)
            {
                yield return note.Span.Onset;
            }

            if (note.Span.End > soundingUntil)
            {
                soundingUntil = note.Span.End;
            }
        }
    }

    /// <summary>Imported when the file marks it; otherwise inferred, as sure as its surest evidence.</summary>
    private static Attribution Attributed(SortedSet<PhraseEvidence> evidence) =>
        evidence.Any(static mark => mark is PhraseEvidence.Slur or PhraseEvidence.RepeatSign) ? Attribution.Imported
        : evidence.Contains(PhraseEvidence.Rest) ? Attribution.Inferred(RestConfidence)
        : evidence.Contains(PhraseEvidence.Repetition) ? Attribution.Inferred(RepetitionConfidence)
        : Attribution.Inferred(RestatementConfidence);

    /// <summary>The top line: the highest note at each onset, in time order.</summary>
    private static (Tick At, int Pitch)[] TopLine(Note[] sorted) =>
        [.. sorted
            .GroupBy(static note => note.Span.Onset)
            .Select(static onset => (onset.Key, onset.Max(static note => note.Pitch.MidiNumber)))];

    /// <summary>
    /// The restatements and repetitions at the bar lines of every stretch between <paramref name="boundaries"/> that runs
    /// longer than <see cref="PhraseMap.LongPhraseBars"/> bars, each less than a beat from no boundary or cut already known.
    /// </summary>
    private static List<(Tick At, PhraseEvidence Evidence)> InsideLongStretches((Tick At, int Pitch)[] line, MeterMap meter, Tick[] boundaries, Tick end)
    {
        var stretches = new LongStretches(line, meter, boundaries, end);
        Tick[] edges = [line[0].At, .. boundaries.Where(tick => tick > line[0].At), end];
        for (int k = 0; k + 1 < edges.Length; k++)
        {
            int firstBar = meter.BarAt(edges[k]).Number;
            int lastBar = meter.BarAt(new Tick(edges[k + 1].Value - 1)).Number;
            if (lastBar - firstBar + 1 > PhraseMap.LongPhraseBars)
            {
                stretches.Read(edges[k], edges[k + 1]);
            }
        }

        return stretches.Cuts;
    }

    /// <summary>The bar lines of the long stretches of one top line, read for passages that start again or come back.</summary>
    private sealed class LongStretches
    {
        private readonly (Tick At, int Pitch)[] _line;
        private readonly MeterMap _meter;

        /// <summary>The bar lines a note starts on: bar number to the index of its onset in the line.</summary>
        private readonly SortedDictionary<int, int> _starts = [];
        private readonly SortedSet<Tick> _known;

        public LongStretches((Tick At, int Pitch)[] line, MeterMap meter, Tick[] boundaries, Tick end)
        {
            _line = line;
            _meter = meter;
            _known = new SortedSet<Tick>(boundaries) { line[0].At };
            var index = new Dictionary<Tick, int>();
            for (int i = 0; i < line.Length; i++)
            {
                index[line[i].At] = i;
            }

            foreach (Bar bar in meter.BarsBefore(end))
            {
                if (index.TryGetValue(bar.Span.Onset, out int at))
                {
                    _starts[bar.Number] = at;
                }
            }
        }

        /// <summary>The cuts found so far, in the order they were found.</summary>
        public List<(Tick At, PhraseEvidence Evidence)> Cuts { get; } = [];

        /// <summary>
        /// The stretch from <paramref name="from"/> to <paramref name="to"/>: a phrase answered at once by one that starts
        /// the same, or a short figure played twice whole, is cut at its edges; then any bar line where a passage heard
        /// earlier comes back.
        /// </summary>
        public void Read(Tick from, Tick to)
        {
            int[] inside = [.. _starts.Keys.Where(bar => _meter.GetBar(bar).Span.Onset > from && _meter.GetBar(bar).Span.Onset < to)];
            var claimed = new HashSet<int>();
            foreach (int first in inside.Where(bar => !claimed.Contains(bar)))
            {
                int second = inside.FirstOrDefault(bar => bar > first && bar - first <= PhraseMap.LongPhraseBars && FirstOfPassage(first, bar));
                if (second == 0 || Onsets(first, second) < MinRepeatNotes)
                {
                    continue;
                }

                claimed.UnionWith(Restated(first, second, to));
            }

            foreach (int later in inside.Where(bar => !claimed.Contains(bar) && _starts.Keys.Any(earlier => earlier < bar && FirstOfPassage(earlier, bar))))
            {
                Cut(later, PhraseEvidence.Repetition);
            }
        }

        /// <summary>The cuts for a passage at <paramref name="first"/> that starts again at <paramref name="second"/>, and the bars it claims.</summary>
        private IEnumerable<int> Restated(int first, int second, Tick to)
        {
            int period = second - first;
            if (period >= MinPhraseBars)
            {
                // A phrase answered by one that starts the same.
                Cut(first, PhraseEvidence.Restatement);
                Cut(second, PhraseEvidence.Repetition);
                return Enumerable.Range(first, period + 1);
            }

            if (2 * period > PhraseMap.LongPhraseBars || !PlayedTwice(_line, _meter, first, second))
            {
                return [];
            }

            // A short figure played twice in a row, whole: one phrase.
            Cut(first, PhraseEvidence.Restatement);
            if (_starts.ContainsKey(second + period) && _meter.GetBar(second + period).Span.Onset < to)
            {
                Cut(second + period, PhraseEvidence.Restatement);
            }

            return Enumerable.Range(first, (2 * period) + 1);
        }

        /// <summary>Whether the passage from one bar line comes back from another; one pitch repeated is a held texture, not a passage.</summary>
        private bool Repeats(int earlier, int later) =>
            _starts.TryGetValue(earlier, out int from) && _starts.TryGetValue(later, out int to)
            && from + MinRepeatNotes <= _line.Length && to + MinRepeatNotes <= _line.Length && Same(_line, from, to)
            && _line.Skip(from).Take(MinRepeatNotes).Select(static onset => onset.Pitch).Distinct().Count() > 1;

        /// <summary>How many onsets of the top line start in the bars from one bar line up to another.</summary>
        private int Onsets(int first, int stop) => _starts.TryGetValue(stop, out int to) ? to - _starts[first] : _line.Length - _starts[first];

        /// <summary>The first bar line of a passage that comes back: the onsets just before the two do not repeat each other.</summary>
        private bool FirstOfPassage(int earlier, int later)
        {
            if (!Repeats(earlier, later))
            {
                return false;
            }

            int from = _starts[earlier];
            int to = _starts[later];
            return from == 0 || _line[from - 1].Pitch != _line[to - 1].Pitch
                || _line[from].At.Value - _line[from - 1].At.Value != _line[to].At.Value - _line[to - 1].At.Value;
        }

        /// <summary>A cut at a bar line, unless a boundary or cut already known is less than a beat away.</summary>
        private void Cut(int bar, PhraseEvidence evidence)
        {
            Tick tick = _line[_starts[bar]].At;
            long beat = _meter.SignatureAt(tick).BeatTicks;
            if (!_known.Any(other => Math.Abs(other.Value - tick.Value) < beat))
            {
                _known.Add(tick);
                Cuts.Add((tick, evidence));
            }
        }
    }

    /// <summary>Whether every onset of the top line from bar <paramref name="first"/> up to bar <paramref name="second"/> comes back as it was from <paramref name="second"/>, the same time later.</summary>
    private static bool PlayedTwice((Tick At, int Pitch)[] line, MeterMap meter, int first, int second)
    {
        Tick from = meter.GetBar(first).Span.Onset;
        Tick to = meter.GetBar(second).Span.Onset;
        long shift = to.Value - from.Value;
        (Tick At, int Pitch)[] statement = [.. line.Where(onset => onset.At >= from && onset.At < to)];
        (Tick At, int Pitch)[] again = [.. line.Where(onset => onset.At >= to && onset.At.Value < to.Value + shift)];
        return statement.Length == again.Length
            && statement.Zip(again).All(pair => pair.First.Pitch == pair.Second.Pitch && pair.Second.At.Value - pair.First.At.Value == shift);
    }

    /// <summary>
    /// Joins every phrase between two others that would lie within one bar to the phrase after it, as a second ending
    /// leads into what follows: the boundary between them is left out, unless a slur marks it.
    /// </summary>
    private static void JoinOneBarPhrases(SortedDictionary<Tick, SortedSet<PhraseEvidence>> found, MeterMap meter, Tick end)
    {
        bool joined = true;
        while (joined && found.Count > 0)
        {
            joined = false;
            Tick[] at = [Tick.Zero, .. found.Keys, end];
            for (int i = 1; i + 2 < at.Length && !joined; i++)
            {
                if (meter.BarAt(at[i]).Number != meter.BarAt(new Tick(at[i + 1].Value - 1)).Number)
                {
                    continue;
                }

                if (!found[at[i + 1]].Contains(PhraseEvidence.Slur))
                {
                    found.Remove(at[i + 1]);
                    joined = true;
                }
            }
        }
    }

    /// <summary>
    /// Where the passage at the start of the piece or at one of <paramref name="boundaries"/> comes
    /// back on the top line, less than a beat from no boundary already known.
    /// </summary>
    private static List<Tick> Repetitions((Tick At, int Pitch)[] line, MeterMap meter, IEnumerable<Tick> boundaries)
    {
        var index = new Dictionary<Tick, int>();
        for (int i = 0; i < line.Length; i++)
        {
            index[line[i].At] = i;
        }

        var known = new SortedSet<Tick>(boundaries) { line[0].At };
        var found = new List<Tick>();
        var sources = new Queue<Tick>(known);
        while (sources.Count > 0)
        {
            if (!index.TryGetValue(sources.Dequeue(), out int start) || start + MinRepeatNotes > line.Length)
            {
                continue;
            }

            for (int at = start + MinRepeatNotes; at + MinRepeatNotes <= line.Length; at++)
            {
                if (!Same(line, start, at))
                {
                    continue;
                }

                Tick tick = line[at].At;
                long beat = meter.SignatureAt(tick).BeatTicks;
                if (!known.Any(other => Math.Abs(other.Value - tick.Value) < beat) || known.Contains(tick))
                {
                    if (known.Add(tick))
                    {
                        sources.Enqueue(tick);
                    }

                    found.Add(tick);
                }

                // The return itself is not searched for a return inside it.
                at += MinRepeatNotes - 1;
            }
        }

        return found;
    }

    /// <summary>Whether the onsets from <paramref name="first"/> and from <paramref name="second"/> have the same pitches and the same time between them.</summary>
    private static bool Same((Tick At, int Pitch)[] line, int first, int second)
    {
        for (int i = 0; i < MinRepeatNotes; i++)
        {
            if (line[first + i].Pitch != line[second + i].Pitch
                || (i > 0 && line[first + i].At.Value - line[first + i - 1].At.Value != line[second + i].At.Value - line[second + i - 1].At.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Adds the boundaries the player set (S1-A2). A player boundary where the source already has
    /// one confirms it and keeps its evidence; anywhere else it is a new boundary with no source
    /// evidence, marked user-edited. Either way the player's word makes it certain.
    /// </summary>
    public static IReadOnlyList<PhraseBoundary> WithPlayerBoundaries(IEnumerable<PhraseBoundary> found, IEnumerable<Tick> setByPlayer)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(setByPlayer);

        var merged = new SortedDictionary<Tick, PhraseBoundary>();
        foreach (PhraseBoundary boundary in found)
        {
            merged[boundary.At] = boundary;
        }

        foreach (Tick at in setByPlayer)
        {
            merged[at] = merged.TryGetValue(at, out PhraseBoundary? source)
                ? source with { Attribution = Attribution.UserConfirmed }
                : new PhraseBoundary(at, [], Attribution.UserEdited);
        }

        return [.. merged.Values];
    }

    /// <summary>The first onset at or after a position, or null when no note starts there or later.</summary>
    private static Tick? FirstOnsetFrom(Tick[] onsets, Tick position)
    {
        int index = Array.BinarySearch(onsets, position);
        if (index < 0)
        {
            index = ~index;
        }

        return index < onsets.Length ? onsets[index] : null;
    }
}