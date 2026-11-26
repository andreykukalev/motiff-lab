using System.Xml.Linq;

using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>
/// How the part writes its notes and rests («Как напечатано», step 1): the written value, dots, tuplet, stem, beam and
/// tuplet bracket of each, and the time a voice skips with a <c>&lt;forward&gt;</c>.
/// </summary>
internal sealed partial class PartReader
{
    /// <summary>A <c>&lt;forward&gt;</c> that names its voice, as a space in that voice; one without a voice belongs to none.</summary>
    private void ReadSpace(XElement forward, QuarterFraction duration)
    {
        if (forward.Element("voice") is not null && duration > QuarterFraction.Zero)
        {
            (int staff, string voice) = StaffAndVoice(forward);
            Spaces.Add(new PartSpace(_measureIndex, _cursor, duration, staff, voice));
        }
    }

    /// <summary>How a note or rest is written: its type, dots and tuplet, the stem, the first beam and the tuplet bracket.</summary>
    private static MusicXmlWriting ReadWriting(XElement note)
    {
        IEnumerable<string?> brackets = note.Elements("notations").Elements("tuplet").Select(tuplet => tuplet.Attribute("type")?.Value.Trim());
        return new MusicXmlWriting(XmlValues.Trimmed(note.Element("type")?.Value), note.Elements("dot").Count(), ReadTuplet(note))
        {
            StemUp = XmlValues.Trimmed(note.Element("stem")?.Value) switch
            {
                "up" => true,
                "down" => false,
                _ => null,
            },
            Beam = ReadBeam(note),
            TupletStart = brackets.Contains("start"),
            TupletStop = brackets.Contains("stop"),
            IsHidden = string.Equals(note.Attribute("print-object")?.Value.Trim(), "no", StringComparison.Ordinal),
        };
    }

    /// <summary>The note's first beam (<c>number="1"</c>, or a beam without a number); the others only add 16ths and shorter.</summary>
    private static MusicXmlBeam ReadBeam(XElement note)
    {
        XElement? beam = note.Elements("beam").FirstOrDefault(beam => (XmlValues.Trimmed(beam.Attribute("number")?.Value) ?? "1") == "1");
        return XmlValues.Trimmed(beam?.Value) switch
        {
            "begin" => MusicXmlBeam.Begin,
            "continue" => MusicXmlBeam.Continue,
            "end" => MusicXmlBeam.End,
            _ => MusicXmlBeam.None,
        };
    }

    private static MusicXmlTuplet? ReadTuplet(XElement note)
    {
        XElement? modification = note.Element("time-modification");
        return modification is not null
            && XmlValues.TryParseInt(modification.Element("actual-notes")?.Value, out int actual) && actual >= 1
            && XmlValues.TryParseInt(modification.Element("normal-notes")?.Value, out int normal) && normal >= 1
            && actual != normal
            ? new MusicXmlTuplet(actual, normal)
            : null;
    }
}