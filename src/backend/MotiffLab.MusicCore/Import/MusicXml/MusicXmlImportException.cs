namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>A MusicXML file that cannot be imported at all. The message is a plain reason for the player (N2).</summary>
public sealed class MusicXmlImportException(MusicXmlImportFailure failure, string message) : Exception(message)
{
    public MusicXmlImportFailure Failure { get; } = failure;
}

public enum MusicXmlImportFailure
{
    /// <summary>The bytes are not a MusicXML score.</summary>
    NotMusicXml,

    /// <summary>A MusicXML file of a kind Stage 1 does not read yet (timewise, decimal durations).</summary>
    Unsupported,

    /// <summary>The XML is broken, or the score is too damaged to place its notes in time.</summary>
    Damaged,

    /// <summary>The file is larger than the Stage 1 limit (S1-I1).</summary>
    TooLarge,
}