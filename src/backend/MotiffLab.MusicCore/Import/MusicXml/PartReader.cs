using System.Globalization;
using System.Xml.Linq;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>Walks one part: keeps a position cursor per measure and collects notes, attributes and directions.</summary>
internal sealed partial class PartReader(int index, string id, string? name, string? instrumentName, ImportIssueCollector issues)
{
    private MusicXmlDivisions? _divisions;
    private int _transpose;
    private int? _velocity;
    private int _order;

    private int _measureIndex;
    private string? _measureNumber;
    private int _noteIndex;
    private QuarterFraction _cursor;
    private QuarterFraction _measureLength;
    private QuarterFraction _lastOnset;

    public int Index => index;

    public string Id => id;

    public string? Name => name;

    public string? InstrumentName => instrumentName;

    public int Staves { get; private set; } = 1;

    public List<MeasureInfo> Measures { get; } = [];

    public List<PartNote> Notes { get; } = [];

    public List<PartGraceNote> GraceNotes { get; } = [];

    public List<PartRest> Rests { get; } = [];

    public List<PartSpace> Spaces { get; } = [];

    public List<PartSlurMark> Slurs { get; } = [];

    public List<PartRepeat> Repeats { get; } = [];

    public List<PartBarline> Barlines { get; } = [];

    public List<PartText> Texts { get; } = [];

    public List<Timed<decimal>> Tempos { get; } = [];

    public List<Timed<TimeSignature>> Times { get; } = [];

    public List<Timed<(int Fifths, string? Mode)>> Keys { get; } = [];

    private MusicXmlDivisions Divisions
    {
        get
        {
            if (_divisions is null)
            {
                issues.Add(ImportIssueKind.Assumed, MusicXmlIssueCodes.DivisionsDefault, index);
                _divisions = new MusicXmlDivisions(1);
            }

            return _divisions;
        }
    }

    public void Read(XElement part)
    {
        foreach (XElement measure in part.Elements("measure"))
        {
            _measureIndex = Measures.Count;
            _measureNumber = XmlValues.Trimmed(measure.Attribute("number")?.Value);
            _noteIndex = 0;
            _cursor = QuarterFraction.Zero;
            _measureLength = QuarterFraction.Zero;
            _lastOnset = QuarterFraction.Zero;

            foreach (XElement element in measure.Elements())
            {
                switch (element.Name.LocalName)
                {
                    case "attributes":
                        ReadAttributes(element);
                        break;
                    case "note":
                        ReadNote(element);
                        _noteIndex++;
                        break;
                    case "backup":
                        if (ReadDuration(element) is QuarterFraction back)
                        {
                            if (back > _cursor)
                            {
                                issues.Add(ImportIssueKind.Repaired, MusicXmlIssueCodes.BackupBeforeMeasure, index);
                                _cursor = QuarterFraction.Zero;
                            }
                            else
                            {
                                _cursor -= back;
                            }
                        }

                        break;
                    case "forward":
                        if (ReadDuration(element) is QuarterFraction forward)
                        {
                            ReadSpace(element, forward);
                            Advance(forward);
                        }

                        break;
                    case "direction":
                        ReadDirection(element);
                        break;
                    case "sound":
                        ReadSound(element);
                        break;
                    case "barline":
                        ReadBarline(element);
                        break;
                }
            }

            bool isImplicit = string.Equals(measure.Attribute("implicit")?.Value.Trim(), "yes", StringComparison.Ordinal);
            bool enginesDisagree = measure.Elements("direction").Elements("direction-type").Elements("other-direction")
                .Any(other => other.Value.Trim() == MusicXmlImport.EnginesDisagreeMark);
            Measures.Add(new MeasureInfo(_measureNumber, isImplicit, _measureLength, enginesDisagree));
        }
    }

    private void Advance(QuarterFraction duration)
    {
        _cursor += duration;
        if (_cursor > _measureLength)
        {
            _measureLength = _cursor;
        }
    }

