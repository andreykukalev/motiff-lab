using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.MusicXml;

/// <summary>
/// The example piece of both apps (testing log item 26, 2026-11-06): the whole of Für Elise from the Mutopia
/// Project's public-domain edition, written by tools/fur-elise/build.mjs, imports as any MusicXML file does.
/// </summary>
public class FurEliseExampleTests
{
    private static readonly byte[] s_file = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));

    [Fact]
    public void The_whole_piece_imports_as_one_piano_part_with_its_pickup()
    {
        PieceImportResult result = PieceImporter.Import("FurElise.musicxml", s_file);
        ImportedPiece piece = Assert.IsType<ImportedPiece>(result.Piece);
        MusicXmlImport import = piece.MusicXml!;

        MusicXmlPartInfo part = Assert.Single(import.Parts);
        Assert.Equal(2, part.Staves);
        Assert.True(part.HasHands);
        // 905 notes in the edition's MIDI file, three of them graces, which sound as no note of their own.
        Assert.Equal(902, piece.Notes.Count);
        Assert.Equal(3, import.GraceNotes.Count);
        Assert.All(piece.Notes, note => Assert.Equal(Attribution.Imported, note.Hand!.Attribution));

        // A 16th pickup, 3/8 in A minor at 72 quarters a minute.
        Assert.Equal(240, piece.Meter.PickupDuration);
        Assert.Equal(new TimeSignature(3, 8), piece.Meter.GetBar(1).Signature);
        Assert.Equal(0, Assert.Single(piece.Keys).Fifths);
        Assert.Equal(106, import.Measures.Count);
    }
}