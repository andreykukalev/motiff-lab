using System.Globalization;
using System.Text;
using System.Xml;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// Writes notes from the shared model as notation (S1-W7, decision record 0008): how MIDI imports,
/// and later motifs and variations, are shown in the score view, whatever the input format.
/// </summary>
/// <remarks>
/// <para>
/// The steps, all deterministic: snap onsets and ends to a 32nd-note grid (counting every note that
/// moves); put each track on a staff, or on two for a piano part (by imported hands, or split at
/// middle C for a single track without hands); group notes that start and end together into chords;
/// give overlapping chords their own voice, up to four per staff, shortening a note only when all
/// four are busy; cut chords at bar lines and tie the pieces; write each piece as the longest note
/// value that fits and starts on a multiple of its own length or of the beat (the dotted quarter in
/// compound meters), and does not cross the middle of a 4/4 or 12/8 bar unless it starts the bar; beam eighths and shorter within the beat; spell pitches from the key signature
/// and write accidentals per bar and staff.
/// </para>
/// <para>
/// Tuplets, grace notes, pedal and dynamics are not written: triplets land on the nearest 32nd and
/// are counted as approximated. The notes themselves are never changed; only how they are written.
/// </para>
/// </remarks>
public static class NotationGenerator
{
    /// <summary>The display grid: a 32nd note.</summary>
    public const long Grid = CanonicalTime.TicksPerQuarter / 8;

    public const int MaxVoicesPerStaff = 4;

    private const int MiddleC = 60;

    private const string MeiNamespace = "http://www.music-encoding.org/ns/mei";

    // Note values from longest to shortest: ticks, MEI @dur, dots. The last three are only for
    // bars whose beats are shorter than the grid (x/64, x/128), so every bar can be filled.
    private static readonly (long Ticks, int Dur, int Dots)[] s_values =
    [
        (1920, 1, 0), (1440, 2, 1), (960, 2, 0), (720, 4, 1), (480, 4, 0), (360, 8, 1), (240, 8, 0),
        (180, 16, 1), (120, 16, 0), (60, 32, 0), (30, 64, 0), (15, 128, 0),
    ];

    private static readonly string[] s_stepNames = ["c", "d", "e", "f", "g", "a", "b"];

    /// <summary>The longest gap before the next onset that <c>holdShortGaps</c> closes: an eighth note.</summary>
    public const long HeldGap = CanonicalTime.TicksPerQuarter / 2;

    /// <summary>Notation for a MIDI import, with its key signatures.</summary>
    /// <param name="holdShortGaps">See <see cref="Generate"/>.</param>
    public static GeneratedNotation FromMidi(MidiImport import, string? title = null, bool holdShortGaps = false)
    {
        ArgumentNullException.ThrowIfNull(import);
        return Generate(import.Notes, import.Meter, import.KeySignatures.Select(key => new KeyChange(key.At, Math.Clamp(key.Sharps, -7, 7))), import.End, title, holdShortGaps);
    }

    /// <summary>Notation for a MusicXML import, generated from its notes rather than engraved as written. Key signatures come from the first part.</summary>
    public static GeneratedNotation FromMusicXml(MusicXmlImport import, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(import);
        int firstTrack = import.KeySignatures.Count == 0 ? 0 : import.KeySignatures.Min(key => key.Track);
        IEnumerable<KeyChange> keys = import.KeySignatures
            .Where(key => key.Track == firstTrack)
            .Select(key => new KeyChange(key.At, Math.Clamp(key.Fifths, -7, 7)));
        return Generate(import.Notes, import.Meter, keys, import.End, title);
    }

    /// <param name="notes">The notes to write, in any order.</param>
    /// <param name="meter">The bars to write them in.</param>
    /// <param name="keys">Key signatures; C major until the first. A change takes effect at the next bar line.</param>
    /// <param name="end">The end of the piece; the last bar is the one that contains the end, or the last note's end if later.</param>
    /// <param name="title">The title written in the MEI header.</param>
    /// <param name="holdShortGaps">
    /// For notes heard in a recording, where a key is let go a little before the next is played: a
    /// note that ends less than <see cref="HeldGap"/> before the next onset on its staff is written up
    /// to that onset, as legato rather than a short note and a rest. The notes themselves keep their lengths.
    /// </param>
    public static GeneratedNotation Generate(IEnumerable<Note> notes, MeterMap meter, IEnumerable<KeyChange> keys, Tick end, string? title = null, bool holdShortGaps = false)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(keys);

