using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.Transcription;

/// <summary>Bars and a tempo worked out from the onsets of a transcription (decision record 0016).</summary>
public class BarGridTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 30, 22, 0, 0, TimeSpan.Zero);

    /// <summary>Transkun 2.0.1's MIDI for Für Elise, synthesised from a public-domain MIDI file: 904 notes in 130 s.</summary>
    private static byte[] FurElise() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Transcription", "Fixtures", "transkun-fur-elise.mid"));

    private static ImportedPiece Import(byte[] midi) => PieceImporter.Import("take.mid", midi).Piece!;

    /// <summary>
    /// Notes played to a steady beat, written as an engine writes them: at 120 BPM in 4/4, one second
    /// in, each onset off by up to 12 ms from a fixed generator. Each note is (16th, length in 16ths, pitch).
    /// </summary>
    private static byte[] Played(int sixteenthMicroseconds, IEnumerable<(int Step, int Length, int Pitch)> notes)
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
            long onset = 1_000_000 + ((long)note.Step * sixteenthMicroseconds) + (Jitter() * 1_000);
            long end = onset + ((long)note.Length * sixteenthMicroseconds) - 20_000;
            return new Note
            {
                Id = $"n_{i + 1:D4}",
                Pitch = new Pitch(note.Pitch),
                Span = TickSpan.FromBounds(new Tick(Ticks(onset)), new Tick(Ticks(end))),
                Velocity = 70,
                Track = 0,
                Attribution = Attribution.Imported,
            };
        })];
        return MidiExporter.Export(played, [], MeterMap.Default, "take");
    }

    /// <summary>Sixteen bars of 3/4 after a quarter's pickup: a low bass on each downbeat, chords on beats 2 and 3, a melody in eighths.</summary>
    private static IEnumerable<(int, int, int)> Waltz()
    {
        yield return (0, 2, 72);
        yield return (2, 2, 74);
        int[] melody = [76, 77, 79, 77, 76, 74];
        for (int bar = 0; bar < 16; bar++)
        {
            int start = 4 + (bar * 12);
            yield return (start, 4, 48);
            yield return (start + 4, 3, 64);
            yield return (start + 4, 3, 67);
            yield return (start + 8, 3, 64);
            yield return (start + 8, 3, 67);
            for (int i = 0; i < 6; i++)
            {
                yield return (start + (2 * i), 2, melody[i]);
            }
        }
    }

    /// <summary>Twelve bars of 4/4: C2 on the downbeat, G2 on beat 3, a melody in eighths.</summary>
    private static IEnumerable<(int, int, int)> March()
    {
        for (int bar = 0; bar < 12; bar++)
        {
            int start = bar * 16;
            yield return (start, 8, 36);
            yield return (start + 8, 8, 43);
            for (int i = 0; i < 8; i++)
            {
                yield return (start + (2 * i), 2, 72 + (i % 5));
            }
        }
    }

    /// <summary>
    /// Twenty bars of 4/4 played freely, written as an engine writes them: the quarter slows from 0.7
    /// to 0.85 s and back every two bars. A bass note on every beat, the lowest on beat 1, and a melody
    /// in 16ths and eighths.
    /// </summary>
    private static byte[] Rubato()
    {
        static long Beat(int k) => 700_000 + (150_000 * (k % 8 < 4 ? k % 8 : 8 - (k % 8)) / 4);
        static long Ticks(long microseconds) => ((microseconds * 480) + 250_000) / 500_000;
        var notes = new List<Note>();
        long at = 1_000_000;
        for (int k = 0; k < 80; k++)
        {
            long beat = Beat(k);
            void Add(long from, long length, int pitch) => notes.Add(new Note
            {
                Id = $"n_{notes.Count + 1:D4}",
                Pitch = new Pitch(pitch),
                Span = TickSpan.FromBounds(new Tick(Ticks(from)), new Tick(Ticks(from + length - 20_000))),
                Velocity = 60,
                Track = 0,
                Attribution = Attribution.Imported,
            });
            Add(at, beat, k % 4 == 0 ? 36 : 43);
            Add(at, beat / 4, 72 + (k % 5));
            Add(at + (beat / 4), beat / 4, 74);
            Add(at + (beat / 2), beat / 2, 76);
            at += beat;
        }

        return MidiExporter.Export(notes, [], MeterMap.Default, "take");
    }

    [Fact]
    public void A_free_tempo_follows_the_bass_and_keeps_the_bars()
    {
        BarGrid grid = BarGrid.Find(Import(Rubato()).Midi!)!;

        Assert.True(grid.FollowsPlaying);
        Assert.Equal(TimeSignature.CommonTime, grid.Signature);
        // The bass falls on beats 1 and 3 when it is the lowest around: beats of a half note, eight 16ths.
        Assert.Equal(8, grid.StepsPerBeat);
        Assert.Equal([0], grid.BarStarts);
        Assert.InRange(grid.QuarterNotesPerMinute, 70, 86);

        // Every bass note on beat 1 starts a bar.
        ImportedPiece barred = grid.Apply(Import(Rubato()));
        foreach (Note bass in barred.Notes.Where(note => note.Pitch.MidiNumber == 36))
        {
            Assert.Equal(0, (bass.Span.Onset.Value + 30) % 1920 / 60);
        }
    }

    [Fact]
    public void Bar_lines_can_move_a_beat_either_way()
    {
        BarGrid grid = new(Tick.Zero, 150_000, new TimeSignature(3, 4), [4, 22]);

        Assert.Equal([0, 18], grid.Shifted(-1).BarStarts);
        Assert.Equal([8, 26], grid.Shifted(1).BarStarts);
        Assert.Equal([8], grid.Shifted(-2).BarStarts.Take(1));
        Assert.Equal(grid, grid.Shifted(1).Shifted(-1));
        Assert.Equal([2], new BarGrid(Tick.Zero, 150_000, new TimeSignature(3, 8), [0]).Shifted(1).BarStarts);
    }

    [Fact]
    public void Fur_elise_gets_its_printed_bars_and_tempo()
    {
        BarGrid grid = BarGrid.Find(Import(FurElise()).Midi!)!;

        Assert.Equal(new TimeSignature(3, 8), grid.Signature);
        Assert.Equal(72, grid.QuarterNotesPerMinute);
        // The E and D-sharp before the first bar are a pickup of two 16ths.
        Assert.Equal(2, grid.BarStarts[0]);
        Assert.True(grid.HasPickup);
    }

    [Fact]
    public void A_waltz_gets_three_four_its_tempo_and_its_pickup()
    {
        BarGrid grid = BarGrid.Find(Import(Played(150_000, Waltz())).Midi!)!;

        Assert.Equal(new TimeSignature(3, 4), grid.Signature);
        Assert.Equal(100, grid.QuarterNotesPerMinute);
        Assert.InRange(grid.SixteenthMicroseconds, 148_500, 151_500);
        Assert.Equal([4], grid.BarStarts);
    }

    [Fact]
    public void A_march_gets_four_four_when_the_bass_also_falls_on_beat_three()
    {
        BarGrid grid = BarGrid.Find(Import(Played(166_667, March())).Midi!)!;

        Assert.Equal(TimeSignature.CommonTime, grid.Signature);
        Assert.Equal(90, grid.QuarterNotesPerMinute);
        Assert.Equal([0], grid.BarStarts);
    }

    [Fact]
    public void Finding_is_deterministic()
    {
        MidiImport import = Import(FurElise()).Midi!;

        Assert.Equal(BarGrid.Find(import), BarGrid.Find(import));
    }

    [Fact]
    public void The_player_can_choose_the_bars_and_twice_or_half_the_tempo()
    {
        MidiImport import = Import(Played(150_000, Waltz())).Midi!;
        BarGrid found = BarGrid.Find(import)!;

        BarGrid faster = BarGrid.Find(import, new TimeSignature(6, 8), found.StepsPerBeat * 2)!;

        Assert.Equal(new TimeSignature(6, 8), faster.Signature);
        Assert.Equal(found.SixteenthMicroseconds / 2, faster.SixteenthMicroseconds);
        Assert.Equal(found.Beats, faster.Beats);
        Assert.Equal(200, faster.QuarterNotesPerMinute);
    }

    [Fact]
    public void Only_the_offered_bars_can_be_chosen()
    {
        MidiImport import = Import(Played(150_000, Waltz())).Midi!;

        Assert.Throws<ArgumentException>(() => BarGrid.Find(import, new TimeSignature(5, 4)));
    }

    [Fact]
    public void Too_few_notes_or_no_steady_beat_give_no_bars()
    {
        Assert.Null(BarGrid.Find(Import(Played(150_000, Waltz().Take(10))).Midi!));

        // Onsets that wander from 0.3 to 0.9 s apart, counted here in milliseconds, follow no steady beat.
        int at = 0;
        IEnumerable<(int, int, int)> wandering = Enumerable.Range(0, 120).Select(i => (at += 300 + (i * i * 37 % 600), 200, 60 + (i % 12))).ToList();
        Assert.Null(BarGrid.Find(Import(Played(1_000, wandering)).Midi!));
    }

    [Fact]
    public void Applying_keeps_the_notes_and_their_sound_and_writes_them_against_the_beat()
    {
        ImportedPiece piece = Import(Played(150_000, Waltz()));
        BarGrid grid = BarGrid.Find(piece.Midi!)!;

        ImportedPiece barred = grid.Apply(piece);

        Assert.Equal(piece.Notes.Select(note => (note.Id, note.Pitch, note.Velocity)), barred.Notes.Select(note => (note.Id, note.Pitch, note.Velocity)));
        Assert.Equal(new TimeSignature(3, 4), barred.Meter.SignatureAt(new Tick(5_000)));
        Assert.Equal(480, barred.Meter.PickupDuration);
        Assert.Same(grid, barred.Bars);

        // The notes sound when they did: the new tempo times the new ticks gives the same time from the first note.
        SoundsAsHeard(piece, barred, grid);

        // Every bass note lands within a 32nd of a bar line.
        foreach (Note bass in barred.Notes.Where(note => note.Pitch.MidiNumber == 48))
        {
            Bar bar = barred.Meter.BarAt(new Tick(bass.Span.Onset.Value + 30));
            Assert.InRange(bass.Span.Onset.Value - bar.Span.Onset.Value, -60, 60);
        }
    }

    [Fact]
    public void Applying_moves_the_pedal_with_the_notes()
    {
        ImportedPiece played = Import(Played(150_000, Waltz()));
        MidiPedal held = new(0, 0, 0, 0, TickSpan.FromBounds(played.Notes[0].Span.Onset, played.Notes[6].Span.Onset));
        ImportedPiece piece = played with { Midi = played.Midi! with { SustainPedal = [held] } };

        ImportedPiece barred = BarGrid.Find(piece.Midi!)!.Apply(piece);

        Assert.Equal(TickSpan.FromBounds(barred.Notes[0].Span.Onset, barred.Notes[6].Span.Onset), Assert.Single(barred.Midi!.SustainPedal).Span);
    }

    private static void SoundsAsHeard(ImportedPiece piece, ImportedPiece barred, BarGrid grid)
    {
        Assert.True(grid.FollowsPlaying);
        MidiTempoChange[] tempos = [.. barred.Midi!.Tempos];
        long Written(long tick)
        {
            long microseconds = 0, at = 0, perQuarter = tempos[0].MicrosecondsPerQuarter;
            foreach (MidiTempoChange tempo in tempos.Where(tempo => tempo.At.Value <= tick))
            {
                microseconds += (tempo.At.Value - at) * perQuarter / 480;
                (at, perQuarter) = (tempo.At.Value, tempo.MicrosecondsPerQuarter);
            }

            return microseconds + ((tick - at) * perQuarter / 480);
        }

        long firstHeard = piece.Notes.Min(note => note.Span.Onset.Value);
        long firstWritten = Written(barred.Notes.Min(note => note.Span.Onset.Value));
        foreach ((Note before, Note after) in piece.Notes.Zip(barred.Notes))
        {
            long heard = (before.Span.Onset.Value - firstHeard) * 500_000 / 480;
            Assert.InRange(Written(after.Span.Onset.Value) - firstWritten - heard, -2_000, 2_000);
        }
    }

    [Fact]
    public void Applying_says_what_was_worked_out_and_drops_the_engines_defaults()
    {
        ImportedPiece piece = Import(Played(150_000, Waltz()));
        BarGrid grid = BarGrid.Find(piece.Midi!)!;

        ImportedPiece barred = grid.Apply(piece);

        ImportIssue notice = Assert.Single(barred.Report.Issues, issue => issue.Code == BarGrid.IssueCode);
        Assert.Equal(ImportIssueKind.Assumed, notice.Kind);
        Assert.Contains("(3/4)", notice.Message);
        Assert.Contains("follows the playing (a quarter at about 100 a minute)", notice.Message);
        Assert.Equal(0, barred.Report.CountOf(MidiIssueCodes.TempoDefault));
        Assert.Equal(17, barred.Report.MeasureCount);
    }

    [Fact]
    public void The_tempo_fact_says_it_was_worked_out_from_the_notes()
    {
        ImportedPiece barred = BarGrid.Find(Import(Played(150_000, Waltz())).Midi!)!.Apply(Import(Played(150_000, Waltz())));

        CharacterContext context = CharacterContext.FromPiece(barred);

        Assert.True(context.TempoEstimated);
        Assert.All(context.Tempos, tempo => Assert.InRange(tempo.MicrosecondsPerQuarter, 540_000, 660_000));
    }

    [Fact]
    public void A_short_bar_starts_the_bars_afresh()
    {
        ImportedPiece piece = Import(Played(150_000, Waltz()));
        BarGrid grid = new(BarGrid.Find(piece.Midi!)!.Origin, 150_000, new TimeSignature(3, 4), [4, 22]);

        MeterMap meter = grid.Apply(piece).Meter;

        Bar shortBar = meter.BarAt(new Tick((4 + 12) * BarGrid.StepTicks));
        Assert.Equal(6 * BarGrid.StepTicks, shortBar.Span.Duration);
        Assert.Equal(22 * BarGrid.StepTicks, meter.BarAt(new Tick(22 * BarGrid.StepTicks)).Span.Onset.Value);
    }

    [Fact]
    public void A_grid_must_be_well_formed()
    {
        TimeSignature waltz = new(3, 4);

        Assert.Throws<ArgumentException>(() => new BarGrid(Tick.Zero, 150_000, waltz, []));
        Assert.Throws<ArgumentException>(() => new BarGrid(Tick.Zero, 150_000, waltz, [12]));
        Assert.Throws<ArgumentException>(() => new BarGrid(Tick.Zero, 150_000, waltz, [4, 4]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BarGrid(Tick.Zero, 1_000, waltz, [0]));
        Assert.Throws<ArgumentException>(() => new BarGrid(Tick.Zero, 150_000, waltz, [0], [5, 300_000]));
        Assert.Throws<ArgumentException>(() => new BarGrid(Tick.Zero, 150_000, waltz, [0], [0, 300_000, 300_000]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BarGrid(Tick.Zero, 150_000, waltz, [0], stepsPerBeat: 17));
    }

    [Fact]
    public void A_transcribed_project_keeps_its_bars_and_reopens_with_them()
    {
        byte[] midi = Played(150_000, Waltz());
        BarGrid grid = BarGrid.Find(Import(midi).Midi!)!;
        Project project = Project.Create("p_heard", "take.mid", midi, s_now) with { Transcription = new TranscriptionOrigin("transkun 2.0.1", grid) };

        Project reread = ProjectFile.Read(ProjectFile.Write(project));
        OpenedProject opened = OpenedProject.Open(reread);

        Assert.Equal(grid, reread.Transcription!.Bars);
        Assert.Equal(new TimeSignature(3, 4), opened.Piece.Meter.SignatureAt(Tick.Zero));
        Assert.Equal(grid.Apply(Import(midi)).Notes.Select(note => note.Span), opened.Notes.Select(note => note.Span));
        Assert.All(opened.Notes, note => Assert.Equal(Provenance.Detected, note.Attribution.Provenance));
    }

    [Fact]
    public void A_transcription_without_bars_keeps_the_engines_bars()
    {
        byte[] midi = Played(150_000, Waltz());
        Project project = Project.Create("p_heard", "take.mid", midi, s_now) with { Transcription = new TranscriptionOrigin("transkun 2.0.1") };

        Project reread = ProjectFile.Read(ProjectFile.Write(project));

        Assert.Null(reread.Transcription!.Bars);
        Assert.Equal(TimeSignature.CommonTime, OpenedProject.Open(reread).Piece.Meter.SignatureAt(Tick.Zero));
    }

    [Fact]
    public void A_version_9_transcription_keeps_the_engines_bars()
    {
        byte[] midi = Played(150_000, Waltz());
        Project project = Project.Create("p_heard", "take.mid", midi, s_now) with
        {
            Transcription = new TranscriptionOrigin("basic-pitch 0.4.0", BarGrid.Find(Import(midi).Midi!)),
        };
        string file = System.Text.Encoding.UTF8.GetString(ProjectFile.Write(project)).Replace($"\"version\": {ProjectFile.Version}", "\"version\": 9", StringComparison.Ordinal).Replace("\"resumePoints\": []", "\"sessions\": []", StringComparison.Ordinal);

        Assert.Null(ProjectFile.Read(System.Text.Encoding.UTF8.GetBytes(file)).Transcription!.Bars);
    }

    [Fact]
    public void Version_10_bars_keep_one_tempo()
    {
        byte[] midi = Played(150_000, Waltz());
        BarGrid found = BarGrid.Find(Import(midi).Midi!)!;
        Project project = Project.Create("p_heard", "take.mid", midi, s_now) with { Transcription = new TranscriptionOrigin("transkun 2.0.1", found) };
        string file = System.Text.Encoding.UTF8.GetString(ProjectFile.Write(project)).Replace($"\"version\": {ProjectFile.Version}", "\"version\": 10", StringComparison.Ordinal).Replace("\"resumePoints\": []", "\"sessions\": []", StringComparison.Ordinal);

        BarGrid read = ProjectFile.Read(System.Text.Encoding.UTF8.GetBytes(file)).Transcription!.Bars!;

        Assert.False(read.FollowsPlaying);
        Assert.Equal(2, read.StepsPerBeat);
        Assert.Equal(found.BarStarts, read.BarStarts);
    }

    [Fact]
    public void Bars_that_are_not_offered_are_damaged()
    {
        byte[] midi = Played(150_000, Waltz());
        Project project = Project.Create("p_heard", "take.mid", midi, s_now) with
        {
            Transcription = new TranscriptionOrigin("transkun 2.0.1", BarGrid.Find(Import(midi).Midi!)),
        };
        string file = System.Text.Encoding.UTF8.GetString(ProjectFile.Write(project)).Replace("\"3/4\"", "\"5/4\"", StringComparison.Ordinal);

        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(System.Text.Encoding.UTF8.GetBytes(file)));
    }
}