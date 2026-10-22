using MotiffLab.MusicCore.Notation;

namespace MotiffLab.Web.Score;

/// <summary>A document for the score view to engrave (S1-W7, decision record 0008).</summary>
/// <param name="Data">MusicXML or MEI text.</param>
/// <param name="Generated">The generated notation the MEI came from; null when the source is shown as written.</param>
public sealed record ScoreSource(string Data, GeneratedNotation? Generated)
{
    /// <summary>A MusicXML file as written.</summary>
    public static ScoreSource AsWritten(string musicXml) => new(musicXml, null);

    /// <summary>Notation generated from the notes.</summary>
    public static ScoreSource FromNotes(GeneratedNotation notation) => new(notation.Mei, notation);
}