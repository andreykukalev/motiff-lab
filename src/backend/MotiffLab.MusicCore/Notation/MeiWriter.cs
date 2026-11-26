using System.Globalization;
using System.Text;
using System.Xml;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// Writes the planned notation as MEI 5.1 (<see cref="NotationGenerator"/>): the header, the staff groups (each ossia's
/// with its label, at <see cref="NotationGenerator.OssiaScale"/>), and each bar's staves, layers and elements, then its
/// words, dynamics, hairpins, slurs, fermatas, pedal marks and metronome marks.
/// </summary>
internal static class MeiWriter
{
    private const string MeiNamespace = "http://www.music-encoding.org/ns/mei";

    /// <summary>
    /// The size of a metronome mark's note: Verovio sets a SMuFL glyph in words at the music font's size, nearly twice the
    /// words' (720 against 405 units on the A4 page), which made "♩ = 92" look like an enlarged note; "x-small" (432) is about the words' size.
    /// </summary>
    private const string MarkNoteSize = "x-small";

    /// <summary>SMuFL's metNoteQuarterUp, the note of a metronome mark.</summary>
    private const string QuarterNoteGlyph = "\uECA5";

    /// <summary>metNoteQuarterUp and metAugmentationDot, as Verovio writes a dotted quarter's mark.</summary>
    private const string DottedQuarterNoteGlyph = "\uECA5 \uECB7";

    private static readonly string[] s_stepNames = ["c", "d", "e", "f", "g", "a", "b"];

    /// <summary>The MEI document: the header with the title, the staves of the ossias and the original, and the bars written.</summary>
    public static string Write(string title, List<PartPlan> parts, NotationOssia[] ossias, List<MeasurePlan> measures)
    {
        // A StringBuilder would make the writer declare UTF-16; the document is handed on as UTF-8 text.
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
        using (var xml = XmlWriter.Create(builder, settings))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("mei", MeiNamespace);
            xml.WriteAttributeString("meiversion", "5.1");

            xml.WriteStartElement("meiHead", MeiNamespace);
            xml.WriteStartElement("fileDesc", MeiNamespace);
            xml.WriteStartElement("titleStmt", MeiNamespace);
            xml.WriteElementString("title", MeiNamespace, title);
            xml.WriteEndElement();
            xml.WriteElementString("pubStmt", MeiNamespace, string.Empty);
            xml.WriteEndElement();
            xml.WriteEndElement();

            xml.WriteStartElement("music", MeiNamespace);
            xml.WriteStartElement("body", MeiNamespace);
            xml.WriteStartElement("mdiv", MeiNamespace);
            xml.WriteStartElement("score", MeiNamespace);

            MeasurePlan first = measures[0];
            xml.WriteStartElement("scoreDef", MeiNamespace);
            WriteMeter(xml, first.Bar.Signature);
            xml.WriteAttributeString("keysig", KeySig(first.Fifths));
            xml.WriteStartElement("staffGrp", MeiNamespace);
            for (int index = 0; index < ossias.Length; index++)
            {
                // An ossia is a group of its own with the label at its left, above the original.
                xml.WriteStartElement("staffGrp", MeiNamespace);
                xml.WriteElementString("label", MeiNamespace, ossias[index].Label);
                WriteStaffGroups(xml, parts[index].Staves, NotationGenerator.OssiaScale);
                xml.WriteEndElement();
            }

            WriteStaffGroups(xml, parts[^1].Staves, null);
            xml.WriteEndElement();
            xml.WriteEndElement();

            xml.WriteStartElement("section", MeiNamespace);
            MeasurePlan? previous = null;
            foreach (MeasurePlan measure in measures)
            {
                bool meterChanged = previous is not null && previous.Bar.Signature != measure.Bar.Signature;
                bool keyChanged = previous is not null && previous.Fifths != measure.Fifths;
                if (meterChanged || keyChanged)
                {
                    xml.WriteStartElement("scoreDef", MeiNamespace);
                    if (meterChanged)
                    {
                        WriteMeter(xml, measure.Bar.Signature);
                    }

                    if (keyChanged)
                    {
                        xml.WriteAttributeString("keysig", KeySig(measure.Fifths));
                    }

                    xml.WriteEndElement();
                }

                WriteMeasure(xml, measure);
                previous = measure;
            }

            xml.WriteEndDocument();
        }

