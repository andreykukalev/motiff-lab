using System.Globalization;
using System.Text;
using System.Xml.Linq;

using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>What became of a mark put into a reading: put in, or not put in (Check flags it).</summary>
internal enum MarkOutcome
{
    Applied,
    Unfit,
}

/// <summary>A mark as a reading took it: the measures (by index in each part) it lies on, null where the reading has none, and its outcome.</summary>
internal sealed record PlacedMark(ReadingMark Mark, int? First, int? Last, MarkOutcome Outcome);

/// <summary>
/// A reading with the player's marks «что не так» put in (<see cref="ReadingMarks.Apply"/>): its MusicXML and each mark with
/// the measures it lies on and its outcome, so the marks are numbered by the bars of the notes the MusicXML gives
/// (<see cref="Numbered"/>), which a meter mark may number otherwise than the reading did.
/// </summary>
internal sealed record MarkedReading(byte[] Score, IReadOnlyList<PlacedMark> Marks)
{
    /// <summary>
    /// The marks on <paramref name="piece"/>'s bars (the notes imported from <see cref="Score"/>), each with its outcome: a
    /// mark on measures numbered by the bars they start, one the reading has no measure for kept within the piece's bars.
    /// </summary>
    public IReadOnlyList<(ReadingMark Mark, MarkOutcome Outcome)> Numbered(ImportedPiece piece)
    {
        IReadOnlyList<MusicXmlMeasure> measures = piece.MusicXml?.Measures ?? [];
        int first = piece.Meter.BarAt(Tick.Zero).Number;
        int end = piece.Meter.BarAt(new Tick(Math.Max(0, piece.End.Value - 1))).Number;
        int BarOf(int index) => piece.Meter.ToPosition(measures[Math.Min(index, measures.Count - 1)].Span.Onset).Bar;
        return [.. Marks.Select(placed =>
        {
            ReadingMark mark = placed.Mark;
            if (placed.First is not { } at || measures.Count == 0)
            {
                int bar = Math.Clamp(mark.Bar, first, Math.Max(first, end));
                return (mark.At(bar), placed.Outcome);
            }

            int from = BarOf(at);
            int to = ReadingMark.FromBarOn(mark.Kind) ? from : Math.Max(from, BarOf(placed.Last ?? at));
            return (new ReadingMark(mark.Kind, from, to, mark.Staff, mark.Value), placed.Outcome);
        })];
    }
}

/// <summary>
/// Puts the player's marks into a new reading of the pages they were made on («Как напечатано», step 3; concept 3.1), after
/// the engines' mistakes are put right (<see cref="ScanRepair"/>), the same for the scan server's engines and the vector
/// reader: the marks never leave the device. The marks name the reading's bars as the workspace numbers them.
/// </summary>
/// <remarks>
/// <para>
/// Hands swapped: the notes of the bars change staves 1 and 2, in parts with two. A clef: the notes of the staff are read
/// again under the printed clef instead of the one the reading wrote, a step for a step on the staff, and the printed clef is written at its first bar, the reading's own after its last. An octave sign: the
/// staff's notes an octave up (8va) or down (8vb). A key signature: from its bar to the next key mark, the reading's key
/// signatures say it and its notes take its sharps or flats. A time signature: from its bar to the next meter mark, the
/// reading's time signatures say it, and a bar that comes out in its length with the split moments put back is re-timed
/// so (<see cref="ScanRepair.Bar"/>); a bar that still does not add up keeps its Check flag. Triplets: the bars' staves
/// re-timed in 3 in the time of 2, or out of it, where their lengths then add up (c-14b2). A bar read badly changes no
/// note here: it is put in where the merge took another engine's reading of it (<see cref="MusicXmlImport.ReadAgain"/>,
/// <see cref="ScanBefore"/>), and does not fit a reading that has none.
/// </para>
/// <para>
/// A note read again under a clef or a key keeps its alteration where the page shows one: an accidental written, one
/// earlier in the bar at the same place on the staff, or an alteration the reading's own key does not give; any other takes
/// the alteration of the key in force. What is changed is the player's, not read: the score says so in its encoding.
/// Pure and deterministic.
/// </para>
/// </remarks>
internal static partial class ReadingMarks
{
    /// <summary>The order the marks that change notes go in: staves first, then the notes on them, then the bars' lengths, then the lengths in them.</summary>
    private static readonly ReadingMarkKind[] s_order = [ReadingMarkKind.Hands, ReadingMarkKind.Clef, ReadingMarkKind.Octave, ReadingMarkKind.Key, ReadingMarkKind.Meter, ReadingMarkKind.Triplets];

