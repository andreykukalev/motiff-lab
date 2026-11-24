using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Projects;

using static MotiffLab.MusicCore.Tests.Import.Scan.ScanAssemblyTests;
using static MotiffLab.MusicCore.Tests.Import.Scan.ScanRepairTests;

namespace MotiffLab.MusicCore.Tests.Import.Scan;

/// <summary>
/// A merged bar to check with the engines' other readings of it and its place on the page (ADR 0035, section 6; v1.3
/// i-2), and a reading chosen as the player's corrections of that bar only. In 4/4: four quarters in the right hand over a
/// whole C3 in the left, the pitches telling the readings apart.
/// </summary>
public class ScanVariantsTests
{
    private static readonly string[] First = ["C", "D", "E", "F"];
    private static readonly string[] Second = ["G", "A", "B", "C"];

    private static string Bar(string[] right) =>
        string.Concat(right.Select(step => Note(step, 5, 2, 1))) + Backup(2 * right.Length) + Note("C", 3, 8, 2);

    private static ScanReading Reading(string engine, params string[][] bars) => new(engine, Score(2, "4", [.. bars.Select(Bar)]));

    /// <summary>Three engines that agree on bar 1 and on bar 2's left hand, and each read bar 2's right hand otherwise.</summary>
    private static ScanReading[] Readings() =>
        [Reading("homr", First, ["G", "A", "B", "D"]), Reading("copisteria", First, Second), Reading("audiveris", First, ["G", "A", "B", "E"])];

    [Fact]
    public void A_bar_to_check_gives_the_other_readings_of_its_staves_to_check_and_the_others_none()
    {
        ScanMergedPage page = ScanMerge.Merge(Readings());

        Assert.Equal([ScanLevel.Sure, ScanLevel.Check], page.Bars.Select(bar => bar.Level));
        Assert.Empty(page.Bars[0].Variants);
        Assert.Null(page.Bars[1].Crop);
        ScanVariant[] variants = [.. page.Bars[1].Variants];
        Assert.Equal([["copisteria"], ["audiveris"]], variants.Select(variant => variant.Engines.ToArray()));
        ScanVariantStaff staff = Assert.Single(variants[0].Staves);
        Assert.Equal((1, 1), (staff.Part, staff.Staff));
        Assert.Equal([new(0, 79, 480), new(480, 81, 480), new(960, 83, 480), new(1440, 72, 480)], staff.Notes);
        Assert.Equal(76, variants[1].Staves[0].Notes[^1].Pitch);
    }

    [Fact]
    public void Engines_that_read_the_same_other_notes_are_one_variant_and_the_reading_taken_is_none()
    {
        ScanMergedPage page = ScanMerge.Merge([Reading("homr", First, ["G", "A", "B", "D"]), Reading("copisteria", First, Second)]);

        ScanVariant variant = Assert.Single(page.Bars[1].Variants);
        Assert.Equal(["copisteria"], variant.Engines);
    }

    [Fact]
    public void With_the_page_s_evidence_a_bar_to_check_has_its_box_on_the_page_image()
    {
        ScanMergedPage page = ScanMerge.Merge(Readings(), Evidence(2));

        Assert.Equal(new ScanCrop(new ScanBox(100, 80, 200, 220), new ScanImage(1000, 400, 0)), page.Bars[1].Crop);
        Assert.Null(page.Bars[0].Crop);
    }