    private void ReadNote(XElement note)
    {
        if (note.Element("grace") is XElement grace)
        {
            ReadGraceNote(note, grace);
            return;
        }

        if (ReadDuration(note) is not QuarterFraction duration)
        {
            return;
        }

        bool isChord = note.Element("chord") is not null;
        QuarterFraction onset = isChord ? _lastOnset : _cursor;
        if (!isChord)
        {
            _lastOnset = _cursor;
            Advance(duration);
        }

        if (note.Element("cue") is not null)
        {
            issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.CueNotesIgnored, index);
            return;
        }

        (int staff, string voice) = StaffAndVoice(note);
        if (note.Element("rest") is XElement rest)
        {
            bool isMeasureRest = string.Equals(rest.Attribute("measure")?.Value.Trim(), "yes", StringComparison.Ordinal);
            Rests.Add(new PartRest(_measureIndex, _noteIndex, onset, duration, staff, voice, isMeasureRest, ReadWriting(note)));
            Staves = Math.Max(Staves, staff);
            return;
        }

        if (note.Element("unpitched") is not null)
        {
            issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.UnpitchedIgnored, index);
            return;
        }

        if (ReadPitch(note.Element("pitch")) is not int pitch)
        {
            issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.PitchInvalid, index);
            return;
        }

        Staves = Math.Max(Staves, staff);
        int? velocity = ToVelocity(note.Attribute("dynamics")?.Value) ?? _velocity;
        (bool tieStart, bool tieStop) = ReadTies(note);

        Notes.Add(new PartNote(_measureIndex, _noteIndex, onset, duration, pitch, velocity, staff, voice, tieStart, tieStop, ReadAccent(note), ReadWriting(note)));
        ReadSlurs(note, onset);
    }

    /// <summary>The staff a note is written on, from 1, and its voice: staff 1 and voice "1" when the file says none.</summary>
    private static (int Staff, string Voice) StaffAndVoice(XElement note) =>
        (XmlValues.TryParseInt(note.Element("staff")?.Value, out int staff) && staff >= 1 ? staff : 1, XmlValues.Trimmed(note.Element("voice")?.Value) ?? "1");

    /// <summary>The accent written on a note (S1-A1); a strong accent wins when both are written.</summary>
    private static AccentKind? ReadAccent(XElement note)
    {
        IEnumerable<XElement> articulations = note.Elements("notations").Elements("articulations").Elements();
        return articulations.Any(mark => mark.Name.LocalName == "strong-accent") ? AccentKind.StrongAccent
            : articulations.Any(mark => mark.Name.LocalName == "accent") ? AccentKind.Accent
            : null;
    }

    /// <summary>Slur starts and stops on a note; a <c>continue</c> neither opens nor closes a slur.</summary>
    private void ReadSlurs(XElement note, QuarterFraction onset)
    {
        foreach (XElement slur in note.Elements("notations").Elements("slur"))
        {
            string? type = slur.Attribute("type")?.Value.Trim();
            if (type is not ("start" or "stop"))
            {
                continue;
            }

            int number = XmlValues.TryParseInt(slur.Attribute("number")?.Value, out int n) ? n : 1;
            Slurs.Add(new PartSlurMark(_measureIndex, onset, number, type == "start"));
        }
    }

    private void ReadBarline(XElement barline)
    {
        string location = barline.Attribute("location")?.Value.Trim() ?? "right";
        if (location == "middle")
        {
            return;
        }

        XElement? repeat = barline.Element("repeat");
        if (XmlValues.Trimmed(barline.Element("bar-style")?.Value) is { } style && style != "regular")
        {
            Barlines.Add(new PartBarline(_measureIndex, location == "right", style, repeat is not null));
        }

        if (repeat?.Attribute("direction")?.Value.Trim() is not string direction)
        {
            return;
        }

        MusicXmlRepeatDirection? parsed = direction switch
        {
            "forward" => MusicXmlRepeatDirection.Forward,
            "backward" => MusicXmlRepeatDirection.Backward,
            _ => null,
        };
        if (parsed is { } value)
        {
            Repeats.Add(new PartRepeat(_measureIndex, location == "right", value));
        }
    }

    /// <summary>A grace note takes no time: it is listed at the cursor, before the note it ornaments.</summary>
    private void ReadGraceNote(XElement note, XElement grace)
    {
        if (note.Element("cue") is not null)
        {
            issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.CueNotesIgnored, index);
            return;
        }

        if (note.Element("rest") is not null || note.Element("unpitched") is not null)
        {
            return;
        }

        if (ReadPitch(note.Element("pitch")) is not int pitch)
        {
            issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.PitchInvalid, index);
            return;
        }

        (int staff, string voice) = StaffAndVoice(note);
        Staves = Math.Max(Staves, staff);
        bool isSlashed = string.Equals(grace.Attribute("slash")?.Value.Trim(), "yes", StringComparison.Ordinal);
        GraceNotes.Add(new PartGraceNote(_measureIndex, _noteIndex, _cursor, pitch, staff, voice, isSlashed));
        ReadSlurs(note, _cursor);
        issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.GraceNotesFlagged, index);
    }

    /// <summary>
    /// Whether a tie starts or stops at the note: the sounding <c>&lt;tie&gt;</c>, or the notated
    /// <c>&lt;tied&gt;</c> when the file has only that.
    /// </summary>
    private static (bool Start, bool Stop) ReadTies(XElement note)
    {
        var types = note.Elements("tie").Select(tie => tie.Attribute("type")?.Value.Trim()).ToList();
        if (types.Count == 0)
        {
            types = note.Elements("notations").Elements("tied").Select(tied => tied.Attribute("type")?.Value.Trim()).ToList();
        }

        bool isContinue = types.Contains("continue");
        return (types.Contains("start") || isContinue, types.Contains("stop") || isContinue);
    }


    private int? ReadPitch(XElement? pitch)
    {
        string? step = XmlValues.Trimmed(pitch?.Element("step")?.Value);
        if (pitch is null || step is not { Length: 1 } || step[0] is < 'A' or > 'G' || !XmlValues.TryParseInt(pitch.Element("octave")?.Value, out int octave))
        {
            return null;
        }

        int alter = 0;
        if (pitch.Element("alter") is XElement alterElement)
        {
            if (!XmlValues.TryParseDecimal(alterElement.Value, out decimal exact))
            {
                return null;
            }

            alter = (int)Math.Round(exact, MidpointRounding.AwayFromZero);
            if (alter != exact)
            {
                issues.Add(ImportIssueKind.Assumed, MusicXmlIssueCodes.MicrotoneRounded, index);
            }
        }

        long midi = StaffPitch.Midi(step[0], octave, alter) + _transpose;
        return midi is >= 0 and <= Pitch.MaxMidiNumber ? (int)midi : null;
    }

    private QuarterFraction? ReadDuration(XElement element)
    {
        string? text = XmlValues.Trimmed(element.Element("duration")?.Value);
        if (text is null)
        {
            issues.Add(ImportIssueKind.Repaired, MusicXmlIssueCodes.NoteWithoutDuration, index);
            return null;
        }

        if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long duration))
        {
            return Divisions.ToQuarters(duration);
        }

        if (XmlValues.TryParseDecimal(text, out decimal fractional) && fractional >= 0)
        {
            throw FractionalTiming();
        }

        string where = _measureNumber is null ? $"measure {_measureIndex + 1}" : $"measure {_measureNumber}";
        throw new MusicXmlImportException(
            MusicXmlImportFailure.Damaged,
            $"This MusicXML file is damaged: a duration in {where} of {name ?? id} is not a number, so the notes after it cannot be placed.");
    }

    private static MusicXmlImportException FractionalTiming() => new(
        MusicXmlImportFailure.Unsupported,
        "This MusicXML file uses fractional durations, which Motiff Lab cannot read yet. Exporting it again from a notation app usually fixes this.");
}