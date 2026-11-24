using System.Globalization;
using System.Xml.Linq;

using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Scan;

public static partial class ScanMerge
{
    /// <summary>
    /// The backbone's score with every staff taken from another reading written into its measure (<see cref="Rewrite"/>),
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
                long divisions = rewritten ? Rewrite(bar, staves) : bar.Divisions;
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
    /// The measure written again from the staves chosen for it, each from its reading's bar in divisions both share; what is
    /// not a note (attributes, directions, bar lines) stays the backbone's, before the notes or, for what ends it, after.
    /// </summary>
    /// <returns>The divisions the measure is written in.</returns>
    private static long Rewrite(ScanRepair.Bar bar, Choice[] staves)
    {
        (ScanRepair.Bar Bar, int Staff)[] sources = [.. staves.Select(choice => (choice.Source.Parts[choice.Part][choice.SourceMeasure], choice.Staff))];
        long divisions = sources.Aggregate(bar.Divisions, (shared, source) => Shared(shared, source.Bar.Divisions));
        List<XElement> music = [];
        long cursor = 0;
        foreach ((ScanRepair.Bar source, int staff) in sources)
        {
            cursor = Emit(music, source, staff, divisions / source.Divisions, cursor);
        }

        List<XElement> children = [.. bar.Measure.Elements()];
        int first = children.FindIndex(IsMusic), last = children.FindLastIndex(IsMusic);
        List<XElement> front = first < 0 ? children : [.. children.Take(first), .. children.Skip(first).Take(last - first + 1).Where(child => !IsMusic(child))];
        List<XElement> end = first < 0 ? [] : [.. children.Skip(last + 1)];
        foreach (XElement offset in front.Concat(end).SelectMany(child => child.Descendants("offset")))
        {
            if (long.TryParse(offset.Value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
            {
                offset.Value = (value * (divisions / bar.Divisions)).ToString(CultureInfo.InvariantCulture);
            }
        }

        bar.Measure.RemoveNodes();
        bar.Measure.Add(front, music, end);
        return divisions;
    }

    /// <summary>
    /// The notes of one staff of a source bar, in its written order, at their onsets in the shared divisions: a chord tone
    /// stays one only after a note of the same staff at its onset, and the cursor is moved by backups and forwards.
    /// </summary>
    /// <returns>The cursor after the staff.</returns>
    private static long Emit(List<XElement> music, ScanRepair.Bar source, int staff, long factor, long cursor)
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

        return cursor;
    }

    /// <summary>Sets the measure's divisions in its first attributes, made where it has none.</summary>
    private static void Declare(XElement measure, long divisions)
    {
        string value = divisions.ToString(CultureInfo.InvariantCulture);
        XElement? attributes = measure.Element("attributes");
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