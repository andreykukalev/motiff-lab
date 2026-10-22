using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.Midi;

/// <summary>The self-made files in Fixtures (see make_fixtures.py) and what each must import as.</summary>
public class MidiFixtureTests
{
    private static MidiImport ImportFixture(string name) =>
        MidiImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Midi", "Fixtures", name)));

    [Fact]
    public void Type_0_scale_with_running_status()
    {
        MidiImport result = ImportFixture("scale-type0.mid");

        Assert.Equal(0, result.Format);
        Assert.Equal(96, result.TimeBase.TicksPerQuarter);
        Assert.Equal("Scale study", Assert.Single(result.Tracks).Name);
        Assert.Equal(15, result.SourceNotes.Count);
        Assert.All(result.SourceNotes, note =>
        {
            Assert.Equal(480, note.Span.Duration);
            Assert.True(note.IsExact);
            Assert.Equal(MidiNoteEnd.NoteOnVelocityZero, note.EndSource);
        });
        Assert.Equal([60, 62, 64, 65, 67, 69, 71, 72, 71, 69, 67, 65, 64, 62, 60], result.SourceNotes.Select(note => note.Pitch));
        Assert.Equal(new Tick(14 * 480), result.SourceNotes[^1].Span.Onset);

        Assert.Equal(new MidiTempoChange(Tick.Zero, 0, 600_000, false), Assert.Single(result.Tempos));
        Assert.Equal(new MidiTimeSignature(Tick.Zero, 0, 4, 4, false), Assert.Single(result.TimeSignatures));
        Assert.Equal(new MidiKeySignature(Tick.Zero, 0, 0, false), Assert.Single(result.KeySignatures));
        Assert.Equal(new Tick(15 * 480), result.End);
        Assert.Empty(result.Report.Issues);
    }

    [Fact]
    public void Type_1_two_hands_keeps_tracks_tempo_map_and_meter()
    {
        MidiImport result = ImportFixture("two-hands-type1.mid");
        const int bar = 3 * 480;

        Assert.Equal(1, result.Format);
        Assert.Equal(["Waltz sketch", "Right hand", "Left hand", "Click"], result.Tracks.Select(track => track.Name));
        Assert.Equal([0, 7, 20, 0], result.Tracks.Select(track => track.NoteCount));
        Assert.Equal([1], result.Tracks[2].Channels);
        Assert.Equal(new MidiChannelProgram(1, 0), Assert.Single(result.Tracks[2].Programs));
        Assert.Empty(result.Tracks[3].Channels);

        Assert.Equal(
            [new MidiTempoChange(Tick.Zero, 0, 666_666, false), new MidiTempoChange(new Tick(2 * bar), 2 * bar, 833_333, false)],
            result.Tempos);
        Assert.Equal(new MidiTimeSignature(Tick.Zero, 0, 3, 4, false), Assert.Single(result.TimeSignatures));
        Assert.Equal(new MidiKeySignature(Tick.Zero, 0, 1, false), Assert.Single(result.KeySignatures));
        Assert.Equal(new Tick(4 * bar), result.End);

        MidiNote high = Assert.Single(result.SourceNotes, note => note.Pitch == 74);
        Assert.Equal(new TickSpan(new Tick(bar), bar), high.Span);
        Assert.Equal(1, high.Track);

        ImportReport report = result.Report;
        Assert.Equal(27, report.NoteCount);
        Assert.Equal(4, report.TrackCount);
        Assert.Equal(4, report.CountOf(MidiIssueCodes.DrumsExcluded));
        Assert.Equal(8, report.CountOf(MidiIssueCodes.SustainPedalIgnored));
        Assert.Equal(0, report.CountOf(MidiIssueCodes.NotesRounded));
        Assert.Equal(2, report.Issues.Count);
        Assert.Equal("Track 4: 4 notes on the drum channel (10) were left out.", report.Issues.Single(i => i.Code == MidiIssueCodes.DrumsExcluded).Message);
    }

