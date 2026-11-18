using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Pieces;

/// <summary>A slur in the source, from the onset of its first note to the onset of its last.</summary>
/// <param name="Track">The track of the notes the slur is drawn over.</param>
public sealed record SlurMark(int Track, Tick Start, Tick Stop);

/// <summary>The phrase marks a source states. MIDI has none; MusicXML has slurs and repeat signs.</summary>
public sealed record PhraseMarks(IReadOnlyList<SlurMark> Slurs, IReadOnlyList<Tick> RepeatSigns)
{
    public static PhraseMarks None { get; } = new([], []);
}