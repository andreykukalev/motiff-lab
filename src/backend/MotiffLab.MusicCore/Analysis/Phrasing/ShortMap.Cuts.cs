using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis.Phrasing;

/// <summary>Where the notes suggest cutting a long phrase into short units: a rest, the opening figure coming back, a held note ending, or the middle.</summary>
public sealed partial class ShortMap
{
    /// <summary>The cuts that divide one phrase into units of at most <see cref="MaxBars"/> bars, in time order.</summary>
    private static IEnumerable<ShortBoundary> Divide(Phrase phrase, Note[] sorted, MeterMap meter)
    {
        if (phrase.BarCount <= MaxBars)
        {
            return [];
        }

        Note[] inside = [.. sorted.Where(note => phrase.Span.Contains(note.Span.Onset))];
        if (inside.Length == 0)
        {
            return [];
        }

        Tick[] onsets = [.. inside.Select(static note => note.Span.Onset).Distinct()];
        var candidates = new List<(int Bar, ShortBoundary Cut)>();
        for (int bar = phrase.FirstBar + 1; bar <= phrase.LastBar; bar++)
        {
            Tick line = meter.GetBar(bar).Span.Onset;
            if (PhraseBoundaryFinder.FirstOnsetFrom(onsets, line) is not { } at || at >= phrase.Span.End || at <= phrase.Span.Onset)
            {
                continue;
            }

            // A cut only at the first onset of its bar: a bar whose first note arrives late belongs with the one before.
            if (meter.BarAt(at).Number != bar)
            {
                continue;
            }

            ShortEvidence evidence = Evidence(inside, onsets, at, line, meter);
            candidates.Add((bar, new ShortBoundary(at, evidence, Attribution.Inferred(Confidence(evidence)))));
        }

        var cuts = new List<ShortBoundary>();
        Split(phrase.FirstBar, phrase.LastBar, candidates, cuts);
        return cuts.OrderBy(static cut => cut.At);
    }

    /// <summary>Divides bars <paramref name="first"/> to <paramref name="last"/> at the best candidate until every part is short enough.</summary>
    private static void Split(int first, int last, List<(int Bar, ShortBoundary Cut)> candidates, List<ShortBoundary> cuts)
    {
        if (last - first + 1 <= MaxBars)
        {
            return;
        }

        double middle = (first + last + 1) / 2.0;
        (int Bar, ShortBoundary Cut)? best = candidates
            .Where(candidate => candidate.Bar > first && candidate.Bar <= last)
            .OrderBy(candidate => Rank(candidate.Cut.Evidence))
            .ThenBy(candidate => Math.Abs(candidate.Bar - middle))
            .ThenBy(candidate => candidate.Bar)
            .Cast<(int Bar, ShortBoundary Cut)?>()
            .FirstOrDefault();
        if (best is not { } chosen)
        {
            return;
        }

        cuts.Add(chosen.Cut);
        Split(first, chosen.Bar - 1, candidates, cuts);
        Split(chosen.Bar, last, candidates, cuts);
    }

    private static int Rank(ShortEvidence evidence) => evidence switch
    {
        ShortEvidence.Rest => 0,
        ShortEvidence.Return => 1,
        ShortEvidence.HeldNote => 2,
        _ => 3,
    };

    private static double Confidence(ShortEvidence evidence) => evidence switch
    {
        ShortEvidence.Rest => RestConfidence,
        ShortEvidence.Return => ReturnConfidence,
        ShortEvidence.HeldNote => HeldNoteConfidence,
        _ => LengthConfidence,
    };

    /// <summary>What the notes say about cutting at <paramref name="at"/>, the first onset at or after the bar line <paramref name="line"/>.</summary>
    private static ShortEvidence Evidence(Note[] inside, Tick[] onsets, Tick at, Tick line, MeterMap meter)
    {
        Note[] before = [.. inside.Where(note => note.Span.Onset < at)];
        if (before.Length == 0)
        {
            return ShortEvidence.Length;
        }

        long beat = meter.SignatureAt(line).BeatTicks;
        long soundingUntil = before.Max(note => note.Span.End.Value);
        if (at.Value - soundingUntil >= beat / 2)
        {
            return ShortEvidence.Rest;
        }

        if (Returns(inside, onsets, at))
        {
            return ShortEvidence.Return;
        }

        long lastOnset = before.Max(note => note.Span.Onset.Value);
        return soundingUntil == line.Value && at == line && line.Value - lastOnset > beat ? ShortEvidence.HeldNote : ShortEvidence.Length;
    }

    /// <summary>Whether the top line's first <see cref="MinReturnNotes"/> onsets of the phrase come back at <paramref name="at"/> with the same pitches and spacing.</summary>
    private static bool Returns(Note[] inside, Tick[] onsets, Tick at)
    {
        int start = Array.IndexOf(onsets, at);
        if (start < 0 || start + MinReturnNotes > onsets.Length || MinReturnNotes > start)
        {
            return false;
        }

        int Top(Tick onset) => inside.Where(note => note.Span.Onset == onset).Max(note => note.Pitch.MidiNumber);
        for (int i = 0; i < MinReturnNotes; i++)
        {
            if (Top(onsets[i]) != Top(onsets[start + i])
                || (i > 0 && onsets[i].Value - onsets[i - 1].Value != onsets[start + i].Value - onsets[start + i - 1].Value))
            {
                return false;
            }
        }

        return true;
    }
}