        return builder.ToString();
    }

    private static void WriteMeter(XmlWriter xml, TimeSignature signature)
    {
        xml.WriteAttributeString("meter.count", Number(signature.Numerator));
        xml.WriteAttributeString("meter.unit", Number(signature.Denominator));
    }

    private static string KeySig(int fifths) => fifths switch
    {
        0 => "0",
        > 0 => $"{Number(fifths)}s",
        _ => $"{Number(-fifths)}f",
    };

    /// <summary>The staves of one part, a braced pair for a piano part, each at <paramref name="scale"/> when given.</summary>
    private static void WriteStaffGroups(XmlWriter xml, List<StaffPlan> staves, string? scale)
    {
        foreach (IGrouping<int, StaffPlan> track in staves.GroupBy(staff => staff.Track))
        {
            bool grand = track.Count() > 1;
            if (grand)
            {
                xml.WriteStartElement("staffGrp", MeiNamespace);
                xml.WriteAttributeString("symbol", "brace");
                xml.WriteAttributeString("bar.thru", "true");
            }

            foreach (StaffPlan staff in track)
            {
                xml.WriteStartElement("staffDef", MeiNamespace);
                xml.WriteAttributeString("n", Number(staff.Number));
                xml.WriteAttributeString("lines", "5");
                xml.WriteAttributeString("clef.shape", staff.Clef == Clef.Treble ? "G" : "F");
                xml.WriteAttributeString("clef.line", staff.Clef == Clef.Treble ? "2" : "4");
                if (scale is not null)
                {
                    xml.WriteAttributeString("scale", scale);
                }

                xml.WriteEndElement();
            }

            if (grand)
            {
                xml.WriteEndElement();
            }
        }
    }

    private static void WriteMeasure(XmlWriter xml, MeasurePlan measure)
    {
        xml.WriteStartElement("measure", MeiNamespace);
        xml.WriteAttributeString("n", Number(measure.Bar.Number));
        if (!measure.Bar.IsComplete || measure.Bar.IsPickup)
        {
            xml.WriteAttributeString("metcon", "false");
        }

        foreach (PartMeasure part in measure.Parts)
        {
            for (int staff = 0; staff < part.Staves.Length; staff++)
            {
                xml.WriteStartElement("staff", MeiNamespace);
                xml.WriteAttributeString("n", Number(part.FirstStaff + staff));
                List<List<Element>> layers = part.Staves[staff].Layers;
                for (int layer = 0; layer < layers.Count; layer++)
                {
                    if (layers[layer].Count == 0)
                    {
                        continue;
                    }

                    xml.WriteStartElement("layer", MeiNamespace);
                    xml.WriteAttributeString("n", Number(layer + 1));
                    foreach (Element element in layers[layer])
                    {
                        WriteElement(xml, element);
                    }

                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
            }
        }

        foreach (PartMeasure part in measure.Parts)
        {
            WriteDirections(xml, measure.Bar, part);
        }

        WriteTempos(xml, measure);
        xml.WriteEndElement();
    }

    /// <summary>
    /// The bar's metronome marks over the original's top staff, as Verovio writes a MusicXML metronome: the SMuFL quarter
    /// note, then "= 92"; in 6/8 the dotted quarter, "♩. = 60", with "mm.dots". As engravers set it (version 0.5.5, #521
    /// section 13), the mark at the piece's start stands over the start of the first system (tstamp 0, above the clef and
    /// the time signature, clear of the notes), and its note is the size of the words beside it ("x-small" of the music
    /// font, as the words are), so it reads as an annotation rather than a note of the music.
    /// </summary>
    private static void WriteTempos(XmlWriter xml, MeasurePlan measure)
    {
        foreach (TempoMark mark in measure.Tempos)
        {
            string perMinute = Number(mark.PerMinute);
            xml.WriteStartElement("tempo", MeiNamespace);
            xml.WriteAttributeString("staff", Number(measure.TempoStaff));
            xml.WriteAttributeString("place", "above");
            xml.WriteAttributeString("tstamp", mark.At == Tick.Zero ? "0" : Beat(measure.Bar, Math.Max(mark.At.Value, measure.Bar.Span.Onset.Value)));
            xml.WriteAttributeString("mm", perMinute);
            xml.WriteAttributeString("mm.unit", "4");
            bool dotted = mark.Beat == TempoBeat.DottedQuarter;
            if (dotted)
            {
                xml.WriteAttributeString("mm.dots", "1");
            }

            // MIDI's beats a minute are quarter notes'.
            xml.WriteAttributeString("midi.bpm", Number(mark.Beat.QuarterNotes(mark.PerMinute)));
            xml.WriteStartElement("rend", MeiNamespace);
            xml.WriteAttributeString("fontsize", MarkNoteSize);
            xml.WriteAttributeString("glyph.auth", "smufl");
            xml.WriteString(dotted ? DottedQuarterNoteGlyph : QuarterNoteGlyph);
            xml.WriteEndElement();
            xml.WriteString(" " + mark.Text);
            xml.WriteEndElement();
        }
    }

    /// <summary>A part's words, dynamics, hairpins, slurs and pedal marks in a bar, on the part's own staves.</summary>
    private static void WriteDirections(XmlWriter xml, Bar bar, PartMeasure part)
    {
        string top = Number(part.FirstStaff);
        string Beat(long at) => MeiWriter.Beat(bar, at);
        if (part.TempoWord is { } word)
        {
            xml.WriteStartElement("dir", MeiNamespace);
            xml.WriteAttributeString("staff", top);
            xml.WriteAttributeString("place", "above");
            xml.WriteAttributeString("tstamp", Beat(Math.Max(bar.Origin, bar.Span.Onset.Value)));
            xml.WriteStartElement("rend", MeiNamespace);
            xml.WriteAttributeString("fontstyle", "italic");
            xml.WriteString(word);
            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        if (part.Dynamic is { } dynamic)
        {
            xml.WriteStartElement("dynam", MeiNamespace);
            xml.WriteAttributeString("staff", top);
            xml.WriteAttributeString("place", "below");
            xml.WriteAttributeString("tstamp", Beat(dynamic.At));
            xml.WriteString(dynamic.Level);
            xml.WriteEndElement();
        }

        foreach (HairpinPlan hairpin in part.Controls.Hairpins.Where(hairpin => hairpin.Tstamp2 is null))
        {
            xml.WriteStartElement("dir", MeiNamespace);
            xml.WriteAttributeString("staff", top);
            xml.WriteAttributeString("place", "below");
            xml.WriteAttributeString("tstamp", Beat(hairpin.At));
            xml.WriteStartElement("rend", MeiNamespace);
            xml.WriteAttributeString("fontstyle", "italic");
            xml.WriteString(hairpin.Crescendo ? "cresc." : "dim.");
            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        foreach (HairpinPlan hairpin in part.Controls.Hairpins.Where(hairpin => hairpin.Tstamp2 is not null))
        {
            xml.WriteStartElement("hairpin", MeiNamespace);
            xml.WriteAttributeString("form", hairpin.Crescendo ? "cres" : "dim");
            xml.WriteAttributeString("staff", top);
            xml.WriteAttributeString("place", "below");
            xml.WriteAttributeString("tstamp", Beat(hairpin.At));
            xml.WriteAttributeString("tstamp2", hairpin.Tstamp2!);
            xml.WriteEndElement();
        }

        foreach ((Chord from, Chord to, int staff) in part.Controls.Slurs)
        {
            xml.WriteStartElement("slur", MeiNamespace);
            xml.WriteAttributeString("staff", Number(staff));
            xml.WriteAttributeString("startid", "#" + ChordId(from));
            xml.WriteAttributeString("endid", "#" + ChordId(to));
            xml.WriteEndElement();
        }

        foreach (long held in part.Controls.Fermatas)
        {
            xml.WriteStartElement("fermata", MeiNamespace);
            xml.WriteAttributeString("staff", top);
            xml.WriteAttributeString("place", "above");
            xml.WriteAttributeString("tstamp", Beat(held));
            xml.WriteEndElement();
        }

        foreach ((long at, bool down, int staff) in part.Controls.Pedals)
        {
            xml.WriteStartElement("pedal", MeiNamespace);
            xml.WriteAttributeString("staff", Number(staff));
            xml.WriteAttributeString("place", "below");
            xml.WriteAttributeString("tstamp", Beat(at));
            xml.WriteAttributeString("dir", down ? "down" : "up");
            xml.WriteEndElement();
        }
    }

    /// <summary>The id of a chord's first piece, as <see cref="WriteElement"/> writes it.</summary>
    private static string ChordId(Chord chord) =>
        XmlConvert.EncodeLocalName(chord.Notes.Length == 1 ? chord.Notes[0].Note.Id : "c_" + chord.Notes[0].Note.Id)!;

    private static void WriteAccent(XmlWriter xml, ChordPiece piece)
    {
        if (piece.Chord.IsAccented && piece.Index == 0 && !piece.IsGrace)
        {
            xml.WriteStartElement("artic", MeiNamespace);
            xml.WriteAttributeString("artic", "acc");
            xml.WriteEndElement();
        }
    }

    private static void WriteElement(XmlWriter xml, Element element)
    {
        switch (element)
        {
            case MeasureRest:
                xml.WriteStartElement("mRest", MeiNamespace);
                xml.WriteEndElement();
                break;

            case Rest rest:
                xml.WriteStartElement(rest.IsSpace ? "space" : "rest", MeiNamespace);
                WriteDuration(xml, rest);
                xml.WriteEndElement();
                break;

            case TupletGroup tuplet:
                xml.WriteStartElement("tuplet", MeiNamespace);
                xml.WriteAttributeString("num", Number(tuplet.Span.Num));
                xml.WriteAttributeString("numbase", Number(tuplet.Span.NumBase));
                xml.WriteAttributeString("num.format", "count");
                foreach (Element inner in tuplet.Elements)
                {
                    WriteElement(xml, inner);
                }

                xml.WriteEndElement();
                break;

            case Beamed beam:
                xml.WriteStartElement("beam", MeiNamespace);
                foreach (Element inner in beam.Elements)
                {
                    WriteElement(xml, inner);
                }

                xml.WriteEndElement();
                break;

            case ChordPiece piece when piece.Chord.Notes.Length == 1:
                xml.WriteStartElement("note", MeiNamespace);
                WriteId(xml, piece.Chord.Notes[0].Note.Id, piece.Index);
                if (piece.IsGrace)
                {
                    xml.WriteAttributeString("grace", "acc");
                }

                WriteDuration(xml, piece);
                WriteStem(xml, piece);
                WritePitch(xml, piece, 0);
                WriteAccent(xml, piece);
                xml.WriteEndElement();
                break;

            case ChordPiece piece:
                xml.WriteStartElement("chord", MeiNamespace);
                WriteId(xml, "c_" + piece.Chord.Notes[0].Note.Id, piece.Index);
                WriteDuration(xml, piece);
                WriteStem(xml, piece);
                WriteAccent(xml, piece);
                for (int i = 0; i < piece.Chord.Notes.Length; i++)
                {
                    xml.WriteStartElement("note", MeiNamespace);
                    WriteId(xml, piece.Chord.Notes[i].Note.Id, piece.Index);
                    WritePitch(xml, piece, i);
                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
                break;

            default:
                throw new InvalidOperationException($"Unknown element {element.GetType().Name}.");
        }
    }

    private static void WriteId(XmlWriter xml, string id, int piece)
    {
        string name = XmlConvert.EncodeLocalName(id)!;
        xml.WriteAttributeString("xml", "id", null, piece == 0 ? name : $"{name}-{Number(piece + 1)}");
    }

    private static void WriteDuration(XmlWriter xml, Element element)
    {
        xml.WriteAttributeString("dur", Number(element.Dur));
        if (element.Dots > 0)
        {
            xml.WriteAttributeString("dots", Number(element.Dots));
        }
    }

    /// <summary>The stem as the score writes it, when it says (<see cref="ChordPiece.StemUp"/>); Verovio places the others.</summary>
    private static void WriteStem(XmlWriter xml, ChordPiece piece)
    {
        if (piece.StemUp is { } up)
        {
            xml.WriteAttributeString("stem.dir", up ? "up" : "down");
        }
    }

    private static void WritePitch(XmlWriter xml, ChordPiece piece, int index)
    {
        SpelledPitch spelled = piece.Spellings[index];
        xml.WriteAttributeString("pname", s_stepNames[spelled.Step]);
        xml.WriteAttributeString("oct", Number(spelled.Octave));
        if (piece.ShowAccidental[index])
        {
            xml.WriteAttributeString("accid", Accidental(spelled.Alter));
        }
        else if (spelled.Alter != 0)
        {
            xml.WriteAttributeString("accid.ges", Accidental(spelled.Alter));
        }

        if (piece.TieIn || piece.TieOut)
        {
            xml.WriteAttributeString("tie", (piece.TieIn, piece.TieOut) switch
            {
                (false, true) => "i",
                (true, true) => "m",
                _ => "t",
            });
        }
    }

    private static string Accidental(int alter) => alter switch
    {
        -2 => "ff",
        -1 => "f",
        0 => "n",
        1 => "s",
        2 => "x",
        _ => throw new InvalidOperationException($"No accidental for an alteration of {alter}."),
    };

    /// <summary>A number as MEI writes it.</summary>
    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A position in a bar as an MEI time stamp: 1 on the bar's first beat, counted in the bar's beats.</summary>
    public static string Beat(Bar bar, long at)
    {
        return (1 + ((decimal)(at - bar.Origin) * bar.Signature.Denominator / (4 * CanonicalTime.TicksPerQuarter))).ToString("0.###", CultureInfo.InvariantCulture);
    }
}