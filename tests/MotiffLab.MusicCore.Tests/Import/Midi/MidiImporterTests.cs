using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.Midi;

public class MidiImporterTests
{
    private static MidiImport Import(SmfBuilder builder) => MidiImporter.Import(builder.Build());

    [Fact]
    public void Note_on_before_note_off_at_the_same_tick_pairs_first_on_first_off()
    {
        // Some exporters write the next note-on before the previous note-off at a shared tick.
        MidiImport result = Import(new SmfBuilder(0, 480).Track(t => t
            .On(0, 60)
            .On(480, 60)
            .Off(0, 60)
            .Off(480, 60)));

        Assert.Equal(
            [TickSpan.FromBounds(new Tick(0), new Tick(480)), TickSpan.FromBounds(new Tick(480), new Tick(960))],
            result.Notes.Select(note => note.Span));
        Assert.Equal(1, result.Report.CountOf(MidiIssueCodes.SamePitchOverlap));
    }

    [Fact]
    public void Same_key_on_different_channels_is_paired_separately()
    {
        MidiImport result = Import(new SmfBuilder(0, 480).Track(t => t
            .On(0, 60, channel: 0)
            .On(0, 60, channel: 1)
            .Off(240, 60, channel: 1)
            .Off(240, 60, channel: 0)));

        Assert.Equal([(0, 480L), (1, 240L)], result.Notes.OrderBy(n => n.Channel).Select(n => (n.Channel, n.Span.Duration)));
        Assert.Equal(0, result.Report.CountOf(MidiIssueCodes.SamePitchOverlap));
    }

    [Fact]
    public void Running_status_spans_note_ons_and_velocity_zero_offs()
    {
        MidiImport result = Import(new SmfBuilder(0, 96).Track(t => t
            .On(0, 60, 100)
            .Raw(0, 64, 90) // running status: note-on 64
            .Raw(96, 60, 0)
            .Raw(0, 64, 0)));

        Assert.Equal([(60, 100), (64, 90)], result.Notes.Select(n => (n.Pitch, n.Velocity)));
        Assert.All(result.Notes, n => Assert.Equal(MidiNoteEnd.NoteOnVelocityZero, n.EndSource));
    }

    [Fact]
    public void Meta_events_cancel_running_status_so_a_bare_data_byte_damages_the_track()
    {
        MidiImport result = Import(new SmfBuilder(0, 96).Track(t => t
            .On(0, 60)
            .Raw(96, 60, 0)
            .Name(0, "x")
            .Raw(0, 62, 80)));

        MidiNote note = Assert.Single(result.Notes);
        Assert.Equal(60, note.Pitch);
        Assert.Equal(1, result.Report.CountOf(MidiIssueCodes.TrackDamaged));
    }

    [Fact]
    public void A_truncated_track_keeps_what_was_read_and_closes_open_notes()
    {
        byte[] bytes = new SmfBuilder(0, 480).Track(t => t
            .On(0, 60)
            .Off(480, 60)
            .On(0, 62)
            .On(480, 64), endOfTrack: false).Build();
        byte[] cut = bytes[..^2]; // lose the last note-on's pitch and velocity

        MidiImport result = MidiImporter.Import(cut);

        Assert.Equal([60, 62], result.Notes.Select(n => n.Pitch));
        Assert.Equal(MidiNoteEnd.Assumed, result.Notes[1].EndSource);
        Assert.Equal(1, result.Report.CountOf(MidiIssueCodes.TrackTruncated));
        Assert.Equal(1, result.Report.CountOf(MidiIssueCodes.TrackDamaged));
        Assert.Equal(1, result.Report.CountOf(MidiIssueCodes.NoteOffMissing));
    }

    [Fact]
    public void A_note_left_open_at_the_very_end_lasts_one_quarter()
    {
        MidiImport result = Import(new SmfBuilder(0, 480).Track(t => t.On(0, 60).On(960, 62)));

        MidiNote last = result.Notes[1];
        Assert.Equal(TickSpan.FromBounds(new Tick(960), new Tick(1440)), last.Span);
        Assert.Equal(new Tick(1440), result.End);
    }

