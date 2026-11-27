using System.Globalization;
using System.Xml.Linq;

using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Scan;

internal static partial class ReadingMarks
{
    /// <summary>
    /// The marked bars of the part in triplets as printed ("yes") or not ("no"), all of them or none (c-14b2): in each bar,
    /// a voice with no triplet that runs on from the bar's start to half as long again as its time signature takes its notes
    /// and rests in 3 in the time of 2 (two thirds of their length, a time modification and the tuplet's start and stop
    /// written), and for "no" a voice all in triplets that runs to two thirds of it takes them out; a voice that runs to the
    /// bar's end as read is kept. False, every note as read, when a voice of a bar does neither, or a bar marked "yes" ends
    /// with no triplet ("no": with one). Exact: the part's divisions are multiplied where a length would not come out whole.
    /// </summary>
    private static bool Triplets(XElement part, int first, int last, bool printed)
    {
        List<ScanRepair.Bar> bars = ScanRepair.Bars(part);
        (long Times, long By) factor = printed ? (2, 3) : (3, 2);
        var plans = new List<(int Index, HashSet<int> Sounds)>();
        for (int index = first; index <= last; index++)
        {
            if (LengthAt(bars, index) is not { } length || TripletSounds(bars[index], length, printed) is not { } sounds)
            {
                return false;
            }

            plans.Add((index, sounds));
        }

        if (plans.Any(plan => plan.Sounds.SelectMany(i => (long[])[bars[plan.Index].Sounds[i].Duration, bars[plan.Index].Onsets[i]]).Any(each => each * factor.Times % factor.By != 0)))
        {
            ScaleDivisions(part, factor.By);
            bars = ScanRepair.Bars(part);
        }

        foreach ((int index, HashSet<int> sounds) in plans)
        {
            WriteTriplets(bars[index], sounds, factor, printed);
        }

        return true;
    }

    /// <summary>
    /// The sounds of the bar the mark re-times, or null when it does not fit the bar: each voice (by staff and
    /// <c>voice</c>) runs on from the bar's start either to its end as read or, re-timed by the mark, to it; and the bar
    /// has a triplet after the mark ("yes") or none ("no").
    /// </summary>
    private static HashSet<int>? TripletSounds(ScanRepair.Bar bar, QuarterFraction length, bool printed)
    {
        List<XElement> notes = [.. bar.Measure.Elements("note")];
        QuarterFraction asRead = printed ? new QuarterFraction(length.Numerator * 3, length.Denominator * 2) : new QuarterFraction(length.Numerator * 2, length.Denominator * 3);
        HashSet<int> sounds = [];
        bool triplets = false;
        foreach (IGrouping<(int Staff, string? Voice), int> voice in Enumerable.Range(0, bar.Sounds.Count).Where(i => !bar.Sounds[i].IsGrace)
            .GroupBy(i => (bar.Sounds[i].Staff, ((string?)notes[i].Element("voice"))?.Trim())))
        {
            int inTriplets = voice.Count(i => IsTriplet(notes[i]));
            if ((printed ? inTriplets == 0 : inTriplets == voice.Count()) && RunsTo(bar, voice, asRead))
            {
                sounds.UnionWith(Enumerable.Range(0, bar.Sounds.Count).Where(i => voice.Contains(i) || (bar.Sounds[i].IsGrace && bar.Sounds[i].Staff == voice.Key.Staff)));
                triplets |= printed;
            }
            else if (RunsTo(bar, voice, length))
            {
                triplets |= inTriplets > 0;
            }
            else
            {
                return null;
            }
        }

        return triplets == printed ? sounds : null;
    }

    /// <summary>Whether the voice's sounds follow on from the bar's start, chords together, with no gap, to <paramref name="length"/>.</summary>
    private static bool RunsTo(ScanRepair.Bar bar, IEnumerable<int> voice, QuarterFraction length)
    {
        long reach = 0;
        foreach (int i in voice.OrderBy(i => bar.Onsets[i]))
        {
            if (bar.Onsets[i] > reach)
            {
                return false;
            }

            reach = Math.Max(reach, bar.Onsets[i] + bar.Sounds[i].Duration);
        }

        return new QuarterFraction(reach, bar.Divisions) == length;
    }

