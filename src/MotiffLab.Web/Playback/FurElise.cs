using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.Web.Playback;

/// <summary>
/// The opening of Beethoven's Für Elise (WoO 59), the pickup and bars 1–8, for trying the lab
/// without a file: the pilot piece (docs/07-product-definition.md). 3/8 in A minor with a 16th
/// pickup, the right hand on track 0 and the left hand on track 1, each note marked with its hand. The composition is in the
/// public domain and these notes are written out here, not taken from an edition or a recording.
/// </summary>
internal static class FurElise
{
    public const string Title = "Für Elise (Beethoven, opening)";

    private const long Sixteenth = CanonicalTime.TicksPerQuarter / 4;

    /// <summary>72 quarters a minute: a dotted quarter, one bar, every 1.25 seconds.</summary>
    private const int MicrosecondsPerQuarter = 833_333;

    private const int RightVelocity = 72;
    private const int LeftVelocity = 56;

    public static KeySignature Key { get; } = new(Tick.Zero, 0, KeyMode.Minor);

    public static (PlaybackScore Score, MeterMap Meter, IReadOnlyList<Note> Notes) Create()
    {
        var notes = new List<(long Onset, long Length, int Pitch, int Velocity, int Track)>();
        long at = 0;

        void Right(params int[] pitches)
        {
            foreach (int pitch in pitches)
            {
                notes.Add((at, Sixteenth, pitch, RightVelocity, 0));
                at += Sixteenth;
            }
        }

        // An 8th in the right hand over a rising left-hand arpeggio in 16ths, then the right hand's 16ths.
        void Arpeggio(int melody, int[] left, params int[] right)
        {
            notes.Add((at, 2 * Sixteenth, melody, RightVelocity, 0));
            for (int i = 0; i < left.Length; i++)
            {
                notes.Add((at + (i * Sixteenth), Sixteenth, left[i], LeftVelocity, 1));
            }

            at += 3 * Sixteenth;
            Right(right);
        }

        int[] a = [45, 52, 57];
        int[] e = [40, 52, 56];

        Right(76, 75);
        for (int half = 0; half < 2; half++)
        {
            Right(76, 75, 76, 71, 74, 72);
            Arpeggio(69, a, 60, 64, 69);
            if (half == 0)
            {
                Arpeggio(71, e, 64, 68, 71);
                Arpeggio(72, a, 64, 76, 75);
            }
            else
            {
                Arpeggio(71, e, 64, 72, 71);
            }
        }

        // Bar 8: the A held through the bar over the arpeggio.
        notes.Add((at, 6 * Sixteenth, 69, RightVelocity, 0));
        for (int i = 0; i < a.Length; i++)
        {
            notes.Add((at + (i * Sixteenth), (6 - i) * Sixteenth, a[i], LeftVelocity, 1));
        }

        at += 6 * Sixteenth;

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
                Hand = new Attributed<Hand>(note.Track == 0 ? Hand.Right : Hand.Left, Attribution.Imported),
                Attribution = Attribution.Imported,
            })
            .ToArray();

        var meter = new MeterMap([new MeterChange(Tick.Zero, new TimeSignature(3, 8))], pickupDuration: 2 * Sixteenth);
        PlaybackScore score = PlaybackScore.Create(shared, [new PlaybackTempo(Tick.Zero, MicrosecondsPerQuarter)], meter, new Tick(at));
        return (score, meter, shared);
    }
}