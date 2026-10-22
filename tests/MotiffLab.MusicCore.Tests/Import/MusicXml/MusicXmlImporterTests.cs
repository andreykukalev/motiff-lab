using System.Text;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.MusicXml;

public class MusicXmlImporterTests
{
    private const string FourFour = "<attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time></attributes>";

    private static string Note(string step, int octave, int duration, string extra = "") =>
        $"<note>{extra}<pitch><step>{step}</step><octave>{octave}</octave></pitch><duration>{duration}</duration></note>";

    /// <summary>A one-part score; each string is the content of one measure.</summary>
    private static MusicXmlImport Import(params string[] measures)
    {
        var xml = new StringBuilder("<score-partwise><part-list><score-part id=\"P1\"><part-name>Piano</part-name></score-part></part-list><part id=\"P1\">");
        for (int i = 0; i < measures.Length; i++)
        {
            xml.Append($"<measure number=\"{i + 1}\">{measures[i]}</measure>");
        }

        return MusicXmlImporter.Import(Encoding.UTF8.GetBytes(xml.Append("</part></score-partwise>").ToString()));
    }

    private static MusicXmlImportFailure FailureOf(string text) => FailureOf(Encoding.UTF8.GetBytes(text));

    private static MusicXmlImportFailure FailureOf(byte[] data) =>
        Assert.Throws<MusicXmlImportException>(() => MusicXmlImporter.Import(data)).Failure;

    [Fact]
    public void Non_xml_input_is_not_musicxml()
    {
        Assert.Equal(MusicXmlImportFailure.NotMusicXml, FailureOf("%PDF-1.7 not a score"));
        Assert.Equal(MusicXmlImportFailure.NotMusicXml, FailureOf("MThd"));
        Assert.Equal(MusicXmlImportFailure.NotMusicXml, FailureOf([]));
        Assert.Equal(MusicXmlImportFailure.NotMusicXml, FailureOf("<html><body>hello</body></html>"));
    }

    [Fact]
    public void Broken_xml_is_damaged_with_a_line_number()
    {
        var error = Assert.Throws<MusicXmlImportException>(() =>
            MusicXmlImporter.Import(Encoding.UTF8.GetBytes("<score-partwise>\n<part id=\"P1\"><measure>")));

        Assert.Equal(MusicXmlImportFailure.Damaged, error.Failure);
        Assert.Contains("line 2", error.Message);
    }

    [Fact]
    public void A_score_without_parts_is_damaged()
    {
        Assert.Equal(MusicXmlImportFailure.Damaged, FailureOf("<score-partwise><part-list/></score-partwise>"));
    }

    [Fact]
    public void Compressed_timewise_and_fractional_files_are_unsupported()
    {
        Assert.Equal(MusicXmlImportFailure.Unsupported, FailureOf([(byte)'P', (byte)'K', 3, 4, 20, 0]));
        Assert.Equal(MusicXmlImportFailure.Unsupported, FailureOf("<score-timewise><measure number=\"1\"/></score-timewise>"));
        Assert.Equal(
            MusicXmlImportFailure.Unsupported,
            Assert.Throws<MusicXmlImportException>(() => Import(FourFour + Note("C", 4, 0).Replace("<duration>0", "<duration>1.5"))).Failure);
        Assert.Equal(
            MusicXmlImportFailure.Unsupported,
            Assert.Throws<MusicXmlImportException>(() => Import("<attributes><divisions>2.5</divisions></attributes>")).Failure);
    }

    [Fact]
    public void A_duration_that_is_not_a_number_is_damaged()
    {
        Assert.Equal(
            MusicXmlImportFailure.Damaged,
            Assert.Throws<MusicXmlImportException>(() => Import(FourFour + Note("C", 4, 0).Replace("<duration>0", "<duration>x"))).Failure);
    }

    [Fact]
    public void Files_over_5_mb_are_too_large()
    {
        Assert.Equal(MusicXmlImportFailure.TooLarge, FailureOf(new byte[MusicXmlImporter.MaxFileBytes + 1]));
    }

