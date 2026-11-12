using System.Globalization;
using System.Text;
using System.Xml;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// Writes the Reference's music (<see cref="ReferenceMusic"/>): whole-bar chords and runs of quavers in bars of 4/4, on a
/// treble staff or a piano's two, each note spelled as given. In a run the notes stay in one layer and cross to the other
/// staff where they belong, so a broken chord keeps its beams across the staves. Beams join the quavers of each half bar;
/// rests fill what a run leaves of its bar. Accidentals follow the key signature and hold to the end of the bar.
/// </summary>
internal sealed class ReferenceScore(bool grandStaff, int fifths)
{
    private const string MeiNamespace = "http://www.music-encoding.org/ns/mei";
    private const int EighthsPerBar = 8;
    private const long EighthTicks = CanonicalTime.TicksPerQuarter / 2;
    private const int Velocity = 80;

    private static readonly string[] s_stepNames = ["c", "d", "e", "f", "g", "a", "b"];

    private readonly List<Written> _written = [];

    /// <summary>The bar after the last written: where the next bar of music goes.</summary>
    public int NextBar => _written.Count == 0 ? 0 : _written.Max(written => written.Bar + ((written.Slot + written.Eighths - 1) / EighthsPerBar)) + 1;

    /// <summary>Tones struck at once and held through bar <paramref name="bar"/>; on a piano's two staves split at middle C unless a staff is given.</summary>
    public void Chord(int bar, IReadOnlyList<WrittenTone> tones, int? staff = null)
    {
        foreach (IGrouping<int, WrittenTone> part in tones.GroupBy(tone => staff ?? StaffOf(tone)).OrderBy(group => group.Key))
        {
            _written.Add(new Written(bar, 0, EighthsPerBar, [.. part.OrderBy(tone => tone.Midi)], part.Key, part.Key, Ring: 0));
        }
    }

    /// <summary>
    /// Tones one after another a quaver apart from <paramref name="slot"/> of bar <paramref name="bar"/>, over as many bars as
    /// they need. With <paramref name="ringing"/> each is held, as a broken chord is let ring, until the run ends.
    /// </summary>
    public void Run(int bar, int slot, IReadOnlyList<WrittenTone> tones, bool ringing = false, int? staff = null)
    {
        int start = (bar * EighthsPerBar) + slot;
        for (int index = 0; index < tones.Count; index++)
        {
            int at = start + index;
            int ring = ringing ? tones.Count - index : 1;
            _written.Add(new Written(at / EighthsPerBar, at % EighthsPerBar, 1, [tones[index]], staff ?? StaffOf(tones[index]), staff ?? 0, ring));
        }
    }

    /// <summary>The music written so far, at <paramref name="microsecondsPerQuarter"/>, with these keys marked on a keyboard.</summary>
    public ReferenceMusic Build(int microsecondsPerQuarter, IReadOnlyList<WrittenTone> marked)
    {
        int bars = Math.Max(1, NextBar);
        var notes = new List<Note>();
        var ids = new Dictionary<(Written, int), string>();
        foreach (Written written in _written.OrderBy(written => (written.Bar * EighthsPerBar) + written.Slot).ThenBy(written => written.Staff))
        {
            long onset = ((written.Bar * EighthsPerBar) + written.Slot) * EighthTicks;
            long length = (written.Ring > 0 ? written.Ring : written.Eighths) * EighthTicks;
            for (int index = 0; index < written.Tones.Count; index++)
            {
                string id = $"ref-{notes.Count.ToString(CultureInfo.InvariantCulture)}";
                ids[(written, index)] = id;
                notes.Add(new Note
                {
                    Id = id,
                    Pitch = new Pitch(written.Tones[index].Midi),
                    Span = new TickSpan(new Tick(onset), length),
                    Velocity = Velocity,
                    Track = 0,
                    Attribution = Attribution.Inferred(1),
                });
            }
        }

        int low = marked.Count == 0 ? 60 : marked.Min(tone => tone.Midi);
        int high = marked.Count == 0 ? 71 : marked.Max(tone => tone.Midi);
        return new ReferenceMusic(
            notes,
            new Tick(bars * EighthsPerBar * EighthTicks),
            microsecondsPerQuarter,
            Mei(bars, ids),
            low - (low % 12),
            high + (11 - (high % 12)),
            [.. marked.Select(tone => new MarkedKey(tone.Midi, tone.Name.Letter))]);
    }

    private int StaffOf(WrittenTone tone) => grandStaff && tone.Midi < 60 ? 2 : 1;