        Note[] input = [.. notes];
        KeyChange[] keyChanges = [.. keys.OrderBy(key => key.At)];

        var shown = input.Select(note => new ShownNote(note, Snap(note.Span.Onset.Value), Snap(note.Span.End.Value))).ToArray();
        int approximated = shown.Count(note => note.Onset != note.Note.Span.Onset.Value || note.End != note.Note.Span.End.Value);

        long last = Math.Max(1, Math.Max(end.Value, shown.Length == 0 ? 0 : shown.Max(note => note.End)));
        Bar[] bars = [.. meter.BarsBefore(new Tick(last))];

        List<StaffPlan> staves = PlanStaves(shown);
        var lengthened = new HashSet<string>();
        if (holdShortGaps)
        {
            staves = [.. staves.Select(staff => staff with { Notes = HeldToNextOnset(staff.Notes, lengthened) })];
        }

        var shortened = new HashSet<string>();
        foreach (StaffPlan staff in staves)
        {
            staff.Voices = AssignVoices(staff.Notes, shortened);
        }

        var measures = new List<MeasurePlan>(bars.Length);
        foreach (Bar bar in bars)
        {
            int fifths = KeyAt(keyChanges, bar.Span.Onset);
            measures.Add(new MeasurePlan(bar, fifths, staves.Select(staff => LayOutStaff(staff, bar, fifths)).ToArray()));
        }

