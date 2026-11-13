using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>
/// Puts right two things the scan engine (homr 0.7.0, run unmodified, 0011) writes wrong in its MusicXML, before the
/// score is kept (v0.5, issue #479, item 20): notes of the two hands it read one after the other although they sound
/// together, and the time signature it works out from those too long bars.
/// </summary>
/// <remarks>
/// <para>
/// The engine reads a page as one stream of moments across both staves of a grand staff, each moment written at the
/// cursor and the cursor moved on by its shortest note. When it splits one moment in two, say a right-hand eighth and
/// the left hand's half note under it, the half note is written after the eighth instead of with it, and every later note
/// of the bar comes half a bar late. Such a pair shows itself: the two moments are on different staves and each staff is
/// silent while the other plays, with no rest written, where the engine writes the rests it reads. Such a moment is put
/// back with the one before it.
/// </para>
/// <para>
/// The engine never reads the upper number of a time signature: it writes the median length of its own bars, so those
/// split moments turn a 4/4 piece into 5/4. A bar is re-timed only when that gives it the length most bars have (counting
/// each bar as it was read and as it would be re-timed), so music where the hands really take turns keeps its reading.
/// The upper number of each time signature becomes that length when at least three bars, and half of those it governs,
/// have it; a signature over fewer bars is kept as written.
/// </para>
/// <para>
/// What is changed is inferred, not read: the score says so in its <c>encoding-description</c>, and its notes stay
/// detected (<see cref="ScanCheck"/>). Pure and deterministic, and repairing a repaired score changes nothing. A score
/// that is not partwise MusicXML comes back as it is; <see cref="ScanJoin"/> says what is wrong with it.
/// </para>
/// <para>
/// Every way the engine's MusicXML reaches the notes runs it (issue #495 BUG-03): <see cref="ScanJoin.Join"/> for pages
/// and parts read one request at a time, and, for a PDF or photo read in one request, the facade's document.read and
/// sources.documentNotes (iPad) and the web app's NotesReading, scan import and Sources. A score kept before it ran keeps
/// its reading until its notes are read again.
/// </para>
/// </remarks>
public static partial class ScanRepair
{
    /// <summary>A time signature is rewritten only when at least this many of its bars have the length most of them have.</summary>
    public const int FewestBarsForMeter = 3;

