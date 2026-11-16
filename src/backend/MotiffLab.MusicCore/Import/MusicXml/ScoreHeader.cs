using System.Xml;
using System.Xml.Linq;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>
/// The title and composer a MusicXML score is headed with, read from the elements before its parts: the title from
/// <c>&lt;work-title&gt;</c>, else <c>&lt;movement-title&gt;</c>, else a <c>&lt;credit&gt;</c> of type title; the composer
/// from <c>&lt;creator type="composer"&gt;</c>, else a credit of type composer. Only the header is read, so a library of
/// many pieces reads each card quickly.
/// </summary>
public sealed record ScoreHeader(string? Title, string? Composer)
{
    public static ScoreHeader None { get; } = new(null, null);

    /// <summary>The header of a MusicXML or compressed .mxl file; none for MIDI or for bytes that cannot be read as a score.</summary>
    public static ScoreHeader Read(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || data.StartsWith("MThd"u8))
        {
            return None;
        }

        try
        {
            byte[] xml = MusicXmlImporter.ReadScoreXml(data);
            using var stream = new MemoryStream(xml, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            });
            return ReadHeader(reader);
        }
        catch (Exception exception) when (exception is XmlException or MusicXmlImportException or InvalidDataException)
        {
            return None;
        }
    }

    private static ScoreHeader ReadHeader(XmlReader reader)
    {
        reader.MoveToContent();
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName is not ("score-partwise" or "score-timewise"))
        {
            return None;
        }

        // Each text found, by where it was found: "work-title", "creator:composer", "credit:title"; the first of each is kept.
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        reader.Read();
        while (!reader.EOF && !(reader.NodeType == XmlNodeType.Element && reader.LocalName is "part-list" or "part" or "measure"))
        {
            if (reader.NodeType != XmlNodeType.Element || !Take(reader, found))
            {
                reader.Read();
            }
        }

        return new(First(found, "work-title", "movement-title", "credit:title"), First(found, "creator:composer", "credit:composer"));
    }

    /// <summary>Reads the element the reader is on when it is one of the header's texts, past its end; false for any other.</summary>
    private static bool Take(XmlReader reader, Dictionary<string, string> found)
    {
        (string? key, string? text) = reader.LocalName switch
        {
            "work-title" or "movement-title" => (reader.LocalName, reader.ReadElementContentAsString()),
            "creator" => ("creator:" + reader.GetAttribute("type"), reader.ReadElementContentAsString()),
            "credit" => Credit((XElement)XNode.ReadFrom(reader)),
            _ => (null, null),
        };
        if (key is null)
        {
            return false;
        }

        if (PieceMetadata.Clean(text) is { } cleaned)
        {
            found.TryAdd(key, cleaned);
        }

        return true;
    }

    /// <summary>A credit's type and its words, joined: ("credit:title", "Für Elise").</summary>
    private static (string Key, string Text) Credit(XElement credit)
    {
        string? type = credit.Elements().FirstOrDefault(element => element.Name.LocalName == "credit-type")?.Value.Trim();
        return ("credit:" + type, string.Join(' ', credit.Elements().Where(element => element.Name.LocalName == "credit-words").Select(element => element.Value)));
    }

    private static string? First(Dictionary<string, string> found, params string[] keys) =>
        keys.Select(key => found.GetValueOrDefault(key)).FirstOrDefault(text => text is not null);
}