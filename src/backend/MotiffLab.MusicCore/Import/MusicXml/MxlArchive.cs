using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>
/// Reads the score out of a compressed MusicXML (<c>.mxl</c>) archive (S1-I1): the root file that
/// <c>META-INF/container.xml</c> names, or, when the container is missing, the only MusicXML file
/// outside <c>META-INF</c>.
/// </summary>
internal static class MxlArchive
{
    /// <summary>The most an archive may unpack to: a score far larger than any piece, short of what a browser can hold.</summary>
    public const int MaxScoreBytes = 200 * 1024 * 1024;

    private const string MusicXmlMediaType = "application/vnd.recordare.musicxml+xml";

    public static bool IsArchive(ReadOnlySpan<byte> data) => data.StartsWith("PK\u0003\u0004"u8);

    public static byte[] ReadScore(ReadOnlySpan<byte> data)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(data.ToArray(), writable: false), ZipArchiveMode.Read);
            ZipArchiveEntry entry = FindRootFile(archive) ?? throw Damaged("it holds no score");
            return ReadEntry(entry);
        }
        catch (InvalidDataException)
        {
            throw Damaged("it is not a readable zip archive");
        }
    }

    private static ZipArchiveEntry? FindRootFile(ZipArchive archive)
    {
        if (archive.GetEntry("META-INF/container.xml") is ZipArchiveEntry container)
        {
            XElement root;
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                using var reader = XmlReader.Create(new MemoryStream(ReadEntry(container), writable: false), settings);
                root = XDocument.Load(reader).Root ?? throw Damaged("its container.xml is empty");
            }
            catch (XmlException)
            {
                throw Damaged("its container.xml is malformed");
            }

            // The first root file is the score; later ones are alternative renditions such as PDFs.
            var rootFiles = root.Descendants().Where(element => element.Name.LocalName == "rootfile").ToList();
            XElement? rootFile = rootFiles.FirstOrDefault(file => (file.Attribute("media-type")?.Value.Trim() ?? MusicXmlMediaType) == MusicXmlMediaType);
            string? path = rootFile?.Attribute("full-path")?.Value.Trim();
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            return archive.GetEntry(path) ?? throw Damaged($"the score it names ({path}) is missing");
        }

        var scores = archive.Entries
            .Where(entry => !entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)
                && (entry.FullName.EndsWith(".musicxml", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return scores.Count == 1 ? scores[0] : null;
    }

    /// <summary>Reads an entry, stopping at <see cref="MaxScoreBytes"/> whatever size the archive declares.</summary>
    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > MaxScoreBytes)
            {
                throw new MusicXmlImportException(
                    MusicXmlImportFailure.TooLarge,
                    "This compressed MusicXML file unpacks to more than 50 MB, the most Motiff Lab can import.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static MusicXmlImportException Damaged(string reason) => new(
        MusicXmlImportFailure.Damaged,
        $"This compressed MusicXML file (.mxl) is damaged: {reason}. Try exporting it again, or export uncompressed MusicXML (.musicxml).");
}