    private static readonly XmlReaderSettings Safe = new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };

    /// <summary>The score with split moments put back and its time signatures from its bars; the same array when nothing changes.</summary>
    public static byte[] Repair(byte[] score)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (Parse(score) is not { } root)
        {
            return score;
        }

        var changes = new Changes();
        foreach (XElement part in root.Elements("part"))
        {
            List<Bar> bars = Bars(part);
            foreach (List<Bar> range in Ranges(bars))
            {
                Mend(range, changes);
            }
        }

        if (changes.Retimed == 0 && changes.Meters.Count == 0)
        {
            return score;
        }

        Describe(root, changes);
        return Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n" + root.ToString(SaveOptions.DisableFormatting));
    }

    /// <summary>One range of bars under one written time signature (or none, before the first): re-timed bars, then the signature.</summary>
    private static void Mend(List<Bar> range, Changes changes)
    {
        XElement? time = range[0].Time;
        QuarterFraction? written = time is null ? null : Written(time);
        if (Most(range, written) is not { } most)
        {
            return;
        }

        foreach (Bar bar in range.Where(bar => bar.Retimed is not null && bar.RetimedLength == most && bar.Length != most))
        {
            Retime(bar);
            changes.Retimed++;
        }

        int count = range.Count(bar => bar.Length == most);
        if (time is null || written is null || written == most || count < FewestBarsForMeter || count * 2 < range.Count)
        {
            return;
        }

        int beatType = int.Parse(time.Element("beat-type")!.Value.Trim(), CultureInfo.InvariantCulture);
        QuarterFraction beats = new(most.Numerator * beatType, most.Denominator * 4);
        if (beats.Denominator == 1 && beats.Numerator is >= 1 and <= TimeSignature.MaxNumerator)
        {
            changes.Meters.Add($"{beats.Numerator}/{beatType} where the engine wrote {time.Element("beats")!.Value.Trim()}/{beatType}");
            time.Element("beats")!.Value = beats.Numerator.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The bar length the range agrees on: the one most bars have, read or re-timed; then the one most bars were read with,
    /// then the written one, then the shorter. Null for a range with no bar that has a length.
    /// </summary>
    private static QuarterFraction? Most(List<Bar> range, QuarterFraction? written)
    {
        var lengths = range.Select(bar => bar.Length)
            .Concat(range.Where(bar => bar.Retimed is not null).Select(bar => bar.RetimedLength))
            .Where(length => length > QuarterFraction.Zero)
            .Distinct()
            .ToList();
        if (lengths.Count == 0)
        {
            return null;
        }

        return lengths
            .OrderByDescending(length => range.Count(bar => bar.Length == length || (bar.Retimed is not null && bar.RetimedLength == length)))
            .ThenByDescending(length => range.Count(bar => bar.Length == length))
            .ThenByDescending(length => length == written)
            .ThenBy(length => length)
            .First();
    }

    /// <summary>The bar length a time signature writes, when it is one plain pair of numbers.</summary>
    private static QuarterFraction? Written(XElement time) =>
        time.Elements("beats").Count() == 1 && time.Elements("beat-type").Count() == 1
            && int.TryParse(time.Element("beats")!.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int beats)
            && int.TryParse(time.Element("beat-type")!.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int beatType)
            && beats >= 1 && beatType >= 1
            ? new QuarterFraction(beats * 4L, beatType)
            : null;

    /// <summary>The bars of a part cut where a time signature is written; the first range may have none.</summary>
    private static List<List<Bar>> Ranges(List<Bar> bars)
    {
        var ranges = new List<List<Bar>>();
        foreach (Bar bar in bars)
        {
            if (ranges.Count == 0 || (bar.Time is not null && Written(bar.Time) is not null))
            {
                ranges.Add([]);
            }

            ranges[^1].Add(bar);
        }

        return ranges;
    }

    private static List<Bar> Bars(XElement part)
    {
        var bars = new List<Bar>();
        long divisions = 1;
        foreach (XElement measure in part.Elements("measure"))
        {
            Bar bar = Bar.Read(measure, divisions);
            divisions = bar.Divisions;
            bars.Add(bar);
        }

        return bars;
    }

    /// <summary>Writes the bar's notes at their new places: its backups and forwards are written again, nothing else moves.</summary>
    private static void Retime(Bar bar)
    {
        bar.Measure.Elements().Where(element => element.Name.LocalName is "backup" or "forward").Remove();
        long cursor = 0;
        int index = 0;
        foreach (XElement note in bar.Measure.Elements("note").ToList())
        {
            Sound sound = bar.Sounds[index];
            long onset = bar.Retimed![index++];
            if (sound.IsChord && !sound.IsGrace)
            {
                continue;
            }

            if (onset != cursor)
            {
                note.AddBeforeSelf(new XElement(onset < cursor ? "backup" : "forward", new XElement("duration", Math.Abs(cursor - onset).ToString(CultureInfo.InvariantCulture))));
            }

            cursor = onset + sound.Duration;
        }

        bar.Length = bar.RetimedLength;
    }

    /// <summary>Says in the score's encoding what was inferred, so the file keeps it.</summary>
    private static void Describe(XElement root, Changes changes)
    {
        XElement? identification = root.Element("identification");
        if (identification is null)
        {
            identification = new XElement("identification");
            XElement? next = root.Elements().FirstOrDefault(element => element.Name.LocalName is "defaults" or "credit" or "part-list" or "part");
            if (next is null)
            {
                root.Add(identification);
            }
            else
            {
                next.AddBeforeSelf(identification);
            }
        }

        XElement? encoding = identification.Element("encoding");
        if (encoding is null)
        {
            encoding = new XElement("encoding");
            XElement? next = identification.Elements().FirstOrDefault(element => element.Name.LocalName is "source" or "relation" or "miscellaneous");
            if (next is null)
            {
                identification.Add(encoding);
            }
            else
            {
                next.AddBeforeSelf(encoding);
            }
        }

        var said = new List<string>();
        if (changes.Retimed > 0)
        {
            said.Add($"{changes.Retimed} bars re-timed where the engine wrote notes that sound together one after the other");
        }

        said.AddRange(changes.Meters.Select(meter => $"time signature {meter}, inferred from the bar contents"));
        encoding.Add(new XElement("encoding-description", "Motiff Lab scan repair: " + string.Join("; ", said) + "."));
    }

    private static XElement? Parse(byte[] score)
    {
        try
        {
            using var stream = new MemoryStream(score, writable: false);
            using var reader = XmlReader.Create(stream, Safe);
            XElement root = XDocument.Load(reader).Root!;
            return root.Name.LocalName == "score-partwise" ? root : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private sealed class Changes
    {
        public int Retimed { get; set; }

        public List<string> Meters { get; } = [];
    }

    /// <summary>A note or rest of a bar as written: its staff, whether it is a chord tone or a grace note, and its length in divisions.</summary>
    private sealed record Sound(int Staff, bool IsChord, bool IsGrace, long Duration);

    /// <summary>One measure: its notes where they were read, and where they would be with split moments put back.</summary>
    private sealed class Bar
    {
        private Bar(XElement measure, long divisions) => (Measure, Divisions) = (measure, divisions);

        public XElement Measure { get; }

        public long Divisions { get; private set; }

        public XElement? Time { get; private set; }

        public List<Sound> Sounds { get; } = [];

        public List<long> Onsets { get; } = [];

        /// <summary>Where each sound starts when re-timed; null when nothing would change or the bar is not as the engine writes one.</summary>
        public long[]? Retimed { get; private set; }

        public QuarterFraction Length { get; set; }

        public QuarterFraction RetimedLength { get; private set; }

        public static Bar Read(XElement measure, long divisions)
        {
            var bar = new Bar(measure, divisions);
            bool plain = bar.ReadElements(out long end);
            bar.Length = new QuarterFraction(end, bar.Divisions);
            if (plain && Moments.Retime(bar.Sounds, bar.Onsets, end) is { } retimed)
            {
                bar.Retimed = retimed;
                bar.RetimedLength = new QuarterFraction(Enumerable.Range(0, retimed.Length).Max(i => retimed[i] + bar.Sounds[i].Duration), bar.Divisions);
            }

            return bar;
        }

        /// <summary>Reads the notes as the importer places them; false when the bar has a forward or a note without a length.</summary>
        private bool ReadElements(out long end)
        {
            long cursor = 0, last = 0;
            bool plain = true;
            end = 0;
            foreach (XElement element in Measure.Elements())
            {
                switch (element.Name.LocalName)
                {
                    case "attributes":
                        Divisions = Number(element.Element("divisions")) is { } value and > 0 ? value : Divisions;
                        Time ??= element.Element("time");
                        break;
                    case "backup":
                        cursor = Math.Max(0, cursor - (Number(element.Element("duration")) ?? 0));
                        break;
                    case "forward":
                        plain = false;
                        cursor += Number(element.Element("duration")) ?? 0;
                        end = Math.Max(end, cursor);
                        break;
                    case "note":
                        plain &= ReadNote(element, ref cursor, ref last, ref end);
                        break;
                }
            }

            return plain;
        }

        private bool ReadNote(XElement note, ref long cursor, ref long last, ref long end)
        {
            bool grace = note.Element("grace") is not null;
            bool chord = note.Element("chord") is not null;
            long? duration = grace ? 0 : Number(note.Element("duration"));
            long onset = grace || !chord ? cursor : last;
            if (!grace && !chord && duration is { } length)
            {
                last = cursor;
                cursor += length;
            }

            int staff = Number(note.Element("staff")) is { } number and >= 1 ? (int)number : 1;
            Sounds.Add(new Sound(staff, chord, grace, duration ?? 0));
            Onsets.Add(onset);
            end = Math.Max(end, onset + (duration ?? 0));
            return duration is not null;
        }

        private static long? Number(XElement? element) =>
            long.TryParse(element?.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long value) ? value : null;
    }
}