    [Fact]
    public void The_facade_answers_variants_crop_and_the_fingerprint_check_finds_and_a_variant_chosen_is_the_player_s_corrections()
    {
        var join = new MusicCoreFacade();
        object[] readings = [.. Readings().Select(reading => new Dictionary<string, string> { ["engine"] = reading.Engine, ["musicxml"] = Encoding.UTF8.GetString(reading.MusicXml) })];
        using JsonDocument evidence = JsonDocument.Parse(EvidenceJson(2));
        using JsonDocument joined = JsonDocument.Parse(join.Call(0, "scan.join", JsonSerializer.Serialize(new { pages = new[] { new { readings, evidence = evidence.RootElement } } })));
        JsonElement bar = joined.RootElement.GetProperty("pages")[0].GetProperty("merge").GetProperty("bars")[1];
        Assert.Equal(["copisteria", "audiveris"], bar.GetProperty("variants").EnumerateArray().Select(variant => variant.GetProperty("engines")[0].GetString()));
        Assert.Equal("G5", bar.GetProperty("variants")[0].GetProperty("staves")[0].GetProperty("notes")[0].GetProperty("name").GetString());
        Assert.Equal([100.0, 80, 200, 220], bar.GetProperty("crop").GetProperty("box").EnumerateArray().Select(value => value.GetDouble()));
        Assert.Equal((1000, 400, 0), (bar.GetProperty("crop").GetProperty("width").GetInt32(), bar.GetProperty("crop").GetProperty("height").GetInt32(), bar.GetProperty("crop").GetProperty("rotated").GetInt32()));
        Assert.False(joined.RootElement.GetProperty("pages")[0].GetProperty("merge").GetProperty("bars")[0].TryGetProperty("variants", out _));
        string score = joined.RootElement.GetProperty("score").GetString()!;

        // The notes read into a PDF piece and saved: check names them by the fingerprint scan.join gave.
        var facade = new MusicCoreFacade();
        Assert.DoesNotContain("error", facade.Open("Photos.pdf", "%PDF-1.4\n%%EOF\n"u8.ToArray(), """{"id": "p_scan", "photos": 1, "now": "2026-11-24T12:00:00Z"}"""), StringComparison.Ordinal);
        string request = JsonSerializer.Serialize(new { musicXml = Convert.ToBase64String(Encoding.UTF8.GetBytes(score)), engine = "homr, copisteria, audiveris", pages = 1, now = "2026-11-24T12:01:00Z" });
        using JsonDocument read = JsonDocument.Parse(facade.Call(1, "document.read", request));
        byte[] file = Encoding.UTF8.GetBytes(read.RootElement.GetProperty("file").GetString()!);
        var piece = new MusicCoreFacade();
        Assert.DoesNotContain("\"error\"", piece.Open("scan.motiff.json", file, null), StringComparison.Ordinal);
        using JsonDocument check = JsonDocument.Parse(piece.Call(1, "check", "{}"));
        Assert.Equal(joined.RootElement.GetProperty("fingerprint").GetString(), check.RootElement.GetProperty("fingerprint").GetString());

        // copisteria's right hand chosen for bar 2: its notes there are copisteria's, the left hand and bar 1 as they were.
        string variant = JsonSerializer.Serialize(new { bar = 2, staves = new[] { new { part = 1, staff = 1, notes = new[] { new { onset = 0, pitch = 79, length = 480 }, new { onset = 480, pitch = 81, length = 480 }, new { onset = 960, pitch = 83, length = 480 }, new { onset = 1440, pitch = 72, length = 480 } } } } });
        using JsonDocument before = JsonDocument.Parse(piece.Call(1, "corrections", """{"bar": 1}"""));
        using JsonDocument chosen = JsonDocument.Parse(piece.Call(1, "corrections.variant", variant));
        Assert.Equal([48, 72, 79, 81, 83], Pitches(chosen.RootElement));
        Assert.Equal(8, chosen.RootElement.GetProperty("count").GetInt32());
        using JsonDocument first = JsonDocument.Parse(piece.Call(1, "corrections", """{"bar": 1}"""));
        Assert.Equal(Pitches(before.RootElement), Pitches(first.RootElement));
        Project saved = ProjectFile.Read(Encoding.UTF8.GetBytes(piece.Call(1, "project.save", "{}")));
        Assert.Single(saved.Edits.Edits.Select(edit => edit.Group).Distinct());
        Assert.All(saved.Edits.Edits, edit => Assert.Null(edit.Origin));

        using JsonDocument undone = JsonDocument.Parse(piece.Call(1, "corrections.undoDraft", """{"bar": 2}"""));
        Assert.Equal([48, 74, 79, 81, 83], Pitches(undone.RootElement));
        Assert.Equal(0, undone.RootElement.GetProperty("count").GetInt32());

        string outside = JsonSerializer.Serialize(new { bar = 2, staves = new[] { new { part = 1, staff = 1, notes = new[] { new { onset = 1920, pitch = 79, length = 480 } } } } });
        Assert.Equal("badRequest", JsonDocument.Parse(piece.Call(1, "corrections.variant", outside)).RootElement.GetProperty("error").GetString());
        Assert.Equal("badRequest", JsonDocument.Parse(piece.Call(1, "corrections.variant", """{"bar": 2}""")).RootElement.GetProperty("error").GetString());
    }

    private static int[] Pitches(JsonElement corrections) => [.. corrections.GetProperty("notes").EnumerateArray().Select(note => note.GetProperty("pitch").GetInt32()).Order()];
}