        string mei = WriteMei(title ?? "Generated notation", staves, measures);
        NotationStaff[] staffInfo = [.. staves.Select(staff => new NotationStaff(staff.Number, staff.Track, staff.Clef, staff.Hand, staff.IsSplitInferred))];
        return new GeneratedNotation(mei, staffInfo, input.Length, approximated, shortened.Count, lengthened.Count);
    }

    private static long Snap(long tick) => (tick + (Grid / 2)) / Grid * Grid;

    /// <summary>Each note that ends less than <see cref="HeldGap"/> before the staff's next onset, written up to it.</summary>
    private static ShownNote[] HeldToNextOnset(ShownNote[] notes, HashSet<string> lengthened)
    {
        long[] onsets = [.. notes.Select(note => note.Onset).Distinct().Order()];
        return [.. notes.Select(note =>
        {
            int index = Array.BinarySearch(onsets, note.Onset);
            if (index + 1 >= onsets.Length)
            {
                return note;
            }

            long next = onsets[index + 1];
            if (note.End >= next || next - note.End >= HeldGap)
            {
                return note;
            }

            lengthened.Add(note.Note.Id);
            return new ShownNote(note.Note, note.Onset, next);
        })];
    }

    private static int KeyAt(KeyChange[] keys, Tick position)
    {
        int fifths = 0;
        foreach (KeyChange key in keys)
        {
            if (key.At > position)
            {
                break;
            }

            fifths = key.Fifths;
        }

        return fifths;
    }

    private static List<StaffPlan> PlanStaves(ShownNote[] notes)
    {
        int[] tracks = [.. notes.Select(note => note.Note.Track).Distinct().Order()];
        var staves = new List<StaffPlan>();
        if (tracks.Length == 0)
        {
            staves.Add(new StaffPlan(1, 0, Clef.Treble, null, false, []));
            return staves;
        }

        foreach (int track in tracks)
        {
            ShownNote[] trackNotes = [.. notes.Where(note => note.Note.Track == track)];
            bool hasHands = trackNotes.Any(note => note.Note.Hand is not null);
            bool spansBothClefs = trackNotes.Any(note => note.Note.Pitch.MidiNumber < MiddleC - 5)
                && trackNotes.Any(note => note.Note.Pitch.MidiNumber > MiddleC + 5);

            if (hasHands || (tracks.Length == 1 && spansBothClefs))
            {
                static bool IsRight(ShownNote note) => note.Note.Hand?.Value is { } hand ? hand == Hand.Right : note.Note.Pitch.MidiNumber >= MiddleC;
                staves.Add(new StaffPlan(staves.Count + 1, track, Clef.Treble, Hand.Right, !hasHands, [.. trackNotes.Where(IsRight)]));
                staves.Add(new StaffPlan(staves.Count + 1, track, Clef.Bass, Hand.Left, !hasHands, [.. trackNotes.Where(note => !IsRight(note))]));
            }
            else
            {
                int high = trackNotes.Count(note => note.Note.Pitch.MidiNumber >= MiddleC);
                Clef clef = high >= trackNotes.Length - high ? Clef.Treble : Clef.Bass;
                staves.Add(new StaffPlan(staves.Count + 1, track, clef, null, false, trackNotes));
            }
        }

        return staves;
    }

    /// <summary>Chords in voices that never overlap, the highest voice first.</summary>
    private static List<List<Chord>> AssignVoices(ShownNote[] notes, HashSet<string> shortened)
    {
        Chord[] chords = [.. notes
            .GroupBy(note => (note.Onset, note.End))
            .Select(group => new Chord(group.Key.Onset, group.Key.End, [.. group.OrderBy(note => note.Note.Pitch.MidiNumber).ThenBy(note => note.Note.Id, StringComparer.Ordinal)]))
            .OrderBy(chord => chord.Onset)
            .ThenByDescending(chord => chord.Notes[^1].Note.Pitch.MidiNumber)
            .ThenByDescending(chord => chord.End)];

        var voices = new List<List<Chord>>();
        foreach (Chord chord in chords)
        {
            List<Chord>? voice = voices.FirstOrDefault(candidate => candidate[^1].End <= chord.Onset);
            if (voice is null && voices.Count < MaxVoicesPerStaff)
            {
                voice = [];
                voices.Add(voice);
            }
            else if (voice is null)
            {
                // Every voice is busy: shorten the chord that ends first, or join it if it starts here too.
                voice = voices.MinBy(candidate => candidate[^1].End)!;
                Chord previous = voice[^1];
                if (previous.Onset == chord.Onset)
                {
                    long joinedEnd = Math.Min(previous.End, chord.End);
                    foreach (ShownNote note in previous.Notes.Concat(chord.Notes).Where(note => note.End > joinedEnd))
                    {
                        shortened.Add(note.Note.Id);
                    }

                    previous.Notes = [.. previous.Notes.Concat(chord.Notes).OrderBy(note => note.Note.Pitch.MidiNumber).ThenBy(note => note.Note.Id, StringComparer.Ordinal)];
                    previous.End = joinedEnd;
                    continue;
                }

                foreach (ShownNote note in previous.Notes)
                {
                    shortened.Add(note.Note.Id);
                }

                previous.End = chord.Onset;
            }

            voice.Add(chord);
        }

        return [.. voices.OrderByDescending(voice => voice.SelectMany(chord => chord.Notes).Average(note => note.Note.Pitch.MidiNumber))];
    }

    private static StaffMeasure LayOutStaff(StaffPlan staff, Bar bar, int fifths)
    {
        var grid = new BarGrid(bar.IsPickup ? bar.Span.End.Value - bar.Signature.BarTicks : bar.Span.Onset.Value, Pulse(bar.Signature), Middle(bar.Signature));
        long beamGroup = BeamGroup(bar.Signature);
        long start = bar.Span.Onset.Value;
        long stop = bar.Span.End.Value;

        var layers = new List<List<Element>>();
        for (int index = 0; index < Math.Max(1, staff.Voices.Count); index++)
        {
            Chord[] inBar = index < staff.Voices.Count
                ? [.. staff.Voices[index].Where(chord => chord.Onset < stop && chord.End > start)]
                : [];
            if (inBar.Length == 0)
            {
                // Lower voices leave empty bars out; the top voice rests for the whole bar.
                if (index == 0)
                {
                    layers.Add(bar.IsComplete && !bar.IsPickup ? [new MeasureRest()] : [.. Fill(start, stop, grid, space: false)]);
                }
                else
                {
                    layers.Add([]);
                }

                continue;
            }

            var elements = new List<Element>();
            long cursor = start;
            foreach (Chord chord in inBar)
            {
                long from = Math.Max(chord.Onset, start);
                long to = Math.Min(chord.End, stop);
                elements.AddRange(Fill(cursor, from, grid, space: index > 0));
                foreach ((long pieceStart, long ticks, int dur, int dots) in Decompose(from, to, grid))
                {
                    elements.Add(new ChordPiece(pieceStart, ticks, dur, dots, chord, chord.NextPiece++, pieceStart > chord.Onset, pieceStart + ticks < chord.End));
                }

                cursor = to;
            }

            elements.AddRange(Fill(cursor, stop, grid, space: index > 0));
            layers.Add(Beam(elements, grid.Origin, beamGroup));
        }

        WriteAccidentals(layers, fifths);
        return new StaffMeasure(layers);
    }

    /// <summary>The span a note value has to start on a multiple of: the beat, or three beats in compound meters (6/8, 9/8, 12/16).</summary>
    private static long Pulse(TimeSignature signature) =>
        IsCompound(signature) ? 3 * signature.BeatTicks : signature.BeatTicks;

    /// <summary>
    /// The middle of a bar of four beats or pulses (4/4, 12/8): only a value that starts the bar may
    /// cross it, so the second half of the bar stays visible. Zero for other meters.
    /// </summary>
    private static long Middle(TimeSignature signature)
    {
        int pulses = IsCompound(signature) ? signature.Numerator / 3 : signature.Numerator;
        return pulses >= 4 && pulses % 2 == 0 ? signature.BarTicks / 2 : 0;
    }

    /// <summary>The span eighths and shorter are beamed within.</summary>
    private static long BeamGroup(TimeSignature signature)
    {
        if (IsCompound(signature))
        {
            return 3 * signature.BeatTicks;
        }

        if (signature.BeatTicks >= CanonicalTime.TicksPerQuarter)
        {
            return signature.BeatTicks;
        }

        // 2/8 and 3/8 beam the whole bar; 5/8 and 7/8 by the quarter.
        return signature.Numerator <= 3 ? signature.BarTicks : CanonicalTime.TicksPerQuarter;
    }

    private static bool IsCompound(TimeSignature signature) =>
        signature.Denominator >= 8 && signature.Numerator > 3 && signature.Numerator % 3 == 0;

    private static IEnumerable<(long Start, long Ticks, int Dur, int Dots)> Decompose(long from, long to, BarGrid grid)
    {
        for (long position = from; position < to;)
        {
            long offset = position - grid.Origin;
            (long ticks, int dur, int dots) = s_values.FirstOrDefault(value => value.Ticks <= to - position
                && offset % Math.Min(value.Ticks, grid.Pulse) == 0
                && (grid.Middle == 0 || offset == 0 || offset >= grid.Middle || offset + value.Ticks <= grid.Middle));
            if (ticks == 0)
            {
                throw new InvalidOperationException($"No note value fits at {position}t before {to}t.");
            }

            yield return (position, ticks, dur, dots);
            position += ticks;
        }
    }

    private static IEnumerable<Element> Fill(long from, long to, BarGrid grid, bool space) =>
        Decompose(from, to, grid).Select(value => new Rest(value.Start, value.Ticks, value.Dur, value.Dots, space));

    private static List<Element> Beam(List<Element> elements, long origin, long beamGroup)
    {
        var result = new List<Element>();
        var group = new List<Element>();
        long groupIndex = -1;

        void Flush()
        {
            if (group.Count >= 2)
            {
                result.Add(new Beamed([.. group]));
            }
            else
            {
                result.AddRange(group);
            }

            group.Clear();
        }

        foreach (Element element in elements)
        {
            long first = (element.Start - origin) / beamGroup;
            long lastTick = (element.Start + element.Ticks - 1 - origin) / beamGroup;
            bool beamable = element is ChordPiece && element.Dur >= 8 && first == lastTick;
            if (!beamable || first != groupIndex)
            {
                Flush();
            }

            if (beamable)
            {
                group.Add(element);
                groupIndex = first;
            }
            else
            {
                result.Add(element);
                groupIndex = -1;
            }
        }

        Flush();
        return result;
    }

    /// <summary>Accidentals per bar and staff, in time order across voices; tied continuations carry theirs silently.</summary>
    private static void WriteAccidentals(List<List<Element>> layers, int fifths)
    {
        int[] key = PitchSpeller.KeyAlterations(fifths);
        var current = new Dictionary<(int Step, int Octave), int>();
        IEnumerable<ChordPiece> pieces = layers
            .SelectMany((layer, index) => Flatten(layer).OfType<ChordPiece>().Select(piece => (piece, index)))
            .OrderBy(entry => entry.piece.Start)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.piece);

        foreach (ChordPiece piece in pieces)
        {
            piece.Spellings = [.. piece.Chord.Notes.Select(note => PitchSpeller.Spell(note.Note.Pitch.MidiNumber, fifths))];
            piece.ShowAccidental = new bool[piece.Spellings.Length];
            for (int i = 0; i < piece.Spellings.Length; i++)
            {
                SpelledPitch spelled = piece.Spellings[i];
                if (piece.TieIn)
                {
                    continue;
                }

                int inEffect = current.TryGetValue((spelled.Step, spelled.Octave), out int alter) ? alter : key[spelled.Step];
                if (spelled.Alter != inEffect)
                {
                    piece.ShowAccidental[i] = true;
                    current[(spelled.Step, spelled.Octave)] = spelled.Alter;
                }
            }
        }
    }

    private static IEnumerable<Element> Flatten(IEnumerable<Element> elements) =>
        elements.SelectMany(element => element is Beamed beam ? beam.Elements : [element]);

    private static string WriteMei(string title, List<StaffPlan> staves, List<MeasurePlan> measures)
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
            WriteStaffGroups(xml, staves);
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

    private static void WriteStaffGroups(XmlWriter xml, List<StaffPlan> staves)
    {
        xml.WriteStartElement("staffGrp", MeiNamespace);
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
                xml.WriteEndElement();
            }

            if (grand)
            {
                xml.WriteEndElement();
            }
        }

        xml.WriteEndElement();
    }

    private static void WriteMeasure(XmlWriter xml, MeasurePlan measure)
    {
        xml.WriteStartElement("measure", MeiNamespace);
        xml.WriteAttributeString("n", Number(measure.Bar.Number));
        if (!measure.Bar.IsComplete || measure.Bar.IsPickup)
        {
            xml.WriteAttributeString("metcon", "false");
        }

        for (int staff = 0; staff < measure.Staves.Length; staff++)
        {
            xml.WriteStartElement("staff", MeiNamespace);
            xml.WriteAttributeString("n", Number(staff + 1));
            List<List<Element>> layers = measure.Staves[staff].Layers;
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

        xml.WriteEndElement();
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
                WriteDuration(xml, piece);
                WritePitch(xml, piece, 0);
                xml.WriteEndElement();
                break;

            case ChordPiece piece:
                xml.WriteStartElement("chord", MeiNamespace);
                WriteId(xml, "c_" + piece.Chord.Notes[0].Note.Id, piece.Index);
                WriteDuration(xml, piece);
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
        -1 => "f",
        0 => "n",
        1 => "s",
        _ => throw new InvalidOperationException($"No accidental for an alteration of {alter}."),
    };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed record ShownNote(Note Note, long Onset, long RawEnd)
    {
        /// <summary>At least one grid step after the onset, so a very short note still shows.</summary>
        public long End { get; } = Math.Max(RawEnd, Onset + Grid);
    }

    private sealed class Chord(long onset, long end, ShownNote[] notes)
    {
        public long Onset { get; } = onset;

        public long End { get; set; } = end;

        /// <summary>Lowest pitch first.</summary>
        public ShownNote[] Notes { get; set; } = notes;

        /// <summary>The piece number the next bar's piece of this chord gets, for its id.</summary>
        public int NextPiece { get; set; }
    }

    private sealed record StaffPlan(int Number, int Track, Clef Clef, Hand? Hand, bool IsSplitInferred, ShownNote[] Notes)
    {
        public List<List<Chord>> Voices { get; set; } = [];
    }

    /// <param name="Origin">Where a full bar would start: before the start of a pickup.</param>
    private readonly record struct BarGrid(long Origin, long Pulse, long Middle);

    private sealed record MeasurePlan(Bar Bar, int Fifths, StaffMeasure[] Staves);

    private sealed record StaffMeasure(List<List<Element>> Layers);

    private abstract class Element(long start, long ticks, int dur, int dots)
    {
        public long Start { get; } = start;

        public long Ticks { get; } = ticks;

        public int Dur { get; } = dur;

        public int Dots { get; } = dots;
    }

    private sealed class MeasureRest() : Element(0, 0, 0, 0);

    private sealed class Rest(long start, long ticks, int dur, int dots, bool space) : Element(start, ticks, dur, dots)
    {
        public bool IsSpace { get; } = space;
    }

    private sealed class Beamed(Element[] elements) : Element(elements[0].Start, elements.Sum(element => element.Ticks), 0, 0)
    {
        public Element[] Elements { get; } = elements;
    }

    private sealed class ChordPiece(long start, long ticks, int dur, int dots, Chord chord, int index, bool tieIn, bool tieOut)
        : Element(start, ticks, dur, dots)
    {
        public Chord Chord { get; } = chord;

        public int Index { get; } = index;

        public bool TieIn { get; } = tieIn;

        public bool TieOut { get; } = tieOut;

        public SpelledPitch[] Spellings { get; set; } = [];

        public bool[] ShowAccidental { get; set; } = [];
    }
}