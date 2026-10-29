using MotiffLab.MusicCore.Import.Transcription;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// Marks a project whose source was heard in an audio or video recording by automatic transcription
/// (decision record 0014): which engine heard it. The recording itself is never kept, only the MIDI
/// the engine wrote. On every open the notes are marked by <see cref="Transcribed"/>, so the marks need
/// not be stored.
/// </summary>
/// <param name="Engine">The engine and its version as it reported them, for example "basic-pitch 0.4.0".</param>
public sealed record TranscriptionOrigin(string Engine)
{
    public string Engine { get; } = !string.IsNullOrWhiteSpace(Engine) ? Engine : throw new ArgumentException("The engine is required.", nameof(Engine));
}