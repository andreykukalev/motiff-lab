using System.Globalization;
using System.Xml.Linq;

using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Scan;

internal static partial class ReadingMarks
{
    /// <summary>
    /// The marked bars of the part in triplets as printed ("yes") or not ("no"), all of them or none (c-14b2): in each bar,
    /// a staff with no triplet that adds up to half as long again as the bar's time signature takes its notes and rests in 3
    /// in the time of 2 (two thirds of their length, a time modification written), and for "no" a staff all in triplets that
    /// adds up to two thirds of it takes them out; a staff that adds up to the bar as read is kept. False, every note as
    /// read, when a staff of a bar does neither, or a bar marked "yes" ends with no triplet ("no": with one). Exact: the
    /// part's divisions are multiplied where a length would not come out whole.
    /// </summary>
    private static bool Triplets(XElement part, int first, int last, bool printed)
    {
        List<ScanRepair.Bar> bars = ScanRepair.Bars(part);
        (long Times, long By) factor = printed ? (2, 3) : (3, 2);
        var plans = new List<(int Index, HashSet<int> Staves)>();
        for (int index = first; index <= last; index++)
        {
            if (LengthAt(bars, index) is not { } length || TripletStaves(bars[index], length, printed) is not { } staves)
            {
                return false;
            }

            plans.Add((index, staves));
        }

        if (plans.Any(plan => Moved(bars[plan.Index], plan.Staves).Any(each => (each * factor.Times) % factor.By != 0)))
        {
            ScaleDivisions(part, factor.By);
            bars = ScanRepair.Bars(part);
        }

        foreach ((int index, HashSet<int> staves) in plans)
        {
            WriteTriplets(bars[index], staves, factor, printed);
        }

        return true;
    }

    /// <summary>
    /// The staves of the bar whose notes change, or null when the mark does not fit it: each staff either adds up to the bar
    /// as read or, re-timed by the mark, comes to add up to it; and the bar has a triplet after the mark ("yes") or none ("no").
    /// </summary>
    private static HashSet<int>? TripletStaves(ScanRepair.Bar bar, QuarterFraction length, bool printed)
    {
        List<XElement> notes = [.. bar.Measure.Elements("note")];
        HashSet<int> staves = [];
        bool triplets = false;
        foreach (IGrouping<int, int> staff in Enumerable.Range(0, bar.Sounds.Count).Where(i => !bar.Sounds[i].IsGrace).GroupBy(i => bar.Sounds[i].Staff))
        {
            int inTriplets = staff.Count(i => IsTriplet(notes[i]));
            QuarterFraction asRead = printed ? new QuarterFraction(length.Numerator * 3, length.Denominator * 2) : new QuarterFraction(length.Numerator * 2, length.Denominator * 3);
            if ((printed ? inTriplets == 0 : inTriplets == staff.Count()) && ScanRepair.AddsUp(bar, asRead, bar.Onsets, staff.Key))
            {
                staves.Add(staff.Key);
                triplets |= printed;
            }
            else if (ScanRepair.AddsUp(bar, length, bar.Onsets, staff.Key))
            {
                triplets |= inTriplets > 0;
            }
            else
            {
                return null;
            }
        }

        return triplets == printed ? staves : null;
    }

    /// <summary>The lengths and onsets, in divisions, of the sounds of the bar on <paramref name="staves"/>, which the mark re-times.</summary>
    private static IEnumerable<long> Moved(ScanRepair.Bar bar, HashSet<int> staves) =>
        Enumerable.Range(0, bar.Sounds.Count).Where(i => staves.Contains(bar.Sounds[i].Staff)).SelectMany(i => (long[])[bar.Sounds[i].Duration, bar.Onsets[i]]);

    /// <summary>The bar's sounds on <paramref name="staves"/> re-timed by <paramref name="factor"/>, each note written in triplets or out of them.</summary>
    private static void WriteTriplets(ScanRepair.Bar bar, HashSet<int> staves, (long Times, long By) factor, bool printed)
    {
        List<XElement> notes = [.. bar.Measure.Elements("note")];
        long[] onsets = [.. bar.Onsets];
        for (int i = 0; i < bar.Sounds.Count; i++)
        {
            ScanRepair.Sound sound = bar.Sounds[i];
            if (!staves.Contains(sound.Staff))
            {
                continue;
            }

            onsets[i] = onsets[i] * factor.Times / factor.By;
            if (sound.IsGrace)
            {
                continue;
            }

            bar.Sounds[i] = sound with { Duration = sound.Duration * factor.Times / factor.By };
            notes[i].SetElementValue("duration", bar.Sounds[i].Duration.ToString(CultureInfo.InvariantCulture));
            notes[i].Elements("time-modification").Remove();
            notes[i].Elements("notations").Elements("tuplet").Remove();
            if (printed)
            {
                TimeModificationPlace(notes[i], new XElement("time-modification", new XElement("actual-notes", "3"), new XElement("normal-notes", "2")));
            }
        }

        bar.WriteOnsets(onsets);
    }

    /// <summary>Puts a note's time modification in MusicXML's order: after its type, dots and accidental, before its stem, staff, beams and notations.</summary>
    private static void TimeModificationPlace(XElement note, XElement modification)
    {
        if (note.Elements().FirstOrDefault(static element => element.Name.LocalName is "stem" or "notehead" or "notehead-text" or "staff" or "beam" or "notations" or "lyric" or "play" or "listen") is { } after)
        {
            after.AddBeforeSelf(modification);
        }
        else
        {
            note.Add(modification);
        }
    }

    /// <summary>Whether the note is written in triplets: 3 in the time of 2.</summary>
    private static bool IsTriplet(XElement note) =>
        note.Element("time-modification") is { } modification && Number(modification.Element("actual-notes")) == 3 && Number(modification.Element("normal-notes")) == 2;

    /// <summary>The length of measure <paramref name="index"/> by the time signature in force there (4/4 where none is written); null for one not of two whole numbers.</summary>
    private static QuarterFraction? LengthAt(List<ScanRepair.Bar> bars, int index)
    {
        if (bars.Take(index + 1).LastOrDefault(static bar => bar.Time is not null)?.Time is not { } time)
        {
            return new QuarterFraction(4, 1);
        }

        return Number(time.Element("beats")) is { } beats and > 0 && Number(time.Element("beat-type")) is { } type and > 0
            ? new QuarterFraction(beats * 4, type)
            : null;
    }

    /// <summary>The part's divisions and every length in it (notes, backups, forwards, offsets) <paramref name="by"/> times, the music as it was.</summary>
    private static void ScaleDivisions(XElement part, long by)
    {
        foreach (XElement measure in part.Elements("measure"))
        {
            foreach (XElement length in measure.Elements("attributes").Elements("divisions")
                .Concat(measure.Elements().Where(static element => element.Name.LocalName is "note" or "backup" or "forward").Elements("duration"))
                .Concat(measure.Descendants("offset")))
            {
                length.Value = ((Number(length) ?? 0) * by).ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}