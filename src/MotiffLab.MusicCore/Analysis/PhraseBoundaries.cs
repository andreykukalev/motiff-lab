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

        // Slurs: outermost per track.
        var tracks = sorted.Select(static note => note.Track).ToHashSet();
        SlurMark[] slurs = [.. marks.Slurs.Where(slur => tracks.Contains(slur.Track)).Distinct()];
        foreach (SlurMark slur in slurs)
        {
            bool nested = slurs.Any(other => other != slur && other.Track == slur.Track && other.Start <= slur.Start && slur.Stop <= other.Stop);
            if (!nested)
            {
                Add(FirstOnsetFrom(onsets, slur.Start), PhraseEvidence.Slur);
                Add(FirstOnsetFrom(onsets, slur.Stop.Offset(1)), PhraseEvidence.Slur);
            }
        }

        // Rests: a beat or more where none of the notes sounds.
        Tick soundingUntil = sorted[0].Span.End;
        foreach (Note note in sorted.Skip(1))
        {
            if (note.Span.Onset.Value - soundingUntil.Value >= meter.SignatureAt(soundingUntil).BeatTicks)
            {
                Add(note.Span.Onset, PhraseEvidence.Rest);
            }

            if (note.Span.End > soundingUntil)
            {
                soundingUntil = note.Span.End;
            }
        }

        // Repeat signs.
        foreach (Tick sign in marks.RepeatSigns)
        {
            Add(FirstOnsetFrom(onsets, sign), PhraseEvidence.RepeatSign);
        }

        // Repetitions: what starts the piece or a boundary, coming back note for note.
        foreach (Tick at in Repetitions(sorted, meter, found.Keys))
        {
            Add(at, PhraseEvidence.Repetition);
        }

        return [.. found.Select(static entry => new PhraseBoundary(
            entry.Key,
            [.. entry.Value],
            Attributed(entry.Value)))];
    }

    /// <summary>Imported when the file marks it; otherwise inferred, as sure as its surest evidence.</summary>
    private static Attribution Attributed(SortedSet<PhraseEvidence> evidence) =>
        evidence.Any(static mark => mark is PhraseEvidence.Slur or PhraseEvidence.RepeatSign) ? Attribution.Imported
        : evidence.Contains(PhraseEvidence.Rest) ? Attribution.Inferred(RestConfidence)
        : Attribution.Inferred(RepetitionConfidence);

    /// <summary>
    /// Where the passage at the start of the piece or at one of <paramref name="boundaries"/> comes
    /// back on the top line, less than a beat from no boundary already known.
    /// </summary>
    private static List<Tick> Repetitions(Note[] sorted, MeterMap meter, IEnumerable<Tick> boundaries)
    {
        // The top line: the highest note at each onset.
        (Tick At, int Pitch)[] line = [.. sorted
            .GroupBy(static note => note.Span.Onset)
            .Select(static onset => (onset.Key, onset.Max(static note => note.Pitch.MidiNumber)))];
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