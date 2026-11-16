using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Performance;

/// <summary>
/// Another version read from a file (decision record 0022, section 1): which is "the notes" and which "the performance"
/// is only which was opened first, so an official score in MusicXML can be added over notes from a performance.
/// </summary>
public class VersionFileTests
{
    private const string FurElise = "fur-elise-original.musicxml";

    private static byte[] Score() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", FurElise));

    /// <summary>Für Elise's opening as a performer plays it: three notes added in bar 5, a fourth to a twelfth above the melody.</summary>
    private static byte[] Cover()
    {
        ImportedPiece piece = PieceImporter.Import(FurElise, Score()).Piece!;
        TickSpan bar5 = piece.Meter.GetBar(5).Span;
        Note[] melody = [.. piece.Notes.Where(note => bar5.Contains(note.Span.Onset)).OrderByDescending(note => note.Pitch.MidiNumber).Take(3)];
        Note[] played = [.. piece.Notes, .. melody.Select((note, i) => note with { Id = $"x{i}", Pitch = new Pitch(note.Pitch.MidiNumber + (i * 7) - 3) })];
        return MidiExporter.Export(played, [], piece.Meter, "cover");
    }

    [Fact]
    public void A_score_in_MusicXML_is_kept_as_MIDI_through_its_tempo()
    {
        VersionFileResult read = VersionFile.Read(FurElise, Score());

        Assert.Null(read.Rejection);
        Assert.Equal(PerformanceSource.Score, read.Source);
        ImportedPiece piece = PieceImporter.Import(FurElise, Score()).Piece!;
        IReadOnlyList<PlayedNote> notes = PlayedNotes.FromMidi(read.Midi);
        Assert.Equal(piece.Notes.Count, notes.Count);
        Assert.Equal(piece.Notes.Select(note => note.Pitch.MidiNumber).Order(), notes.Select(note => note.Pitch.MidiNumber).Order());
        Assert.Equal(read.Midi, VersionFile.Read(FurElise, Score()).Midi);
    }

    [Fact]
    public void A_MIDI_file_is_kept_as_it_is()
    {
        byte[] cover = Cover();

        VersionFileResult read = VersionFile.Read("cover.mid", cover);

        Assert.Equal(PerformanceSource.Midi, read.Source);
        Assert.Same(cover, read.Midi);
    }

    [Fact]
    public void The_official_score_over_notes_from_a_performance_shows_what_the_performer_added_as_left_out()
    {
        ImportedPiece notes = PieceImporter.Import("cover.mid", Cover()).Piece!;
        IReadOnlyList<PlayedNote> official = PlayedNotes.FromMidi(VersionFile.Read(FurElise, Score()).Midi);

        PerformanceComparison comparison = PerformanceComparison.Compare(notes.Notes, notes.Meter, official, PerformanceAlignment.Align(notes.Notes, official));

        BarComparison bar = Assert.Single(comparison.Differences);
        Assert.Equal(BarDifference.Simplified, bar.Difference);
        Assert.Equal(3, bar.PrintedNotHeard.Count);
        Assert.Empty(bar.PlayedNotPrinted);
        TakenBar taken = PerformanceTake.Take("perf_1", bar, official, comparison.Alignment, notes.Meter, notes.Notes);
        Assert.Equal(notes.Notes.Count - 3, PerformanceTake.Apply(notes.Notes, [taken]).Count);
    }

    [Fact]
    public void A_file_that_is_not_notes_is_turned_down_with_the_reason()
    {
        VersionFileResult read = VersionFile.Read("notes.mid", [.. "not a midi file"u8]);

        Assert.Null(read.Midi);
        Assert.NotNull(read.Rejection);
    }
}