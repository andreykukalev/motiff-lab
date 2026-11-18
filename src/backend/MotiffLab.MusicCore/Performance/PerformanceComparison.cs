using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Performance;

/// <summary>
/// The print and a performance compared bar by bar (decision record 0022, section 3). Pure and
/// deterministic: the same notes and alignment always give the same comparison.
/// </summary>
/// <remarks>
/// Matching is done over the whole piece, printed notes in order of onset: each takes the nearest
/// unmatched played note of the same pitch within half a beat of it in score time. What is left is
/// sorted into bars by its place in score time; a played note up to <see cref="EarlyTolerance"/>
/// before a bar line counts in the next bar, as players anticipate downbeats. Notes played before the
/// first printed note or after the last one ends (the rest of a piece whose first page was scanned) are
/// left out. The matching is <see cref="NoteMatcher"/>'s, the naming of a bar's kind <see cref="DifferenceClassifier"/>'s.
/// </remarks>
public sealed record PerformanceComparison(PerformanceAlignment Alignment, IReadOnlyList<BarComparison> Bars)
{
    /// <summary>Printed notes below C2 that are not heard are "probably not heard", not a difference: transcription often misses the lowest octaves.</summary>
    public const int ReliableLowestPitch = 36;

    /// <summary>A played note quieter than this is likely a hearing error.</summary>
    public const int QuietVelocity = 15;

    /// <summary>A played note this close to a louder one, in microseconds, can be its overtone: 60 ms.</summary>
    public const long OvertoneWindowMicroseconds = 60_000;

    /// <summary>A played note this many ticks before a bar line belongs to the next bar: a 16th.</summary>
    public const long EarlyTolerance = CanonicalTime.TicksPerQuarter / 4;

    /// <summary>A bar differs only when more notes than this are not heard or not printed, unless its bass changes.</summary>
    public const int MinNotesForDifference = 2;

    /// <summary>The intervals above a sounding note at which an extra note is likely its overtone: octave, twelfth, double octave.</summary>
    private static readonly int[] s_overtones = [12, 19, 24];

    /// <summary>Printed notes heard in aligned bars.</summary>
    public int MatchedCount => Bars.Sum(bar => bar.Matched.Count);

    /// <summary>The bars that differ.</summary>
    public IEnumerable<BarComparison> Differences => Bars.Where(bar => bar.Difference != BarDifference.None);

    /// <summary>Compares the printed notes with a performance through an alignment.</summary>
    public static PerformanceComparison Compare(IReadOnlyList<Note> score, MeterMap meter, IReadOnlyList<PlayedNote> played, PerformanceAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(played);
        ArgumentNullException.ThrowIfNull(alignment);

        List<Note> printed = [.. score.OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch).ThenBy(note => note.Id, StringComparer.Ordinal)];
        long scoreEnd = printed.Count == 0 ? 0 : printed.Max(note => note.Span.End.Value);
        (List<PlayedNote> inScore, List<PlacedNote> placed) = InScore(printed, played, alignment, scoreEnd);
        Dictionary<string, PlayedNote> playedById = played.ToDictionary(note => note.Id, StringComparer.Ordinal);
        (Dictionary<string, NoteMatch> matchOf, bool[] used) = NoteMatcher.Match(printed, meter, placed);
        (List<Bar> bars, List<Note>[] printedIn, List<PlacedNote>[] extraIn) = IntoBars(printed, placed, used, meter, scoreEnd);

        int[] heardCount = new int[bars.Count];
        int[] hearableCount = new int[bars.Count];
        for (int b = 0; b < bars.Count; b++)
        {
            heardCount[b] = printedIn[b].Count(note => matchOf.ContainsKey(note.Id));
            hearableCount[b] = printedIn[b].Count(note => matchOf.ContainsKey(note.Id) || note.Pitch.MidiNumber >= ReliableLowestPitch);
        }

        List<BarComparison> result = [];
        for (int b = 0; b < bars.Count; b++)
        {
            Bar bar = bars[b];
            if (!IsAligned(alignment, bar))
            {
                result.Add(new BarComparison(bar.Number, false, BarDifference.None, 0, [], [], [], [], []));
                continue;
            }

            List<NoteMatch> matched = [.. printedIn[b].Where(note => matchOf.ContainsKey(note.Id)).Select(note => matchOf[note.Id])];
            List<Note> missing = [.. printedIn[b].Where(note => !matchOf.ContainsKey(note.Id))];
            List<string> notHeard = [.. missing.Where(note => note.Pitch.MidiNumber >= ReliableLowestPitch).Select(note => note.Id)];
            List<string> probablyNotHeard = [.. missing.Where(note => note.Pitch.MidiNumber < ReliableLowestPitch).Select(note => note.Id)];
            List<PlacedNote> artefacts = [.. extraIn[b].Where(note => IsLikelyArtefact(note, playedById, inScore))];
            List<PlacedNote> extra = [.. extraIn[b].Where(note => !artefacts.Contains(note))];

            int from = Math.Max(0, b - 1), to = Math.Min(bars.Count - 1, b + 1);
            int heard = 0, hearable = 0;
            for (int k = from; k <= to; k++)
            {
                heard += heardCount[k];
                hearable += hearableCount[k];
            }

            int confidence = hearable == 0 ? 0 : (int)(100L * heard / hearable);
            List<Note> missingNotes = [.. missing.Where(note => note.Pitch.MidiNumber >= ReliableLowestPitch)];
            BarDifference difference = DifferenceClassifier.Classify(missingNotes, extra, printedIn[b], placed, meter.SignatureAt(bar.Span.Onset));
            result.Add(new BarComparison(bar.Number, true, difference, confidence, matched, notHeard, probablyNotHeard, extra, artefacts));
        }

