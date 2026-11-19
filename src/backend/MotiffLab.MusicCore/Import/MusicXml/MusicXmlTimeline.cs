using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>The timeline of a score: its tempos, its time signatures and bars (with a pickup), and each part's keys.</summary>
internal static class MusicXmlTimeline
{
    /// <summary>The tempo changes, one per tick; an assumed tempo at the start when the score marks none there.</summary>
    public static List<MusicXmlTempoChange> Tempos(List<PartReader> parts, Func<int, QuarterFraction, QuarterFraction> at, ImportIssueCollector issues)
    {
        var tempos = TimedValues.Merge(
                parts.SelectMany(part => part.Tempos),
                at,
                issues,
                MusicXmlIssueCodes.TempoSuperseded)
            .Select(tempo => new MusicXmlTempoChange(tempo.Tick, tempo.MeasureIndex, tempo.Value, IsAssumed: false))
            .ToList();
        if (tempos.Count == 0 || tempos[0].At != Tick.Zero)
        {
            issues.Add(ImportIssueKind.Assumed, MusicXmlIssueCodes.TempoDefault);
            tempos.Insert(0, new MusicXmlTempoChange(Tick.Zero, 0, MusicXmlTempoChange.DefaultQuarterNotesPerMinute, IsAssumed: true));
        }

        return tempos;
    }

    /// <summary>
    /// The time signatures, a change only where the signature changes (4/4 assumed at the start when none is written there),
    /// and the bars: a short first measure is a pickup, a signature inside it is dropped, and the bars are aligned to the
    /// measures (<see cref="AlignBarsToMeasures"/>).
    /// </summary>
    /// <param name="boundaries">Where each measure starts, and the end of the last.</param>
    public static (List<MusicXmlTimeSignature> TimeSignatures, MeterMap Meter) Meter(List<Timed<TimeSignature>> times, Func<int, QuarterFraction, QuarterFraction> at, ImportIssueCollector issues, Tick[] boundaries)
    {
        var timeSignatures = new List<MusicXmlTimeSignature>();
        foreach (Placed<TimeSignature> time in TimedValues.Merge(times, at, issues, MusicXmlIssueCodes.MeterSuperseded))
        {
            if (timeSignatures.Count == 0 || timeSignatures[^1].Numerator != time.Value.Numerator || timeSignatures[^1].Denominator != time.Value.Denominator)
            {
                timeSignatures.Add(new MusicXmlTimeSignature(time.Tick, time.MeasureIndex, time.Value.Numerator, time.Value.Denominator, IsAssumed: false));
            }
        }

        if (timeSignatures.Count == 0 || timeSignatures[0].At != Tick.Zero)
        {
            issues.Add(ImportIssueKind.Assumed, MusicXmlIssueCodes.MeterDefault);
            timeSignatures.Insert(0, new MusicXmlTimeSignature(Tick.Zero, 0, 4, 4, IsAssumed: true));
        }

        long pickup = Pickup(timeSignatures[0], boundaries);

        var changes = new SortedList<long, TimeSignature>();
        foreach (MusicXmlTimeSignature time in timeSignatures)
        {
            if (time.At.Value > 0 && time.At.Value < pickup)
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MeterInPickup);
                continue;
            }

            changes[time.At.Value] = new TimeSignature(time.Numerator, time.Denominator);
        }

        timeSignatures.RemoveAll(time => time.At.Value > 0 && time.At.Value < pickup);
        AlignBarsToMeasures(changes, boundaries, pickup, issues);
        return (timeSignatures, new MeterMap(changes.Select(change => new MeterChange(new Tick(change.Key), change.Value)), pickup));
    }

    /// <summary>Each part's key signatures, a change only where the key or mode changes.</summary>
    public static List<MusicXmlKeySignature> Keys(List<PartReader> parts, Func<int, QuarterFraction, QuarterFraction> at, ImportIssueCollector issues)
    {
        var keys = new List<MusicXmlKeySignature>();
        foreach (PartReader part in parts)
        {
            foreach (Placed<(int Fifths, string? Mode)> key in TimedValues.Merge(part.Keys, at, issues, supersededCode: null))
            {
                MusicXmlKeySignature placed = new(key.Tick, part.Index, key.MeasureIndex, key.Value.Fifths, key.Value.Mode);
                MusicXmlKeySignature? previous = keys.Count > 0 && keys[^1].Track == part.Index ? keys[^1] : null;
                if (previous is null || previous.Fifths != placed.Fifths || previous.Mode != placed.Mode)
                {
                    keys.Add(placed);
                }
            }
        }

        return keys;
    }

    /// <summary>The length of a first measure shorter than a full bar of the first signature, when there are two or more: a pickup; else 0.</summary>
    private static long Pickup(MusicXmlTimeSignature first, Tick[] boundaries)
    {
        int measureCount = boundaries.Length - 1;
        var firstSignature = new TimeSignature(first.Numerator, first.Denominator);
        return measureCount > 1 && boundaries[1].Value < firstSignature.BarTicks ? boundaries[1].Value : 0;
    }

    /// <summary>
    /// Restates the time signature after any measure that is shorter or longer than a full bar, so
    /// the next measure starts on a bar line of the meter map (decision record 0004: a change ends
    /// the bar in progress). Measures that contain a time signature change are left as they are.
    /// </summary>
    private static void AlignBarsToMeasures(SortedList<long, TimeSignature> changes, Tick[] boundaries, long pickup, ImportIssueCollector issues)
    {
        for (int i = 0; i + 2 < boundaries.Length; i++)
        {
            long start = boundaries[i].Value;
            long end = boundaries[i + 1].Value;
            if ((i == 0 && pickup > 0) || changes.Keys.Any(position => position > start && position <= end))
            {
                continue;
            }

            TimeSignature signature = changes.Last(change => change.Key <= start).Value;
            long length = end - start;
            if (length == signature.BarTicks)
            {
                continue;
            }

            issues.Add(ImportIssueKind.Assumed, length < signature.BarTicks ? MusicXmlIssueCodes.MeasureShort : MusicXmlIssueCodes.MeasureLong);
            changes[end] = signature;
        }
    }
}