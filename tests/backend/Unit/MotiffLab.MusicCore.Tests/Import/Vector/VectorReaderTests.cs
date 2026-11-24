using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Import.Vector;
using MotiffLab.MusicCore.Pieces;

using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace MotiffLab.MusicCore.Tests.Import.Vector;

/// <summary>
/// The vector reader (decision record 0029): a PDF a notation program drew is read on the device from its glyphs and lines,
/// a page at a time, into the scan server's MusicXML shape, and joined as scans are. The fixtures are Mutopia's LilyPond
/// engravings of public-domain music (Fixtures/README.md).
/// </summary>
public class VectorReaderTests
{
    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Vector", "Fixtures", name));

    private static ImportedPiece Joined(IReadOnlyList<VectorPageReading> pages) =>
        PieceImporter.Import("scan.musicxml", ScanJoin.Join([.. pages.Select(page => page.MusicXml!)])).Piece!;

    /// <summary>Each time signature as "beats/beat-type@bar", bars from 1.</summary>
    private static string[] Meters(ImportedPiece piece) =>
        [.. piece.MusicXml!.TimeSignatures.Select(time => $"{time.Numerator}/{time.Denominator}@{time.MeasureIndex + 1}")];

    private static int[] FirstPitches(ImportedPiece piece, int count) =>
        [.. piece.Notes.OrderBy(note => note.Span.Onset).ThenByDescending(note => note.Pitch.MidiNumber).Take(count).Select(note => note.Pitch.MidiNumber)];

    [Fact]
    public void Promenade_is_read_on_the_device_with_its_printed_meters()
    {
        IReadOnlyList<VectorPageReading> pages = VectorReader.Read(Fixture("promenade-a4.pdf"));

        Assert.Equal([VectorPageKind.Read, VectorPageKind.Read], pages.Select(page => page.Kind));
        Assert.All(pages, page => Assert.Empty(page.Issues));
        Assert.Equal(24, pages.Sum(page => page.Bars));
        ImportedPiece piece = Joined(pages);
        // As printed: the scan engines read 6/4 everywhere here, or lose the changes after the first page.
        Assert.Equal(["5/4@1", "6/4@2", "5/4@3", "6/4@4", "5/4@5", "6/4@6", "5/4@7", "6/4@8", "5/4@11", "6/4@12"], Meters(piece).Take(10));
        // G F B♭ C F D in B♭ major, the opening phrase.
        Assert.Equal([67, 65, 70, 72, 77, 74], FirstPitches(piece, 6));
        Assert.Equal(-2, piece.Keys[0].Fifths);
    }

    [Fact]
    public void Fur_elise_is_read_in_3_8_with_its_pickup()
    {
        IReadOnlyList<VectorPageReading> pages = VectorReader.Read(Fixture("fur-elise-a4.pdf"));

        Assert.Equal([VectorPageKind.Read, VectorPageKind.Read, VectorPageKind.Read], pages.Select(page => page.Kind));
        ImportedPiece piece = Joined(pages);
        Assert.Equal("3/8@1", Meters(piece)[0]);
        Assert.All(Meters(piece), meter => Assert.StartsWith("3/8@", meter, StringComparison.Ordinal));
        // The pickup of two sixteenths, E D♯, then E D♯ E B D C.
        Assert.Equal(240, piece.MusicXml!.Measures[0].Span.Duration);
        Assert.Equal([76, 75, 76, 75, 76, 71, 74, 72], FirstPitches(piece, 8));
    }

    [Fact]
    public void The_same_pdf_is_read_the_same_way()
    {
        byte[] pdf = Fixture("fur-elise-a4.pdf");

        IReadOnlyList<VectorPageReading> first = VectorReader.Read(pdf), second = VectorReader.Read(pdf);

        Assert.Equal(first.Select(page => page.MusicXml), second.Select(page => page.MusicXml));
    }

    [Fact]
    public void A_page_of_words_has_no_music_and_a_file_that_is_no_pdf_is_refused()
    {
        var builder = new PdfDocumentBuilder();
        PdfPageBuilder page = builder.AddPage(PageSize.A4);
        page.AddText("Notes on the performance", 12, new PdfPoint(60, 700), builder.AddStandard14Font(Standard14Font.Helvetica));

        IReadOnlyList<VectorPageReading> read = VectorReader.Read(builder.Build());

        Assert.Equal(VectorPageKind.Blank, Assert.Single(read).Kind);
        Assert.Null(read[0].MusicXml);
        Assert.Throws<ArgumentException>(() => VectorReader.Read("<score-partwise/>"u8.ToArray()));
    }

    [Theory]
    [InlineData("ABCDEF+Emmentaler-20", "Emmentaler")]
    [InlineData("MScore", "MScore")]
    [InlineData("Bravura", "Smufl")]
    [InlineData("Bravura Text", "None")]
    [InlineData("Opus Std", "Legacy")]
    [InlineData("ABCDEF+Petrucci-0-50", "Legacy")]
    [InlineData("OpusTextStd", "None")]
    [InlineData("Tamburo", "Unknown")]
    [InlineData("ABCDEF+OpusChordsStd", "Unknown")]
    [InlineData("Opus Roman Chords", "Unknown")]
    [InlineData("OpusFiguredBassStd", "Unknown")]
    [InlineData("OpusFunctionSymbolsStd", "Unknown")]
    [InlineData("OpusPercussionStd", "Unknown")]
    [InlineData("ABCDEF+OpusSpecialStd", "LegacySpecial")]
    [InlineData("HelsinkiSpecialStd", "LegacySpecial")]
    [InlineData("TimesNewRomanPSMT", "None")]
    public void A_font_is_known_by_its_name(string font, string family) => Assert.Equal(family, MusicFonts.Of(font).ToString());

    [Fact]
    public void The_facade_reads_a_pdf_and_gives_each_page_its_music_and_report()
    {
        var facade = new MusicCoreFacade();
        string request = JsonSerializer.Serialize(new Dictionary<string, string> { ["pdf"] = Convert.ToBase64String(Fixture("promenade-a4.pdf")) });

        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "scan.vector", request));

        JsonElement root = answer.RootElement;
        Assert.Equal("vector 1", root.GetProperty("engine").GetString());
        JsonElement[] pages = [.. root.GetProperty("pages").EnumerateArray()];
        Assert.Equal(["read", "read"], pages.Select(page => page.GetProperty("kind").GetString()));
        Assert.All(pages, page => Assert.Equal(page.GetProperty("bars").GetInt32(), page.GetProperty("report").GetProperty("measures").GetInt32()));
        Assert.StartsWith("<?xml", pages[0].GetProperty("musicxml").GetString(), StringComparison.Ordinal);

        using JsonDocument notBase64 = JsonDocument.Parse(facade.Call(0, "scan.vector", "{\"pdf\": \"not base64!\"}"));
        Assert.Equal("badRequest", notBase64.RootElement.GetProperty("error").GetString());
        using JsonDocument notPdf = JsonDocument.Parse(facade.Call(0, "scan.vector", "{\"pdf\": \"aGVsbG8=\"}"));
        Assert.Equal("badRequest", notPdf.RootElement.GetProperty("error").GetString());
    }
}