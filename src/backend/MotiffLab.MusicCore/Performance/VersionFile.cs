using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Performance;

/// <summary>
/// Another version of the piece read from a file the player picked (decision record 0022, section 1): a MIDI file as it
/// is, or a score in MusicXML written out as MIDI through its tempo marks, so every version is kept and compared the same
/// way. Which one is "the notes" and which "the performance" is only which was opened first.
/// </summary>
public static class VersionFile
{
    /// <summary>The extensions the file picker offers: the same as for opening a piece.</summary>
    public const string AcceptedExtensions = PieceImporter.AcceptedExtensions;

    /// <summary>The file's notes as MIDI and where they come from, or why the file cannot be read. Pure and deterministic.</summary>
    public static VersionFileResult Read(string fileName, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(data);
        PieceImportResult imported = PieceImporter.Import(fileName, data);
        if (imported.Piece is not { } piece)
        {
            return new(null, PerformanceSource.Midi, imported.Rejection);
        }

        if (piece.Notes.Count == 0)
        {
            return new(null, PerformanceSource.Midi, null);
        }

        if (piece.Midi is not null)
        {
            return new(data, PerformanceSource.Midi, null);
        }

        byte[] midi = MidiExporter.Export(piece.Notes, PlaybackScore.FromPiece(piece).Tempos, piece.Meter, Path.GetFileNameWithoutExtension(fileName));
        return new(midi, PerformanceSource.Score, null);
    }
}

/// <summary>A version read from a file: its notes as MIDI and their source, or why there are none.</summary>
/// <param name="Midi">The notes; null when the file could not be read or has no notes.</param>
/// <param name="Rejection">Why the file could not be read; null when it was read, or read with no notes.</param>
public sealed record VersionFileResult(byte[]? Midi, PerformanceSource Source, ImportRejection? Rejection);