    /// <summary>The bar's <paramref name="sounds"/> re-timed by <paramref name="factor"/>, each note written in triplets or out of them.</summary>
    private static void WriteTriplets(ScanRepair.Bar bar, HashSet<int> sounds, (long Times, long By) factor, bool printed)
    {
        List<XElement> notes = [.. bar.Measure.Elements("note")];
        long[] onsets = [.. bar.Onsets];
        foreach (int i in sounds)
        {
            onsets[i] = onsets[i] * factor.Times / factor.By;
            ScanRepair.Sound sound = bar.Sounds[i];
            if (sound.IsGrace)
            {
                continue;
            }

            bar.Sounds[i] = sound with { Duration = sound.Duration * factor.Times / factor.By };
            notes[i].SetElementValue("duration", bar.Sounds[i].Duration.ToString(CultureInfo.InvariantCulture));
            notes[i].Elements("time-modification").Remove();
            notes[i].Elements("notations").Elements("tuplet").Remove();
            notes[i].Elements("notations").Where(static notations => !notations.HasElements).Remove();
            if (printed)
            {
                PutBefore(notes[i], ["stem", "notehead", "notehead-text", "staff", "beam", "notations", "lyric", "play", "listen"],
                    new XElement("time-modification", new XElement("actual-notes", "3"), new XElement("normal-notes", "2")));
            }
        }

        if (printed)
        {
            Brackets(bar, notes, sounds, onsets, factor);
        }

        bar.WriteOnsets(onsets);
    }

    /// <summary>
    /// The tuplets' starts and stops on the re-timed notes of each voice, chords counted once: a bracket opens on a note
    /// and closes on the first after which it has lasted a whole multiple of the time of two of its first note as read, a
    /// quarter at most (three triplet eighths in a quarter, three triplet quarters in a half).
    /// </summary>
    private static void Brackets(ScanRepair.Bar bar, List<XElement> notes, HashSet<int> sounds, long[] onsets, (long Times, long By) factor)
    {
        foreach (IGrouping<(int Staff, string? Voice), int> voice in sounds.Where(i => !bar.Sounds[i].IsGrace && !bar.Sounds[i].IsChord).Order()
            .GroupBy(i => (bar.Sounds[i].Staff, ((string?)notes[i].Element("voice"))?.Trim())))
        {
            int? open = null;
            long unit = 0;
            foreach (int i in voice)
            {
                if (open is null)
                {
                    (open, unit) = (i, Math.Min(2 * bar.Sounds[i].Duration * factor.By / factor.Times, bar.Divisions));
                    Tuplet(notes[i], "start");
                }

                if ((onsets[i] + bar.Sounds[i].Duration - onsets[open.Value]) % unit == 0)
                {
                    Tuplet(notes[i], "stop");
                    open = null;
                }
            }

            if (open is { } unclosed)
            {
                notes[unclosed].Elements("notations").Elements("tuplet").Remove();
            }
        }
    }

    /// <summary>A tuplet's start or stop in the note's notations, made in MusicXML's order where it has none.</summary>
    private static void Tuplet(XElement note, string type)
    {
        if (note.Element("notations") is not { } notations)
        {
            notations = new XElement("notations");
            PutBefore(note, ["lyric", "play", "listen"], notations);
        }

        notations.Add(new XElement("tuplet", new XAttribute("type", type)));
    }

    /// <summary>Puts <paramref name="element"/> in the note before the first of its elements named in <paramref name="after"/>, else at its end.</summary>
    private static void PutBefore(XElement note, string[] after, XElement element)
    {
        if (note.Elements().FirstOrDefault(each => after.Contains(each.Name.LocalName)) is { } next)
        {
            next.AddBeforeSelf(element);
        }
        else
        {
            note.Add(element);
        }
    }

    /// <summary>Whether the note is written in triplets: 3 in the time of 2.</summary>
    private static bool IsTriplet(XElement note) =>
        note.Element("time-modification") is { } modification && Number(modification.Element("actual-notes")) == 3 && Number(modification.Element("normal-notes")) == 2;

    /// <summary>The length of measure <paramref name="index"/> by the time signature in force there (4/4 where none is written); null for one not of two whole numbers.</summary>
    private static QuarterFraction? LengthAt(List<ScanRepair.Bar> bars, int index) =>
        bars.Take(index + 1).LastOrDefault(static bar => bar.Time is not null)?.Time is { } time ? ScanRepair.Written(time) : new QuarterFraction(4, 1);

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