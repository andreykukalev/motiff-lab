using System.Text;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;

namespace MotiffLab.MusicCore.Tests.Import;

/// <summary>The N2 import step: recognising the file, the report and the start choices, and plain reasons for files it cannot read.</summary>
public class PieceImporterTests
{
    private static byte[] Fixture(string folder, string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", folder, "Fixtures", name));

    private static ImportedPiece Imported(string fileName, byte[] data)
    {
        PieceImportResult result = PieceImporter.Import(fileName, data);
        Assert.Null(result.Rejection);
        return Assert.IsType<ImportedPiece>(result.Piece);
    }

    private static ImportRejection Rejected(string fileName, byte[] data)
    {
        PieceImportResult result = PieceImporter.Import(fileName, data);
        Assert.Null(result.Piece);
        ImportRejection rejection = Assert.IsType<ImportRejection>(result.Rejection);
        Assert.False(string.IsNullOrWhiteSpace(rejection.Message));
        Assert.False(string.IsNullOrWhiteSpace(rejection.Tip));
        return rejection;
    }

    [Fact]
    public void Type_1_midi_lists_the_tracks_with_notes_and_offers_each()
    {
        ImportedPiece piece = Imported("waltz.mid", Fixture("Midi", "two-hands-type1.mid"));

        Assert.Equal(SourceFormat.Midi, piece.Format);
        Assert.NotNull(piece.Midi);
        Assert.Null(piece.MusicXml);
        Assert.Equal(
            [new ImportedTrack(1, "Right hand", null, 7, TrackHands.None), new ImportedTrack(2, "Left hand", null, 20, TrackHands.None)],
            piece.Tracks.Select(track => track with { InstrumentName = null }));
        Assert.Equal(27, piece.Notes.Count);
        Assert.Equal(piece.Midi!.Report, piece.Report);
        Assert.Equal(
            ["Whole piece", "Track 2: Right hand", "Track 3: Left hand"],
            piece.StartChoices.Select(choice => choice.Label));
        Assert.Equal(SelectionFilter.ForTrack(2), piece.StartChoices[2].Filter);
    }

    [Fact]
    public void Single_piano_track_offers_its_inferred_hands()
    {
        ImportedPiece piece = Imported("piano.mid", Fixture("Midi", "piano-single-track.mid"));

        Assert.Equal(TrackHands.Inferred, Assert.Single(piece.Tracks).Hands);
        Assert.Equal(
            [("Whole piece", false), ("Right hand (inferred)", true), ("Left hand (inferred)", true)],
            piece.StartChoices.Select(choice => (choice.Label, choice.IsInferred)));
        Assert.Equal(SelectionFilter.ForHand(Hand.Left), piece.StartChoices[2].Filter);
    }

    [Fact]
    public void Grand_staff_musicxml_offers_its_imported_hands_and_keeps_the_score()
    {
        byte[] data = Fixture("MusicXml", "importer-grand-staff.musicxml");
        ImportedPiece piece = Imported("minuet.musicxml", data);

        Assert.Equal(SourceFormat.MusicXml, piece.Format);
        Assert.Equal(TrackHands.Imported, Assert.Single(piece.Tracks).Hands);
        Assert.Equal(["Whole piece", "Right hand", "Left hand"], piece.StartChoices.Select(choice => choice.Label));
        Assert.Equal(data, piece.ScoreXml);
        Assert.Equal(piece.MusicXml!.Report.MeasureCount, piece.Report.MeasureCount);
    }

    [Fact]
    public void Mxl_is_recognised_by_content_and_unpacked_for_the_score_view()
    {
        ImportedPiece piece = Imported("renamed.bin", Fixture("MusicXml", "grand-staff.mxl"));

        Assert.Equal(SourceFormat.MusicXml, piece.Format);
        Assert.StartsWith("<?xml", Encoding.UTF8.GetString(piece.ScoreXml!).TrimStart('﻿'));
    }

    [Fact]
    public void Content_beats_the_file_name()
    {
        ImportedPiece piece = Imported("looks-like-xml.musicxml", Fixture("Midi", "scale-type0.mid"));

        Assert.Equal(SourceFormat.Midi, piece.Format);
    }

    [Fact]
    public void The_5000_note_file_reports_tracks_notes_and_measures()
    {
        ImportedPiece piece = Imported("large.mid", Fixture("Midi", "large-5000-notes.mid"));

        Assert.Equal(5000, piece.Report.NoteCount);
        Assert.Equal(200, piece.Report.MeasureCount);
        Assert.Equal(5000, piece.Tracks.Sum(track => track.NoteCount));
    }

    [Theory]
    [InlineData("score.pdf", "%PDF-1.7\n", ImportRejectionReason.Pdf)]
    [InlineData("scan.png", "\u0089PNG\r\n\u001a\n", ImportRejectionReason.Image)]
    [InlineData("scan.jpg", "ÿØÿà", ImportRejectionReason.Image)]
    [InlineData("take.mp3", "ID3\u0004\0", ImportRejectionReason.Audio)]
    [InlineData("take.wav", "RIFF\0\0\0\0WAVEfmt ", ImportRejectionReason.Audio)]
    [InlineData("notes.txt", "Für Elise, bar 1", ImportRejectionReason.NotMusic)]
    public void Files_that_are_not_notation_get_a_reason_and_a_tip(string fileName, string content, ImportRejectionReason reason)
    {
        ImportRejection rejection = Rejected(fileName, Encoding.Latin1.GetBytes(content));

        Assert.Equal(reason, rejection.Reason);
    }

    [Fact]
    public void A_pdf_gets_the_spec_wording()
    {
        ImportRejection rejection = Rejected("score.mid", "%PDF-1.4"u8.ToArray());

        Assert.Equal("This looks like a PDF.", rejection.Message);
        Assert.Equal("Motiff Lab reads MIDI and MusicXML; most notation apps can export MusicXML.", rejection.Tip);
    }

    [Fact]
    public void Empty_and_oversized_files_are_rejected_before_reading()
    {
        Assert.Equal(ImportRejectionReason.Empty, Rejected("empty.mid", []).Reason);
        Assert.Equal(ImportRejectionReason.Empty, PieceImporter.CheckSize(0)?.Reason);
        Assert.Equal(ImportRejectionReason.TooLarge, PieceImporter.CheckSize(PieceImporter.MaxFileBytes + 1)?.Reason);
        Assert.Null(PieceImporter.CheckSize(PieceImporter.MaxFileBytes));
        Assert.Equal(ImportRejectionReason.TooLarge, Rejected("huge.mid", new byte[PieceImporter.MaxFileBytes + 1]).Reason);
    }

    [Fact]
    public void Damaged_files_keep_the_importer_reason()
    {
        byte[] midi = Fixture("Midi", "scale-type0.mid")[..10];
        ImportRejection truncated = Rejected("scale.mid", midi);
        Assert.Equal(ImportRejectionReason.Damaged, truncated.Reason);

        ImportRejection brokenXml = Rejected("score.musicxml", "<?xml version=\"1.0\"?><score-partwise><part"u8.ToArray());
        Assert.Equal(ImportRejectionReason.Damaged, brokenXml.Reason);
        Assert.Contains("malformed", brokenXml.Message);
    }

    [Fact]
    public void Unsupported_midi_keeps_the_importer_reason()
    {
        byte[] type2 = [.. "MThd"u8, 0, 0, 0, 6, 0, 2, 0, 1, 1, 0xE0];

        ImportRejection rejection = Rejected("patterns.mid", type2);

        Assert.Equal(ImportRejectionReason.Unsupported, rejection.Reason);
        Assert.Contains("type 2", rejection.Message);
    }

    [Fact]
    public void A_file_with_no_notes_is_rejected()
    {
        byte[] noNotes = [.. "MThd"u8, 0, 0, 0, 6, 0, 0, 0, 1, 1, 0xE0, .. "MTrk"u8, 0, 0, 0, 4, 0, 0xFF, 0x2F, 0];

        Assert.Equal(ImportRejectionReason.NoNotes, Rejected("silence.mid", noNotes).Reason);
    }

    [Fact]
    public void Garbage_with_a_midi_name_gets_the_midi_reason()
    {
        ImportRejection rejection = Rejected("song.mid", "hello there"u8.ToArray());

        Assert.Equal(ImportRejectionReason.NotMusic, rejection.Reason);
        Assert.Contains("MThd", rejection.Message);
    }

    [Fact]
    public void Import_is_deterministic()
    {
        byte[] data = Fixture("Midi", "two-hands-type1.mid");

        ImportedPiece first = Imported("a.mid", data);
        ImportedPiece second = Imported("a.mid", data);

        Assert.Equal(first.Notes, second.Notes);
        Assert.Equal(first.Tracks, second.Tracks);
        Assert.Equal(first.Report.Issues, second.Report.Issues);
    }
}