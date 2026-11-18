using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>A value read at a position within a measure, in document order.</summary>
internal sealed record Timed<T>(int Track, int Order, int MeasureIndex, QuarterFraction Offset, T Value);

/// <summary>A value at its tick, with the measure it was read in.</summary>
internal sealed record Placed<T>(Tick Tick, int MeasureIndex, T Value);

/// <summary>Values read at positions in the parts (tempos, time and key signatures), placed on the canonical timeline.</summary>
internal static class TimedValues
{
    /// <summary>
    /// Orders values by exact position (ties keep part order, then document order), rounds them to
    /// canonical ticks and keeps one value per tick. Repeats of the same value (a tempo mark copied
    /// into every part) merge silently; a different value at the same tick replaces the earlier one.
    /// </summary>
    public static List<Placed<T>> Merge<T>(
        IEnumerable<Timed<T>> values,
        Func<int, QuarterFraction, QuarterFraction> at,
        ImportIssueCollector issues,
        string? supersededCode)
    {
        var ordered = values
            .Select(value => (Value: value, Position: at(value.MeasureIndex, value.Offset)))
            .OrderBy(entry => entry.Position)
            .ThenBy(entry => entry.Value.Track)
            .ThenBy(entry => entry.Value.Order)
            .ToList();

        var merged = new List<Placed<T>>();
        foreach (var (value, position) in ordered)
        {
            ConvertedTick converted = CanonicalTime.ToTick(position);
            if (!converted.IsExact)
            {
                issues.Add(ImportIssueKind.Rounded, MusicXmlIssueCodes.MetaRounded);
            }

            var placed = new Placed<T>(converted.Tick, value.MeasureIndex, value.Value);
            if (merged.Count > 0 && merged[^1].Tick == placed.Tick)
            {
                if (!EqualityComparer<T>.Default.Equals(merged[^1].Value, placed.Value))
                {
                    if (supersededCode is not null)
                    {
                        issues.Add(ImportIssueKind.Dropped, supersededCode);
                    }

                    merged[^1] = placed;
                }
            }
            else
            {
                merged.Add(placed);
            }
        }

        return merged;
    }
}