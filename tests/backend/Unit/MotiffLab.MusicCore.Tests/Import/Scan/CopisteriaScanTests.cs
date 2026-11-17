using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;

namespace MotiffLab.MusicCore.Tests.Import.Scan;

/// <summary>
/// Scores read by copisteria, the second engine on the scan server (v0.5.8, once behind the experiment «Выбор движка
/// распознавания», since v0.6.5 the setting scan.engine): its MusicXML goes through the same repair, join and check as
/// homr's. Unlike homr, it reads the printed time signature, so a real change of meter it writes comes through as it
/// wrote it. The fixtures are its readings of Mutopia's engravings of public-domain music (Für Elise page 1;
/// Mussorgsky's Promenade, two pages in 5/4 and 6/4).
/// </summary>
public class CopisteriaScanTests
{
    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", name));

    private static ImportedPiece Import(byte[] score) => PieceImporter.Import("scan.musicxml", score).Piece!;

    /// <summary>Each time signature as "beats/beat-type@bar", bars from 1.</summary>
    private static string[] Meters(ImportedPiece piece) =>
        [.. piece.MusicXml!.TimeSignatures.Select(time => $"{time.Numerator}/{time.Denominator}@{time.MeasureIndex + 1}")];

    [Fact]
    public void Its_reading_of_fur_elise_imports_and_every_note_belongs_to_one_bar()
    {
        ImportedPiece piece = Import(ScanRepair.Repair(Fixture("copisteria-fur-elise-page1.musicxml")));
        ScanCheck check = ScanCheck.Of(piece);

        Assert.Equal(32, check.Bars.Count);
        // homr's reading of the same page is flagged at bars 8, 16, 21, 27, 29, 32 and 36 (ScanCheckTests); copisteria's
        // only where the page itself asks for a look: the short bar 8 before the repeat, and the grace notes of bars 25 and 28.
        Assert.Equal([8, 25, 28], check.Flagged.Select(bar => bar.Bar));
        Assert.Equal([ScanFlag.BarTooShort], check.Find(8)!.Flags);
        Assert.Equal(piece.Notes.Select(note => note.Id).Order(), check.Bars.SelectMany(bar => bar.NoteIds).Order());
    }

    [Fact]
    public void The_meters_it_reads_on_promenade_stay_as_it_wrote_them_read_a_page_a_request()
    {
        byte[][] pages = [Fixture("copisteria-promenade-page-1.musicxml"), Fixture("copisteria-promenade-page-2.musicxml")];

        ImportedPiece joined = Import(ScanJoin.Join(pages));

        // As printed, and as copisteria wrote them on each page: homr wrote 6/4 everywhere here.
        Assert.Equal(["5/4@1", "6/4@2", "5/4@3", "6/4@4", "5/4@5", "6/4@6", "5/4@7", "6/4@8", "5/4@11", "6/4@12"], Meters(joined));
    }
}