    [Fact]
    public void Missing_end_of_track_unknown_chunks_and_header_mismatch_are_repaired()
    {
        var builder = new SmfBuilder(1, 480) { DeclaredTrackCount = 3 }
            .Track(t => t.On(0, 60).Off(480, 60), endOfTrack: false)
            .Chunk("XFIH", [1, 2, 3])
            .Track(t => t.On(0, 64).Off(480, 64));

        MidiImport result = Import(builder);

        Assert.Equal(2, result.Notes.Count);
        Assert.Equal(1, result.Report.CountOf(MidiIssueCodes.EndOfTrackMissing));
        Assert.Equal(1, result.Report.CountOf(MidiIssueCodes.UnknownChunk));
        Assert.Equal(1, result.Report.CountOf(MidiIssueCodes.TrackCountMismatch));
    }

    [Fact]
    public void Tempo_and_meter_at_the_same_position_keep_the_last_value()
    {
        MidiImport result = Import(new SmfBuilder(1, 480)
            .Track(t => t.Tempo(0, 500_000).Tempo(0, 400_000).TimeSignature(0, 4, 2).TimeSignature(0, 6, 3))
            .Track(t => t.Tempo(960, 300_000).On(0, 60).Off(480, 60)));

        Assert.Equal([400_000, 300_000], result.Tempos.Select(change => change.MicrosecondsPerQuarter));
        Assert.Equal((6, 8), (result.TimeSignatures[0].Numerator, result.TimeSignatures[0].Denominator));
        Assert.Equal(1, result.Report.CountOf(MidiIssueCodes.TempoSuperseded));
        Assert.Equal(1, result.Report.CountOf(MidiIssueCodes.MeterSuperseded));
        Assert.Equal(1, result.Report.CountOf(MidiIssueCodes.TempoOutsideFirstTrack));
    }

    [Fact]
    public void A_tempo_after_the_start_gets_the_default_before_it()
    {
        MidiImport result = Import(new SmfBuilder(0, 480).Track(t => t.Tempo(960, 400_000)));

        Assert.Equal(
            [new MidiTempoChange(Tick.Zero, 0, 500_000, true), new MidiTempoChange(new Tick(960), 960, 400_000, false)],
            result.Tempos);
        Assert.Equal(1, result.Report.CountOf(MidiIssueCodes.TempoDefault));
    }

    [Fact]
    public void Meta_positions_that_do_not_land_on_the_grid_are_reported_as_rounded()
    {
        // At PPQ 960, tick 1 is half a canonical tick and rounds up to 1.
        MidiImport result = Import(new SmfBuilder(0, 960).Track(t => t.Tempo(0, 500_000).TimeSignature(1, 3, 2)));

        Assert.Equal(new Tick(1), result.TimeSignatures[^1].At);
        Assert.Equal(1, result.TimeSignatures[^1].MidiTick);
        Assert.Equal(1, result.Report.CountOf(MidiIssueCodes.MetaRounded));
    }

    [Fact]
    public void Invalid_meta_values_are_dropped()
    {
        MidiImport result = Import(new SmfBuilder(0, 480).Track(t => t
            .Meta(0, 0x51, 0, 0, 0)
            .Meta(0, 0x58, 0, 2, 24, 8)
            .Meta(0, 0x59, 9, 0)
            .Meta(0, 0x59, 1)));

        Assert.Empty(result.KeySignatures);
        Assert.True(Assert.Single(result.Tempos).IsAssumed);
        Assert.Equal(4, result.Report.CountOf(MidiIssueCodes.MetaInvalid));
    }

    [Fact]
    public void Names_decode_as_utf8_or_latin1()
    {
        MidiImport result = Import(new SmfBuilder(1, 480)
            .Track(t => t.Name(0, "Étude à deux"))
            .Track(t => t.Meta(0, 0x03, 0x43, 0x61, 0x66, 0xE9).Meta(0, 0x04, 0x50, 0x69, 0x61, 0x6E, 0x6F, 0x00)));

        Assert.Equal("Étude à deux", result.Tracks[0].Name);
        Assert.Equal("Café", result.Tracks[1].Name);
        Assert.Equal("Piano", result.Tracks[1].InstrumentName);
    }

