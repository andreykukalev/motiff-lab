using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

using static MotiffLab.MusicCore.Tests.Import.Scan.ScanRepairTests;

namespace MotiffLab.MusicCore.Tests.Import.Scan;

/// <summary>
/// Several engines' readings of a page merged staff by staff, each bar with its level and the engines that gave each staff
/// (ADR 0035, section 5; v1.3, c-1a). The readings are written here, in 4/4: four quarters in the right hand over a whole
/// note in the left, the pitches telling the bars apart.
/// </summary>
public class ScanMergeTests
{
    private static readonly string[] First = ["C", "D", "E", "F"];
    private static readonly string[] Second = ["G", "A", "B", "C"];
    private static readonly string[] Third = ["E", "D", "C", "D"];

    /// <summary>A bar: the right hand's quarters (an empty step leaves its quarter out), the left hand's whole C3; <paramref name="quarter"/> divisions to the quarter.</summary>
    private static string Bar(string[] right, int quarter = 2) =>
        string.Concat(right.Select(step => step.Length == 0 ? "" : Note(step, 5, quarter, 1)))
        + Backup(quarter * right.Count(step => step.Length > 0)) + Note("C", 3, 4 * quarter, 2);

    private static ScanReading Reading(string engine, params string[][] bars) => new(engine, Score(2, "4", [.. bars.Select(bar => Bar(bar))]));

    private static ImportedPiece Import(byte[] score) => PieceImporter.Import("scan.musicxml", score).Piece!;

    /// <summary>The pitches the right hand plays in a bar of the merged score, from 1.</summary>
    private static int[] Right(ScanMergedPage page, int bar)
    {
        ImportedPiece piece = Import(page.MusicXml);
        MusicXmlMeasure measure = piece.MusicXml!.Measures[bar - 1];
        return [.. piece.MusicXml.SourceNotes.Where(note => note.MeasureIndex == measure.Index && note.Staff == 1).OrderBy(note => note.Onset).Select(note => note.Pitch)];
    }

    private static string[][] Engines(ScanMergedBar bar) => [.. bar.Staves.Select(staff => staff.Engines.ToArray())];

    [Fact]
    public void Each_staff_is_taken_from_the_reading_two_or_more_engines_agree_on()
    {
        ScanMergedPage page = ScanMerge.Merge(
        [
            Reading("homr", First, ["G", "A", "B", "D"], Third),
            Reading("copisteria", First, Second, Third),
            Reading("audiveris", First, Second, Third),
        ]);

        Assert.Equal([79, 81, 83, 72], Right(page, 2));
        Assert.Equal([ScanLevel.Sure, ScanLevel.Probable, ScanLevel.Sure], page.Bars.Select(bar => bar.Level));
        Assert.Equal([["copisteria", "audiveris"], ["homr", "copisteria", "audiveris"]], Engines(page.Bars[1]));
        Assert.Equal(["homr", "copisteria", "audiveris"], page.Engines);
        Assert.All(Import(page.MusicXml).MusicXml!.Measures, measure => Assert.Equal(1920, measure.Span.Duration));
    }

    [Fact]
    public void A_staff_from_a_reading_in_other_divisions_is_written_in_divisions_both_share()
    {
        ScanReading copisteria = new("copisteria", Score(4, "4", Bar(First, 4), Bar(Second, 4), Bar(Third, 4)));

        ScanMergedPage page = ScanMerge.Merge([Reading("homr", First, ["G", "A", "B", "D"], Third), copisteria, Reading("audiveris", First, Second, Third)]);

        ImportedPiece piece = Import(page.MusicXml);
        Assert.Equal([79, 81, 83, 72], Right(page, 2));
        Assert.Equal([1920L, 1920L, 1920L], piece.MusicXml!.Measures.Select(measure => measure.Span.Duration));
        Assert.Equal(1920 + (3 * 480), piece.Notes.Single(note => note.Pitch.MidiNumber == 72 && note.Span.Onset.Value is >= 1920 and < 3840).Span.Onset.Value);
        Assert.Contains("<divisions>4</divisions>", Encoding.UTF8.GetString(page.MusicXml), StringComparison.Ordinal);
    }