    private string Mei(int bars, Dictionary<(Written, int), string> ids)
    {
        // A StringBuilder would make the writer declare UTF-16; the document is handed on as UTF-8 text.
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        using (var xml = XmlWriter.Create(builder, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("mei", MeiNamespace);
            xml.WriteAttributeString("meiversion", "5.1");
            xml.WriteStartElement("meiHead", MeiNamespace);
            xml.WriteStartElement("fileDesc", MeiNamespace);
            xml.WriteStartElement("titleStmt", MeiNamespace);
            xml.WriteElementString("title", MeiNamespace, string.Empty);
            xml.WriteEndElement();
            xml.WriteElementString("pubStmt", MeiNamespace, string.Empty);
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteStartElement("music", MeiNamespace);
            xml.WriteStartElement("body", MeiNamespace);
            xml.WriteStartElement("mdiv", MeiNamespace);
            xml.WriteStartElement("score", MeiNamespace);
            WriteScoreDef(xml);
            xml.WriteStartElement("section", MeiNamespace);
            for (int bar = 0; bar < bars; bar++)
            {
                WriteMeasure(xml, bar, ids);
            }

            xml.WriteEndDocument();
        }

        return builder.ToString();
    }

    private void WriteScoreDef(XmlWriter xml)
    {
        xml.WriteStartElement("scoreDef", MeiNamespace);
        xml.WriteAttributeString("meter.count", "4");
        xml.WriteAttributeString("meter.unit", "4");
        xml.WriteAttributeString("keysig", fifths switch
        {
            0 => "0",
            > 0 => $"{fifths.ToString(CultureInfo.InvariantCulture)}s",
            _ => $"{(-fifths).ToString(CultureInfo.InvariantCulture)}f",
        });
        xml.WriteStartElement("staffGrp", MeiNamespace);
        if (grandStaff)
        {
            xml.WriteAttributeString("symbol", "brace");
            xml.WriteAttributeString("bar.thru", "true");
        }

        for (int staff = 1; staff <= (grandStaff ? 2 : 1); staff++)
        {
            xml.WriteStartElement("staffDef", MeiNamespace);
            xml.WriteAttributeString("n", staff.ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("lines", "5");
            xml.WriteAttributeString("clef.shape", staff == 1 ? "G" : "F");
            xml.WriteAttributeString("clef.line", staff == 1 ? "2" : "4");
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    private void WriteMeasure(XmlWriter xml, int bar, Dictionary<(Written, int), string> ids)
    {
        Written[] inBar = [.. _written.Where(written => written.Bar == bar)];
        xml.WriteStartElement("measure", MeiNamespace);
        xml.WriteAttributeString("n", (bar + 1).ToString(CultureInfo.InvariantCulture));
        var accidentals = new Accidentals(fifths);
        for (int staff = 1; staff <= (grandStaff ? 2 : 1); staff++)
        {
            Written[] layer = [.. inBar.Where(written => LayerOf(written, inBar) == staff).OrderBy(written => written.Slot)];
            xml.WriteStartElement("staff", MeiNamespace);
            xml.WriteAttributeString("n", staff.ToString(CultureInfo.InvariantCulture));
            xml.WriteStartElement("layer", MeiNamespace);
            xml.WriteAttributeString("n", "1");
            if (layer.Length == 0)
            {
                // Empty, or crossed into by the other staff's run: a rest only where nothing sounds.
                xml.WriteStartElement(inBar.Any(written => written.Staff == staff) ? "mSpace" : "mRest", MeiNamespace);
                xml.WriteEndElement();
            }
            else
            {
                WriteLayer(xml, staff, layer, ids, accidentals);
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
    }

    /// <summary>
    /// The staff whose layer holds a note: a chord's own, a run's given one, else the lower staff whenever the run
    /// reaches into it within the bar, so the run's notes stay in one layer and its beams cross.
    /// </summary>
    private static int LayerOf(Written written, Written[] inBar)
    {
        if (written.Eighths > 1 || written.Layer > 0)
        {
            return written.Staff;
        }

        return inBar.Any(other => other.Eighths == 1 && other.Layer == 0 && other.Staff == 2) ? 2 : 1;
    }

    private static void WriteLayer(XmlWriter xml, int staff, Written[] layer, Dictionary<(Written, int), string> ids, Accidentals accidentals)
    {
        int slot = 0;
        int index = 0;
        while (index < layer.Length)
        {
            Written next = layer[index];
            WriteRests(xml, slot, next.Slot);
            if (next.Eighths > 1)
            {
                WriteEvent(xml, staff, next, "1", ids, accidentals);
                slot = next.Slot + next.Eighths;
                index++;
                continue;
            }

            // The quavers that follow one another within this half bar share a beam.
            int half = next.Slot / 4;
            int end = index;
            while (end + 1 < layer.Length && layer[end + 1].Eighths == 1 && layer[end + 1].Slot == layer[end].Slot + 1 && layer[end + 1].Slot / 4 == half)
            {
                end++;
            }

            if (end > index)
            {
                xml.WriteStartElement("beam", MeiNamespace);
            }

            for (int i = index; i <= end; i++)
            {
                WriteEvent(xml, staff, layer[i], "8", ids, accidentals);
            }

            if (end > index)
            {
                xml.WriteEndElement();
            }

            slot = layer[end].Slot + 1;
            index = end + 1;
        }

        WriteRests(xml, slot, EighthsPerBar);
    }

    /// <summary>Rests from <paramref name="from"/> to <paramref name="to"/> in quavers, each on its beat: a half, a quarter or a quaver.</summary>
    private static void WriteRests(XmlWriter xml, int from, int to)
    {
        while (from < to)
        {
            int length = from % 4 == 0 && to - from >= 4 ? 4 : from % 2 == 0 && to - from >= 2 ? 2 : 1;
            xml.WriteStartElement("rest", MeiNamespace);
            xml.WriteAttributeString("dur", length switch { 4 => "2", 2 => "4", _ => "8" });
            xml.WriteEndElement();
            from += length;
        }
    }

    private static void WriteEvent(XmlWriter xml, int layerStaff, Written written, string dur, Dictionary<(Written, int), string> ids, Accidentals accidentals)
    {
        bool chord = written.Tones.Count > 1;
        if (chord)
        {
            xml.WriteStartElement("chord", MeiNamespace);
            xml.WriteAttributeString("xml", "id", null, $"{ids[(written, 0)]}-c");
            xml.WriteAttributeString("dur", dur);
        }

        for (int index = 0; index < written.Tones.Count; index++)
        {
            WrittenTone tone = written.Tones[index];
            xml.WriteStartElement("note", MeiNamespace);
            xml.WriteAttributeString("xml", "id", null, ids[(written, index)]);
            if (!chord)
            {
                xml.WriteAttributeString("dur", dur);
            }

            xml.WriteAttributeString("pname", s_stepNames[tone.Name.Step]);
            xml.WriteAttributeString("oct", tone.Octave.ToString(CultureInfo.InvariantCulture));
            if (accidentals.Shows(written.Staff, tone))
            {
                xml.WriteAttributeString("accid", Accidental(tone.Name.Alter));
            }
            else if (tone.Name.Alter != 0)
            {
                xml.WriteAttributeString("accid.ges", Accidental(tone.Name.Alter));
            }

            if (written.Staff != layerStaff)
            {
                xml.WriteAttributeString("staff", written.Staff.ToString(CultureInfo.InvariantCulture));
            }

            xml.WriteEndElement();
        }

        if (chord)
        {
            xml.WriteEndElement();
        }
    }

    private static string Accidental(int alter) => alter switch
    {
        -2 => "ff",
        -1 => "f",
        1 => "s",
        2 => "x",
        _ => "n",
    };

    /// <summary>One entry of the music: tones at a quaver of a bar, struck together, for some quavers.</summary>
    /// <param name="Staff">The staff the tones are written on.</param>
    /// <param name="Layer">The staff whose layer holds them when given; 0 for a run that keeps to one layer.</param>
    /// <param name="Ring">Quavers it sounds for when let ring longer than written; 0 for a chord, which sounds as written.</param>
    private sealed record Written(int Bar, int Slot, int Eighths, IReadOnlyList<WrittenTone> Tones, int Staff, int Layer, int Ring);

    /// <summary>The accidentals in force in one bar: the key signature's, then each one written, staff by staff, to the bar line.</summary>
    private sealed class Accidentals(int fifths)
    {
        private readonly int[] _key = Notation.PitchSpeller.KeyAlterations(fifths);
        private readonly Dictionary<(int Staff, int Step, int Octave), int> _written = [];

        /// <summary>Whether the tone needs its accidental written; from then on it is in force.</summary>
        public bool Shows(int staff, WrittenTone tone)
        {
            var place = (staff, tone.Name.Step, tone.Octave);
            int inForce = _written.TryGetValue(place, out int alter) ? alter : _key[tone.Name.Step];
            _written[place] = tone.Name.Alter;
            return inForce != tone.Name.Alter;
        }
    }
}