    [Fact]
    public void Played_performance_reports_rounding_and_guesses()
    {
        MidiImport result = ImportFixture("performance-type1.mid");

        Assert.Equal(1000, result.TimeBase.TicksPerQuarter);
        Assert.Equal(7, result.SourceNotes.Count);

        // 13 × 480 / 1000 = 6.24 → 6; 470 × 0.48 = 225.6 → 226.
        MidiNote first = result.SourceNotes[0];
        Assert.Equal(new TickSpan(new Tick(6), 220), first.Span);
        Assert.False(first.IsExact);

        // The re-struck key pairs first on, first off.
        MidiNote[] repeated = [.. result.SourceNotes.Where(note => note.Pitch == 67)];
        Assert.Equal([(1007L, 1990L), (1500L, 2420L)], repeated.Select(note => (note.NoteOnTick, note.NoteOffTick)));

        MidiNote zeroLength = Assert.Single(result.SourceNotes, note => note.Pitch == 71);
        Assert.Equal(MidiNoteEnd.Widened, zeroLength.EndSource);
        Assert.Equal(new TickSpan(new Tick(1440), 1), zeroLength.Span);

        MidiNote hanging = Assert.Single(result.SourceNotes, note => note.Pitch == 72);
        Assert.Equal(MidiNoteEnd.Assumed, hanging.EndSource);
        Assert.Equal(4000, hanging.NoteOffTick);

        Assert.True(Assert.Single(result.Tempos).IsAssumed);
        Assert.True(Assert.Single(result.TimeSignatures).IsAssumed);

        ImportReport report = result.Report;
        Assert.Equal(6, report.CountOf(MidiIssueCodes.NotesRounded));
        Assert.Equal(1, report.CountOf(MidiIssueCodes.SamePitchOverlap));
        Assert.Equal(1, report.CountOf(MidiIssueCodes.NoteOffUnmatched));
        Assert.Equal(1, report.CountOf(MidiIssueCodes.ZeroLengthWidened));
        Assert.Equal(1, report.CountOf(MidiIssueCodes.NoteOffMissing));
        Assert.Equal(1, report.CountOf(MidiIssueCodes.TempoDefault));
        Assert.Equal(1, report.CountOf(MidiIssueCodes.MeterDefault));
        Assert.Contains(report.OfKind(ImportIssueKind.Rounded), issue => issue.Message.StartsWith("6 notes had timing rounded", StringComparison.Ordinal));
    }

    [Fact]
    public void Rounding_never_moves_a_boundary_more_than_half_a_tick()
    {
        MidiImport result = ImportFixture("performance-type1.mid");

        foreach (MidiNote note in result.SourceNotes.Where(note => note.EndSource != MidiNoteEnd.Widened))
        {
            AssertWithinHalfTick(note.NoteOnTick, note.Span.Onset);
            AssertWithinHalfTick(note.NoteOffTick, note.Span.End);
        }

        static void AssertWithinHalfTick(long midiTick, Tick canonical)
        {
            // |canonical − midiTick × 480 / 1000| ≤ 1/2, scaled by 2000 to stay in integers.
            long error = Math.Abs((canonical.Value * 2000) - (midiTick * 960));
            Assert.True(error <= 1000, $"MIDI tick {midiTick} landed on {canonical}");
        }
    }

    [Fact]
    public void Notes_map_to_the_shared_event_model_as_imported_facts()
    {
        MidiImport result = ImportFixture("scale-type0.mid");

        Note first = result.Notes[0];
        Assert.Equal("n_0001", first.Id);
        Assert.Equal(new Pitch(60), first.Pitch);
        Assert.Equal(new TickSpan(Tick.Zero, 480), first.Span);
        Assert.Equal(80, first.Velocity);
        Assert.Equal(0, first.Track);
        Assert.Null(first.Voice);
        Assert.Null(first.Hand);
        Assert.Equal(Attribution.Imported, first.Attribution);
        Assert.Equal(new MidiSourceLocation(0, 0, result.SourceNotes[0].NoteOnIndex), first.Source);
        Assert.Equal(result.SourceNotes.Count, result.Notes.Count);
        Assert.Equal(result.Notes.Count, result.Notes.Select(note => note.Id).Distinct().Count());
    }

    [Fact]
    public void A_note_whose_end_was_assumed_is_marked_inferred()
    {
        MidiImport result = ImportFixture("performance-type1.mid");

        Note hanging = Assert.Single(result.Notes, note => note.Pitch == new Pitch(72));
        Assert.Equal(Attribution.Inferred(MidiImporter.AssumedEndConfidence), hanging.Attribution);
        Assert.All(result.Notes.Where(note => note != hanging), note => Assert.Equal(Attribution.Imported, note.Attribution));
    }

    [Theory]
    [InlineData("scale-type0.mid", 4, "4/4")] // 15 quarters: three full bars and a partial fourth
    [InlineData("two-hands-type1.mid", 4, "3/4")]
    [InlineData("performance-type1.mid", 1, "4/4")] // assumed 4/4; 4000 ticks at PPQ 1000 is one bar
    public void Meter_map_and_measure_count_come_from_the_time_signatures(string fixture, int measures, string signature)
    {
        MidiImport result = ImportFixture(fixture);

        Assert.Equal(measures, result.Report.MeasureCount);
        Assert.Equal(signature, result.Meter.SignatureAt(Tick.Zero).ToString());
        Assert.Equal(result.TimeSignatures.Count, result.Meter.Changes.Count);
    }

    [Fact]
    public void Import_is_deterministic()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Midi", "Fixtures", "two-hands-type1.mid"));

        MidiImport first = MidiImporter.Import(bytes);
        MidiImport second = MidiImporter.Import(bytes);

        Assert.Equal(first.SourceNotes, second.SourceNotes);
        Assert.Equal(first.Notes, second.Notes);
        Assert.Equal(first.Report.Issues, second.Report.Issues);
    }
}