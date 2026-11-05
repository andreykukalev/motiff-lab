using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Performance;

/// <summary>
/// A place where a performance contradicts the notes recognised from a scan (decision record 0022, section 6),
/// offered to the player as a Check flag; nothing changes until they choose "Fix from the recording".
/// </summary>
/// <param name="Reason"><see cref="CheckReason.RecordingOctave"/> or <see cref="CheckReason.RecordingBarLength"/>.</param>
/// <param name="FirstBar">The first bar it covers, as the workspace numbers bars.</param>
/// <param name="LastBar">The last bar it covers.</param>
/// <param name="NoteIds">The printed notes concerned: for an octave, the ones to move.</param>
/// <param name="Semitones">For an octave, how far the recording has them: −12 or +12. Zero otherwise.</param>
/// <param name="PlayedTicks">For a bar length, how long the recording plays the bar, in ticks at the neighbouring bars' tempo, to the nearest 16th. Zero otherwise.</param>
public sealed record RecordingCorrection(CheckReason Reason, int FirstBar, int LastBar, IReadOnlyList<string> NoteIds, int Semitones, long PlayedTicks)
{
    /// <summary>One open Check flag per bar it covers.</summary>
    public IEnumerable<CheckFlag> Flags() =>
        Enumerable.Range(FirstBar, LastBar - FirstBar + 1).Select(bar => new CheckFlag(bar, CheckSource.Performance, Reason, false));
}

/// <summary>
/// The two first rules by which a performance corrects a recognised scan (decision record 0022, section 6): a
/// hand played an octave away from the print over two bars or more (a lost octave clef), and a bar whose
/// printed length disagrees with its meter while the recording plays it in the meter's length. Pure and
/// deterministic.
/// </summary>
public static class RecordingCorrections
{
    /// <summary>The fewest bars in a row a hand must sound an octave away before it is offered: one bar can be the performer's choice.</summary>
    public const int FewestOctaveBars = 2;

    /// <summary>The fewest of a hand's printed notes in a bar that make its octave worth judging.</summary>
    public const int FewestOctaveNotes = 2;

    /// <summary>The share of a hand's printed notes in a bar that must be heard an octave away, in percent.</summary>
    public const int OctaveShare = 75;

    /// <summary>
    /// Finds the corrections the performance suggests for a recognised scan.
    /// </summary>
    /// <param name="score">The scan's notes, as the comparison read them.</param>
    /// <param name="meter">The scan's meter map.</param>
    /// <param name="scan">The scan's own check: its measures and their length flags.</param>
    /// <param name="comparison">The scan compared with the performance.</param>
    public static IReadOnlyList<RecordingCorrection> Find(IReadOnlyList<Note> score, MeterMap meter, ScanCheck scan, PerformanceComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(comparison);

        List<RecordingCorrection> found = [.. Octaves(score, meter, comparison), .. BarLengths(meter, scan, comparison)];
        return [.. found.OrderBy(correction => correction.FirstBar).ThenBy(correction => correction.Reason)];
    }

