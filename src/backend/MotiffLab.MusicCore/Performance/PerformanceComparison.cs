using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Performance;

/// <summary>What a bar's differences between the print and the performance amount to (decision record 0022, section 3). Inferred.</summary>
public enum BarDifference
{
    /// <summary>The bar is played as printed, give or take one note.</summary>
    None,

    /// <summary>Notes are added: a filling figure, an inner voice, a pulsing note.</summary>
    AddedNotes,

    /// <summary>A printed bass note is replaced by another.</summary>
    DifferentBass,

    /// <summary>A quick stepwise run is played where the print has one note or an ornament sign.</summary>
    Ornament,

    /// <summary>Printed notes are left out.</summary>
    Simplified,

    /// <summary>The printed notes are played an octave away.</summary>
    Octave,

    /// <summary>The printed pitches are played at other times in the bar.</summary>
    Rhythm,

    /// <summary>Played differently in a way none of the others names.</summary>
    Other,
}

/// <summary>A printed note and the played note that matches it.</summary>
/// <param name="Offset">The played note's place in score time minus the printed note's onset, in ticks: late positive.</param>
public sealed record NoteMatch(string PrintedId, string PlayedId, long Offset);

/// <summary>A played note placed in score time through the alignment.</summary>
public sealed record PlacedNote(string Id, Pitch Pitch, Tick At, int Velocity);

