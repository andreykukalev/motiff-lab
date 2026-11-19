namespace MotiffLab.MusicCore.Pieces;

/// <summary>What an original is (version 0.3, local-first originals).</summary>
public enum OriginalKind
{
    Pdf,

    /// <summary>Photos of pages.</summary>
    Images,

    Audio,
    Midi,
    MusicXml,
}

/// <summary>What kind of original a file is.</summary>
public static class OriginalKinds
{
    /// <summary>MIDI or MusicXML by the file's extension, as the importers take it; null for anything else.</summary>
    public static OriginalKind? FileKind(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".mid" or ".midi" => OriginalKind.Midi,
            ".musicxml" or ".mxl" or ".xml" => OriginalKind.MusicXml,
            _ => null,
        };
    }
}