    [Fact]
    public void Channel_data_that_stage_1_does_not_use_is_counted_as_dropped()
    {
        MidiImport result = Import(new SmfBuilder(0, 480).Track(t => t
            .Control(0, 64, 127)
            .Control(0, 7, 100)
            .Raw(0, 0xE0, 0, 64)
            .Raw(0, 0xD0, 30)
            .Raw(0, 0xA0, 60, 30)
            .Raw(0, 0xF0, 1, 0xF7)
            .Meta(0, 0x06, (byte)'A')
            .Off(0, 61)));

        ImportReport report = result.Report;
        Assert.All(
            new[]
            {
                MidiIssueCodes.SustainPedalIgnored, MidiIssueCodes.ControllersIgnored, MidiIssueCodes.PitchBendIgnored,
                MidiIssueCodes.SysExIgnored, MidiIssueCodes.MetaIgnored, MidiIssueCodes.NoteOffUnmatched,
            },
            code => Assert.Equal(1, report.CountOf(code)));
        Assert.Equal(2, report.CountOf(MidiIssueCodes.AftertouchIgnored));
        Assert.All(
            report.Issues.Where(issue => issue.Code is not (MidiIssueCodes.TempoDefault or MidiIssueCodes.MeterDefault)),
            issue => Assert.Equal(ImportIssueKind.Dropped, issue.Kind));
    }

    [Fact]
    public void Data_after_end_of_track_is_ignored()
    {
        MidiImport result = Import(new SmfBuilder(0, 480).Track(t => t.On(0, 60).Off(480, 60).Meta(0, 0x2F).On(0, 62)));

        Assert.Single(result.Notes);
        Assert.Equal(1, result.Report.CountOf(MidiIssueCodes.DataAfterEndOfTrack));
    }

    [Fact]
    public void Issues_name_their_track_in_the_message()
    {
        MidiImport result = Import(new SmfBuilder(1, 480)
            .Track(t => t.Tempo(0, 500_000).TimeSignature(0, 4, 2))
            .Track(t => t.On(0, 38, channel: 9).Off(60, 38, channel: 9)));

        ImportIssue issue = Assert.Single(result.Report.Issues);
        Assert.Equal(1, issue.Track);
        Assert.Equal("Track 2: 1 note on the drum channel (10) was left out.", issue.Message);
    }

    [Theory]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0, 0, 0, 0, 0, 0 }, MidiImportFailure.NotMidi)]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0 }, MidiImportFailure.Unsupported)]
    [InlineData(new byte[] { 0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 2, 0, 1, 0, 96 }, MidiImportFailure.Unsupported)]
    [InlineData(new byte[] { 0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 1, 0, 1, 0xE7, 0x28 }, MidiImportFailure.Unsupported)]
    [InlineData(new byte[] { 0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 1, 0, 1, 0, 0 }, MidiImportFailure.Damaged)]
    [InlineData(new byte[] { 0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 1, 0, 1, 0, 96 }, MidiImportFailure.Damaged)]
    [InlineData(new byte[] { 0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 9, 0, 1, 0, 1, 0, 96 }, MidiImportFailure.Damaged)]
    public void Files_that_cannot_be_imported_fail_with_a_reason(byte[] bytes, MidiImportFailure failure)
    {
        var exception = Assert.Throws<MidiImportException>(() => MidiImporter.Import(bytes));

        Assert.Equal(failure, exception.Failure);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    [Fact]
    public void Files_over_5_mb_are_rejected()
    {
        var exception = Assert.Throws<MidiImportException>(() => MidiImporter.Import(new byte[MidiImporter.MaxFileBytes + 1]));

        Assert.Equal(MidiImportFailure.TooLarge, exception.Failure);
    }

    [Fact]
    public void A_5000_note_file_imports_quickly()
    {
        var builder = new SmfBuilder(1, 480).Track(t =>
        {
            for (int i = 0; i < 5000; i++)
            {
                t.On(0, 40 + (i % 48)).Off(120, 40 + (i % 48));
            }
        });
        byte[] bytes = builder.Build();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        MidiImport result = MidiImporter.Import(bytes);
        watch.Stop();

        Assert.Equal(5000, result.Notes.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Import took {watch.Elapsed}"); // AC-S1-01
    }
}