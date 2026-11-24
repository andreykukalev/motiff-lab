using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;

using static MotiffLab.MusicCore.Tests.Import.Scan.ScanRepairTests;

namespace MotiffLab.MusicCore.Tests.Import.Scan;

/// <summary>
/// The rules an engine's reading of a page gets before the merge (ADR 0035, section 5; v1.3, c-1a): the whole-bar rest, the
/// shared notehead and the bar lengths from the printed meters. Every score is written here, in the engine's shape (one
/// piano part on two staves, divisions 2: an eighth is 1); a score a single engine read is repaired as before.
/// </summary>
public class ScanReadingRulesTests
{
    private const long Quarter = 480;

    private static string WholeRest(int staff) =>
        $"<note><rest/><duration>8</duration><voice>{(staff == 1 ? 1 : 5)}</voice><type>whole</type><staff>{staff}</staff></note>";

    private static string Time(int beats) => $"<attributes><time><beats>{beats}</beats><beat-type>4</beat-type></time></attributes>";

    /// <summary>A bar of <paramref name="quarters"/> quarters in the right hand over a note as long in the left.</summary>
    private static string Bar(int quarters) =>
        string.Concat(Enumerable.Repeat(Note("C", 5, 2, 1), quarters)) + Backup(2 * quarters) + Note("C", 3, 2 * quarters, 2);

    /// <summary>The right hand's <paramref name="quarters"/> quarters over a whole rest in the left, as homr writes it in any meter.</summary>
    private static string RestBar(int quarters) =>
        string.Concat(Enumerable.Repeat(Note("E", 5, 2, 1), quarters)) + Backup(2 * quarters) + WholeRest(2);

    private static ImportedPiece Import(byte[] score) => PieceImporter.Import("scan.musicxml", score).Piece!;

    [Fact]
    public void A_whole_rest_alone_on_its_staff_lasts_the_bar_whatever_the_meter()
    {
        byte[] read = Score(2, "3", Bar(3), Bar(3), Bar(3), RestBar(3));

        ImportedPiece piece = Import(ScanRepair.RepairReading(read, printedMeters: false));

        Assert.All(piece.MusicXml!.Measures, measure => Assert.Equal(3 * Quarter, measure.Span.Duration));
        Assert.Equal(3 * Quarter, piece.MusicXml.Rests.Single().Span.Duration);
        Assert.Same(read, ScanRepair.Repair(read));
        Assert.Equal(4 * Quarter, Import(read).MusicXml!.Measures[3].Span.Duration);
    }

    [Fact]
    public void A_whole_rest_stays_as_read_where_the_bar_would_not_add_up()
    {
        byte[] read = Score(2, "3", Bar(3), Bar(3), Bar(3), RestBar(2));

        Assert.Same(read, ScanRepair.RepairReading(read, printedMeters: false));
    }

    [Fact]
    public void Two_voices_sharing_a_notehead_start_together_and_the_other_staff_moves_with_them()
    {
        // A half note E5 whose head also starts a run of eighths: homr writes the run after the half note, and the left hand's
        // G2 under the A4 by the same late clock, so the bar runs eleven eighths.
        string shared = Note("E", 5, 4, 1) + Note("D", 5, 1, 1) + Note("C", 5, 1, 1) + Note("B", 4, 1, 1)
            + Note("A", 4, 2, 1) + Note("G", 4, 2, 1) + Backup(11) + Note("C", 3, 4, 2) + "<forward><duration>3</duration></forward>" + Note("G", 2, 4, 2);
        byte[] read = Score(2, "4", Bar(4), Bar(4), Bar(4), shared);

        ImportedPiece piece = Import(ScanRepair.RepairReading(read, printedMeters: false));

        long start = 3 * 4 * Quarter;
        Assert.Equal(4 * Quarter, piece.MusicXml!.Measures[3].Span.Duration);
        Assert.Equal(start + (Quarter / 2), piece.Notes.Single(note => note.Pitch.MidiNumber == 74).Span.Onset.Value);
        Assert.Equal(start + (2 * Quarter), piece.Notes.Single(note => note.Pitch.MidiNumber == 69).Span.Onset.Value);
        Assert.Equal(start + (2 * Quarter), piece.Notes.Single(note => note.Pitch.MidiNumber == 43).Span.Onset.Value);
        Assert.Same(read, ScanRepair.Repair(read));
    }

    [Fact]
    public void A_reading_that_changes_meter_keeps_its_printed_meters_as_the_bars_lengths()
    {
        // Printed 3/4 then 2/4; three bars of the 3/4 were read a quarter long, so their majority says 4/4.
        byte[] read = Score(2, "3", Bar(4), Bar(4), Bar(4), RestBar(3), Time(2) + Bar(2), Bar(2), RestBar(2));

        ImportedPiece printed = Import(ScanRepair.RepairReading(read, printedMeters: true));
        ImportedPiece inferred = Import(ScanRepair.RepairReading(read, printedMeters: false));

        Assert.Equal([3, 2], printed.MusicXml!.TimeSignatures.Select(signature => signature.Numerator));
        Assert.Equal([3 * Quarter, 2 * Quarter], printed.MusicXml.Rests.Select(rest => rest.Span.Duration));
        Assert.Equal([4, 2], inferred.MusicXml!.TimeSignatures.Select(signature => signature.Numerator));
    }

    [Fact]
    public void A_reading_in_one_meter_settles_its_length_from_its_bars_as_any_scan()
    {
        byte[] read = Score(2, "3", Bar(4), Bar(4), Bar(4), RestBar(3));

        Assert.Equal(4, Import(ScanRepair.RepairReading(read, printedMeters: true)).MusicXml!.TimeSignatures[0].Numerator);
    }

    [Fact]
    public void What_the_rules_changed_is_said_in_the_score_s_encoding()
    {
        string text = System.Text.Encoding.UTF8.GetString(ScanRepair.RepairReading(Score(2, "3", Bar(3), Bar(3), Bar(3), RestBar(3)), printedMeters: false));

        Assert.Contains("Motiff Lab scan repair: 1 whole rests alone on their staff made to last the bar.", text, StringComparison.Ordinal);
    }
}