    [Fact]
    public void External_dtds_are_not_fetched()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 4.0 Partwise//EN" "http://example.invalid/partwise.dtd">
            <score-partwise><part id="P1"><measure><note><pitch><step>C</step><octave>4</octave></pitch><duration>1</duration></note></measure></part></score-partwise>
            """;

        MusicXmlImport result = MusicXmlImporter.Import(Encoding.UTF8.GetBytes(xml));

        Assert.Equal(60, Assert.Single(result.Notes).Pitch.MidiNumber);
        Assert.Equal(1, result.Report.CountOf(MusicXmlIssueCodes.PartNotListed));
        Assert.Equal(1, result.Report.CountOf(MusicXmlIssueCodes.DivisionsDefault));
    }

    [Fact]
    public void Missing_time_signature_and_tempo_are_assumed()
    {
        MusicXmlImport result = Import("<attributes><divisions>1</divisions></attributes>" + Note("C", 4, 4));

        Assert.True(Assert.Single(result.TimeSignatures).IsAssumed);
        Assert.True(Assert.Single(result.Tempos).IsAssumed);
        Assert.Equal(1, result.Report.CountOf(MusicXmlIssueCodes.MeterDefault));
        Assert.Equal(1, result.Report.CountOf(MusicXmlIssueCodes.TempoDefault));
    }

    [Fact]
    public void Divisions_changes_accumulate_exactly()
    {
        // 3 divisions then 5 divisions per quarter: a triplet quarter and a quintuplet eighth line up exactly.
        MusicXmlImport result = Import(
            "<attributes><divisions>3</divisions><time><beats>2</beats><beat-type>4</beat-type></time></attributes>"
                + Note("C", 4, 2) + Note("D", 4, 2) + Note("E", 4, 2),
            "<attributes><divisions>5</divisions></attributes>" + Note("F", 4, 1) + Note("G", 4, 9));

        Assert.Equal([0L, 320, 640, 960, 1056], result.Notes.Select(note => note.Span.Onset.Value));
        Assert.All(result.SourceNotes, note => Assert.True(note.IsExact));
        Assert.Equal(new QuarterFraction(11, 5), result.SourceNotes[^1].Onset);
        Assert.Equal(new Tick(1920), result.SourceNotes[^1].Span.End);
    }

    [Fact]
    public void Backup_and_forward_place_voices_and_backup_past_the_measure_start_is_repaired()
    {
        MusicXmlImport result = Import(
            FourFour + Note("C", 5, 4) + "<backup><duration>4</duration></backup><forward><duration>2</duration></forward>" + Note("C", 4, 2),
            Note("D", 5, 2) + "<backup><duration>3</duration></backup>" + Note("D", 4, 4));

        Assert.Equal([(72, 0L), (60, 960), (62, 1920), (74, 1920)], result.Notes.Select(note => (note.Pitch.MidiNumber, note.Span.Onset.Value)));
        Assert.Equal(1, result.Report.CountOf(MusicXmlIssueCodes.BackupBeforeMeasure));
    }

    [Fact]
    public void Chords_share_the_onset_of_the_note_before_them()
    {
        MusicXmlImport result = Import(FourFour + Note("C", 4, 2) + Note("E", 4, 1, "<chord/>") + Note("G", 4, 2));

        Assert.Equal([(60, 0L, 960L), (64, 0, 480), (67, 960, 960)], result.Notes.Select(note => (note.Pitch.MidiNumber, note.Span.Onset.Value, note.Span.Duration)));
    }

    [Fact]
    public void Rests_grace_cue_and_unpitched_notes_are_not_imported_but_rests_and_cues_take_time()
    {
        MusicXmlImport result = Import(
            FourFour
                + "<note><rest/><duration>1</duration></note>"
                + "<note><grace/><pitch><step>B</step><octave>3</octave></pitch></note>"
                + Note("C", 4, 1)
                + "<note><cue/><pitch><step>D</step><octave>4</octave></pitch><duration>1</duration></note>"
                + "<note><unpitched><display-step>E</display-step><display-octave>4</display-octave></unpitched><duration>1</duration></note>");

        var note = Assert.Single(result.SourceNotes);
        Assert.Equal((60, 480L, 2), (note.Pitch, note.Span.Onset.Value, note.NoteIndex));
        Assert.Equal(1, result.Report.CountOf(MusicXmlIssueCodes.GraceNotesIgnored));
        Assert.Equal(1, result.Report.CountOf(MusicXmlIssueCodes.CueNotesIgnored));
        Assert.Equal(1, result.Report.CountOf(MusicXmlIssueCodes.UnpitchedIgnored));
        Assert.Equal(new Tick(1920), result.End);
    }

    [Fact]
    public void Alterations_set_the_pitch_and_microtones_round_to_a_semitone()
    {
        MusicXmlImport result = Import(
            FourFour
                + "<note><pitch><step>B</step><alter>-1</alter><octave>3</octave></pitch><duration>1</duration></note>"
                + "<note><pitch><step>C</step><alter>0.5</alter><octave>4</octave></pitch><duration>1</duration></note>"
                + "<note><pitch><step>H</step><octave>4</octave></pitch><duration>1</duration></note>"
                + "<note><pitch><step>C</step><octave>10</octave></pitch><duration>1</duration></note>");

        Assert.Equal([58, 61], result.Notes.Select(note => note.Pitch.MidiNumber));
        Assert.Equal(1, result.Report.CountOf(MusicXmlIssueCodes.MicrotoneRounded));
        Assert.Equal(2, result.Report.CountOf(MusicXmlIssueCodes.PitchInvalid));
    }

    [Fact]
    public void A_single_staff_part_has_no_imported_hands()
    {
        MusicXmlImport result = Import(FourFour + Note("C", 4, 4));

        Assert.Null(Assert.Single(result.Notes).Hand);
        Assert.False(Assert.Single(result.Parts).HasHands);
    }

    [Fact]
    public void A_short_measure_in_the_middle_keeps_later_measures_on_bar_lines()
    {
        // Bar 2 is split at a repeat: 2 beats, then the next measure is a full bar again.
        MusicXmlImport result = Import(
            FourFour + Note("C", 4, 4),
            Note("D", 4, 2),
            Note("E", 4, 4));

        Assert.Equal(new MetricPosition(3, 1, 0), result.Meter.ToPosition(result.Notes[2].Span.Onset));
        Assert.False(result.Meter.GetBar(2).IsComplete);
        Assert.Equal(1, result.Report.CountOf(MusicXmlIssueCodes.MeasureShort));
        Assert.Single(result.TimeSignatures);
        Assert.Equal(3, result.Report.MeasureCount);
    }

    [Fact]
    public void A_short_last_measure_is_a_partial_bar_and_not_reported()
    {
        MusicXmlImport result = Import(FourFour + Note("C", 4, 4), Note("D", 4, 3));

        Assert.False(result.Meter.HasPickup);
        Assert.Equal(0, result.Report.CountOf(MusicXmlIssueCodes.MeasureShort));
        Assert.Equal(2, result.Report.MeasureCount);
    }

    [Fact]
    public void An_overfull_measure_is_reported_and_the_next_measure_still_starts_a_bar()
    {
        MusicXmlImport result = Import(FourFour + Note("C", 4, 5), Note("D", 4, 4));

        Assert.Equal(new Tick(2400), result.Notes[1].Span.Onset);
        Assert.Equal(0, result.Meter.ToPosition(result.Notes[1].Span.Onset).BeatOffset);
        Assert.Equal(1, result.Meter.ToPosition(result.Notes[1].Span.Onset).Beat);
        Assert.Equal(1, result.Report.CountOf(MusicXmlIssueCodes.MeasureLong));
    }

    [Fact]
    public void An_empty_measure_lasts_a_full_bar()
    {
        MusicXmlImport result = Import(
            "<attributes><divisions>1</divisions><time><beats>3</beats><beat-type>4</beat-type></time></attributes>" + Note("C", 4, 3),
            string.Empty,
            Note("D", 4, 3));

        Assert.Equal(new Tick(2 * 1440), result.Notes[1].Span.Onset);
    }

    [Fact]
    public void Additive_and_unusual_time_signatures()
    {
        MusicXmlImport additive = Import("<attributes><divisions>2</divisions><time><beats>3+2</beats><beat-type>8</beat-type></time></attributes>" + Note("C", 4, 5));
        Assert.Equal((5, 8), (additive.TimeSignatures[0].Numerator, additive.TimeSignatures[0].Denominator));

        MusicXmlImport free = Import("<attributes><divisions>1</divisions><time><senza-misura/></time></attributes>" + Note("C", 4, 4));
        Assert.True(free.TimeSignatures[0].IsAssumed);
        Assert.Equal(1, free.Report.CountOf(MusicXmlIssueCodes.MetaUnsupported));

        MusicXmlImport invalid = Import("<attributes><divisions>1</divisions><time><beats>3</beats><beat-type>5</beat-type></time></attributes>" + Note("C", 4, 4));
        Assert.Equal(1, invalid.Report.CountOf(MusicXmlIssueCodes.MetaInvalid));
    }

    [Fact]
    public void Different_tempos_at_the_same_position_keep_the_last_and_report_it()
    {
        MusicXmlImport result = Import(FourFour + "<sound tempo=\"60\"/><direction><sound tempo=\"90.5\"/></direction>" + Note("C", 4, 4));

        Assert.Equal(90.5m, Assert.Single(result.Tempos).QuarterNotesPerMinute);
        Assert.Equal(1, result.Report.CountOf(MusicXmlIssueCodes.TempoSuperseded));
    }

    [Fact]
    public void Metronome_marks_convert_their_beat_unit_to_quarters()
    {
        MusicXmlImport result = Import(
            FourFour
                + "<direction><direction-type><metronome><beat-unit>quarter</beat-unit><beat-unit-dot/><per-minute>c. 60</per-minute></metronome></direction-type></direction>"
                + Note("C", 4, 4),
            "<direction><direction-type><metronome><beat-unit>half</beat-unit><per-minute>50-56</per-minute></metronome></direction-type></direction>"
                + Note("D", 4, 4));

        Assert.Equal([90m, 100m], result.Tempos.Select(tempo => tempo.QuarterNotesPerMinute));
        Assert.Equal(new Tick(1920), result.Tempos[1].At);
    }

    [Fact]
    public void Zero_duration_notes_are_widened_to_one_tick()
    {
        MusicXmlImport result = Import(FourFour + Note("C", 4, 0) + Note("D", 4, 4));

        Assert.Equal(1, result.Notes[0].Span.Duration);
        Assert.False(result.SourceNotes[0].IsExact);
        Assert.Equal(1, result.Report.CountOf(MusicXmlIssueCodes.ZeroLengthWidened));
        Assert.Equal(0, result.Report.CountOf(MusicXmlIssueCodes.NotesRounded));
    }

    [Fact]
    public void Notes_get_ids_in_onset_order_and_source_locations()
    {
        MusicXmlImport result = Import(
            FourFour + Note("G", 4, 4) + "<backup><duration>4</duration></backup>" + Note("C", 4, 4, "<voice>2</voice>"));

        Assert.Equal(["n_0001", "n_0002"], result.Notes.Select(note => note.Id));
        Assert.Equal([60, 67], result.Notes.Select(note => note.Pitch.MidiNumber));
        var source = Assert.IsType<MusicXmlSourceLocation>(result.Notes[0].Source);
        Assert.Equal(("P1", 0, 1, 1, "2"), (source.PartId, source.MeasureIndex, source.NoteIndex, source.Staff, source.Voice));
        Assert.Equal(2, result.Notes[0].Voice);
    }

    [Fact]
    public void The_same_bytes_give_the_same_result()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(
            "<score-partwise><part id=\"P1\"><measure>" + FourFour + Note("C", 4, 1) + Note("E", 4, 1, "<chord/>") + "</measure></part></score-partwise>");

        MusicXmlImport first = MusicXmlImporter.Import(bytes);
        MusicXmlImport second = MusicXmlImporter.Import(bytes);

        Assert.Equal(first.Notes, second.Notes);
        Assert.Equal(first.Report.Issues, second.Report.Issues);
    }
}