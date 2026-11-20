using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// A piece whose only original is a recording (version 1.1, finding 29; project file version 17): added on the device as
/// it is, its sound kept beside the project file (<see cref="OriginalKept.Device"/>), with no notes until the player
/// recognises it (<see cref="NotesOperations.HeardFrom"/>).
/// </summary>
public sealed partial record Project
{
    /// <summary>Whether the piece keeps a recording among its live originals: the sound of a recording added or of a version.</summary>
    public bool KeepsRecording => Originals.Any(static original => original.Kind == OriginalKind.Audio);

    /// <summary>
    /// Whether the piece is only its recording (version 17): no notes, no PDF here or described, and a live recording, so
    /// it opens without notes, as a PDF piece does, until the notes heard in it are kept.
    /// </summary>
    public bool IsOnlyRecording => !HasNotes && Document is null && !DescribesDocument && KeepsRecording;

    /// <summary>
    /// A new piece for a recording the player added as it is (finding 29; Andrei, 2026-11-19: free, on the device): no
    /// notes, its original described as kept on the device by <paramref name="device"/>, with its length, fingerprint and
    /// size when known. The piece is named as the notes heard in it will be (<c>Etude.mid</c> for <c>Etude.m4a</c>), so
    /// its title is the recording's name without its extension and stays so once they are kept.
    /// </summary>
    /// <param name="fileName">The recording's file name as the player picked it.</param>
    /// <exception cref="ArgumentException">No file name, or a fingerprint that is not 64 lower-case hex digits.</exception>
    public static Project CreateRecording(string id, string fileName, long? durationMs, string? sha256, long? bytes, HoldingDevice device, DateTimeOffset now)
    {
        var original = new PieceOriginal(PieceOriginal.RecordingId, OriginalKind.Audio, fileName, OriginalKept.Device, now)
        {
            DurationMs = durationMs,
            Sha256 = sha256,
            Bytes = bytes,
        };
        return new Project(id, Path.ChangeExtension(fileName, ".mid"), [], now, now, [], []) { KeptOriginals = [original.HeldBy(device, now)] };
    }
}