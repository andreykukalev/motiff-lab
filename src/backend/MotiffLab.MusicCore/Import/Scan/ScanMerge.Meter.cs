using System.Globalization;
using System.Xml.Linq;

using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>The merged page's time signatures, where the backbone is the engine that reads none (issue #647).</summary>
internal static partial class ScanMerge
{
    /// <summary>
    /// When the backbone is the engine that reads no printed time signature (homr, first in <see cref="EngineOrder"/>: it
    /// writes the median length of its bars, and on a page of fewer bars than <see cref="ScanRepair.FewestBarsForMeter"/>
    /// the repair keeps it, 5/4 over bars of four quarters, issue #647), each signature it wrote takes the one that two or
    /// more of the engines that read the printed signature have in force at the bar paired with it; with no such agreement
    /// it stays as written. Only an engine that wrote a signature at or before that bar counts: one that wrote none (a
    /// continuation page prints none) is read in 4/4 by default, which is no vote. Only the signatures change: the bars keep
    /// the notes the merge took.
    /// </summary>
    private static void PrintedMeters(Reading backbone, List<(Reading Reading, int?[] Pairs)> compared)
    {
        if (backbone.Rank != 0)
        {
            return;
        }

        for (int measure = 0; measure < backbone.Measures; measure++)
        {
            int at = measure;
            XElement[] times = [.. backbone.Parts.Where(part => at < part.Count).SelectMany(part => part[at].Measure.Elements("attributes").Elements("time"))];
            if (times.Length == 0)
            {
                continue;
            }

            IGrouping<TimeSignature, TimeSignature>? printed = compared
                .Where(pair => pair.Reading.Rank != 0 && pair.Pairs[at] is not null && WroteTime(pair.Reading, pair.Pairs[at]!.Value))
                .Select(pair => pair.Reading.Import.Meter.SignatureAt(pair.Reading.Import.Measures[pair.Pairs[at]!.Value].Span.Onset))
                .GroupBy(static signature => signature)
                .OrderByDescending(static group => group.Count())
                .FirstOrDefault();
            if (printed is null || printed.Count() < AgreeingEngines || printed.Key == backbone.Import.Meter.SignatureAt(backbone.Import.Measures[at].Span.Onset))
            {
                continue;
            }

            foreach (XElement time in times)
            {
                time.SetElementValue("beats", printed.Key.Numerator.ToString(CultureInfo.InvariantCulture));
                time.SetElementValue("beat-type", printed.Key.Denominator.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>Whether the reading wrote a time signature in its bar <paramref name="measure"/> or a bar before it.</summary>
    private static bool WroteTime(Reading reading, int measure) =>
        reading.Parts.Any(part => part.Take(measure + 1).Any(static bar => bar.Time is not null));
}