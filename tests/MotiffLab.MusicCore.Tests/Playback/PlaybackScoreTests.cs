using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Playback;

public class PlaybackScoreTests
{
    private static Note Note(string id, int pitch, long onset, long duration, int track = 0, int velocity = 80) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = velocity,
        Track = track,
        Attribution = Attribution.Imported,
    };

    private static PlaybackTempo Tempo(long at, int microsecondsPerQuarter) => new(new Tick(at), microsecondsPerQuarter);

    [Fact]
    public void Notes_are_ordered_by_onset_then_pitch_then_track()
    {
        PlaybackScore score = PlaybackScore.Create(
            [Note("a", 64, 480, 480), Note("b", 60, 0, 480, track: 1), Note("c", 60, 0, 480, track: 0), Note("d", 55, 0, 960)],
            [],
            MeterMap.Default,
            new Tick(1920));

        Assert.Equal(
            [(0L, 55, 0), (0L, 60, 0), (0L, 60, 1), (480L, 64, 0)],
            score.Notes.Select(note => (note.Span.Onset.Value, note.Pitch, note.Track)));
    }

    [Fact]
    public void Missing_tempo_is_120_bpm()
    {
        PlaybackScore score = PlaybackScore.Create([], [], MeterMap.Default, new Tick(1920));

        Assert.Equal(Tempo(0, 500_000), Assert.Single(score.Tempos));
    }

    [Fact]
    public void Tempo_map_must_start_at_zero_and_increase()
    {
        Assert.Throws<ArgumentException>(() => PlaybackScore.Create([], [Tempo(480, 500_000)], MeterMap.Default, new Tick(1920)));
        Assert.Throws<ArgumentException>(() => PlaybackScore.Create([], [Tempo(0, 500_000), Tempo(960, 400_000), Tempo(960, 300_000)], MeterMap.Default, new Tick(1920)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Tempo(0, 0));
    }

    [Fact]
    public void End_extends_to_the_last_note()
    {
        PlaybackScore score = PlaybackScore.Create([Note("a", 60, 1800, 480)], [], MeterMap.Default, new Tick(1920));

        Assert.Equal(new Tick(2280), score.End);
    }

    [Fact]
    public void Beats_follow_the_meter_with_accented_downbeats()
    {
        MeterMap meter = new([new MeterChange(Tick.Zero, new TimeSignature(3, 4)), new MeterChange(new Tick(1440), new TimeSignature(6, 8))]);

        PlaybackScore score = PlaybackScore.Create([], [], meter, new Tick(1440 * 2));

        Assert.Equal(
            [
                new PlaybackBeat(new Tick(0), true), new PlaybackBeat(new Tick(480), false), new PlaybackBeat(new Tick(960), false),
                new PlaybackBeat(new Tick(1440), true), new PlaybackBeat(new Tick(1680), false), new PlaybackBeat(new Tick(1920), false),
                new PlaybackBeat(new Tick(2160), false), new PlaybackBeat(new Tick(2400), false), new PlaybackBeat(new Tick(2640), false),
            ],
            score.Beats);
        Assert.Equal(
            [
                new PlaybackBar(1, new TickSpan(Tick.Zero, 1440), 480, 3, false),
                new PlaybackBar(2, new TickSpan(new Tick(1440), 1440), 240, 6, false),
            ],
            score.Bars);
    }

    [Fact]
    public void Pickup_beats_count_from_the_end_of_a_full_bar_and_are_not_accented()
    {
        // A dotted-quarter pickup in 4/4: beats of the full bar would fall at -1200, -720, -240 and 240,
        // so the only beat inside the pickup is at 240, and bar 1 starts at 720.
        MeterMap meter = new([new MeterChange(Tick.Zero, TimeSignature.CommonTime)], pickupDuration: 720);

        PlaybackScore score = PlaybackScore.Create([], [], meter, new Tick(720 + 1920));

        Assert.Equal(
            [240L, 720, 1200, 1680, 2160],
            score.Beats.Select(beat => beat.At.Value));
        Assert.Equal([false, true, false, false, false], score.Beats.Select(beat => beat.IsDownbeat));
        Assert.True(score.Bars[0].IsPickup);
        Assert.Equal(0, score.Bars[0].Number);
    }

    [Fact]
    public void Beats_stop_at_the_end_of_the_piece()
    {
        PlaybackScore score = PlaybackScore.Create([], [], MeterMap.Default, new Tick(1000));

        Assert.Equal([0L, 480, 960], score.Beats.Select(beat => beat.At.Value));
        Assert.Single(score.Bars);
    }

    [Fact]
    public void A_variation_shares_the_timeline()
    {
        MeterMap meter = new([new MeterChange(Tick.Zero, new TimeSignature(3, 4))], pickupDuration: 480);
        PlaybackScore original = PlaybackScore.Create([Note("a", 60, 0, 480)], [Tempo(0, 600_000), Tempo(960, 400_000)], meter, new Tick(1920));

        PlaybackScore variation = original.WithNotes([Note("v", 67, 480, 480), Note("w", 72, 0, 240)]);

        Assert.Equal(original.Tempos, variation.Tempos);
        Assert.Equal(original.Beats, variation.Beats);
        Assert.Equal(original.Bars, variation.Bars);
        Assert.Equal(original.End, variation.End);
        Assert.Equal([72, 67], variation.Notes.Select(note => note.Pitch));
    }

    [Fact]
    public void Midi_import_maps_notes_tempo_and_meter()
    {
        byte[] file = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Midi", "Fixtures", "scale-type0.mid"));
        MidiImport import = MidiImporter.Import(file);

        PlaybackScore score = PlaybackScore.FromMidi(import);

        Assert.Equal(15, score.Notes.Count);
        Assert.Equal(Tempo(0, 600_000), Assert.Single(score.Tempos));
        Assert.Equal(import.End, score.End);
        Assert.Equal(15, score.Beats.Count);
        Assert.True(score.Beats[0].IsDownbeat);
        Assert.True(score.Beats[4].IsDownbeat);
    }

    [Fact]
    public void MusicXml_import_maps_notes_tempo_and_meter()
    {
        byte[] file = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "importer-grand-staff.musicxml"));
        MusicXmlImport import = MusicXmlImporter.Import(file);

        PlaybackScore score = PlaybackScore.FromMusicXml(import);

        Assert.Equal(import.Notes.Count, score.Notes.Count);
        Assert.Equal(import.Tempos.Select(tempo => tempo.MicrosecondsPerQuarter), score.Tempos.Select(tempo => tempo.MicrosecondsPerQuarter));
        Assert.Equal(import.End, score.End);
        Assert.Equal(import.Meter.BarsBefore(score.End).Count(), score.Bars.Count);
    }

    [Fact]
    public void Building_is_deterministic()
    {
        Note[] notes = [Note("a", 60, 0, 480), Note("b", 60, 0, 480, velocity: 90), Note("c", 64, 0, 240)];

        PlaybackScore first = PlaybackScore.Create(notes, [], MeterMap.Default, new Tick(1920));
        PlaybackScore second = PlaybackScore.Create(notes.Reverse(), [], MeterMap.Default, new Tick(1920));

        Assert.Equal(first.Notes, second.Notes);
    }
}