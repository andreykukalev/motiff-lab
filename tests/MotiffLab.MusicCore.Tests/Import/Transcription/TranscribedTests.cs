using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Import.Transcription;

/// <summary>Notes heard in a recording by transcription (decision record 0014).</summary>
public class TranscribedTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 29, 20, 0, 0, TimeSpan.Zero);

    /// <summary>Basic Pitch 0.4.0's MIDI for a synthesised C E G C' G E C melody, half a second a note.</summary>
    private static byte[] Melody() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Transcription", "Fixtures", "basic-pitch-melody.mid"));

    [Fact]
    public void Basic_pitchs_midi_imports_with_the_notes_it_heard()
    {
        ImportedPiece piece = PieceImporter.Import("take.mid", Melody()).Piece!;

        Assert.Equal([60, 64, 67, 72, 67, 64, 60], piece.Notes.OrderBy(note => note.Span.Onset).Select(note => note.Pitch.MidiNumber));
    }

    [Fact]
    public void Every_transcribed_note_is_detected_at_low_confidence()
    {
        ImportedPiece piece = Transcribed.Mark(PieceImporter.Import("take.mid", Melody()).Piece!);

        Assert.All(piece.Notes, note => Assert.Equal(Attribution.Detected(Transcribed.Confidence), note.Attribution));
        Assert.True(Transcribed.Confidence < 0.6);
    }

    [Fact]
    public void Marking_is_pure_and_keeps_everything_but_the_attribution()
    {
        ImportedPiece piece = PieceImporter.Import("take.mid", Melody()).Piece!;

        IReadOnlyList<Note> once = Transcribed.Mark(piece.Notes);

        Assert.Equal(once, Transcribed.Mark(once));
        Assert.Equal(piece.Notes.Select(note => (note.Id, note.Pitch, note.Span, note.Hand)), once.Select(note => (note.Id, note.Pitch, note.Span, note.Hand)));
    }

    [Fact]
    public void Only_midi_is_a_transcription()
    {
        byte[] musicXml = "<score-partwise><part-list><score-part id=\"P1\"/></part-list><part id=\"P1\"><measure number=\"1\"><note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration></note></measure></part></score-partwise>"u8.ToArray();
        ImportedPiece piece = PieceImporter.Import("score.musicxml", musicXml).Piece!;

        Assert.Throws<ArgumentException>(() => Transcribed.Mark(piece));
    }

    [Fact]
    public void A_transcribed_project_reopens_with_its_notes_detected_and_its_engine_kept()
    {
        Project project = Project.Create("p_heard", "take.mid", Melody(), s_now) with { Transcription = new TranscriptionOrigin("basic-pitch 0.4.0") };

        Project reread = ProjectFile.Read(ProjectFile.Write(project));
        OpenedProject opened = OpenedProject.Open(reread);

        Assert.Equal(new TranscriptionOrigin("basic-pitch 0.4.0"), reread.Transcription);
        Assert.Null(reread.Scan);
        Assert.All(opened.Notes, note => Assert.Equal(Provenance.Detected, note.Attribution.Provenance));
    }

    [Fact]
    public void A_midi_file_the_player_imported_is_not_a_transcription()
    {
        Project reread = ProjectFile.Read(ProjectFile.Write(Project.Create("p_file", "take.mid", Melody(), s_now)));

        Assert.Null(reread.Transcription);
        Assert.All(OpenedProject.Open(reread).Notes, note => Assert.Equal(Attribution.Imported, note.Attribution));
    }

    [Fact]
    public void A_project_that_says_it_was_both_scanned_and_transcribed_is_damaged()
    {
        Project project = Project.Create("p_both", "take.mid", Melody(), s_now) with
        {
            Transcription = new TranscriptionOrigin("basic-pitch 0.4.0"),
            Scan = ScanOrigin.Read("homr 0.7.0"),
        };

        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(ProjectFile.Write(project)));
    }

    [Fact]
    public void A_transcription_with_no_engine_is_damaged()
    {
        string file = System.Text.Encoding.UTF8.GetString(ProjectFile.Write(Project.Create("p_heard", "take.mid", Melody(), s_now) with
        {
            Transcription = new TranscriptionOrigin("basic-pitch 0.4.0"),
        })).Replace("\"basic-pitch 0.4.0\"", "\" \"", StringComparison.Ordinal);

        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(System.Text.Encoding.UTF8.GetBytes(file)));
    }
}