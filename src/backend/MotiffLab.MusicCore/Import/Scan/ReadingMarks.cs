using System.Globalization;
using System.Text;
using System.Xml.Linq;

using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>
/// A reading with the player's marks «что не так» put in (<see cref="ReadingMarks.Apply"/>): its MusicXML, the marks that
/// changed it or found it as marked, and those that could not be put in (their bars are not in the reading, or it has no
/// such staff or clef), which the notes flag in Check.
/// </summary>
internal sealed record MarkedReading(byte[] Score, IReadOnlyList<ReadingMark> Applied, IReadOnlyList<ReadingMark> Unfit);

/// <summary>
/// Puts the player's marks into a new reading of the pages they were made on («Как напечатано», step 3; concept 3.1), after
/// the engines' mistakes are put right (<see cref="ScanRepair"/>), the same for the scan server's engines and the vector
/// reader: the marks never leave the device. The marks name the reading's bars as the workspace numbers them.
/// </summary>
/// <remarks>
/// <para>
/// Hands swapped: the notes of the bars change staves 1 and 2, in parts with two. A clef: the notes of the staff are read
/// again under the printed clef instead of the one the reading wrote, a step for a step on the staff. An octave sign: the
/// staff's notes an octave up (8va) or down (8vb). A key signature: from its bar to the next key mark, the reading's key
/// signatures say it and its notes take its sharps or flats. A time signature: from its bar to the next meter mark, the
/// reading's time signatures say it, and a bar that comes out in its length with the split moments put back is re-timed
/// so (<see cref="ScanRepair.Bar"/>); a bar that still does not add up keeps its Check flag. Triplets and a bar read badly
/// wait for the reading itself (c-14b2) and pass through unchanged.
/// </para>
/// <para>
/// A note read again under a clef or a key keeps its alteration where the page shows one: an accidental written, one
/// earlier in the bar at the same place on the staff, or an alteration the reading's own key does not give; any other takes
/// the alteration of the key in force. What is changed is the player's, not read: the score says so in its encoding.
/// Pure and deterministic.
/// </para>
/// </remarks>
internal static class ReadingMarks
{
    /// <summary>The order the marks go in: staves first, then the notes on them, then the bars' lengths.</summary>
    private static readonly ReadingMarkKind[] s_order = [ReadingMarkKind.Hands, ReadingMarkKind.Clef, ReadingMarkKind.Octave, ReadingMarkKind.Key, ReadingMarkKind.Meter];

    /// <summary>
    /// <paramref name="score"/>, the reading <paramref name="read"/> was imported from, with <paramref name="marks"/> put in
    /// (their bars <paramref name="read"/>'s); the same array when no mark changes it.
    /// </summary>
    public static MarkedReading Apply(byte[] score, ImportedPiece read, IReadOnlyList<ReadingMark> marks)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(marks);
        ReadingMark[] ours = [.. marks.Where(static mark => s_order.Contains(mark.Kind))];
        if (ours.Length == 0)
        {
            return new MarkedReading(score, [], []);
        }

        if (ScanRepair.Parse(score) is not { } root || read.MusicXml is not { } import)
        {
            return new MarkedReading(score, [], ours);
        }

        Dictionary<int, int> measureOf = [];
        foreach (MusicXmlMeasure measure in import.Measures)
        {
            measureOf.TryAdd(read.Meter.ToPosition(measure.Span.Onset).Bar, measure.Index);
        }

        List<XElement> parts = [.. root.Elements("part")];
        int count = parts.Count == 0 ? 0 : parts.Min(static part => part.Elements("measure").Count());
        List<ReadingMark> applied = [], unfit = [];
        foreach (ReadingMark mark in ours.OrderBy(mark => Array.IndexOf(s_order, mark.Kind)).ThenBy(static mark => mark.Bar))
        {
            bool done = measureOf.TryGetValue(mark.Bar, out int first) && first < count
                && Put(parts, mark, first, LastMeasure(mark, ours, measureOf, first, count));
            (done ? applied : unfit).Add(mark);
        }

        if (applied.Count == 0)
        {
            return new MarkedReading(score, [], unfit);
        }

        ScanRepair.Say(root, "Motiff Lab reading marks, the player's: " + string.Join("; ", applied.Select(mark => mark.Text(Words.English))) + ".");
        byte[] marked = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n" + root.ToString(SaveOptions.DisableFormatting));
        return new MarkedReading(marked, applied, unfit);
    }

    /// <summary>
    /// The last measure a mark puts in: for a meter or key mark, the one before the next mark of its kind (else the last);
    /// for the others, that of its last bar, or the last before it when the reading has none.
    /// </summary>
    private static int LastMeasure(ReadingMark mark, ReadingMark[] marks, Dictionary<int, int> measureOf, int first, int count)
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
                note.SetElementValue("staff", (3 - staff).ToString(CultureInfo.InvariantCulture));
            }
        }

        return true;
    }

    /// <summary>The staff's notes read under the printed clef; false when the part has no such staff or the reading writes it in a clef other than treble or bass.</summary>
    private static bool Clef(List<XElement> measures, int first, int last, ReadingMark mark)
    {
        int staff = mark.Staff!.Value;
        if (Staves(measures) < staff)
        {
            return false;
        }

        (char sign, int line) = ReadingMark.Clefs[mark.Value!];
        bool fits = true;
        Walk(measures, first, last, (note, state) =>
        {
            if (StaffOf(note) != staff || state.ClefOf(staff) is not { } read || !ReadingMark.Clefs.Values.Contains(read))
            {
                fits &= StaffOf(note) != staff;
                return null;
            }

            return (StaffPitch.Middle(sign, line, 0) - StaffPitch.Middle(read.Sign, read.Line, 0), state.Fifths);
        });
        return fits;
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
            .Where(bar => bar.Retimed is not null && bar.RetimedLength == length && bar.Length != length))
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

    /// <summary>The measure's first attributes' element <paramref name="name"/> ("key" or "time"), made in its place where missing, with the attributes before the first note.</summary>
    private static XElement AttributeOf(XElement measure, string name)
    {
        XElement? attributes = measure.Element("attributes");
        if (attributes is null)
        {
            attributes = new XElement("attributes");
            if (measure.Elements().FirstOrDefault(static element => element.Name.LocalName is "note" or "backup" or "forward") is { } music)
            {
                music.AddBeforeSelf(attributes);
            }
            else
            {
                measure.Add(attributes);
            }
        }

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

    private static long? Number(XElement? element) =>
        long.TryParse(element?.Value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value) ? value : null;
}