    /// <summary>Runs of bars where most of a hand's printed notes are heard an octave away, in one direction.</summary>
    private static IEnumerable<RecordingCorrection> Octaves(IReadOnlyList<Note> score, MeterMap meter, PerformanceComparison comparison)
    {
        Dictionary<string, Note> printedById = score.ToDictionary(note => note.Id, StringComparer.Ordinal);
        List<PlacedNote> unmatched = [.. comparison.Bars.SelectMany(bar => bar.PlayedNotPrinted.Concat(bar.LikelyArtefacts)).OrderBy(note => note.At).ThenBy(note => note.Id, StringComparer.Ordinal)];
        Dictionary<int, BarComparison> bars = comparison.Bars.ToDictionary(bar => bar.Bar);

        foreach (IGrouping<string, Note> hand in score.GroupBy(HandOf).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            // Per aligned bar: the direction most of the hand's notes are heard in, or 0.
            List<(int Bar, int Shift, List<string> Ids)> verdicts = [];
            foreach (IGrouping<int, Note> inBar in hand.GroupBy(note => meter.BarAt(note.Span.Onset).Number).OrderBy(group => group.Key))
            {
                if (!bars.TryGetValue(inBar.Key, out BarComparison? bar) || !bar.Aligned)
                {
                    continue;
                }

                List<Note> notes = [.. inBar.Where(note => note.Pitch.MidiNumber >= PerformanceComparison.ReliableLowestPitch)];
                HashSet<string> notHeard = [.. bar.PrintedNotHeard];
                bool[] taken = new bool[unmatched.Count];
                List<string> down = [], up = [];
                foreach (Note note in notes.Where(note => notHeard.Contains(note.Id)))
                {
                    long reach = meter.SignatureAt(note.Span.Onset).BeatTicks / 2;
                    foreach (int shift in new[] { -12, 12 })
                    {
                        int k = Enumerable.Range(0, unmatched.Count).FirstOrDefault(
                            i => !taken[i]
                                && unmatched[i].Pitch.MidiNumber == note.Pitch.MidiNumber + shift
                                && Math.Abs(unmatched[i].At.Value - note.Span.Onset.Value) <= reach,
                            -1);
                        if (k >= 0)
                        {
                            taken[k] = true;
                            (shift < 0 ? down : up).Add(note.Id);
                            break;
                        }
                    }
                }

                int shiftOf = notes.Count < FewestOctaveNotes ? 0
                    : down.Count * 100 >= notes.Count * OctaveShare ? -12
                    : up.Count * 100 >= notes.Count * OctaveShare ? 12
                    : 0;
                verdicts.Add((inBar.Key, shiftOf, shiftOf < 0 ? down : up));
            }

            int start = 0;
            while (start < verdicts.Count)
            {
                int end = start;
                while (end + 1 < verdicts.Count && verdicts[start].Shift != 0
                    && verdicts[end + 1].Shift == verdicts[start].Shift && verdicts[end + 1].Bar == verdicts[end].Bar + 1)
                {
                    end++;
                }

                if (verdicts[start].Shift != 0 && end - start + 1 >= FewestOctaveBars)
                {
                    List<string> ids = [.. verdicts.Skip(start).Take(end - start + 1).SelectMany(verdict => verdict.Ids).Order(StringComparer.Ordinal)];
                    yield return new RecordingCorrection(CheckReason.RecordingOctave, verdicts[start].Bar, verdicts[end].Bar, ids, verdicts[start].Shift, 0);
                }

                start = end + 1;
            }
        }

        static string HandOf(Note note) => note.Hand?.Value.ToString() ?? $"track {note.Track}";
    }

    /// <summary>Bars the scan reads too long or too short that the recording plays in the meter's length.</summary>
    private static IEnumerable<RecordingCorrection> BarLengths(MeterMap meter, ScanCheck scan, PerformanceComparison comparison)
    {
        PerformanceAlignment alignment = comparison.Alignment;
        Dictionary<int, BarComparison> bars = comparison.Bars.ToDictionary(bar => bar.Bar);
        IReadOnlyList<ScanBar> measures = scan.Bars;
        for (int i = 1; i < measures.Count - 1; i++)
        {
            ScanBar measure = measures[i];
            if (!measure.Flags.Contains(ScanFlag.BarTooLong) && !measure.Flags.Contains(ScanFlag.BarTooShort))
            {
                continue;
            }

            if (!bars.TryGetValue(measure.Bar, out BarComparison? bar) || !bar.Aligned)
            {
                continue;
            }

            // The tempo of the neighbouring measures, in microseconds per tick, as a fraction.
            TickSpan before = measures[i - 1].Span, after = measures[i + 1].Span;
            long neighbourTime = Duration(alignment, before) + Duration(alignment, after);
            long neighbourTicks = before.Duration + after.Duration;
            long played = Duration(alignment, measure.Span);
            if (neighbourTime <= 0)
            {
                continue;
            }

            // The bar as the recording plays it, in ticks at the neighbours' tempo, to the nearest 16th.
            long sixteenth = CanonicalTime.TicksPerQuarter / 4;
            long ticks = ((played * neighbourTicks / neighbourTime) + (sixteenth / 2)) / sixteenth * sixteenth;
            long expected = measure.Span.Duration;
            long meterTicks = meter.SignatureAt(measure.Span.Onset).BarTicks;
            if (ticks == meterTicks && ticks != expected)
            {
                yield return new RecordingCorrection(CheckReason.RecordingBarLength, measure.Bar, measure.Bar, measure.NoteIds, 0, ticks);
            }
        }
    }

    private static long Duration(PerformanceAlignment alignment, TickSpan span) =>
        alignment.MicrosecondsAt(span.End) - alignment.MicrosecondsAt(span.Onset);
}