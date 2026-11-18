using System.Xml;
using System.Xml.Linq;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>
/// Imports a partwise MusicXML score, uncompressed or <c>.mxl</c>, onto the canonical timeline
/// (S1-I1, S1-I2, S1-I3, S1-I4, S1-I8). Decision record 0006 describes the policies.
/// </summary>
/// <remarks>
/// <para>
/// Each part is walked in document order. Note, <c>&lt;forward&gt;</c> and <c>&lt;backup&gt;</c>
/// durations convert through the part's current <see cref="MusicXmlDivisions"/> to exact
/// <see cref="QuarterFraction"/>s and are summed exactly, so a divisions change or a septuplet adds
/// no error. Measures are aligned across parts by index; positions round to ticks only in
/// <see cref="CanonicalTime"/>, onset and end separately.
/// </para>
/// <para>
/// Notes map to the shared <see cref="Note"/> as imported facts, with tied notes merged into one.
/// A part with two staves is a grand staff: staff 1 is the right hand and staff 2 the left, both
/// imported. Grace notes, rests, slurs and repeat signs are kept as source facts beside the notes.
/// </para>
/// <para>
/// The import is pure and deterministic: the same bytes give the same result and report.
/// </para>
/// <para>
/// The parts: <see cref="PartReader"/> walks each part; <see cref="ScoreBuilder"/> puts them together, with
/// <see cref="ScoreNotes"/> (ties merged, the shared notes), <see cref="ScoreMarks"/> (grace notes, rests, slurs, repeats,
/// bar lines, texts) and <see cref="ScoreTimeline"/> (tempos, bars, keys).
/// </para>
/// </remarks>
public static class MusicXmlImporter
{
    /// <summary>The Stage 1 file size limit (S1-I1).</summary>
    public const int MaxFileBytes = 50 * 1024 * 1024;

    public static MusicXmlImport Import(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxFileBytes)
        {
            throw new MusicXmlImportException(MusicXmlImportFailure.TooLarge, "This file is larger than 50 MB, the most Motiff Lab can import.");
        }

        XElement root = Load(MxlArchive.IsArchive(data) ? MxlArchive.ReadScore(data) : data);
        switch (root.Name.LocalName)
        {
            case "score-partwise":
                break;
            case "score-timewise":
                throw new MusicXmlImportException(
                    MusicXmlImportFailure.Unsupported,
                    "This MusicXML file is arranged measure by measure (timewise), which Motiff Lab cannot read yet. Most notation apps export the usual part-by-part layout.");
            default:
                throw NotMusicXml();
        }

        var issues = new ImportIssueCollector();
        var listed = ReadPartList(root);
        var parts = new List<PartReader>();
        foreach (XElement part in root.Elements("part"))
        {
            int index = parts.Count;
            string? id = XmlValues.Trimmed(part.Attribute("id")?.Value);
            if (id is null || !listed.TryGetValue(id, out (string? Name, string? Instrument) names))
            {
                issues.Add(ImportIssueKind.Repaired, MusicXmlIssueCodes.PartNotListed, index);
                names = (null, null);
            }

            var reader = new PartReader(index, id ?? $"P{index + 1}", names.Name, names.Instrument, issues);
            reader.Read(part);
            parts.Add(reader);
        }

        if (parts.Count == 0 || parts.All(part => part.Measures.Count == 0))
        {
            throw new MusicXmlImportException(MusicXmlImportFailure.Damaged, "This MusicXML file has no parts or measures, so there is nothing to import.");
        }

        return ScoreBuilder.Build(root, parts, issues);
    }

    /// <summary>
    /// The score's XML: the root file of a compressed <c>.mxl</c> archive, or the bytes as given.
    /// Lets a caller show the file as written without unpacking it itself.
    /// </summary>
    public static byte[] ReadScoreXml(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxFileBytes)
        {
            throw new MusicXmlImportException(MusicXmlImportFailure.TooLarge, "This file is larger than 50 MB, the most Motiff Lab can import.");
        }

        return MxlArchive.IsArchive(data) ? MxlArchive.ReadScore(data) : data.ToArray();
    }

    private static XElement Load(ReadOnlySpan<byte> data)
    {
        if (!LooksLikeXml(data))
        {
            throw NotMusicXml();
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        try
        {
            using var stream = new MemoryStream(data.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader).Root ?? throw NotMusicXml();
        }
        catch (XmlException e)
        {
            throw new MusicXmlImportException(
                MusicXmlImportFailure.Damaged,
                $"This MusicXML file is damaged: its XML breaks off or is malformed near line {e.LineNumber}. Try exporting it again.");
        }
    }

    /// <summary>True when the first character after any byte order mark and white space is '&lt;'.</summary>
    private static bool LooksLikeXml(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || data.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return true;
        }

        if (data.StartsWith("\uFEFF"u8))
        {
            data = data[3..];
        }

        int first = data.IndexOfAnyExcept(" \t\r\n"u8);
        return first >= 0 && data[first] == (byte)'<';
    }

    private static MusicXmlImportException NotMusicXml() => new(
        MusicXmlImportFailure.NotMusicXml,
        "This file is not a MusicXML score. Motiff Lab reads MIDI and MusicXML; most notation apps can export MusicXML.");

    private static Dictionary<string, (string? Name, string? Instrument)> ReadPartList(XElement root)
    {
        var parts = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
        foreach (XElement scorePart in root.Element("part-list")?.Elements("score-part") ?? [])
        {
            if (XmlValues.Trimmed(scorePart.Attribute("id")?.Value) is string id)
            {
                parts.TryAdd(id, (
                    XmlValues.Trimmed(scorePart.Element("part-name")?.Value),
                    XmlValues.Trimmed(scorePart.Element("score-instrument")?.Element("instrument-name")?.Value)));
            }
        }

        return parts;
    }
}