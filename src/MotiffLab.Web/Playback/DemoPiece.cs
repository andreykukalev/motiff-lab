using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.Web.Playback;

/// <summary>
/// A short self-made piece for trying playback without a file: two hands in 3/4 at 96 BPM with a
/// one-beat pickup, so the count-in, downbeat accents and bar loops all have something to show.
/// It is not published music.
/// </summary>
internal static class DemoPiece
{
    private const long Beat = CanonicalTime.TicksPerQuarter;
    private const long Bar = 3 * Beat;

    // Right hand: (beat from the start of bar 1, length in beats, pitch). The pickup is at -1.
    private static readonly (double Beat, double Length, int Pitch)[] s_melody =
    [
        (-1, 1, 67),
        (0, 1.5, 72), (1.5, 0.5, 71), (2, 1, 69),
        (3, 2, 67), (5, 1, 64),
        (6, 1, 65), (7, 1, 69), (8, 1, 72),
        (9, 3, 71),
        (12, 1.5, 72), (13.5, 0.5, 74), (14, 1, 76),
        (15, 1, 77), (16, 1, 74), (17, 1, 71),
        (18, 1, 72), (19, 1, 67), (20, 1, 64),
        (21, 3, 60),
    ];

    // Left hand: one chord root per bar, as bass on beat 1 and a chord on beats 2 and 3.
    private static readonly int[][] s_chords =
    [
        [48, 64, 67], [48, 64, 67], [53, 65, 69], [55, 62, 67],
        [45, 64, 69], [50, 65, 69], [55, 62, 65], [48, 64, 67],
    ];

    public static (PlaybackScore Score, MeterMap Meter, IReadOnlyList<Note> Notes) Create()
    {
        var notes = new List<(long Onset, long Length, int Pitch, int Velocity, int Track)>();
        foreach ((double beat, double length, int pitch) in s_melody)
        {
            notes.Add((Beat + (long)(beat * Beat), (long)(length * Beat), pitch, 84, 0));
        }

        for (int bar = 0; bar < s_chords.Length; bar++)
        {
            long start = Beat + (bar * Bar);
            int[] chord = s_chords[bar];
            notes.Add((start, Beat, chord[0], 70, 1));
            foreach (int pitch in chord[1..])
            {
                notes.Add((start + Beat, Beat - 30, pitch, 56, 1));
                notes.Add((start + (2 * Beat), Beat - 30, pitch, 52, 1));
            }
        }

        Note[] shared = notes
            .OrderBy(note => note.Onset)
            .ThenBy(note => note.Pitch)
            .Select((note, index) => new Note
            {
                Id = $"n_{index + 1:D4}",
                Pitch = new Pitch(note.Pitch),
                Span = new TickSpan(new Tick(note.Onset), note.Length),
                Velocity = note.Velocity,
                Track = note.Track,
                Attribution = Attribution.Imported,
            })
            .ToArray();

        var meter = new MeterMap([new MeterChange(Tick.Zero, new TimeSignature(3, 4))], pickupDuration: Beat);
        var end = new Tick(Beat + (s_chords.Length * Bar));
        PlaybackScore score = PlaybackScore.Create(shared, [new PlaybackTempo(Tick.Zero, 625_000)], meter, end);
        return (score, meter, shared);
    }
}