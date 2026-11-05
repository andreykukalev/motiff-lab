using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Performance;

/// <summary>
/// A recording correcting notes recognised from a scan (decision record 0022, section 6), on public-domain
/// material: homr's reading of the first page of Für Elise and Transkun's hearing of a recording of the whole piece.
/// </summary>
public class RecordingCorrectionsTests
{
    private static ImportedPiece Fixture(string folder, string name) =>
        PieceImporter.Import(name, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", folder, "Fixtures", name))).Piece!;

    private static ImportedPiece Scan() => Fixture("Scan", "homr-fur-elise-page1.musicxml");

    /// <summary>Transkun's notes at the engine's 120 BPM, a tick being 500000 / 480 microseconds.</summary>
    private static List<PlayedNote> Heard() =>
        [.. Fixture("Transcription", "transkun-fur-elise.mid").Notes.Select(note => new PlayedNote(note.Id, note.Pitch, note.Span.Onset.Value * 500_000 / 480, note.Velocity))];

    private static (PerformanceComparison Comparison, IReadOnlyList<RecordingCorrection> Corrections) Run(ImportedPiece scan, List<PlayedNote> played)
    {
        PerformanceComparison comparison = PerformanceComparison.Compare(scan.Notes, scan.Meter, played, PerformanceAlignment.Align(scan.Notes, played));
        return (comparison, RecordingCorrections.Find(scan.Notes, scan.Meter, ScanCheck.Of(scan), comparison));
    }

    [Fact]
    public void The_first_page_of_a_scan_lines_up_with_a_recording_of_the_whole_piece()
    {
        ImportedPiece scan = Scan();
        (PerformanceComparison comparison, _) = Run(scan, Heard());

        Assert.All(comparison.Bars, bar => Assert.True(bar.Aligned));
        Assert.True(comparison.MatchedCount >= scan.Notes.Count - 2);
        // The rest of the piece, played after the page ends, is not counted as notes the print lacks.
        Assert.Empty(comparison.Differences);
    }

    [Fact]
    public void Bars_the_scan_read_too_long_that_the_recording_plays_in_three_eighths_are_flagged()
    {
        (_, IReadOnlyList<RecordingCorrection> corrections) = Run(Scan(), Heard());

        Assert.Equal([16, 21, 27], corrections.Select(correction => correction.FirstBar));
        Assert.All(corrections, correction =>
        {
            Assert.Equal(CheckReason.RecordingBarLength, correction.Reason);
            Assert.Equal(720, correction.PlayedTicks);
        });
        Assert.All(corrections.SelectMany(correction => correction.Flags()), flag => Assert.Equal((CheckSource.Performance, false), (flag.Source, flag.Resolved)));
    }

    [Fact]
    public void A_hand_heard_an_octave_lower_for_two_bars_suggests_a_lost_octave_sign()
    {
        ImportedPiece scan = Scan();
        List<PlayedNote> heard = Heard();
        PerformanceAlignment alignment = PerformanceAlignment.Align(scan.Notes, heard);
        TickSpan bars = TickSpan.FromBounds(scan.Meter.GetBar(10).Span.Onset, scan.Meter.GetBar(11).Span.End);

        // The right hand of bars 10 and 11 played an octave lower, as if the print had an 8vb the scan lost.
        List<PlayedNote> lower = [.. heard.Select(note =>
            note.Pitch.MidiNumber >= 64 && bars.Contains(alignment.TickAt(note.Onset).Offset(PerformanceComparison.EarlyTolerance / 2))
                ? note with { Pitch = new Pitch(note.Pitch.MidiNumber - 12) }
                : note)];
        (_, IReadOnlyList<RecordingCorrection> corrections) = Run(scan, lower);

        RecordingCorrection octave = Assert.Single(corrections, correction => correction.Reason == CheckReason.RecordingOctave);
        Assert.Equal((10, 11, -12), (octave.FirstBar, octave.LastBar, octave.Semitones));
        Assert.All(octave.NoteIds, id => Assert.Equal(Hand.Right, scan.Notes.Single(note => note.Id == id).Hand?.Value));
    }

    [Fact]
    public void One_bar_an_octave_away_is_left_to_the_performer()
    {
        ImportedPiece scan = Scan();
        List<PlayedNote> heard = Heard();
        PerformanceAlignment alignment = PerformanceAlignment.Align(scan.Notes, heard);
        TickSpan bar = scan.Meter.GetBar(10).Span;
        List<PlayedNote> lower = [.. heard.Select(note =>
            note.Pitch.MidiNumber >= 64 && bar.Contains(alignment.TickAt(note.Onset).Offset(PerformanceComparison.EarlyTolerance / 2))
                ? note with { Pitch = new Pitch(note.Pitch.MidiNumber - 12) }
                : note)];

        Assert.DoesNotContain(Run(scan, lower).Corrections, correction => correction.Reason == CheckReason.RecordingOctave);
    }
}