    /// <summary>
    /// <paramref name="score"/>, the reading <paramref name="read"/> was imported from, with <paramref name="marks"/> put in
    /// (their bars <paramref name="read"/>'s), but those whose index in <paramref name="marks"/> is <paramref name="unplaced"/>, whose music has no bar there, which
    /// are only placed; the same array when no mark changes it.
    /// </summary>
    public static MarkedReading Apply(byte[] score, ImportedPiece read, IReadOnlyList<ReadingMark> marks, IReadOnlySet<int>? unplaced = null)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(marks);
        XElement? root = marks.Any(static mark => s_order.Contains(mark.Kind)) ? ScanRepair.Parse(score) : null;
        Dictionary<int, int> measureOf = [];
        foreach (MusicXmlMeasure measure in read.MusicXml?.Measures ?? [])
        {
            measureOf.TryAdd(read.Meter.ToPosition(measure.Span.Onset).Bar, measure.Index);
        }

        List<XElement>? parts = root is null ? null : [.. root.Elements("part")];
        int count = parts is not { Count: > 0 } ? measureOf.Count : parts.Min(static part => part.Elements("measure").Count());
        HashSet<int> readAgain = [.. read.MusicXml?.ReadAgain ?? []];
        PlacedMark[] placed = [.. marks.Select(static (mark, index) => (Mark: mark, Index: index))
            .OrderBy(static each => s_order.Contains(each.Mark.Kind) ? Array.IndexOf(s_order, each.Mark.Kind) : s_order.Length).ThenBy(static each => each.Mark.Bar)
            .Select(each => Place(each.Mark, marks, unplaced?.Contains(each.Index) == true, parts, measureOf, count, readAgain))];
        PlacedMark[] applied = [.. placed.Where(static mark => mark.Outcome == MarkOutcome.Applied)];
        if (root is null || applied.Length == 0)
        {
            return new MarkedReading(score, placed);
        }

