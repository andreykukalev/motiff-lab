using System.Globalization;
using System.Xml.Linq;

using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Scan;

public static partial class ScanMerge
{
    /// <summary>
    /// The backbone's score with every staff taken from another reading written into its measure (<see cref="WriteChosenStaves"/>),
    /// the divisions declared wherever the measures written so change them, and each bar to check marked in its first part.
    /// </summary>
    private static XElement Written(Reading backbone, List<Choice> taken, List<ScanMergedBar> bars)
    {
        ILookup<(int Part, int Measure), Choice> chosen = taken.ToLookup(choice => (choice.Part, choice.Measure));
        for (int part = 0; part < backbone.Parts.Count; part++)
        {
            long inForce = 1;
            foreach ((ScanRepair.Bar bar, int measure) in backbone.Parts[part].Select((bar, measure) => (bar, measure)))
            {
                Choice[] staves = [.. chosen[(part, measure)]];
                bool rewritten = staves.Any(choice => choice.Source != backbone);
                long divisions = rewritten ? WriteChosenStaves(bar, staves) : bar.Divisions;
                if (divisions != inForce || (rewritten && divisions != bar.Divisions))
                {
                    Declare(bar.Measure, divisions);
                }

                inForce = divisions;
            }
        }

        foreach ((ScanRepair.Bar bar, int measure) in backbone.Parts[0].Select((bar, measure) => (bar, measure)).Where(pair => bars[pair.measure].Level == ScanLevel.Check))
        {
            Mark(bar.Measure);
        }

        return backbone.Root;
    }

    /// <summary>
    /// Marks a measure as one no two engines agree on, with a hidden direction (<see cref="MusicXmlImport.EnginesDisagreeMark"/>)
    /// at its start, after its attributes and layout, which the importer reads and <see cref="ScanCheck"/> flags.
    /// </summary>
    private static void Mark(XElement measure)
    {
        XElement mark = new("direction", new XElement("direction-type",
            new XElement("other-direction", new XAttribute("print-object", "no"), MusicXmlImport.EnginesDisagreeMark)));
        if (measure.Elements().TakeWhile(child => child.Name.LocalName is "attributes" or "print").LastOrDefault() is { } before)
        {
            before.AddAfterSelf(mark);
        }
        else
        {
            measure.AddFirst(mark);
        }
    }

