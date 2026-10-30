using MotiffLab.MusicCore.Import.Transcription;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// Marks a project whose source was heard in an audio or video recording by automatic transcription
/// (decision record 0014): which engine heard it. The recording itself is never kept, only the MIDI
/// the engine wrote. On every open the notes are marked by <see cref="Transcribed"/>, so the marks need
/// not be stored.
/// </summary>
/// <param name="Engine">The engine and its version as it reported them, for example "transkun 2.0.1".</param>
/// <param name="Bars">
/// The bars and tempo worked out from the notes when the piece was opened (decision record 0016),
/// applied to the engine's MIDI on every open; null keeps the engine's 120 BPM and 4/4. The grid is
/// stored rather than worked out again, so the notes and their corrections stay where they were.
/// </param>
public sealed record TranscriptionOrigin(string Engine, BarGrid? Bars = null)
{
    public string Engine { get; } = !string.IsNullOrWhiteSpace(Engine) ? Engine : throw new ArgumentException("The engine is required.", nameof(Engine));
}