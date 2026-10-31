using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Tests.Import.Midi;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.Transcription;

public class RecordingPartsTests
{
    /// <summary>
    /// A part's MIDI as an engine writes it, at 480 ticks a quarter and 480,000 microseconds a quarter, so a
    /// tick is a millisecond of the part. Events are (millisecond, bytes).
    /// </summary>
    private static byte[] Part(params (int At, byte[] Data)[] events) =>
        new SmfBuilder(1, 480)
            .Track(t => t.Meta(0, 0x51, 0x07, 0x53, 0x00))
            .Track(t =>
            {
                int last = 0;
                foreach ((int at, byte[] data) in events.OrderBy(e => e.At))
                {
                    t.Raw(at - last, data);
                    last = at;
                }
            })
            .Build();

    private static (int, byte[])[] Note(int at, int pitch, int length = 500) => [(at, [0x90, (byte)pitch, 80]), (at + length, [0x80, (byte)pitch, 64])];

    /// <summary>Where a millisecond of the recording lands in the joined file, at 120 BPM.</summary>
    private static long Tick(long milliseconds) => ((milliseconds * 1000 * 480) + 250_000) / 500_000;

    [Fact]
    public void Up_to_two_minutes_is_heard_in_one_part()
    {
        Assert.Equal(new RecordingPart(0, 0, 100_000, 0, 100_000), Assert.Single(RecordingParts.Plan(100_000)));
        Assert.Single(RecordingParts.Plan(120_000));
    }

    [Fact]
    public void Seven_minutes_are_heard_in_four_overlapping_parts_the_server_can_take()
    {
        IReadOnlyList<RecordingPart> parts = RecordingParts.Plan(420_000);

        Assert.Equal(
            [(0L, 105_000L), (105_000L, 210_000L), (210_000L, 315_000L), (315_000L, 420_000L)],
            parts.Select(part => (part.KeepFromMilliseconds, part.KeepToMilliseconds)));
        Assert.Equal(
            [(0L, 110_000L), (100_000L, 215_000L), (205_000L, 320_000L), (310_000L, 420_000L)],
            parts.Select(part => (part.SendFromMilliseconds, part.SendToMilliseconds)));
        Assert.All(parts, part => Assert.InRange(part.SendToMilliseconds - part.SendFromMilliseconds, 1, 150_000));
    }

    [Fact]
    public void Parts_join_on_the_recordings_clock_keeping_each_note_once_and_the_pedal_as_one_press()
    {
        RecordingPart[] plan = [.. RecordingParts.Plan(240_000)];
        Assert.Equal(115_000, plan[1].SendFromMilliseconds);

        // The first part hears a note just before the seam, one after it in the overlap, and the pedal pressed to its end.
        byte[] first = Part([.. Note(1_000, 60), .. Note(119_990, 64), .. Note(122_000, 67), (119_000, [0xB0, 64, 127]), (125_000, [0xFF, 0x01, 0x00])]);

        // The second part, from 115 s: a note in its lead-in, the seam note heard again 20 ms later, and the pedal let go.
        byte[] second = Part([.. Note(2_000, 62), .. Note(5_010, 64), .. Note(7_000, 67), .. Note(124_000, 72), (0, [0xB0, 64, 127]), (6_000, [0xB0, 64, 0])]);

        MidiImport joined = MidiImporter.Import(RecordingParts.Join([(plan[1], second), (plan[0], first)]));

        Assert.Equal(
            [(Tick(1_000), 60), (Tick(119_990), 64), (Tick(122_000), 67), (Tick(239_000), 72)],
            joined.SourceNotes.Select(note => (note.Span.Onset.Value, note.Pitch)));
        Assert.Equal(Tick(1_500), joined.SourceNotes[0].Span.End.Value);
        Assert.Equal(TickSpan.FromBounds(new Tick(Tick(119_000)), new Tick(Tick(121_000))), Assert.Single(joined.SustainPedal).Span);
        Assert.Equal(500_000, Assert.Single(joined.Tempos).MicrosecondsPerQuarter);
    }

    [Fact]
    public void Joining_is_deterministic()
    {
        RecordingPart[] plan = [.. RecordingParts.Plan(240_000)];
        byte[] first = Part(Note(1_000, 60));
        byte[] second = Part(Note(10_000, 62));

        Assert.Equal(RecordingParts.Join([(plan[0], first), (plan[1], second)]), RecordingParts.Join([(plan[1], second), (plan[0], first)]));
    }
}