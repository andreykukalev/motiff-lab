using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

using static MotiffLab.MusicCore.Tests.Import.Scan.ScanAssemblyTests;
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

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    public void Homr_s_5_4_over_bars_of_four_quarters_gives_way_to_the_4_4_the_printed_meter_engines_read(int bars)
    {
        // Issue #647: homr writes no upper number it reads, only the median of its bars, and on a page of few bars the
        // repair keeps the 5/4 it wrote; the engines that read the printed signature say 4/4.
        string[][] music = [.. Enumerable.Range(0, bars).Select(index => index % 2 == 0 ? First : Second)];
        ScanReading homr = new("homr", Score(2, "5", [.. music.Select(bar => Bar(bar))]));

        ScanMergedPage page = ScanMerge.Merge([homr, Reading("copisteria", music), Reading("audiveris", music)]);

        ImportedPiece merged = Import(page.MusicXml);
        Assert.Equal(4, merged.Meter.SignatureAt(MotiffLab.MusicCore.Time.Tick.Zero).Numerator);
        Assert.All(merged.MusicXml!.Measures, measure => Assert.Equal(1920, measure.Span.Duration));
        Assert.All(page.Bars, bar => Assert.Equal(ScanLevel.Sure, bar.Level));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void Homr_s_meter_stays_when_the_other_engines_wrote_no_time_signature(int beats)
    {
        // Review 1 of #647: a continuation page prints no signature, and an engine that wrote none is read in 4/4 by
        // default; two such readings are no vote against homr's right 3/4 or 5/4.
        string[] bars = [Quarters(beats, First), Quarters(beats, Second)];
        ScanReading homr = new("homr", Score(2, beats.ToString(System.Globalization.CultureInfo.InvariantCulture), bars));

        ScanMergedPage page = ScanMerge.Merge([homr, new("copisteria", Score(2, null, bars)), new("audiveris", Score(2, null, bars))]);

        ImportedPiece merged = Import(page.MusicXml);
        Assert.Equal(beats, merged.Meter.SignatureAt(MotiffLab.MusicCore.Time.Tick.Zero).Numerator);
        Assert.All(merged.MusicXml!.Measures, measure => Assert.Equal(beats * 480, measure.Span.Duration));
    }

    /// <summary>A bar of <paramref name="beats"/> quarters: the right hand's quarters on <paramref name="steps"/>, from the first again when they run out, over the left hand's C3 held through it.</summary>
    private static string Quarters(int beats, string[] steps) =>
        string.Concat(Enumerable.Range(0, beats).Select(index => Note(steps[index % steps.Length], 5, 2, 1)))
        + Backup(2 * beats) + Note("C", 3, 2 * beats, 2);

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
    public void A_clef_change_and_a_dynamic_inside_a_rewritten_bar_stay_at_their_own_onsets()
    {
        // Bar 2 of homr, the backbone: a wrong right hand, and a left hand of C3, then a treble clef and an f, then E4.
        string left = Note("C", 3, 4, 2) + "<attributes><clef number=\"2\"><sign>G</sign><line>2</line></clef></attributes>"
            + "<direction><direction-type><dynamics><f/></dynamics></direction-type><staff>2</staff></direction>" + Note("E", 4, 4, 2);
        static string Hand(string[] steps) => string.Concat(steps.Select(step => Note(step, 5, 2, 1))) + Backup(8);
        ScanReading homr = new("homr", Score(2, "4", Bar(First), Hand(["G", "A", "B", "D"]) + left));
        ScanReading copisteria = new("copisteria", Score(4, "4", Bar(First, 4), string.Concat(Second.Select(step => Note(step, 5, 4, 1))) + Backup(16) + Note("C", 3, 8, 2) + Note("E", 4, 8, 2)));
        ScanReading audiveris = new("audiveris", Score(2, "4", Bar(First), Hand(Second) + Note("C", 3, 4, 2) + Note("E", 4, 4, 2)));

        ScanMergedPage page = ScanMerge.Merge([homr, copisteria, audiveris]);

        Assert.Equal([79, 81, 83, 72], Right(page, 2));
        ScanRepair.Bar bar = ScanRepair.Bars(ScanRepair.Parse(page.MusicXml)!.Element("part")!)[1];
        Assert.Equal(4, bar.Divisions);
        Assert.Equal([("divisions", 0L), ("clef", 8L), ("dynamics", 8L)], bar.Placed().Select(other => (other.Element.Descendants().First(child => child.Name.LocalName is "divisions" or "clef" or "dynamics").Name.LocalName, other.Onset)));
    }

    [Fact]
    public void A_tie_into_a_bar_taken_from_another_engine_ends_where_its_note_does()
    {
        string tied = "<note><pitch><step>F</step><octave>5</octave></pitch><duration>2</duration><tie type=\"start\"/><voice>1</voice><staff>1</staff><notations><tied type=\"start\"/></notations></note>";
        string stop = "<note><pitch><step>F</step><octave>5</octave></pitch><duration>2</duration><tie type=\"stop\"/><voice>1</voice><staff>1</staff><notations><tied type=\"stop\"/></notations></note>";
        string first = Note("C", 5, 2, 1) + Note("D", 5, 2, 1) + Note("E", 5, 2, 1) + tied + Backup(8) + Note("C", 3, 8, 2);
        ScanReading homr = new("homr", Score(2, "4", first, stop + Note("A", 5, 2, 1) + Note("B", 5, 2, 1) + Note("C", 5, 2, 1) + Backup(8) + Note("C", 3, 8, 2)));

        ScanMergedPage page = ScanMerge.Merge([homr, Reading("copisteria", First, Second), Reading("audiveris", First, Second)]);

        ImportedPiece piece = Import(page.MusicXml);
        Assert.Equal([79, 81, 83, 72], Right(page, 2));
        Assert.Equal(480, piece.Notes.Single(note => note.Pitch.MidiNumber == 77).Span.Duration);
        Assert.Equal(10, piece.Notes.Count);
    }

    [Fact]
    public void A_reading_that_is_not_music_is_left_out_and_the_page_merges_on_the_rest()
    {
        ScanMergedPage page = ScanMerge.Merge([new ScanReading("homr", "<score"u8.ToArray()), Reading("copisteria", First, Second), Reading("audiveris", First, Second), new ScanReading("transcoda", "<score-partwise/>"u8.ToArray())]);

        Assert.Equal(["copisteria", "audiveris"], page.Engines);
        Assert.All(page.Bars, bar => Assert.Equal(ScanLevel.Probable, bar.Level));
        Assert.Throws<ArgumentException>(() => ScanMerge.Merge([new ScanReading("homr", "<score"u8.ToArray()), new ScanReading("copisteria", "<score-partwise/>"u8.ToArray())]));
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

        using JsonDocument unread = JsonDocument.Parse(facade.Call(0, "scan.join", """{"pages": [[{"engine": "homr", "musicxml": "<score"}]]}"""));
        Assert.Equal("badRequest", unread.RootElement.GetProperty("error").GetString());
        using JsonDocument empty = JsonDocument.Parse(facade.Call(0, "scan.join", """{"pages": [[]]}"""));
        Assert.Equal("badRequest", empty.RootElement.GetProperty("error").GetString());
        using JsonDocument wrong = JsonDocument.Parse(facade.Call(0, "scan.join", """{"pages": [1]}"""));
        Assert.Equal("badRequest", wrong.RootElement.GetProperty("error").GetString());
    }

    /// <summary>
    /// The heads the page shows in one bar of evidence: the right hand's quarters at octave 5 in the treble clef (a step a
    /// staff position from B4), a quarter's length given (a half note for a step after a "/"), and the left hand's whole C3.
    /// </summary>
    private static object[] Page(int bar, params string[] right) =>
    [
        .. right.Select((step, i) => Head(bar, 1, 10 + (20 * i), (5 * 7) + "CDEFGAB".IndexOf(step[^1], StringComparison.Ordinal) - 34, step.StartsWith('/') ? "half" : "quarter")),
        Head(bar, 2, 10, -1, "whole"),
    ];

    [Fact]
    public void Without_agreement_the_reading_the_page_s_symbols_assemble_is_taken()
    {
        ScanReading[] readings = [Reading("homr", First, ["G", "A", "B", "D"]), Reading("copisteria", First, Second)];

        ScanMergedPage page = ScanMerge.Merge(readings, Evidence(2, [.. Page(0, First), .. Page(1, Second)]));

        Assert.Equal([79, 81, 83, 72], Right(page, 2));
        Assert.Equal(ScanLevel.Check, page.Bars[1].Level);
        Assert.Equal([["copisteria"], ["homr", "copisteria"]], Engines(page.Bars[1]));
        Assert.Equal([79, 81, 83, 74], Right(ScanMerge.Merge(readings), 2));
        Assert.Contains("without agreement, the reading the page's symbols agree with; 1 of 2 bars to check.", Encoding.UTF8.GetString(page.MusicXml), StringComparison.Ordinal);
        Assert.Equal(page.MusicXml, ScanMerge.Merge(readings, Evidence(2, [.. Page(0, First), .. Page(1, Second)])).MusicXml);
    }

    [Fact]
    public void Two_against_two_is_settled_by_the_page_s_symbols_before_the_first_engine()
    {
        ScanMergedPage page = ScanMerge.Merge(
            [Reading("homr", First, ["G", "A", "B", "D"]), Reading("copisteria", First, ["G", "A", "B", "D"]), Reading("audiveris", First, Second), Reading("transcoda", First, Second)],
            Evidence(2, [.. Page(0, First), .. Page(1, Second)]));

        Assert.Equal([79, 81, 83, 72], Right(page, 2));
        Assert.Equal(ScanLevel.Probable, page.Bars[1].Level);
        Assert.Equal(["audiveris", "transcoda"], page.Bars[1].Staves[0].Engines);
    }

    [Fact]
    public void When_the_symbols_do_not_fill_the_bar_the_reading_whose_heads_agree_best_with_the_page_is_taken()
    {
        ScanMergedPage page = ScanMerge.Merge([Reading("homr", First, ["G", "A", "B", "D"]), Reading("copisteria", First, Second)], Evidence(2, [.. Page(0, First), .. Page(1, "G", "A", "B", "/C")]));

        Assert.Equal([79, 81, 83, 72], Right(page, 2));
        Assert.Equal(["copisteria"], page.Bars[1].Staves[0].Engines);
    }

    [Fact]
    public void The_heads_on_the_page_choose_only_among_readings_that_add_up()
    {
        string longer = Note("G", 5, 2, 1) + Note("A", 5, 2, 1) + Note("B", 5, 2, 1) + Note("C", 5, 4, 1) + Backup(10) + Note("C", 3, 8, 2);
        ScanReading homr = new("homr", Score(2, "4", Bar(First), longer));

        ScanMergedPage page = ScanMerge.Merge([homr, Reading("copisteria", First, ["G", "A", "B", "D"])], Evidence(2, [.. Page(0, First), .. Page(1, "G", "A", "B", "/C")]));

        Assert.Equal([79, 81, 83, 74], Right(page, 2));
        Assert.Equal(["copisteria"], page.Bars[1].Staves[0].Engines);
    }

    [Fact]
    public void The_page_s_bars_are_paired_with_the_readings_by_their_heads_when_the_page_found_another_number()
    {
        ScanMergedPage page = ScanMerge.Merge([Reading("homr", First, ["G", "A", "B", "D"]), Reading("copisteria", First, Second)], Evidence(3, [.. Page(1, First), .. Page(2, "G", "A", "B", "/C")]));

        Assert.Equal([79, 81, 83, 72], Right(page, 2));
    }

    [Fact]
    public void A_key_change_inside_a_bar_whose_staves_come_from_another_reading_stays_at_its_onset()
    {
        string homrBar = Note("G", 5, 2, 1) + Note("A", 5, 2, 1) + "<attributes><key><fifths>-1</fifths></key></attributes>" + Note("B", 5, 2, 1) + Note("D", 5, 2, 1) + Backup(8) + Note("C", 3, 8, 2);
        string otherBar = string.Concat(Second.Select(step => Note(step, 5, 2, 1))) + Backup(8) + Note("E", 3, 8, 2);
        ScanReading homr = new("homr", Score(2, "4", Bar(First), homrBar));

        ScanMergedPage page = ScanMerge.Merge([homr, new ScanReading("copisteria", Score(2, "4", Bar(First), otherBar)), new ScanReading("audiveris", Score(2, "4", Bar(First), otherBar))]);

        ScanRepair.Bar bar = ScanRepair.Bars(ScanRepair.Parse(page.MusicXml)!.Element("part")!)[1];
        Assert.Equal([("key", 4L)], bar.Placed().Where(other => other.Element.Element("key") is not null).Select(other => ("key", other.Onset)));
        Assert.Equal([1, -1], Import(page.MusicXml).MusicXml!.KeySignatures.Select(key => key.Fifths));
        Assert.Equal([79, 81, 83, 72], Right(page, 2));
    }

    [Fact]
    public void The_facade_merges_a_page_sent_with_its_evidence()
    {
        var facade = new MusicCoreFacade();
        static string Text(ScanReading reading) => Encoding.UTF8.GetString(reading.MusicXml);
        object[] readings =
        [
            new Dictionary<string, string> { ["engine"] = "homr", ["musicxml"] = Text(Reading("homr", First, ["G", "A", "B", "D"])) },
            new Dictionary<string, string> { ["engine"] = "copisteria", ["musicxml"] = Text(Reading("copisteria", First, Second)) },
        ];
        using JsonDocument page = JsonDocument.Parse(EvidenceJson(2, [.. Page(0, First), .. Page(1, Second)]));
        string request = JsonSerializer.Serialize(new { pages = new[] { new { readings, evidence = page.RootElement } } });

        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "scan.join", request));

        JsonElement staff = answer.RootElement.GetProperty("pages")[0].GetProperty("merge").GetProperty("bars")[1].GetProperty("staves")[0];
        Assert.Equal(["copisteria"], staff.GetProperty("engines").EnumerateArray().Select(engine => engine.GetString()));
        using JsonDocument without = JsonDocument.Parse(facade.Call(0, "scan.join", JsonSerializer.Serialize(new { pages = new[] { new { readings } } })));
        Assert.Equal(["homr"], without.RootElement.GetProperty("pages")[0].GetProperty("merge").GetProperty("bars")[1].GetProperty("staves")[0].GetProperty("engines").EnumerateArray().Select(engine => engine.GetString()));
        using JsonDocument wrong = JsonDocument.Parse(facade.Call(0, "scan.join", JsonSerializer.Serialize(new { pages = new[] { new { readings, evidence = JsonDocument.Parse("""{"evidence": 2}""").RootElement } } })));
        Assert.Equal("badRequest", wrong.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void On_a_real_page_its_bars_pair_in_order_and_the_assembly_agrees_with_an_engine_on_most_staves()
    {
        // Für Elise's first page (public domain, the example's page) as the scan server's job gave it: homr's and
        // copisteria's readings and the page's evidence (s-2, 2026-11-24).
        using JsonDocument job = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "fur-elise-page1-job.json")));
        ScanReading[] sent = [.. job.RootElement.GetProperty("engines").EnumerateObject()
            .Select(engine => new ScanReading(engine.Name, Encoding.UTF8.GetBytes(engine.Value.GetProperty("musicxml").GetString()!)))];
        ScanEvidence evidence = ScanEvidence.Parse(job.RootElement.GetProperty("evidence").GetRawText());
        ScanMerge.Reading[] readings = [.. sent.Select((reading, index) => ScanMerge.Reading.Of(reading, index)!)];
        ScanMerge.Page page = ScanMerge.Page.Of(evidence, readings[0])!;

        Assert.Equal([32, 32], readings.Select(reading => reading.Measures));
        Assert.Equal(Enumerable.Range(0, 32).Select(bar => (int?)bar), Enumerable.Range(0, 32).Select(page.BarOf));
        int agreeing = Enumerable.Range(0, 32).Sum(measure => new[] { 1, 2 }.Count(staff => page.Assembled(measure, 0, staff) is { } assembled
            && readings.Any(reading => reading.Content(measure, 0, staff) == assembled)));
        Assert.True(agreeing >= 59, $"The assembly agrees with an engine on {agreeing} of 64 staves.");
        ScanMergedPage merged = ScanMerge.Merge(sent, evidence);
        Assert.Equal(32, merged.Bars.Count);
        Assert.Equal(merged.MusicXml, ScanMerge.Merge(sent, evidence).MusicXml);
    }
}