/// <summary>
/// One bar of the comparison: which printed notes were heard, which were not, what was played that is
/// not printed, and what that amounts to.
/// </summary>
/// <param name="Bar">The bar number, as the meter map numbers it.</param>
/// <param name="Aligned">
/// Whether the alignment holds here: anchors within a bar on each side. An unaligned bar (a free
/// introduction, a cut) claims nothing, and every list is empty.
/// </param>
/// <param name="Confidence">
/// How far the comparison can be trusted here, in percent: the share of printed notes heard in this bar
/// and its neighbours, leaving out the ones the recording probably could not hear.
/// </param>
/// <param name="ProbablyNotHeard">Printed notes not heard that lie below <see cref="PerformanceComparison.ReliableLowestPitch"/>, which transcription often misses.</param>
/// <param name="LikelyArtefacts">Played notes not printed that are probably a hearing error: very quiet, or an octave, twelfth or double octave above a louder note struck with them.</param>
public sealed record BarComparison(
    int Bar,
    bool Aligned,
    BarDifference Difference,
    int Confidence,
    IReadOnlyList<NoteMatch> Matched,
    IReadOnlyList<string> PrintedNotHeard,
    IReadOnlyList<string> ProbablyNotHeard,
    IReadOnlyList<PlacedNote> PlayedNotPrinted,
    IReadOnlyList<PlacedNote> LikelyArtefacts)
{
    /// <summary>The kind in a word or two, for the marker above the bar: «добавлены ноты», "different bass".</summary>
    public static string NameOf(BarDifference difference, Words words) => difference switch
    {
        BarDifference.None => words.T("as printed", "как в нотах"),
        BarDifference.AddedNotes => words.T("added notes", "добавлены ноты"),
        BarDifference.DifferentBass => words.T("different bass", "другой бас"),
        BarDifference.Ornament => words.T("ornament", "украшение"),
        BarDifference.Simplified => words.T("simplified", "упрощено"),
        BarDifference.Octave => words.T("octave", "октава"),
        BarDifference.Rhythm => words.T("rhythm", "ритм"),
        BarDifference.Other => words.T("played differently", "иначе"),
        _ => throw new ArgumentOutOfRangeException(nameof(difference), difference, "Unknown difference."),
    };

    /// <summary>
    /// The marker above the bar (0022, section 4): the kind, after the count of notes added or left out where
    /// that is the point: "+3 · added notes", «−2 · упрощено», "different bass". Empty for a bar played as printed.
    /// </summary>
    public string Marker(Words words) => Difference switch
    {
        BarDifference.None => "",
        BarDifference.AddedNotes or BarDifference.Ornament => $"+{PlayedNotPrinted.Count} · {NameOf(Difference, words)}",
        BarDifference.Simplified => $"−{PrintedNotHeard.Count} · {NameOf(Difference, words)}",
        _ => NameOf(Difference, words),
    };
}

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
/// left out.
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
        // A performance may run on past the printed notes (a page of a longer score) or start before them: only
        // what is played from the first printed note to the end of the last is compared.
        long scoreStart = printed.Count == 0 ? 0 : printed[0].Span.Onset.Value;
        long scoreEnd = printed.Count == 0 ? 0 : printed.Max(note => note.Span.End.Value);
        long playedFrom = alignment.MicrosecondsAt(new Tick(Math.Max(0, scoreStart - EarlyTolerance)));
        long until = alignment.MicrosecondsAt(new Tick(scoreEnd));
        List<PlayedNote> inScore = [.. played.Where(note => note.Onset >= playedFrom && note.Onset < until)];
        List<PlacedNote> placed = [.. inScore
            .OrderBy(note => note.Onset).ThenBy(note => note.Pitch).ThenBy(note => note.Id, StringComparer.Ordinal)
            .Select(note => new PlacedNote(note.Id, note.Pitch, alignment.TickAt(note.Onset), note.Velocity))];
        Dictionary<string, PlayedNote> playedById = played.ToDictionary(note => note.Id, StringComparer.Ordinal);

        // Match over the whole piece.
        Dictionary<int, List<int>> byPitch = [];
        for (int k = 0; k < placed.Count; k++)
        {
            if (!byPitch.TryGetValue(placed[k].Pitch.MidiNumber, out List<int>? list))
            {
                byPitch[placed[k].Pitch.MidiNumber] = list = [];
            }

            list.Add(k);
        }

        bool[] used = new bool[placed.Count];
        Dictionary<string, NoteMatch> matchOf = new(StringComparer.Ordinal);
        foreach (Note note in printed)
        {
            if (!byPitch.TryGetValue(note.Pitch.MidiNumber, out List<int>? candidates))
            {
                continue;
            }

            long reach = meter.SignatureAt(note.Span.Onset).BeatTicks / 2;
            int best = -1;
            long bestDistance = long.MaxValue;
            foreach (int k in candidates)
            {
                long distance = Math.Abs(placed[k].At.Value - note.Span.Onset.Value);
                if (!used[k] && distance <= reach && distance < bestDistance)
                {
                    best = k;
                    bestDistance = distance;
                }
            }

            if (best >= 0)
            {
                used[best] = true;
                matchOf[note.Id] = new NoteMatch(note.Id, placed[best].Id, placed[best].At.Value - note.Span.Onset.Value);
            }
        }

        // Sort into bars.
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
            BarDifference difference = Classify(missingNotes, extra, printedIn[b], placed, meter.SignatureAt(bar.Span.Onset));
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

    /// <summary>The kind of a bar's differences, from the printed notes not heard and the played notes not printed.</summary>
    /// <remarks>
    /// Two notes or fewer (one wrong pitch, one note lost or added) are what a transcription gets wrong
    /// most often, so they make a difference only when they change the bass.
    /// </remarks>
    private static BarDifference Classify(List<Note> missing, List<PlacedNote> extra, List<Note> printedInBar, List<PlacedNote> placed, TimeSignature signature)
    {
        if (missing.Count + extra.Count <= 1)
        {
            return BarDifference.None;
        }

        BarDifference kind = Kind(missing, extra, printedInBar, placed, signature);
        return missing.Count + extra.Count <= MinNotesForDifference && kind != BarDifference.DifferentBass ? BarDifference.None : kind;
    }

    private static BarDifference Kind(List<Note> missing, List<PlacedNote> extra, List<Note> printedInBar, List<PlacedNote> placed, TimeSignature signature)
    {

        long beat = signature.BeatTicks;

        // Octave: every missing note has an extra note an octave away, near it.
        if (missing.Count > 0 && missing.Count == extra.Count && Pairs(missing, extra, (note, played) =>
            Math.Abs(note.Pitch.MidiNumber - played.Pitch.MidiNumber) == 12 && Math.Abs(played.At.Value - note.Span.Onset.Value) <= beat / 2))
        {
            return BarDifference.Octave;
        }

        // Rhythm: the same pitches, at other times.
        if (missing.Count > 0 && missing.Count == extra.Count && Pairs(missing, extra, (note, played) => note.Pitch == played.Pitch))
        {
            return BarDifference.Rhythm;
        }

        // Different bass: a printed bass note (lowest of its chord, below middle C) is missing, and a played note
        // lowest at its moment and below middle C sounds near it instead.
        foreach (Note bass in missing.Where(note => note.Pitch.MidiNumber < 60 && IsLowestPrinted(note, printedInBar)))
        {
            if (extra.Any(played => played.Pitch.MidiNumber < 60
                && Math.Abs(played.At.Value - bass.Span.Onset.Value) <= beat / 2
                && IsLowestPlayed(played, placed)))
            {
                return BarDifference.DifferentBass;
            }
        }

        if (extra.Count >= 2 && missing.Count <= 1)
        {
            return HasRun(extra) ? BarDifference.Ornament : BarDifference.AddedNotes;
        }

        if (missing.Count >= 2 && extra.Count <= 1)
        {
            return BarDifference.Simplified;
        }

        return BarDifference.Other;
    }

    /// <summary>Whether each missing note can be paired with its own extra note by a rule, greedily in order.</summary>
    private static bool Pairs(List<Note> missing, List<PlacedNote> extra, Func<Note, PlacedNote, bool> rule)
    {
        bool[] taken = new bool[extra.Count];
        foreach (Note note in missing)
        {
            int k = Enumerable.Range(0, extra.Count).FirstOrDefault(i => !taken[i] && rule(note, extra[i]), -1);
            if (k < 0)
            {
                return false;
            }

            taken[k] = true;
        }

        return true;
    }

    private static bool IsLowestPrinted(Note note, List<Note> bar) =>
        !bar.Any(other => other.Span.Onset == note.Span.Onset && other.Pitch < note.Pitch);

    private static bool IsLowestPlayed(PlacedNote note, List<PlacedNote> placed) =>
        !placed.Any(other => Math.Abs(other.At.Value - note.At.Value) <= EarlyTolerance / 2 && other.Pitch < note.Pitch);

    /// <summary>Two or more extra notes in a quick stepwise run: each within a 16th of the last and at most two semitones from it.</summary>
    private static bool HasRun(List<PlacedNote> extra)
    {
        List<PlacedNote> ordered = [.. extra.OrderBy(note => note.At).ThenBy(note => note.Pitch)];
        int run = 1;
        for (int i = 1; i < ordered.Count; i++)
        {
            bool quick = ordered[i].At.Value - ordered[i - 1].At.Value <= EarlyTolerance;
            bool step = Math.Abs(ordered[i].Pitch.MidiNumber - ordered[i - 1].Pitch.MidiNumber) is > 0 and <= 2;
            run = quick && step ? run + 1 : 1;
            if (run >= 3)
            {
                return true;
            }
        }

        return false;
    }
}