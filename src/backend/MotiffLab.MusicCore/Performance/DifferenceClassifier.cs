using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Performance;

/// <summary>What a bar's differences amount to (<see cref="BarDifference"/>), from the printed notes not heard and the played notes not printed.</summary>
internal static class DifferenceClassifier
{
    /// <summary>The kind of a bar's differences, from the printed notes not heard and the played notes not printed.</summary>
    /// <remarks>
    /// Two notes or fewer (one wrong pitch, one note lost or added) are what a transcription gets wrong
    /// most often, so they make a difference only when they change the bass.
    /// </remarks>
    public static BarDifference Classify(List<Note> missing, List<PlacedNote> extra, List<Note> printedInBar, List<PlacedNote> placed, TimeSignature signature)
    {
        if (missing.Count + extra.Count <= 1)
        {
            return BarDifference.None;
        }

        BarDifference kind = Kind(missing, extra, printedInBar, placed, signature);
        return missing.Count + extra.Count <= PerformanceComparison.MinNotesForDifference && kind != BarDifference.DifferentBass ? BarDifference.None : kind;
    }

    private static BarDifference Kind(List<Note> missing, List<PlacedNote> extra, List<Note> printedInBar, List<PlacedNote> placed, TimeSignature signature)
    {
        long beat = signature.BeatTicks;
        if (IsOctave(missing, extra, beat))
        {
            return BarDifference.Octave;
        }

        if (IsRhythm(missing, extra))
        {
            return BarDifference.Rhythm;
        }

        if (HasDifferentBass(missing, extra, printedInBar, placed, beat))
        {
            return BarDifference.DifferentBass;
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

    /// <summary>Octave: every missing note has an extra note an octave away, near it.</summary>
    private static bool IsOctave(List<Note> missing, List<PlacedNote> extra, long beat) =>
        missing.Count > 0 && missing.Count == extra.Count && Pairs(missing, extra, (note, played) =>
            Math.Abs(note.Pitch.MidiNumber - played.Pitch.MidiNumber) == 12 && Math.Abs(played.At.Value - note.Span.Onset.Value) <= beat / 2);

    /// <summary>Rhythm: the same pitches, at other times.</summary>
    private static bool IsRhythm(List<Note> missing, List<PlacedNote> extra) =>
        missing.Count > 0 && missing.Count == extra.Count && Pairs(missing, extra, (note, played) => note.Pitch == played.Pitch);

    /// <summary>
    /// Different bass: a printed bass note (lowest of its chord, below middle C) is missing, and a played note
    /// lowest at its moment and below middle C sounds near it instead.
    /// </summary>
    private static bool HasDifferentBass(List<Note> missing, List<PlacedNote> extra, List<Note> printedInBar, List<PlacedNote> placed, long beat)
    {
        foreach (Note bass in missing.Where(note => note.Pitch.MidiNumber < 60 && IsLowestPrinted(note, printedInBar)))
        {
            if (extra.Any(played => played.Pitch.MidiNumber < 60
                && Math.Abs(played.At.Value - bass.Span.Onset.Value) <= beat / 2
                && IsLowestPlayed(played, placed)))
            {
                return true;
            }
        }

        return false;
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
        !placed.Any(other => Math.Abs(other.At.Value - note.At.Value) <= PerformanceComparison.EarlyTolerance / 2 && other.Pitch < note.Pitch);

    /// <summary>Two or more extra notes in a quick stepwise run: each within a 16th of the last and at most two semitones from it.</summary>
    private static bool HasRun(List<PlacedNote> extra)
    {
        List<PlacedNote> ordered = [.. extra.OrderBy(note => note.At).ThenBy(note => note.Pitch)];
        int run = 1;
        for (int i = 1; i < ordered.Count; i++)
        {
            bool quick = ordered[i].At.Value - ordered[i - 1].At.Value <= PerformanceComparison.EarlyTolerance;
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