using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>
/// Writes the bars read from one page as MusicXML, the shape the scan server's engines give (decision record 0029): one
/// partwise part with a staff per printed staff, 480 divisions a quarter, each staff's voices one after another with a
/// backup between them, each note's beam as read, and the time a voice skips, up to the bar's end, as a
/// <c>&lt;forward&gt;</c> in that voice, so that every voice adds up to the bar. The
/// first bar holds the clefs, key and meter in force, so the page stands on its own and <see cref="Scan.ScanJoin"/> joins it
/// with the others.
/// </summary>
internal static class VectorMusicXml
{
    private const int Divisions = 480;

    /// <summary>The page as MusicXML.</summary>
    /// <param name="bars">The page's bars, timed.</param>
    /// <param name="staves">How many staves the part has.</param>
    /// <param name="opening">The clefs, key and meter in force where the page starts.</param>
    public static byte[] Write(IReadOnlyList<VectorBar> bars, int staves, (IReadOnlyList<VectorClef> Clefs, int Fifths, VectorMeter? Meter) opening)
    {
        var part = new XElement("part", new XAttribute("id", "P1"));
        for (int index = 0; index < bars.Count; index++)
        {
            part.Add(Measure(bars[index], index + 1, staves, index == 0 ? opening : null));
        }

        var score = new XElement("score-partwise", new XAttribute("version", "4.0"),
            new XElement("identification", new XElement("encoding", new XElement("software", VectorReader.EngineAndVersion))),
            new XElement("part-list", new XElement("score-part", new XAttribute("id", "P1"), new XElement("part-name", "Piano"))),
            part);
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            new XDocument(new XDeclaration("1.0", "UTF-8", null), score).Save(writer);
        }

