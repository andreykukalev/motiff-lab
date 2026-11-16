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

    /// <summary>Loudness in levels of 100 ms over <paramref name="milliseconds"/>: <paramref name="loud"/> everywhere but the quiet stretches given.</summary>
    private static int[] Levels(long milliseconds, int loud, params (long From, long To)[] quiet) =>
        [.. Enumerable.Range(0, (int)(milliseconds / RecordingParts.LevelMilliseconds))
            .Select(level => quiet.Any(q => level * 100L >= q.From && level * 100L < q.To) ? 0 : loud)];

    [Fact]
    public void Forty_minutes_are_heard_in_twenty_parts_each_short_enough_for_the_server()
    {
        IReadOnlyList<RecordingPart> parts = RecordingParts.Plan(2_400_000);

        Assert.Equal(20, parts.Count);
        Assert.Equal(0, parts[0].KeepFromMilliseconds);
        Assert.Equal(2_400_000, parts[^1].KeepToMilliseconds);
        Assert.All(parts.Skip(1), part => Assert.Equal(parts[part.Index - 1].KeepToMilliseconds, part.KeepFromMilliseconds));
        Assert.All(parts, part => Assert.InRange(part.SendToMilliseconds - part.SendFromMilliseconds, 1, 150_000));
    }

    [Fact]
    public void A_seam_moves_to_the_quietest_moment_within_ten_seconds_of_the_even_cut()
    {
        // Silence from 125.0 to 126.1 s; a longer silence at 135 s is too far from the even cut at 120 s.
        int[] levels = Levels(240_000, 300, (125_000, 126_100), (135_000, 138_000));
        IReadOnlyList<RecordingPart> parts = RecordingParts.Plan(240_000, levels);

        // The quiet half seconds run from the level at 125.2 s to the one at 125.8 s; the nearest the cut wins.
        Assert.Equal([125_250L], RecordingParts.Seams(parts));
        Assert.Equal(new RecordingPart(0, 0, 130_250, 0, 125_250), parts[0]);
        Assert.Equal(new RecordingPart(1, 120_250, 240_000, 125_250, 240_000), parts[1]);
        Assert.Equal(parts, RecordingParts.Plan(240_000, levels));
    }

    [Fact]
    public void Without_loudness_the_seams_are_cut_evenly_and_with_even_loudness_stay_by_the_cut()
    {
        Assert.Equal(RecordingParts.Plan(420_000), RecordingParts.Plan(420_000, []));
        Assert.Equal([105_000L, 210_000L, 315_000L], RecordingParts.Seams(RecordingParts.Plan(420_000)));

        IReadOnlyList<long> seams = RecordingParts.Seams(RecordingParts.Plan(420_000, Levels(420_000, 50)));
        Assert.Equal([104_950L, 209_950L, 314_950L], seams);
    }

    [Fact]
    public void Moved_seams_never_make_a_part_longer_than_the_server_hears()
    {
        // The first seam moves 10 s early, and the silence after the second even cut lies beyond what the part between
        // may keep then: that seam stays by its cut.
        int[] levels = Levels(360_000, 300, (100_000, 110_200), (241_000, 250_000));
        IReadOnlyList<RecordingPart> parts = RecordingParts.Plan(360_000, levels);

        Assert.Equal([110_050L, 239_950L], RecordingParts.Seams(parts));

        // Had the first seam stayed by its cut, the second would have reached that silence.
        Assert.Equal([119_950L, 241_250L], RecordingParts.Seams(RecordingParts.Plan(360_000, Levels(360_000, 300, (241_000, 250_000)))));
        Assert.All(parts, part => Assert.InRange(part.KeepToMilliseconds - part.KeepFromMilliseconds, 1, RecordingParts.MostKeptMilliseconds));
        Assert.All(parts, part => Assert.InRange(part.SendToMilliseconds - part.SendFromMilliseconds, 1, 140_000));
    }

    [Fact]
    public void A_note_held_across_a_seam_keeps_the_length_the_next_part_hears()
    {
        RecordingPart[] plan = [.. RecordingParts.Plan(240_000)];
        Assert.Equal((115_000L, 125_000L), (plan[1].SendFromMilliseconds, plan[0].SendToMilliseconds));

        // The first part's sound ends while three notes still sound: C3 from 118 s, E3 from 110 s, G3 from 119 s; and B3,
        // let go at 123 s, ends before it.
        byte[] first = Part([.. Note(118_000, 48, 7_000), .. Note(110_000, 52, 15_000), .. Note(119_000, 55, 6_000), .. Note(119_500, 59, 3_500)]);

        // The second part, from 115 s, hears C3 from its onset to 127 s, E3 from its own start to 135 s, nothing of G3,
        // and B3 to 128 s, which the first part heard end.
        byte[] second = Part([.. Note(3_000, 48, 9_000), .. Note(0, 52, 20_000), .. Note(4_500, 59, 8_500)]);

        MidiImport joined = MidiImporter.Import(RecordingParts.Join([(plan[0], first), (plan[1], second)]));

        Assert.Equal(
            [(Tick(110_000), Tick(135_000), 52), (Tick(118_000), Tick(127_000), 48), (Tick(119_000), Tick(125_000), 55), (Tick(119_500), Tick(123_000), 59)],
            joined.SourceNotes.Select(note => (note.Span.Onset.Value, note.Span.End.Value, note.Pitch)));
    }
}