    /// <summary>
    /// The measure written again from the staves chosen for it, each from its reading's bar in divisions both share. What is
    /// not a note stays the backbone's: what comes before its first note and after its last stays there, and what stands
    /// between them (a clef, key or meter change, a dynamic, a pedal mark) is written on its staff at its own onset.
    /// </summary>
    /// <returns>The divisions the measure is written in.</returns>
    private static long WriteChosenStaves(ScanRepair.Bar bar, Choice[] staves)
    {
        (ScanRepair.Bar Bar, int Staff)[] sources = [.. staves.Select(choice => (choice.Source.Parts[choice.Part][choice.SourceMeasure], choice.Staff))];
        long divisions = sources.Aggregate(bar.Divisions, (shared, source) => Shared(shared, source.Bar.Divisions));
        long factor = divisions / bar.Divisions;
        List<XElement> children = [.. bar.Measure.Elements()];
        int first = children.FindIndex(IsMusic), last = children.FindLastIndex(IsMusic);
        List<(XElement Element, long Onset)> placed = [.. bar.Placed().Select(other => (other.Element, other.Onset * factor))];
        List<XElement> front = first < 0 ? children : [.. children.Take(first)];
        List<XElement> end = first < 0 ? [] : [.. children.Skip(last + 1)];
        List<(XElement Element, long Onset)> between = [.. placed.Where(other => !front.Contains(other.Element) && !end.Contains(other.Element))];
        foreach (XElement offset in placed.SelectMany(other => other.Element.Descendants("offset")))
        {
            if (long.TryParse(offset.Value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
            {
                offset.Value = (value * factor).ToString(CultureInfo.InvariantCulture);
            }
        }

        List<XElement> music = [];
        long cursor = 0;
        foreach ((ScanRepair.Bar source, int staff) in sources)
        {
            bool top = staff == sources[0].Staff;
            Queue<(XElement Element, long Onset)> own = new(between
                .Where(other => StaffOf(other.Element) == staff || (top && sources.All(source => source.Staff != StaffOf(other.Element))))
                .OrderBy(other => other.Onset));
            cursor = Emit(music, source, staff, divisions / source.Divisions, cursor, own);
        }

        bar.Measure.RemoveNodes();
        bar.Measure.Add(front, music, end);
        return divisions;
    }

    /// <summary>The staff an element other than a note is for: its <c>staff</c>, or the <c>number</c> of its first numbered child (a clef), else 1.</summary>
    private static int StaffOf(XElement element) =>
        int.TryParse(((string?)element.Element("staff") ?? (string?)element.Elements().Attributes("number").FirstOrDefault())?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int staff)
            ? staff
            : 1;

    /// <summary>
    /// The notes of one staff of a source bar, in its written order, at their onsets in the shared divisions: a chord tone
    /// stays one only after a note of the same staff at its onset, and the cursor is moved by backups and forwards. The
    /// backbone's elements for the staff (<paramref name="others"/>, by onset) go in before the first note at or after theirs.
    /// </summary>
    /// <returns>The cursor after the staff.</returns>
    private static long Emit(List<XElement> music, ScanRepair.Bar source, int staff, long factor, long cursor, Queue<(XElement Element, long Onset)> others)
    {
        List<XElement> notes = [.. source.Measure.Elements("note")];
        (long Onset, bool Grace)? previous = null;
        for (int i = 0; i < notes.Count; i++)
        {
            ScanRepair.Sound sound = source.Sounds[i];
            if (sound.Staff != staff)
            {
                continue;
            }

            long onset = source.Onsets[i] * factor;
            XElement note = new(notes[i]);
            if (note.Element("duration") is { } duration)
            {
                duration.Value = (sound.Duration * factor).ToString(CultureInfo.InvariantCulture);
            }

            bool chord = sound.IsChord && previous is { } before && before.Onset == onset && before.Grace == sound.IsGrace;
            if (!chord)
            {
                cursor = Place(music, others, onset, cursor);
                note.Element("chord")?.Remove();
                if (onset != cursor)
                {
                    music.Add(ScanRepair.Bar.Move(cursor, onset));
                }

                cursor = sound.IsGrace ? onset : onset + (sound.Duration * factor);
            }

            music.Add(note);
            previous = (onset, sound.IsGrace);
        }

        return Place(music, others, long.MaxValue, cursor);
    }

    /// <summary>Writes the elements whose onset is no later than <paramref name="until"/>, each at its onset.</summary>
    /// <returns>The cursor after them.</returns>
    private static long Place(List<XElement> music, Queue<(XElement Element, long Onset)> others, long until, long cursor)
    {
        while (others.TryPeek(out (XElement Element, long Onset) other) && other.Onset <= until)
        {
            others.Dequeue();
            if (other.Onset != cursor)
            {
                music.Add(ScanRepair.Bar.Move(cursor, other.Onset));
            }

            music.Add(other.Element);
            cursor = other.Onset;
        }

        return cursor;
    }

    /// <summary>Sets the measure's divisions in its attributes before its first note, made where it has none.</summary>
    private static void Declare(XElement measure, long divisions)
    {
        string value = divisions.ToString(CultureInfo.InvariantCulture);
        XElement? attributes = measure.Elements().TakeWhile(child => !IsMusic(child)).FirstOrDefault(child => child.Name.LocalName == "attributes");
        if (attributes is null)
        {
            measure.AddFirst(new XElement("attributes", new XElement("divisions", value)));
        }
        else if (attributes.Element("divisions") is { } written)
        {
            written.Value = value;
        }
        else
        {
            attributes.AddFirst(new XElement("divisions", value));
        }
    }

    /// <summary>The least divisions both counts divide: a × (b over their greatest common divisor), the denominator of a/b in lowest terms.</summary>
    private static long Shared(long a, long b) => a * new QuarterFraction(a, b).Denominator;

    private static bool IsMusic(XElement element) => element.Name.LocalName is "note" or "backup" or "forward";
}