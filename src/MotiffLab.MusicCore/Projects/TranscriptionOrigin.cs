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

    /// <summary>
    /// Where a recording heard in parts was joined (<see cref="RecordingParts.Seams"/>), in milliseconds from the start
    /// of the stretch sent, in order; empty for one part. A note there may be missing or doubled, so the Check layer
    /// flags those bars (<see cref="CheckFlag.FromTranscription"/>).
    /// </summary>
    public IReadOnlyList<long> Seams
    {
        get;
        init => field = value is null || value.Any(seam => seam <= 0) || !value.Zip(value.Skip(1)).All(pair => pair.First < pair.Second)
            ? throw new ArgumentException("Seams are positive times in order.", nameof(Seams))
            : [.. value];
    } = [];

    public bool Equals(TranscriptionOrigin? other) =>
        other is not null && Engine == other.Engine && Equals(Bars, other.Bars) && Seams.SequenceEqual(other.Seams);

    public override int GetHashCode() => HashCode.Combine(Engine, Bars, Seams.Count);
}