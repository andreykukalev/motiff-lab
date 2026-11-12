using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.Transcription;

/// <summary>Notes played as a recording hears them, written as an engine writes them: at 120 BPM in 4/4.</summary>
internal static class LivePlaying
{
    /// <summary>Notes in 16ths (16th, length in 16ths, pitch) played at a steady 16th from <paramref name="start"/>, in microseconds: (onset, length, pitch).</summary>
    public static IEnumerable<(long Onset, long Length, int Pitch)> At(long start, long sixteenthMicroseconds, IEnumerable<(int Step, int Length, int Pitch)> notes) =>
        notes.Select(note => (start + (note.Step * sixteenthMicroseconds), note.Length * sixteenthMicroseconds, note.Pitch));

    /// <summary>The notes, each onset off by up to 12 ms from a fixed generator, as the engine's MIDI.</summary>
    public static byte[] Heard(IEnumerable<(long Onset, long Length, int Pitch)> notes)
    {
        uint seed = 12345;
        long Jitter()
        {
            seed = (seed * 1_103_515_245) + 12_345;
            return (long)((seed >> 16) % 25) - 12;
        }

        // At 120 BPM a tick lasts 500000 / 480 microseconds.
        static long Ticks(long microseconds) => ((microseconds * 480) + 250_000) / 500_000;
        Note[] played = [.. notes.Select((note, i) =>
        {
            long onset = note.Onset + (Jitter() * 1_000);
            return new Note
            {
                Id = $"n_{i + 1:D4}",
                Pitch = new Pitch(note.Pitch),
                Span = TickSpan.FromBounds(new Tick(Ticks(onset)), new Tick(Ticks(onset + note.Length - 20_000))),
                Velocity = 70,
                Track = 0,
                Attribution = Attribution.Imported,
            };
        })];
        return MidiExporter.Export(played, [], MeterMap.Default, "take");
    }

    /// <summary>The length of <paramref name="notes"/> in 16ths: where the last one ends.</summary>
    public static int Length(IEnumerable<(int Step, int Length, int Pitch)> notes) => notes.Max(note => note.Step + note.Length);
}