        return new PerformanceComparison(alignment, result);
    }

    /// <summary>Whether anchors hold the bar from both sides, each within a bar of it.</summary>
    private static bool IsAligned(PerformanceAlignment alignment, Bar bar)
    {
        long reach = bar.Signature.BarTicks;
        bool before = alignment.Anchors.Any(anchor => anchor.Score < bar.Span.End && anchor.Score.Value >= bar.Span.Onset.Value - reach);
        bool after = alignment.Anchors.Any(anchor => anchor.Score >= bar.Span.Onset && anchor.Score.Value < bar.Span.End.Value + reach);
        return before && after;
    }

    /// <summary>
    /// What is played from the first printed note to the end of the last, and those notes placed in score time. A
    /// performance may run on past the printed notes (a page of a longer score) or start before them.
    /// </summary>
    private static (List<PlayedNote> InScore, List<PlacedNote> Placed) InScore(List<Note> printed, IReadOnlyList<PlayedNote> played, PerformanceAlignment alignment, long scoreEnd)
    {
        long scoreStart = printed.Count == 0 ? 0 : printed[0].Span.Onset.Value;
        long playedFrom = alignment.MicrosecondsAt(new Tick(Math.Max(0, scoreStart - EarlyTolerance)));
        long until = alignment.MicrosecondsAt(new Tick(scoreEnd));
        List<PlayedNote> inScore = [.. played.Where(note => note.Onset >= playedFrom && note.Onset < until)];
        List<PlacedNote> placed = [.. inScore
            .OrderBy(note => note.Onset).ThenBy(note => note.Pitch).ThenBy(note => note.Id, StringComparer.Ordinal)
            .Select(note => new PlacedNote(note.Id, note.Pitch, alignment.TickAt(note.Onset), note.Velocity))];
        return (inScore, placed);
    }

    /// <summary>The bars up to the last note on either side, the printed notes in each and the played notes no printed note took.</summary>
    private static (List<Bar> Bars, List<Note>[] PrintedIn, List<PlacedNote>[] ExtraIn) IntoBars(List<Note> printed, List<PlacedNote> placed, bool[] used, MeterMap meter, long scoreEnd)
    {
        Tick end = new(Math.Max(scoreEnd, placed.Count == 0 ? 0 : placed.Max(note => note.At.Value) + 1));
        List<Bar> bars = [.. meter.BarsBefore(end)];
        Dictionary<int, int> barIndex = bars.Select((bar, index) => (bar.Number, index)).ToDictionary(pair => pair.Number, pair => pair.index);
        List<Note>[] printedIn = [.. bars.Select(_ => new List<Note>())];
        List<PlacedNote>[] extraIn = [.. bars.Select(_ => new List<PlacedNote>())];
        foreach (Note note in printed)
        {
            printedIn[barIndex[meter.BarAt(note.Span.Onset).Number]].Add(note);
        }

        for (int k = 0; k < placed.Count; k++)
        {
            if (!used[k])
            {
                // A note played just early for the next bar counts in it; there is none after the last one.
                Tick at = new(Math.Min(placed[k].At.Value + EarlyTolerance, end.Value - 1));
                extraIn[barIndex[meter.BarAt(at).Number]].Add(placed[k]);
            }
        }

        return (bars, printedIn, extraIn);
    }

    private static bool IsLikelyArtefact(PlacedNote note, Dictionary<string, PlayedNote> byId, IReadOnlyList<PlayedNote> played)
    {
        if (note.Velocity < QuietVelocity)
        {
            return true;
        }

        PlayedNote self = byId[note.Id];
        return played.Any(other =>
            other.Id != note.Id
            && Math.Abs(other.Onset - self.Onset) <= OvertoneWindowMicroseconds
            && s_overtones.Contains(note.Pitch.MidiNumber - other.Pitch.MidiNumber)
            && other.Velocity > note.Velocity);
    }
}