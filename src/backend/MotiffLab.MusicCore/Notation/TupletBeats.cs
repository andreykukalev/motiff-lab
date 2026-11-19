using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>The beats of a staff written as triplets, and the notes put on the triplets' grid in them.</summary>
internal static class TupletBeats
{
    private const long Grid = NotationGenerator.Grid;

    /// <summary>
    /// The beats of a staff whose onsets are triplets: beats of a simple meter (not 6/8, 9/8, 12/8, which
    /// already divide by three) where the onsets, within a 12th of a beat early, lie on thirds of the beat
    /// (triplet eighths in a quarter) or sixths (triplet 16ths), at least one off the 16ths, with half the
    /// distance from that grid they have from the 16ths, and none further than a 12th of the beat from a
    /// third, or a 24th from a sixth, and no triplet value shorter than a 32nd. A beat with only two onsets
    /// counts only next to a beat of three or more, so that a dotted rhythm played a little short stays dotted.
    /// </summary>
    public static TupletSpan[] Find(ShownNote[] notes, Bar[] bars)
    {
        var onsets = new List<long>();
        foreach (long onset in notes.Where(note => !note.IsGrace).Select(note => note.Note.Span.Onset.Value).Order())
        {
            if (onsets.Count == 0 || onset - onsets[^1] >= NotationGenerator.HeardChord)
            {
                onsets.Add(onset);
            }
        }

        var candidates = new List<(TupletSpan Span, int Onsets)>();
        foreach (Bar bar in bars)
        {
            long beat = bar.Signature.BeatTicks;
            if (BarValues.IsCompound(bar.Signature) || beat % 24 != 0)
            {
                continue;
            }

            long origin = bar.Origin;
            for (long start = origin; start + beat <= bar.Span.End.Value; start += beat)
            {
                if (Candidate(onsets, start, beat) is { } candidate)
                {
                    candidates.Add(candidate);
                }
            }
        }

        var full = candidates.Where(candidate => candidate.Onsets >= 3).Select(candidate => candidate.Span.Start).ToHashSet();
        return [.. candidates
            .Where(candidate => candidate.Onsets >= 3 || full.Contains(candidate.Span.Start - candidate.Span.Length) || full.Contains(candidate.Span.End))
            .Select(candidate => candidate.Span)];
    }

    /// <summary>
    /// A tick on its triplet beat's grid, or on <paramref name="grid"/> outside triplets; a tick just before a
    /// triplet beat's end that <paramref name="grid"/> would put inside it goes on the triplets' grid too.
    /// </summary>
    public static long SnapIn(long tick, TupletSpan[] tuplets, long grid)
    {
        long snapped = NotationGenerator.SnapTo(tick, grid);
        TupletSpan? around = TupletAround(tick, tuplets) ?? (tuplets.FirstOrDefault(span => span.Start < snapped && snapped < span.End) is { Length: > 0 } inside ? inside : null);
        return around is { } span ? span.Start + NotationGenerator.SnapTo(tick - span.Start + span.Length, span.Step) - span.Length : snapped;
    }

    /// <summary>The shortest value written at a position: a step of the triplet beat it is in, or a 32nd.</summary>
    public static long StepAt(long tick, TupletSpan[] tuplets) =>
        tuplets.FirstOrDefault(span => span.Start <= tick && tick < span.End) is { Length: > 0 } span ? span.Step : Grid;

    /// <summary>Notes written on the 32nd grid, with those in triplet beats moved to the triplets' grid.</summary>
    public static ShownNote[] OnTuplets(ShownNote[] notes, TupletSpan[] tuplets)
    {
        if (tuplets.Length == 0)
        {
            return notes;
        }

        return [.. notes.Select(note =>
        {
            long onset = SnapIn(note.Note.Span.Onset.Value, tuplets, Grid);
            long end = Math.Max(SnapIn(note.Note.Span.End.Value, tuplets, Grid), onset + StepAt(onset, tuplets));
            return note with { Onset = onset, RawEnd = end };
        })];
    }

    /// <summary>The beat from <paramref name="start"/> as a triplet of three or six, with how many onsets it holds; null when it is none.</summary>
    private static (TupletSpan Span, int Onsets)? Candidate(List<long> onsets, long start, long beat)
    {
        long early = beat / 12;
        int first = onsets.BinarySearch(start - early);
        first = first >= 0 ? first : ~first;
        long[] offsets = [.. onsets.Skip(first).TakeWhile(onset => onset < start + beat - early).Select(onset => onset - start)];
        if (offsets.Length < 2 || offsets.All(offset => offset < 0))
        {
            return null;
        }

        long sixteenths = offsets.Sum(offset => Distance(offset, beat / 4, beat));
        long thirds = offsets.Sum(offset => Distance(offset, beat / 3, beat));
        long sixths = offsets.Sum(offset => Distance(offset, beat / 6, beat));
        // A triplet value is never shorter than the 32nd every note is shown at least as.
        bool three = Fits(offsets, beat, beat / 3, beat / 12, thirds, sixteenths);
        bool six = Fits(offsets, beat, beat / 6, beat / 24, sixths, sixteenths);
        if (three && (!six || thirds <= sixths + (offsets.Length * beat / 48)))
        {
            return (new TupletSpan(start, beat, 3), offsets.Length);
        }

        return six ? (new TupletSpan(start, beat, 6), offsets.Length) : null;
    }

    /// <summary>
    /// Whether the offsets lie on <paramref name="step"/>: a value no shorter than a 32nd, half the distance from it they
    /// have from the 16ths, none further than <paramref name="tolerance"/> from it, and at least one off the 16ths.
    /// </summary>
    private static bool Fits(long[] offsets, long beat, long step, long tolerance, long distance, long sixteenths) =>
        step >= Grid
        && 2 * distance < sixteenths
        && offsets.All(offset => Distance(offset, step, beat) <= tolerance)
        && offsets.Any(offset => Nearest(offset, step, beat) % (beat / 4) != 0);

    /// <summary>The distance of an offset from the beat's start to the nearest point of a grid (an onset may be a little early).</summary>
    private static long Distance(long offset, long step, long beat) => Math.Abs(offset - Nearest(offset, step, beat));

    /// <summary>The grid point nearest an offset from the beat's start.</summary>
    private static long Nearest(long offset, long step, long beat) => NotationGenerator.SnapTo(offset + beat, step) - beat;

    /// <summary>The triplet beat whose onsets a tick goes with: from a 12th of the beat before it to a 12th before its end.</summary>
    private static TupletSpan? TupletAround(long tick, TupletSpan[] tuplets)
    {
        foreach (TupletSpan span in tuplets)
        {
            if (tick >= span.Start - (span.Length / 12) && tick < span.End - (span.Length / 12))
            {
                return span;
            }
        }

        return null;
    }
}