    [Fact]
    public void Two_engines_that_differ_leave_the_bar_to_check_and_the_first_engine_s_reading_is_taken()
    {
        ScanMergedPage page = ScanMerge.Merge([Reading("copisteria", First, Second), Reading("homr 0.7.0", First, ["G", "A", "B", "D"])]);

        Assert.Equal([ScanLevel.Probable, ScanLevel.Check], page.Bars.Select(bar => bar.Level));
        Assert.Equal([79, 81, 83, 74], Right(page, 2));
        Assert.Equal([["homr 0.7.0"], ["homr 0.7.0", "copisteria"]], Engines(page.Bars[1]));
        Assert.Equal(["homr 0.7.0", "copisteria"], page.Engines);
    }

    [Fact]
    public void Without_agreement_the_reading_that_adds_up_is_taken_before_the_first_engine_s()
    {
        ScanMergedPage page = ScanMerge.Merge([Reading("homr", First, ["G", "A", "B", ""]), Reading("copisteria", First, Second)]);

        Assert.Equal(ScanLevel.Check, page.Bars[1].Level);
        Assert.Equal([79, 81, 83, 72], Right(page, 2));
        Assert.Equal(["copisteria"], page.Bars[1].Staves[0].Engines);
    }

    [Fact]
    public void Two_against_two_is_settled_by_the_reading_that_adds_up_then_by_the_first_engine()
    {
        string[] short3 = ["G", "A", "B", ""];
        ScanMergedPage tied = ScanMerge.Merge(
            [Reading("homr", First, ["G", "A", "B", "D"]), Reading("copisteria", First, ["G", "A", "B", "D"]), Reading("audiveris", First, Second), Reading("transcoda", First, Second)]);
        ScanMergedPage shorter = ScanMerge.Merge(
            [Reading("homr", First, short3), Reading("copisteria", First, short3), Reading("audiveris", First, Second), Reading("transcoda", First, Second)]);

        Assert.Equal([79, 81, 83, 74], Right(tied, 2));
        Assert.Equal(ScanLevel.Probable, tied.Bars[1].Level);
        Assert.Equal(["homr", "copisteria"], tied.Bars[1].Staves[0].Engines);
        Assert.Equal([79, 81, 83, 72], Right(shorter, 2));
        Assert.Equal(["audiveris", "transcoda"], shorter.Bars[1].Staves[0].Engines);
    }

    [Fact]
    public void A_reading_that_lost_a_bar_still_pairs_the_bars_after_it()
    {
        ScanMergedPage page = ScanMerge.Merge([Reading("homr", First, Second, Third), Reading("copisteria", First, Second, Third), Reading("audiveris", First, Third)]);
        ScanMergedPage backbone = ScanMerge.Merge([Reading("homr", First, Third), Reading("copisteria", First, Second, Third), Reading("audiveris", First, Second, Third)]);

        Assert.Equal([ScanLevel.Sure, ScanLevel.Probable, ScanLevel.Sure], page.Bars.Select(bar => bar.Level));
        Assert.Equal([["homr", "copisteria"], ["homr", "copisteria"]], Engines(page.Bars[1]));
        Assert.Equal(3, backbone.Bars.Count);
        Assert.Equal([79, 81, 83, 72], Right(backbone, 2));
        Assert.Equal([ScanLevel.Sure, ScanLevel.Probable, ScanLevel.Sure], backbone.Bars.Select(bar => bar.Level));
    }

    [Fact]
    public void One_reading_alone_leaves_every_bar_to_check()
    {
        ScanMergedPage page = ScanMerge.Merge([Reading("audiveris", First, Second)]);

        Assert.All(page.Bars, bar => Assert.Equal(ScanLevel.Check, bar.Level));
        Assert.Equal([["audiveris"], ["audiveris"]], Engines(page.Bars[0]));
        Assert.Equal([79, 81, 83, 72], Right(page, 2));
    }

    [Fact]
    public void A_bar_no_two_engines_agree_on_becomes_a_Check_flag_the_project_file_keeps()
    {
        ScanMergedPage page = ScanMerge.Merge([Reading("homr", First, ["G", "A", "B", "D"]), Reading("copisteria", First, Second)]);
        ImportedPiece piece = Import(page.MusicXml);
        ScanCheck check = ScanCheck.Of(piece);

        Assert.Equal([1], piece.MusicXml!.EnginesDisagree);
        Assert.Empty(check.Bars[0].Flags);
        Assert.Equal([ScanFlag.EnginesDisagree], check.Bars[1].Flags);
        Assert.Equal([1, 3], Import(ScanJoin.Join([page.MusicXml, page.MusicXml])).MusicXml!.EnginesDisagree);

        var facade = new MusicCoreFacade();
        Assert.DoesNotContain("error", facade.Open("Photos.pdf", "%PDF-1.4\n%%EOF\n"u8.ToArray(), """{"id": "p_scan", "photos": 1, "now": "2026-11-24T12:00:00Z"}"""), StringComparison.Ordinal);
        string request = JsonSerializer.Serialize(new { musicXml = Convert.ToBase64String(page.MusicXml), engine = "homr 0.7.0, copisteria", pages = 1, now = "2026-11-24T12:01:00Z" });
        using JsonDocument read = JsonDocument.Parse(facade.Call(1, "document.read", request));
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(read.RootElement.GetProperty("file").GetString()!));

