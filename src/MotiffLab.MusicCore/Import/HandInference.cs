using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Import;

/// <summary>
/// Infers the hand of each note of a single-track piano part by pitch and continuity (S1-I4), and
/// records the player's reassignments (AC-S1-04).
/// </summary>
/// <remarks>
/// <para>
/// Notes are taken in onset order, in groups that start together (within <see cref="ChordWindow"/>
/// ticks). Each group is sorted by pitch and split once: the lower notes go to the left hand, the
/// rest to the right. The split chosen is the one that costs least, where a note costs how far it
/// lies outside the range of its hand's last <see cref="RecentNotes"/> notes, plus a quarter of its
/// distance from that hand's last note; a hand stretched over more than an octave and a chord left
/// to one hand while the other is free cost extra. Hands start around C3 and C5.
/// </para>
/// <para>
/// Every inferred hand is <see cref="Provenance.Inferred"/>. Its confidence grows with the margin
/// between the best split and the next best: from 0.5 when the two cost the same to 0.95 when they
/// differ by an octave or more.
/// </para>
/// <para>
/// Notes that already have a hand (a grand staff, or the player's choice), parts spread over
/// several tracks and single melodic lines are left untouched. Inference is pure: the same notes give the same hands.
/// </para>
/// </remarks>
public static class HandInference
{
    /// <summary>Notes starting within this many ticks of a group's first note count as one chord (a 64th note).</summary>
    public const long ChordWindow = 30;

    /// <summary>How many of a hand's latest notes give its current range.</summary>
    public const int RecentNotes = 4;

    public const double MinConfidence = 0.5;

    public const double MaxConfidence = 0.95;

    private const int LeftHome = 48; // C3
    private const int RightHome = 72; // C5
    private const int Span = 12;
    private const double OverSpanCost = 4;
    private const double OneHandedChordCost = 6;
    private const double LastNoteWeight = 0.25;
    private const double FullConfidenceMargin = 12;

    /// <summary>
    /// True when the notes are one part with no hands yet (all on one track, none with a hand) and
    /// two of them sound at once somewhere. A single melodic line gives no evidence of two hands, so
    /// it gets none.
    /// </summary>
    public static bool Applies(IReadOnlyCollection<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        return notes.Count > 1
            && notes.All(static note => note.Hand is null)
            && notes.Select(static note => note.Track).Distinct().Count() == 1
            && HasTwoAtOnce(notes);
    }

    private static bool HasTwoAtOnce(IEnumerable<Note> notes)
    {
        long soundingUntil = long.MinValue;
        foreach (Note note in notes.OrderBy(static note => note.Span.Onset))
        {
            if (note.Span.Onset.Value < soundingUntil)
            {
                return true;
            }

            soundingUntil = Math.Max(soundingUntil, note.Span.End.Value);
        }

        return false;
    }

    /// <summary>
    /// The notes, in the same order, with an inferred hand on each. Returns the notes unchanged when
    /// inference does not apply (see <see cref="Applies"/>).
    /// </summary>
    public static IReadOnlyList<Note> Infer(IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (!Applies(notes))
        {
            return notes;
        }

        int[] order = [.. Enumerable.Range(0, notes.Count)
            .OrderBy(i => notes[i].Span.Onset)
            .ThenBy(i => notes[i].Pitch)
            .ThenBy(i => notes[i].Id, StringComparer.Ordinal)];
        var hands = new Attributed<Hand>[notes.Count];
        var left = new List<int> { LeftHome };
        var right = new List<int> { RightHome };

        for (int start = 0; start < order.Length;)
        {
            int end = start + 1;
            long groupOnset = notes[order[start]].Span.Onset.Value;
            while (end < order.Length && notes[order[end]].Span.Onset.Value - groupOnset <= ChordWindow)
            {
                end++;
            }

            int[] group = [.. order[start..end].OrderBy(i => notes[i].Pitch).ThenBy(i => notes[i].Id, StringComparer.Ordinal)];
            int[] pitches = [.. group.Select(i => notes[i].Pitch.MidiNumber)];

            double best = double.MaxValue;
            double second = double.MaxValue;
            int split = 0;
            for (int k = 0; k <= pitches.Length; k++)
            {
                double cost = SplitCost(pitches, k, left, right);
                if (cost < best)
                {
                    (second, best, split) = (best, cost, k);
                }
                else if (cost < second)
                {
                    second = cost;
                }
            }

            double margin = second == double.MaxValue ? FullConfidenceMargin : second - best;
            double confidence = Math.Round(MinConfidence + ((MaxConfidence - MinConfidence) * Math.Min(1, margin / FullConfidenceMargin)), 2);
            var attribution = Attribution.Inferred(confidence);
            for (int j = 0; j < group.Length; j++)
            {
                bool isLeft = j < split;
                hands[group[j]] = new Attributed<Hand>(isLeft ? Hand.Left : Hand.Right, attribution);
                (isLeft ? left : right).Add(pitches[j]);
            }

            start = end;
        }

        return [.. notes.Select((note, i) => note with { Hand = hands[i] })];
    }

    /// <summary>The notes with one note's hand set by the player, recorded as user-edited (AC-S1-04).</summary>
    public static IReadOnlyList<Note> Reassign(IReadOnlyList<Note> notes, string noteId, Hand hand)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentException.ThrowIfNullOrWhiteSpace(noteId);
        if (!Enum.IsDefined(hand))
        {
            throw new ArgumentOutOfRangeException(nameof(hand), hand, "Unknown hand.");
        }

        int index = notes.Select(static note => note.Id).ToList().IndexOf(noteId);
        if (index < 0)
        {
            throw new ArgumentException($"No note has the id {noteId}.", nameof(noteId));
        }

        Note[] result = [.. notes];
        result[index] = result[index] with { Hand = new Attributed<Hand>(hand, Attribution.UserEdited) };
        return result;
    }

    /// <summary>The cost of giving the lowest <paramref name="split"/> pitches to the left hand and the rest to the right.</summary>
    private static double SplitCost(int[] pitches, int split, List<int> left, List<int> right)
    {
        ReadOnlySpan<int> low = pitches.AsSpan(0, split);
        ReadOnlySpan<int> high = pitches.AsSpan(split);
        double cost = HandCost(low, left) + HandCost(high, right);
        if (pitches.Length > 1 && (low.IsEmpty || high.IsEmpty))
        {
            cost += OneHandedChordCost;
        }

        return cost;
    }

    private static double HandCost(ReadOnlySpan<int> pitches, List<int> history)
    {
        if (pitches.IsEmpty)
        {
            return 0;
        }

        int from = Math.Max(0, history.Count - RecentNotes);
        int lowest = int.MaxValue;
        int highest = int.MinValue;
        for (int i = from; i < history.Count; i++)
        {
            lowest = Math.Min(lowest, history[i]);
            highest = Math.Max(highest, history[i]);
        }

        int last = history[^1];
        double cost = 0;
        foreach (int pitch in pitches)
        {
            int outside = pitch < lowest ? lowest - pitch : pitch > highest ? pitch - highest : 0;
            cost += outside + (LastNoteWeight * Math.Abs(pitch - last));
        }

        int stretch = pitches[^1] - pitches[0];
        if (stretch > Span)
        {
            cost += OverSpanCost * (stretch - Span);
        }

        return cost;
    }
}