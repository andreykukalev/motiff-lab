using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>A note of the Reference's music: the key it sounds and the name it is written with (B♯3 for C4 in C♯ major).</summary>
public readonly record struct WrittenTone(int Midi, SpelledName Name)
{
    /// <summary>The octave the name is written in.</summary>
    public int Octave => Name.OctaveOf(Midi);
}

/// <summary>A key the Reference's keyboard marks, with the note's name on it.</summary>
public sealed record MarkedKey(int Midi, string Name);

/// <summary>
/// Music the Reference writes and plays (version 0.5, «Справочник»): a chord as it sounds, a scale, a progression of the
/// harmony explorer. The notes start at tick 0, a bar of 4/4 at a time; the MEI engraves them with each note spelled as
/// theory spells it, its <c>xml:id</c> the note's id. Everything is inferred: the app wrote it, no piece holds it.
/// </summary>
/// <param name="Notes">The notes from tick 0, ids "ref-0", "ref-1", … as in the MEI.</param>
/// <param name="End">Where the last bar ends: a loop of the music is from 0 to here.</param>
/// <param name="MicrosecondsPerQuarter">How fast it is meant to sound.</param>
/// <param name="Mei">The music as an MEI 5 document, in 4/4, on a treble staff or a piano's two.</param>
/// <param name="KeyboardLow">The C at or below the lowest key marked, where a keyboard that shows them starts.</param>
/// <param name="KeyboardHigh">The B at or above the highest key marked, where it ends.</param>
/// <param name="Keys">The keys a keyboard marks, each with its note's name; empty when the music shows none.</param>
public sealed record ReferenceMusic(
    IReadOnlyList<Note> Notes,
    Tick End,
    int MicrosecondsPerQuarter,
    string Mei,
    int KeyboardLow,
    int KeyboardHigh,
    IReadOnlyList<MarkedKey> Keys)
{
    /// <summary>The music as a playback score of its own at its tempo, for a screen with no piece open (the library).</summary>
    public PlaybackScore Playback() =>
        PlaybackScore.Create(Notes, [new PlaybackTempo(Tick.Zero, MicrosecondsPerQuarter)], MeterMap.Default, End);

    /// <summary>
    /// The music placed on a piece's timeline at <paramref name="at"/>, where the piece goes at
    /// <paramref name="timelineMicrosecondsPerQuarter"/>: each time stretched by the music's tempo over the piece's, in whole
    /// ticks, so it sounds at its own tempo over the piece's. Where its first bar starts and its last ends come with it.
    /// </summary>
    public PlacedMusic Place(Tick at, int timelineMicrosecondsPerQuarter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timelineMicrosecondsPerQuarter, 1);
        long Stretch(long ticks) => ((ticks * MicrosecondsPerQuarter) + (timelineMicrosecondsPerQuarter / 2)) / timelineMicrosecondsPerQuarter;
        Note[] notes =
        [
            .. Notes.Select(note =>
            {
                long onset = Stretch(note.Span.Onset.Value);
                return note with { Span = new TickSpan(new Tick(at.Value + onset), Math.Max(1, Stretch(note.Span.End.Value) - onset)) };
            }),
        ];
        return new PlacedMusic(notes, new TickSpan(at, Math.Max(1, Stretch(End.Value))));
    }

    /// <summary>The ticks the music lasts on a timeline at <paramref name="timelineMicrosecondsPerQuarter"/>.</summary>
    public long LengthOn(int timelineMicrosecondsPerQuarter) => Place(Tick.Zero, timelineMicrosecondsPerQuarter).Span.Duration;
}

/// <summary>The Reference's music on a piece's timeline: its notes there and the span of its bars.</summary>
public sealed record PlacedMusic(IReadOnlyList<Note> Notes, TickSpan Span);