        Assert.Equal([new CheckFlag(check.Bars[1].Bar, CheckSource.Scan, CheckReason.EnginesDisagree, false)], project.Check!);
        Assert.Equal(project.Check, ProjectFile.Read(ProjectFile.Write(project)).Check);
        Assert.Contains("\"reason\": \"EnginesDisagree\"", Encoding.UTF8.GetString(ProjectFile.Write(project)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_merge_is_deterministic_and_said_in_the_score_s_encoding()
    {
        ScanReading[] readings = [Reading("homr", First, ["G", "A", "B", "D"]), Reading("copisteria", First, Second), Reading("audiveris", First, Second)];

        byte[] once = ScanMerge.Merge(readings).MusicXml;

        Assert.Equal(once, ScanMerge.Merge(readings).MusicXml);
        Assert.Contains("Motiff Lab scan merge: each staff of each bar from the reading most of homr, copisteria, audiveris agree on; 0 of 2 bars to check.", Encoding.UTF8.GetString(once), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ScanMerge.Merge([]));
        Assert.Throws<ArgumentException>(() => ScanMerge.Merge([new ScanReading("homr", "<score"u8.ToArray())]));
    }

    [Fact]
    public void The_facade_merges_a_page_sent_as_readings_and_answers_its_bars_levels_beside_a_page_sent_as_one()
    {
        var facade = new MusicCoreFacade();
        static string Text(ScanReading reading) => Encoding.UTF8.GetString(reading.MusicXml);
        string request = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["pages"] = new object[]
            {
                new[]
                {
                    new Dictionary<string, string> { ["engine"] = "homr", ["musicxml"] = Text(Reading("homr", First, ["G", "A", "B", "D"])) },
                    new Dictionary<string, string> { ["engine"] = "copisteria", ["musicxml"] = Text(Reading("copisteria", First, Second)) },
                    new Dictionary<string, string> { ["engine"] = "audiveris", ["musicxml"] = Text(Reading("audiveris", First, Second)) },
                },
                Text(Reading("homr", Third)),
            },
        });

        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "scan.join", request));

        JsonElement[] pages = [.. answer.RootElement.GetProperty("pages").EnumerateArray()];
        JsonElement merge = pages[0].GetProperty("merge");
        Assert.Equal(["homr", "copisteria", "audiveris"], merge.GetProperty("engines").EnumerateArray().Select(engine => engine.GetString()));
        Assert.Equal([(1, "sure"), (2, "probable")], merge.GetProperty("bars").EnumerateArray().Select(bar => (bar.GetProperty("bar").GetInt32(), bar.GetProperty("level").GetString())));
        JsonElement staff = merge.GetProperty("bars")[1].GetProperty("staves")[0];
        Assert.Equal((1, 1, "copisteria,audiveris"), (staff.GetProperty("part").GetInt32(), staff.GetProperty("staff").GetInt32(), string.Join(",", staff.GetProperty("engines").EnumerateArray().Select(engine => engine.GetString()))));
        Assert.False(pages[1].TryGetProperty("merge", out _));
        Assert.Equal(3, Import(Encoding.UTF8.GetBytes(answer.RootElement.GetProperty("score").GetString()!)).MusicXml!.Measures.Count);

        using JsonDocument empty = JsonDocument.Parse(facade.Call(0, "scan.join", """{"pages": [[]]}"""));
        Assert.Equal("badRequest", empty.RootElement.GetProperty("error").GetString());
        using JsonDocument wrong = JsonDocument.Parse(facade.Call(0, "scan.join", """{"pages": [1]}"""));
        Assert.Equal("badRequest", wrong.RootElement.GetProperty("error").GetString());
    }
}