        ScanRepair.Say(root, "Motiff Lab reading marks, the player's: " + string.Join("; ", applied.Select(static mark => mark.Mark.Text(Words.English))) + ".");
        byte[] marked = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n" + root.ToString(SaveOptions.DisableFormatting));
        return new MarkedReading(marked, placed);
    }

    /// <summary>
    /// The mark on the measures of its bars, put into <paramref name="parts"/> (null: the reading is no MusicXML) unless
    /// <paramref name="unplaced"/>; a bar read badly fits when the merge read each of its measures again (<paramref name="readAgain"/>).
    /// </summary>
    private static PlacedMark Place(ReadingMark mark, IReadOnlyList<ReadingMark> marks, bool unplaced, List<XElement>? parts, Dictionary<int, int> measureOf, int count, HashSet<int> readAgain)
    {
        int? first = measureOf.TryGetValue(mark.Bar, out int found) && found < count ? found : null;
        int? last = first is { } at ? LastMeasure(mark, marks, measureOf, at, count) : null;
        bool fits = !unplaced && first is { } from && (mark.Kind == ReadingMarkKind.BadlyRead
            ? Enumerable.Range(from, last!.Value - from + 1).All(readAgain.Contains)
            : parts is not null && Put(parts, mark, from, last!.Value));
        MarkOutcome outcome = fits ? MarkOutcome.Applied : MarkOutcome.Unfit;
        return new PlacedMark(mark, first, last, outcome);
    }

    /// <summary>
    /// The last measure a mark puts in: for a meter or key mark, the one before the next mark of its kind (else the last);
    /// for the others, that of its last bar, or the last before it when the reading has none.
    /// </summary>
    private static int LastMeasure(ReadingMark mark, IReadOnlyList<ReadingMark> marks, Dictionary<int, int> measureOf, int first, int count)
    {
        if (ReadingMark.FromBarOn(mark.Kind))
        {
            int[] next = [.. marks.Where(other => other.Kind == mark.Kind && other.Bar > mark.Bar && measureOf.ContainsKey(other.Bar)).Select(other => measureOf[other.Bar])];
            return next.Length > 0 ? Math.Max(first, next.Min() - 1) : count - 1;
        }

        int[] within = [.. measureOf.Where(pair => pair.Key <= mark.Last && pair.Value >= first && pair.Value < count).Select(static pair => pair.Value)];
        return within.Length > 0 ? within.Max() : first;
    }

    /// <summary>Puts the mark into each part's measures <paramref name="first"/> to <paramref name="last"/>; false when no part can take it.</summary>
    private static bool Put(List<XElement> parts, ReadingMark mark, int first, int last)
    {
        bool any = false;
        foreach (XElement part in parts)
        {
            List<XElement> measures = [.. part.Elements("measure")];
            any |= mark.Kind switch
            {
                ReadingMarkKind.Hands => SwapHands(measures, first, last),
                ReadingMarkKind.Clef => Clef(measures, first, last, mark),
                ReadingMarkKind.Octave => Octave(measures, first, last, mark),
                ReadingMarkKind.Key => Key(measures, first, last, mark.Fifths),
                ReadingMarkKind.Triplets => Triplets(part, first, last, mark.Value == "yes"),
                _ => Meter(part, measures, first, last, mark.Meter),
            };
        }

        return any;
    }

    /// <summary>The notes on staves 1 and 2 of the measures change staves, in a part with two staves or more.</summary>
    private static bool SwapHands(List<XElement> measures, int first, int last)
    {
        if (Staves(measures) < 2)
        {
            return false;
        }

        foreach (XElement note in measures.Skip(first).Take(last - first + 1).SelectMany(static measure => measure.Elements("note")))
        {
            int staff = StaffOf(note);
            if (staff is 1 or 2)
            {
                SetStaff(note, 3 - staff);
            }
        }

        return true;
    }

    /// <summary>The staff's notes read under the printed clef; false, every note left as it was, when the part has no such staff or the reading writes it in a clef other than treble or bass in any of the bars.</summary>
    private static bool Clef(List<XElement> measures, int first, int last, ReadingMark mark)
    {
        int staff = mark.Staff!.Value;
        if (Staves(measures) < staff)
        {
            return false;
        }

        (char sign, int line) = ReadingMark.Clefs[mark.Value!];
        // Every note of the staff in the bars is first found under a treble or bass clef, so the mark moves all of them or none.
        bool fits = true;
        Walk(measures, first, last, (note, state) =>
        {
            fits &= StaffOf(note) != staff || state.ClefOf(staff) is { } read && ReadingMark.Clefs.Values.Contains(read);
            return null;
        });
        if (!fits)
        {
            return false;
        }

        Walk(measures, first, last, (note, state) => StaffOf(note) == staff && state.ClefOf(staff) is { } read
            ? (StaffPitch.Middle(sign, line, 0) - StaffPitch.Middle(read.Sign, read.Line, 0), state.Fifths)
            : null);

        // The clef as printed over the bars, and the reading's own again after them, where it did not write one there.
        (char Sign, int Line)? after = ReadState.After(measures, last).ClefOf(staff);
        foreach (XElement measure in measures.Skip(first + 1).Take(last - first))
        {
            measure.Elements("attributes").Elements("clef").Where(clef => ((int?)clef.Attribute("number") ?? 1) == staff).Remove();
        }

        PutClef(measures[first], staff, (sign, line));
        if (last + 1 < measures.Count && after is { } restored
            && !measures[last + 1].Elements("attributes").Elements("clef").Any(clef => ((int?)clef.Attribute("number") ?? 1) == staff))
        {
            PutClef(measures[last + 1], staff, restored);
        }

        return true;
    }

    /// <summary>The measure's clef of <paramref name="staff"/>, in the place of one it writes, in MusicXML's order among its attributes.</summary>
    private static void PutClef(XElement measure, int staff, (char Sign, int Line) clef)
    {
        XElement attributes = AttributesOf(measure);
        attributes.Elements("clef").Where(written => ((int?)written.Attribute("number") ?? 1) == staff).Remove();
        XElement made = new("clef", new XAttribute("number", staff), new XElement("sign", clef.Sign.ToString()), new XElement("line", clef.Line.ToString(CultureInfo.InvariantCulture)));
        string[] before = ["footnote", "level", "divisions", "key", "time", "staves", "part-symbol", "instruments"];
        if (attributes.Elements().LastOrDefault(element => before.Contains(element.Name.LocalName)
            || (element.Name.LocalName == "clef" && ((int?)element.Attribute("number") ?? 1) < staff)) is { } previous)
        {
            previous.AddAfterSelf(made);
        }
        else
        {
            attributes.AddFirst(made);
        }
    }

    /// <summary>The staff's notes an octave up or down; false when the part has no such staff.</summary>
    private static bool Octave(List<XElement> measures, int first, int last, ReadingMark mark)
    {
        int staff = mark.Staff!.Value;
        if (Staves(measures) < staff)
        {
            return false;
        }

        int by = mark.Value == "8va" ? 1 : -1;
        foreach (XElement octave in measures.Skip(first).Take(last - first + 1).SelectMany(static measure => measure.Elements("note"))
            .Where(note => StaffOf(note) == staff).Select(static note => note.Element("pitch")?.Element("octave")).OfType<XElement>())
        {
            octave.Value = ((Number(octave) ?? 4) + by).ToString(CultureInfo.InvariantCulture);
        }

        return true;
    }

    /// <summary>The key signatures of the measures say <paramref name="fifths"/>, the first's written where it has none, and their notes take it.</summary>
    private static bool Key(List<XElement> measures, int first, int last, int fifths)
    {
        Walk(measures, first, last, (_, _) => (0, fifths));
        foreach (XElement key in measures.Skip(first + 1).Take(last - first).SelectMany(static measure => measure.Elements("attributes").Elements("key")))
        {
            SetFifths(key, fifths);
        }

        SetFifths(AttributeOf(measures[first], "key"), fifths);
        return true;
    }

    /// <summary>
    /// The time signatures of the measures say <paramref name="meter"/>, the first's written where it has none, and each bar
    /// that comes out in its length with split moments put back re-timed so.
    /// </summary>
    private static bool Meter(XElement part, List<XElement> measures, int first, int last, TimeSignature meter)
    {
        foreach (XElement time in measures.Skip(first + 1).Take(last - first).SelectMany(static measure => measure.Elements("attributes").Elements("time")))
        {
            SetTime(time, meter);
        }

        SetTime(AttributeOf(measures[first], "time"), meter);
        QuarterFraction length = new(meter.Numerator * 4L, meter.Denominator);
        foreach (ScanRepair.Bar bar in ScanRepair.Bars(part).Skip(first).Take(last - first + 1)
            .Where(bar => bar.RetimesTo(length)))
        {
            bar.WriteOnsets(bar.Retimed!);
        }

        return true;
    }

    /// <summary>
    /// Walks every measure up to <paramref name="last"/> with the clefs and key the reading has in force, and writes each
    /// pitched note of <paramref name="first"/> on <paramref name="move"/>'s steps under its key (null leaves it), keeping
    /// the alteration where the page shows one (see the remarks).
    /// </summary>
    private static void Walk(List<XElement> measures, int first, int last, Func<XElement, ReadState, (int Steps, int Fifths)?> move)
    {
        ReadState state = new();
        for (int index = 0; index <= last; index++)
        {
            HashSet<(int Staff, int Diatonic)> shown = [];
            foreach (XElement element in measures[index].Elements())
            {
                if (element.Name.LocalName == "attributes")
                {
                    state.Read(element);
                }
                else if (element.Name.LocalName == "note" && element.Element("pitch") is { } pitch && index >= first && move(element, state) is { } to)
                {
                    Respell(element, pitch, state.Fifths, to, shown);
                }
            }
        }
    }

    /// <summary>A note's pitch moved by <paramref name="to"/>'s steps under its key, its alteration kept where the page shows one.</summary>
    private static void Respell(XElement note, XElement pitch, int readFifths, (int Steps, int Fifths) to, HashSet<(int Staff, int Diatonic)> shown)
    {
        char step = ((string?)pitch.Element("step"))?.Trim() is [char letter] && "CDEFGAB".Contains(letter, StringComparison.Ordinal) ? letter : 'C';
        int diatonic = StaffPitch.Diatonic(step, (int)(Number(pitch.Element("octave")) ?? 4));
        int alter = (int)(Number(pitch.Element("alter")) ?? 0);
        bool written = note.Element("accidental") is not null;
        bool kept = written || shown.Contains((StaffOf(note), diatonic)) || alter != StaffPitch.KeyAlter(readFifths, step);
        if (written)
        {
            shown.Add((StaffOf(note), diatonic));
        }

        int moved = diatonic + to.Steps;
        char newStep = StaffPitch.StepOf(moved);
        int newAlter = kept ? alter : StaffPitch.KeyAlter(to.Fifths, newStep);
        pitch.SetElementValue("step", newStep.ToString());
        pitch.SetElementValue("alter", newAlter == 0 ? null : newAlter.ToString(CultureInfo.InvariantCulture));
        pitch.SetElementValue("octave", StaffPitch.OctaveOf(moved).ToString(CultureInfo.InvariantCulture));
        if (pitch.Element("alter") is { } placed && pitch.Element("octave") is { } octave)
        {
            // MusicXML writes step, alter, octave in that order.
            placed.Remove();
            octave.AddBeforeSelf(placed);
        }
    }

    /// <summary>The clefs and key a reading has in force as its measures are walked.</summary>
    private sealed class ReadState
    {
        private readonly Dictionary<int, (char Sign, int Line)> _clefs = [];

        public int Fifths { get; private set; }

        /// <summary>The clefs and key in force at the end of measure <paramref name="last"/>.</summary>
        public static ReadState After(List<XElement> measures, int last)
        {
            ReadState state = new();
            foreach (XElement attributes in measures.Take(last + 1).SelectMany(static measure => measure.Elements("attributes")))
            {
                state.Read(attributes);
            }

            return state;
        }

        public (char Sign, int Line)? ClefOf(int staff) => _clefs.TryGetValue(staff, out (char Sign, int Line) clef) ? clef : null;

        public void Read(XElement attributes)
        {
            if (attributes.Element("key")?.Element("fifths") is { } fifths && int.TryParse(fifths.Value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
            {
                Fifths = value;
            }

            foreach (XElement clef in attributes.Elements("clef"))
            {
                int staff = (int?)clef.Attribute("number") ?? 1;
                if (((string?)clef.Element("sign"))?.Trim() is [char sign] && Number(clef.Element("line")) is { } line)
                {
                    _clefs[staff] = (sign, (int)line);
                }
            }
        }
    }

    /// <summary>The staves the part writes in its first attributes that say; 1 when none does.</summary>
    private static int Staves(List<XElement> measures) =>
        (int)(measures.SelectMany(static measure => measure.Elements("attributes").Elements("staves")).Select(Number).FirstOrDefault(static staves => staves is not null) ?? 1);

    /// <summary>The note's staff, a <c>staff</c> made where it has none in MusicXML's order: before its beams, notations and lyrics.</summary>
    private static void SetStaff(XElement note, int staff)
    {
        string value = staff.ToString(CultureInfo.InvariantCulture);
        if (note.Element("staff") is { } written)
        {
            written.Value = value;
        }
        else if (note.Elements().FirstOrDefault(static element => element.Name.LocalName is "beam" or "notations" or "lyric" or "play" or "listen") is { } after)
        {
            after.AddBeforeSelf(new XElement("staff", value));
        }
        else
        {
            note.Add(new XElement("staff", value));
        }
    }

    private static int StaffOf(XElement note) => Number(note.Element("staff")) is { } staff and >= 1 ? (int)staff : 1;

    private static void SetFifths(XElement key, int fifths)
    {
        key.RemoveNodes();
        key.Add(new XElement("fifths", fifths.ToString(CultureInfo.InvariantCulture)));
    }

    private static void SetTime(XElement time, TimeSignature meter)
    {
        time.RemoveAll();
        time.Add(new XElement("beats", meter.Numerator.ToString(CultureInfo.InvariantCulture)), new XElement("beat-type", meter.Denominator.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The measure's first attributes' element <paramref name="name"/> ("key" or "time"), made in its place where missing.</summary>
    private static XElement AttributeOf(XElement measure, string name)
    {
        XElement attributes = AttributesOf(measure);
        if (attributes.Element(name) is { } found)
        {
            return found;
        }

        XElement made = new(name);
        string[] before = name == "key" ? ["footnote", "level", "divisions"] : ["footnote", "level", "divisions", "key"];
        if (attributes.Elements().LastOrDefault(element => before.Contains(element.Name.LocalName)) is { } after)
        {
            after.AddAfterSelf(made);
        }
        else
        {
            attributes.AddFirst(made);
        }

        return made;
    }

    /// <summary>The measure's first attributes, made before its first note where it has none.</summary>
    private static XElement AttributesOf(XElement measure)
    {
        if (measure.Element("attributes") is { } attributes)
        {
            return attributes;
        }

        XElement made = new("attributes");
        if (measure.Elements().FirstOrDefault(static element => element.Name.LocalName is "note" or "backup" or "forward") is { } music)
        {
            music.AddBeforeSelf(made);
        }
        else
        {
            measure.Add(made);
        }

        return made;
    }

    private static long? Number(XElement? element) =>
        long.TryParse(element?.Value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value) ? value : null;
}