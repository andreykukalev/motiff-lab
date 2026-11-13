using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Import.Scan;

/// <summary>
/// A 4/4 piece the scan engine reads as 5/4 (issue #495 BUG-03, after #479 item 20): the time signature comes back 4/4
/// however the scan reaches the notes, including a PDF of up to four pages read in one request, which never went through
/// <see cref="ScanJoin"/>. The fixture is the engine's own output for a real page (see its header).
/// </summary>
public class ScanMeterTests
{
    private const int Quarter = 480;

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    private static byte[] Page() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-grace-eighths-4-4.musicxml"));

    private static ImportedPiece Import(byte[] score) => PieceImporter.Import("scan.musicxml", score).Piece!;

    private static (int Numerator, int Denominator) Meter(ImportedPiece piece) =>
        (piece.MusicXml!.TimeSignatures[0].Numerator, piece.MusicXml.TimeSignatures[0].Denominator);

    private static long[] Quarters(ImportedPiece piece) => [.. piece.MusicXml!.Measures.Select(measure => measure.Span.Duration * 2 / Quarter)];

    [Fact]
    public void The_engine_writes_5_4_for_the_4_4_page_and_the_repair_gives_4_4_back()
    {
        ImportedPiece read = Import(Page());
        ImportedPiece repaired = Import(ScanRepair.Repair(Page()));

        Assert.Equal((5, 4), Meter(read));
        Assert.Equal((4, 4), Meter(repaired));
        Assert.Single(repaired.MusicXml!.TimeSignatures);

        // In half quarters: bars 3-5 and 8 come back to four quarters; bar 6 (a left-hand note on the upper staff) and bar 7
        // (the left hand an eighth late) are the engine's misreadings of notes, kept as read for the player to check.
        Assert.Equal([8, 8, 12, 12, 12, 12, 9, 12], Quarters(read));
        Assert.Equal([8, 8, 8, 8, 8, 12, 9, 8], Quarters(repaired));
        Assert.Equal(
            ["6", "7"],
            ScanCheck.Of(repaired).Bars.Where(bar => bar.Flags.Contains(ScanFlag.BarTooLong)).Select(bar => bar.Measure));
    }

    [Fact]
    public void Grace_notes_are_kept_and_never_count_toward_the_length_of_a_bar()
    {
        ImportedPiece read = Import(Page());
        ImportedPiece repaired = Import(ScanRepair.Repair(Page()));

        Assert.NotEmpty(read.MusicXml!.GraceNotes);
        Assert.Equal(read.MusicXml.GraceNotes.Count, repaired.MusicXml!.GraceNotes.Count);
        Assert.Equal(read.Notes.Count, repaired.Notes.Count);

        // A bar read right: eighth rest, three eighths, a grace note and four eighths are four quarters, not four and a half.
        Assert.Equal(4 * Quarter, repaired.MusicXml.Measures[0].Span.Duration);
        Assert.Contains(repaired.MusicXml.GraceNotes, grace => repaired.MusicXml.Measures[0].Span.Contains(grace.At));
    }

    [Fact]
    public void A_pdf_read_in_one_request_is_repaired_when_its_notes_go_into_the_piece()
    {
        var facade = new MusicCoreFacade();
        Assert.False(JsonDocument.Parse(facade.Open("Piece.pdf", Pdf, """{"id": "p_meter"}""")).RootElement.TryGetProperty("error", out _));
        string request = JsonSerializer.Serialize(new { musicXml = Convert.ToBase64String(Page()), engine = "homr 0.7.0", pages = 1, now = "2026-11-13T12:00:00Z" });

        JsonElement answer = JsonDocument.Parse(facade.Call(1, "document.read", request)).RootElement;

        Assert.False(answer.TryGetProperty("error", out _), answer.ToString());
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(answer.GetProperty("file").GetString()!));
        Assert.Equal(ScanRepair.Repair(Page()), project.Source);
        ImportedPiece piece = Import(project.Source);
        Assert.Equal((4, 4), Meter(piece));
        Assert.Contains("inferred from the bar contents", Encoding.UTF8.GetString(project.Source), StringComparison.Ordinal);

        // What the scan read stays detected, never imported fact.
        Assert.All(OpenedProject.Open(project).Notes, note => Assert.Equal(Provenance.Detected, note.Attribution.Provenance));
    }

    [Fact]
    public void The_notes_of_a_kept_pdf_added_as_a_version_are_repaired_too()
    {
        static string Performance(byte[] musicXml)
        {
            var facade = new MusicCoreFacade();
            facade.Open("Piece.musicxml", ScanRepair.Repair(Page()), """{"id": "p_version"}""");
            facade.Call(1, "sources.document", JsonSerializer.Serialize(new { pdf = Convert.ToBase64String(Pdf), pages = 1 }));
            string answer = facade.Call(1, "sources.documentNotes", JsonSerializer.Serialize(new { musicXml = Convert.ToBase64String(musicXml), engine = "homr 0.7.0", pages = 1 }));
            Assert.DoesNotContain("\"error\"", answer, StringComparison.Ordinal);
            return answer;
        }

        // The engine's page and the page already repaired give the same version: the page is repaired on the way in.
        Assert.Equal(Performance(ScanRepair.Repair(Page())), Performance(Page()));
    }

    [Fact]
    public void Each_way_a_scan_reaches_the_notes_gives_the_same_score()
    {
        byte[] once = ScanRepair.Repair(Page());

        Assert.Equal(once, ScanJoin.Join([Page()]));
        Assert.Same(once, ScanRepair.Repair(once));
        Assert.Equal(once, ScanRepair.Repair(Page()));
    }
}