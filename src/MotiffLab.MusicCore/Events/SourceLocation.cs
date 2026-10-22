namespace MotiffLab.MusicCore.Events;

/// <summary>
/// Where an imported event sits in its source file, so "Show in piece", corrections and exports
/// can find it again. The file itself (hash, format, name) is recorded once per project, not per event.
/// </summary>
public abstract record SourceLocation;

/// <summary>A note in a Standard MIDI File.</summary>
/// <param name="Track">Zero-based track chunk index.</param>
/// <param name="Channel">MIDI channel, 0–15 as stored in the file (channel 10 is 9).</param>
/// <param name="NoteOnIndex">Zero-based index of the note-on event among all events in its track chunk.</param>
public sealed record MidiSourceLocation(int Track, int Channel, int NoteOnIndex) : SourceLocation
{
    public int Track { get; init; } = Track >= 0 ? Track : throw new ArgumentOutOfRangeException(nameof(Track));

    public int Channel { get; init; } = Channel is >= 0 and <= 15 ? Channel : throw new ArgumentOutOfRangeException(nameof(Channel));

    public int NoteOnIndex { get; init; } = NoteOnIndex >= 0 ? NoteOnIndex : throw new ArgumentOutOfRangeException(nameof(NoteOnIndex));
}

/// <summary>A note in a MusicXML part. For tied notes merged into one, this is the first note of the tie.</summary>
/// <param name="PartId">The part's <c>id</c> attribute, for example "P1".</param>
/// <param name="MeasureIndex">Zero-based position of the <c>&lt;measure&gt;</c> in the part (not its <c>number</c> label, which can repeat or be non-numeric).</param>
/// <param name="NoteIndex">Zero-based index of the <c>&lt;note&gt;</c> element within the measure.</param>
/// <param name="Staff">The <c>&lt;staff&gt;</c> number, 1 when the part has one staff.</param>
/// <param name="Voice">The <c>&lt;voice&gt;</c> value as written, for example "1".</param>
public sealed record MusicXmlSourceLocation(string PartId, int MeasureIndex, int NoteIndex, int Staff, string Voice) : SourceLocation
{
    public string PartId { get; init; } = !string.IsNullOrWhiteSpace(PartId) ? PartId : throw new ArgumentException("A part id is required.", nameof(PartId));

    public int MeasureIndex { get; init; } = MeasureIndex >= 0 ? MeasureIndex : throw new ArgumentOutOfRangeException(nameof(MeasureIndex));

    public int NoteIndex { get; init; } = NoteIndex >= 0 ? NoteIndex : throw new ArgumentOutOfRangeException(nameof(NoteIndex));

    public int Staff { get; init; } = Staff >= 1 ? Staff : throw new ArgumentOutOfRangeException(nameof(Staff));

    public string Voice { get; init; } = Voice ?? throw new ArgumentNullException(nameof(Voice));
}