        return stream.ToArray();
    }

    private static XElement Measure(VectorBar bar, int number, int staves, (IReadOnlyList<VectorClef> Clefs, int Fifths, VectorMeter? Meter)? opening)
    {
        var measure = new XElement("measure", new XAttribute("number", Text(number)));
        XElement? attributes = Attributes(bar, staves, opening);
        if (attributes is not null)
        {
            measure.Add(attributes);
        }

        int position = 0;
        // The bar ends where its longest voice does.
        int end = bar.Staves.SelectMany(events => events).Where(item => !item.Grace).Select(item => item.Onset + item.Ticks).DefaultIfEmpty(0).Max();
        foreach (IGrouping<int, VectorEvent> voice in bar.Staves.SelectMany(events => events).GroupBy(item => item.Voice).OrderBy(group => group.Key))
        {
            if (position > 0)
            {
                measure.Add(new XElement("backup", new XElement("duration", Text(position))));
                position = 0;
            }

            List<ClefChange> clefs = [.. bar.Clefs.Where(clef => clef.Staff == voice.First().Staff && clef.X > voice.Min(item => item.X) && (voice.Key - 1) % 4 == 0)];
            VectorEvent[] ordered = [.. voice.OrderBy(item => item.Onset).ThenBy(item => item.Grace ? 0 : 1).ThenBy(item => item.X)];
            Dictionary<VectorEvent, string> beams = Beams(ordered);
            foreach (VectorEvent item in ordered)
            {
                foreach (ClefChange clef in clefs.Where(clef => clef.X <= item.X).ToList())
                {
                    measure.Add(new XElement("attributes", Clef(clef.Clef, clef.Staff + 1)));
                    clefs.Remove(clef);
                }

                if (item.Onset > position)
                {
                    measure.Add(Forward(item.Onset - position, item));
                    position = item.Onset;
                }

                measure.Add(Notes(item, beams.GetValueOrDefault(item)));
                position += item.Ticks;
            }

            if (end > position)
            {
                measure.Add(Forward(end - position, ordered[0]));
                position = end;
            }
        }

        return measure;
    }

    /// <summary>Time the voice of <paramref name="item"/> skips, in that voice and on its staff.</summary>
    private static XElement Forward(int duration, VectorEvent item) =>
        new("forward", new XElement("duration", Text(duration)), new XElement("voice", Text(item.Voice)), new XElement("staff", Text(item.Staff + 1)));

    private static XElement? Attributes(VectorBar bar, int staves, (IReadOnlyList<VectorClef> Clefs, int Fifths, VectorMeter? Meter)? opening)
    {
        var attributes = new XElement("attributes");
        if (opening is not null)
        {
            attributes.Add(new XElement("divisions", Text(Divisions)));
        }

        int? fifths = bar.Fifths ?? opening?.Fifths;
        if (fifths is { } key)
        {
            attributes.Add(new XElement("key", new XElement("fifths", Text(key))));
        }

        if ((bar.Meter ?? opening?.Meter) is { } meter)
        {
            attributes.Add(new XElement("time", new XElement("beats", Text(meter.Beats)), new XElement("beat-type", Text(meter.BeatType))));
        }

        if (opening is { } first)
        {
            attributes.Add(new XElement("staves", Text(staves)));
        }

        attributes.Add(OpeningClefs(bar, staves, opening?.Clefs));
        // Clefs at the bar's start are written here; the writer puts the rest among the notes.
        bar.Clefs.RemoveAll(change => !bar.Staves[change.Staff].Any(item => item.X < change.X));
        return attributes.HasElements ? attributes : null;
    }

    /// <summary>
    /// The clefs written at the bar's start: on a page's first bar every staff's, the one printed before its first note or
    /// else the one in force; on another bar only those printed before the staff's first note.
    /// </summary>
    private static IEnumerable<XElement> OpeningClefs(VectorBar bar, int staves, IReadOnlyList<VectorClef>? inForce)
    {
        bool AtStart(ClefChange change) => !bar.Staves[change.Staff].Any(item => item.X < change.X);
        if (inForce is null)
        {
            return bar.Clefs.Where(AtStart).Select(change => Clef(change.Clef, change.Staff + 1));
        }

        return Enumerable.Range(0, staves).Select(staff =>
        {
            ClefChange? printed = bar.Clefs.Where(change => change.Staff == staff && AtStart(change)).Cast<ClefChange?>().FirstOrDefault();
            VectorClef clef = printed?.Clef ?? (staff < inForce.Count ? inForce[staff] : staff == 0 ? VectorClef.Treble : VectorClef.Bass);
            return Clef(clef, staff + 1);
        });
    }

    private static XElement Clef(VectorClef clef, int staff)
    {
        var element = new XElement("clef", new XAttribute("number", Text(staff)), new XElement("sign", clef.Sign.ToString()), new XElement("line", Text(clef.Line)));
        if (clef.Octave != 0)
        {
            element.Add(new XElement("clef-octave-change", Text(clef.Octave)));
        }

        return element;
    }

    /// <summary>
    /// Where each of a voice's notes stands under its beam, as MusicXML writes the first beam: "begin", "continue" or "end";
    /// a beam with one note of the voice under it is left out.
    /// </summary>
    private static Dictionary<VectorEvent, string> Beams(VectorEvent[] voice)
    {
        var beams = new Dictionary<VectorEvent, string>();
        foreach (IGrouping<int, VectorEvent> beam in voice.Where(item => item.Beam >= 0 && !item.Rest && !item.Grace).GroupBy(item => item.Beam))
        {
            VectorEvent[] under = [.. beam];
            for (int index = 0; under.Length >= 2 && index < under.Length; index++)
            {
                beams[under[index]] = index == 0 ? "begin" : index == under.Length - 1 ? "end" : "continue";
            }
        }

        return beams;
    }

    private static IEnumerable<XElement> Notes(VectorEvent item, string? beam)
    {
        if (item.Rest)
        {
            yield return Note(item, null, false, null);
            yield break;
        }

        for (int index = 0; index < item.Pitches.Count; index++)
        {
            yield return Note(item, item.Pitches[index], index > 0, index == 0 ? beam : null);
        }
    }

    /// <param name="beam">Where the note stands under its beam, written on a chord's first note; null for none.</param>
    private static XElement Note(VectorEvent item, VectorPitch? pitch, bool chord, string? beam)
    {
        var note = new XElement("note");
        if (item.Grace)
        {
            note.Add(new XElement("grace", new XAttribute("slash", "yes")));
        }

        if (chord)
        {
            note.Add(new XElement("chord"));
        }

        note.Add(pitch is { } written ? Pitch(written) : item.WholeBar ? new XElement("rest", new XAttribute("measure", "yes")) : new XElement("rest"));
        if (!item.Grace)
        {
            note.Add(new XElement("duration", Text(item.Ticks)));
        }

        note.Add(Ties(pitch, "tie"));
        note.Add(new XElement("voice", Text(item.Voice)), new XElement("type", Type(item.WholeBar ? 1920 : item.Written)));
        note.Add(Enumerable.Range(0, item.Dots).Select(_ => new XElement("dot")));
        if (item.Tuplet != (1, 1))
        {
            note.Add(new XElement("time-modification", new XElement("actual-notes", Text(item.Tuplet.Actual)), new XElement("normal-notes", Text(item.Tuplet.Normal))));
        }

        if (item.StemUp is { } up)
        {
            note.Add(new XElement("stem", up ? "up" : "down"));
        }

        note.Add(new XElement("staff", Text(item.Staff + 1)));
        if (beam is not null)
        {
            note.Add(new XElement("beam", new XAttribute("number", "1"), beam));
        }

        if (pitch is { TieStop: true } or { TieStart: true })
        {
            note.Add(new XElement("notations", Ties(pitch, "tied")));
        }

        return note;
    }

    private static XElement Pitch(VectorPitch pitch)
    {
        var element = new XElement("pitch", new XElement("step", pitch.Step.ToString()));
        if (pitch.Alter != 0)
        {
            element.Add(new XElement("alter", Text(pitch.Alter)));
        }

        element.Add(new XElement("octave", Text(pitch.Octave)));
        return element;
    }

    /// <summary>A tied pitch's tie elements (a note's "tie", its notations' "tied"), the stop before the start.</summary>
    private static IEnumerable<XElement> Ties(VectorPitch? pitch, string name)
    {
        if (pitch is { TieStop: true })
        {
            yield return new XElement(name, new XAttribute("type", "stop"));
        }

        if (pitch is { TieStart: true })
        {
            yield return new XElement(name, new XAttribute("type", "start"));
        }
    }

    private static string Type(int ticks) => ticks switch
    {
        >= 3840 => "breve",
        >= 1920 => "whole",
        >= 960 => "half",
        >= 480 => "quarter",
        >= 240 => "eighth",
        >= 120 => "16th",
        >= 60 => "32nd",
        _ => "64th",
    };

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}