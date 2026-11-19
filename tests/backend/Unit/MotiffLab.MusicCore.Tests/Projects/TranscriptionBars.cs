using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests;

/// <summary>
/// The bars the engine finds in a transcription's notes, for the tests to compare with what the facade kept (the core's
/// <c>LiveBars</c>, which no app called, went in W1.11).
/// </summary>
internal static class TranscriptionBars
{
    /// <summary>
    /// The bars that fit the transcription's notes best (decision record 0016), in the signature and with a beat of
    /// as many 16ths as asked when given; null when the notes follow no beat.
    /// </summary>
    /// <exception cref="InvalidOperationException">The project is not a transcription.</exception>
    public static BarGrid? FindBars(this Project project, TimeSignature? signature = null, int? stepsPerBeat = null, GridSearch? search = null)
    {
        if (project.Transcription is null)
        {
            throw new InvalidOperationException("Only a transcription has bars worked out from its notes.");
        }

        MidiImport heard = PieceImporter.Import(project.FileName, project.Source).Piece?.Midi
            ?? throw new ProjectFileException("This project says it was transcribed from a recording, but its piece is not MIDI.");
        return RecordingBars.Find(heard, signature, stepsPerBeat, search);
    }
}