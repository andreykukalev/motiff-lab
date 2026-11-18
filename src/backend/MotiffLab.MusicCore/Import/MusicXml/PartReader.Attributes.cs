using System.Globalization;
using System.Xml.Linq;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>A part's attributes and directions: divisions, staves, transposition, key, time, tempo and dynamics.</summary>
internal sealed partial class PartReader
{
    /// <summary>MusicXML dynamics are a percentage of forte, which is velocity 90.</summary>
    private const int ForteVelocity = 90;

    private void ReadAttributes(XElement attributes)
    {
        if (attributes.Element("divisions") is XElement divisions)
        {
            if (long.TryParse(divisions.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long value) && value >= 1)
            {
                _divisions = new MusicXmlDivisions(value);
            }
            else if (XmlValues.TryParseDecimal(divisions.Value, out decimal fractional) && fractional > 0)
            {
                throw FractionalTiming();
            }
            else
            {
                issues.Add(ImportIssueKind.Repaired, MusicXmlIssueCodes.DivisionsInvalid, index);
            }
        }

        if (attributes.Element("staves") is XElement staves && XmlValues.TryParseInt(staves.Value, out int count) && count >= 1)
        {
            Staves = Math.Max(Staves, count);
        }

        if (attributes.Element("transpose") is XElement transpose)
        {
            XmlValues.TryParseInt(transpose.Element("chromatic")?.Value, out int chromatic);
            XmlValues.TryParseInt(transpose.Element("octave-change")?.Value, out int octaves);
            _transpose = chromatic + (12 * octaves);
        }

        if (attributes.Element("key") is XElement key)
        {
            ReadKey(key);
        }

        if (attributes.Element("time") is XElement time)
        {
            ReadTime(time);
        }
    }

    private void ReadKey(XElement key)
    {
        XElement? fifths = key.Element("fifths");
        if (fifths is null)
        {
            issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaUnsupported, index);
            return;
        }

        if (!XmlValues.TryParseInt(fifths.Value, out int value) || value is < -7 or > 7)
        {
            issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaInvalid, index);
            return;
        }

        Keys.Add(new Timed<(int, string?)>(index, _order++, _measureIndex, _cursor, (value, XmlValues.Trimmed(key.Element("mode")?.Value))));
    }

    private void ReadTime(XElement time)
    {
        var beats = time.Elements("beats").ToList();
        var beatTypes = time.Elements("beat-type").ToList();
        if (time.Element("senza-misura") is not null)
        {
            issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaUnsupported, index);
            return;
        }

        if (beats.Count == 0 || beats.Count != beatTypes.Count)
        {
            issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaInvalid, index);
            return;
        }

        var denominators = new List<int>();
        foreach (XElement beatType in beatTypes)
        {
            if (!XmlValues.TryParseInt(beatType.Value, out int denominator))
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaInvalid, index);
                return;
            }

            denominators.Add(denominator);
        }

        if (denominators.Distinct().Count() > 1)
        {
            issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaUnsupported, index);
            return;
        }

        // Additive numerators (3+2/8) and repeated pairs with one beat type (3/8 + 2/8) are given as their sum.
        long numerator = 0;
        foreach (string part in beats.SelectMany(beat => beat.Value.Split('+')))
        {
            if (!XmlValues.TryParseInt(part, out int value) || value < 1)
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaInvalid, index);
                return;
            }

            numerator += value;
        }

        int den = denominators[0];
        if (numerator > TimeSignature.MaxNumerator || den < 1 || den > TimeSignature.MaxDenominator || !int.IsPow2(den))
        {
            issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaInvalid, index);
            return;
        }

        Times.Add(new Timed<TimeSignature>(index, _order++, _measureIndex, _cursor, new TimeSignature((int)numerator, den)));
    }

    private void ReadDirection(XElement direction)
    {
        foreach (XElement type in direction.Elements("direction-type"))
        {
            foreach (XElement text in type.Elements().Where(static element => element.Name.LocalName is "words" or "rehearsal"))
            {
                if (XmlValues.Trimmed(text.Value) is { } written)
                {
                    Texts.Add(new PartText(_measureIndex, _cursor, written, text.Name.LocalName == "rehearsal", text.Attribute("font-weight")?.Value.Trim() == "bold"));
                }
            }
        }

        bool hasSoundTempo = false;
        foreach (XElement sound in direction.Elements("sound"))
        {
            hasSoundTempo |= ReadSound(sound);
        }

        // A metronome mark stands for the tempo only when no <sound tempo> says otherwise.
        if (!hasSoundTempo && direction.Elements("direction-type").Elements("metronome").FirstOrDefault() is XElement metronome)
        {
            ReadMetronome(metronome);
        }
    }

    /// <summary>Reads playback tempo and dynamics; true when the sound sets a tempo.</summary>
    private bool ReadSound(XElement sound)
    {
        if (ToVelocity(sound.Attribute("dynamics")?.Value) is int velocity)
        {
            _velocity = velocity;
        }

        if (sound.Attribute("tempo") is not XAttribute tempo)
        {
            return false;
        }

        if (!XmlValues.TryParseDecimal(tempo.Value, out decimal bpm) || bpm <= 0)
        {
            issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaInvalid, index);
            return false;
        }

        Tempos.Add(new Timed<decimal>(index, _order++, _measureIndex, _cursor, bpm));
        return true;
    }

    private void ReadMetronome(XElement metronome)
    {
        var units = metronome.Elements("beat-unit").ToList();
        string? perMinute = XmlValues.Trimmed(metronome.Element("per-minute")?.Value);
        if (units.Count != 1 || perMinute is null)
        {
            // Metric modulations (one beat unit equals another) and text marks set no number.
            return;
        }

        // "c. 120" or "120-132": the first number is the tempo.
        int start = perMinute.AsSpan().IndexOfAnyInRange('0', '9');
        if (start < 0)
        {
            return;
        }

        int length = perMinute.AsSpan(start).IndexOfAnyExcept("0123456789.");
        string digits = length < 0 ? perMinute[start..] : perMinute.Substring(start, length);
        decimal? unit = BeatUnitQuarters(XmlValues.Trimmed(units[0].Value), metronome.Elements("beat-unit-dot").Count());
        if (!XmlValues.TryParseDecimal(digits, out decimal value) || value <= 0 || unit is null)
        {
            issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaInvalid, index);
            return;
        }

        Tempos.Add(new Timed<decimal>(index, _order++, _measureIndex, _cursor, value * unit.Value));
    }

    private static decimal? BeatUnitQuarters(string? unit, int dots)
    {
        decimal? quarters = unit switch
        {
            "long" => 16,
            "breve" => 8,
            "whole" => 4,
            "half" => 2,
            "quarter" => 1,
            "eighth" => 0.5m,
            "16th" => 0.25m,
            "32nd" => 0.125m,
            "64th" => 0.0625m,
            _ => null,
        };

        // Each dot adds half of the previous value.
        decimal added = quarters ?? 0;
        for (int i = 0; i < dots && quarters is not null; i++)
        {
            added /= 2;
            quarters += added;
        }

        return quarters;
    }

    private static int? ToVelocity(string? dynamics)
    {
        if (!XmlValues.TryParseDecimal(dynamics, out decimal percent) || percent < 0)
        {
            return null;
        }

        decimal velocity = Math.Round(percent * ForteVelocity / 100, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(velocity, Note.MinVelocity, Note.MaxVelocity);
    }
}