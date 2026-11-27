using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

using static MotiffLab.MusicCore.Tests.Import.Scan.ScanRepairTests;

namespace MotiffLab.MusicCore.Tests.Import.Scan;

/// <summary>
/// A bar the player marked «такт прочитан плохо» read again (c-14b2, «Как напечатано» step 3): the merge of the page's new
/// readings takes another engine's reading of it than the one taken before and leaves it to check with the others, and
/// scan.join answers the fingerprint of the notes with the marks put in, so Check finds what the iPad keeps of the reading.
/// In 4/4: four quarters in the right hand over a whole C3, homr and copisteria reading bar 2's last quarter D5, audiveris C5.
/// </summary>
public class ScanReadAgainTests
{
    private static readonly string[] First = ["C", "D", "E", "F"];

    private static string Bar(string[] right) =>
        string.Concat(right.Select(step => Note(step, 5, 2, 1))) + Backup(2 * right.Length) + Note("C", 3, 8, 2);

    private static ScanReading Reading(string engine, params string[][] bars) => new(engine, Score(2, "4", [.. bars.Select(Bar)]));

    private static ScanReading[] Readings() =>
        [Reading("homr", First, ["G", "A", "B", "D"]), Reading("copisteria", First, ["G", "A", "B", "D"]), Reading("audiveris", First, ["G", "A", "B", "C"])];

    private static int[] Right(ImportedPiece piece, int bar) =>
        [.. piece.Notes.Where(note => piece.Meter.BarAt(note.Span.Onset).Number == bar && note.Hand?.Value == Hand.Right)
            .OrderBy(static note => note.Span.Onset).Select(static note => note.Pitch.MidiNumber)];

    [Fact]
    public void A_bar_marked_read_badly_takes_another_engines_reading_and_is_to_check_with_the_one_taken_before()
    {
        ScanMergedPage before = ScanMerge.Merge(Readings());
        Assert.Equal([ScanLevel.Sure, ScanLevel.Probable], before.Bars.Select(static bar => bar.Level));

        ScanMergedPage again = ScanMerge.Merge(Readings(), before: new ScanBefore(before.MusicXml, new HashSet<int> { 1 }));

        Assert.Equal([ScanLevel.Sure, ScanLevel.Check], again.Bars.Select(static bar => bar.Level));
        Assert.True(again.Bars[1].ReadAgain);
        Assert.False(again.Bars[0].ReadAgain);
        Assert.Equal(["audiveris"], again.Bars[1].Staves[0].Engines);
        ScanVariant variant = Assert.Single(again.Bars[1].Variants);
        Assert.Equal(["homr", "copisteria"], variant.Engines);
        Assert.Equal(2, variant.Staves.Count);
        Assert.Equal(74, variant.Staves[0].Notes[^1].Pitch);

        // The bar is marked read again, not as one the engines disagree on, and the check flags it so.
        ImportedPiece piece = PieceImporter.Import("scan.musicxml", ScanJoin.Join([again.MusicXml])).Piece!;
        Assert.Equal([79, 81, 83, 72], Right(piece, 2));
        Assert.Equal([1], piece.MusicXml!.ReadAgain);
        Assert.Empty(piece.MusicXml.EnginesDisagree);
        Assert.Equal([ScanFlag.ReadAgain], ScanCheck.Of(piece).Find(2)!.Flags);
        Assert.Equal("Прочитан другим движком", CheckFlag.NameOf(CheckReason.ReadAgain, Words.Russian));
        Assert.Equal(again.MusicXml, ScanMerge.Merge(Readings(), before: new ScanBefore(before.MusicXml, new HashSet<int> { 1 })).MusicXml);
    }

    [Fact]
    public void A_bar_every_engine_read_as_before_keeps_its_reading_and_is_not_read_again()
    {
        ScanReading[] alike = [Reading("homr", First, First), Reading("copisteria", First, First)];
        ScanMergedPage before = ScanMerge.Merge(alike);

        ScanMergedPage again = ScanMerge.Merge(alike, before: new ScanBefore(before.MusicXml, new HashSet<int> { 1 }));

        Assert.Equal(before.MusicXml, again.MusicXml);
        Assert.All(again.Bars, static bar => Assert.False(bar.ReadAgain));
    }

    [Fact]
    public void Scan_join_reads_a_page_again_with_its_bad_bars_and_answers_the_fingerprint_of_the_notes_the_marks_make()
    {
        object[] readings = Sent(Readings());
        var facade = new MusicCoreFacade();
        string score = Join(facade, new { pages = new[] { readings } }).GetProperty("score").GetString()!;

        // Notes read from that score, the player marking bar 2 read badly and an octave higher on the upper staff, and bar 1's hands swapped.
        string file = Marked(facade, score, """{"kind": "badlyRead", "bar": 2}""", """{"kind": "hands", "bar": 1}""", """{"kind": "octave", "bar": 2, "staff": 1, "value": "8va"}""");

        JsonElement joined = Join(facade, new { score, replace = new[] { new { page = 1, readings, bad = new[] { 2 } } }, file });
        JsonElement bar = joined.GetProperty("pages")[0].GetProperty("merge").GetProperty("bars")[1];
        Assert.Equal(("check", 2), (bar.GetProperty("level").GetString(), bar.GetProperty("bar").GetInt32()));
        Assert.Equal(["homr", "copisteria"], bar.GetProperty("variants")[0].GetProperty("engines").EnumerateArray().Select(static engine => engine.GetString()));

        // The reading taken before, as the notes would be with it chosen: an octave higher, as marked; only its upper staff differs.
        JsonElement staff = Assert.Single(bar.GetProperty("variants")[0].GetProperty("staves").EnumerateArray());
        Assert.Equal((1, 1), (staff.GetProperty("part").GetInt32(), staff.GetProperty("staff").GetInt32()));
        Assert.Equal([91, 93, 95, 86], staff.GetProperty("notes").EnumerateArray().Select(static note => note.GetProperty("pitch").GetInt32()));
        Assert.Equal("badRequest", Json(facade.Call(0, "scan.join", JsonSerializer.Serialize(new { score, replace = new[] { new { page = 1, readings, bad = new[] { 9 } } } })))
            .GetProperty("error").GetString());

        string recognise = JsonSerializer.Serialize(new { original = PieceOriginal.DocumentId, musicXml = Convert.ToBase64String(Encoding.UTF8.GetBytes(joined.GetProperty("score").GetString()!)), engine = "audiveris", pages = 1, now = "2026-11-27T10:05:00Z" });
        JsonElement marked = Json(facade.Call(1, "sources.notes.recognise", recognise)).GetProperty("marked");
        Assert.Equal((3, 0), (marked.GetProperty("used").GetInt32(), marked.GetProperty("unfit").GetInt32()));
        Assert.DoesNotContain("\"error\"", facade.Call(1, "sources.makeWorking", """{"id": "n_2"}"""), StringComparison.Ordinal);

        // Check finds the reading by the fingerprint scan.join gave, which the marks changed; bar 2 is flagged read again.
        JsonElement check = Json(facade.Call(1, "check", "{}"));
        Assert.Equal(joined.GetProperty("fingerprint").GetString(), check.GetProperty("fingerprint").GetString());
        Assert.NotEqual(Join(facade, new { score = joined.GetProperty("score").GetString() }).GetProperty("fingerprint").GetString(), check.GetProperty("fingerprint").GetString());
        Assert.Contains(check.GetProperty("flags").EnumerateArray(), static flag => flag.GetProperty("bar").GetInt32() == 2 && flag.GetProperty("reason").GetString() == "readAgain");
        Project working = ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));
        Assert.Equal([91, 93, 95, 84], Right(PieceImporter.Import("a.musicxml", working.Source).Piece!, 2));

        // The bar read badly did its job: it is in Check with its readings and is not carried to be read again once more.
        Assert.Equal([ReadingMarkKind.Hands, ReadingMarkKind.Octave], working.Scan!.Marks.Select(static mark => mark.Kind));
    }

    [Fact]
    public void A_variant_a_mark_makes_the_same_as_the_notes_is_left_out_so_choosing_never_takes_back_what_a_mark_put_right()
    {
        // Both read a key of one sharp; homr reads bar 2's last quarter F#5 as the key gives it, copisteria F5 natural (no
        // accidental written): the bar is to check, homr's taken.
        string sharp = Encoding.UTF8.GetString(Reading("homr", ["C", "D", "E", "G"], ["G", "A", "B", "F"]).MusicXml);
        int last = sharp.LastIndexOf("<step>F</step>", StringComparison.Ordinal) + "<step>F</step>".Length;
        ScanReading homr = new("homr", Encoding.UTF8.GetBytes(sharp.Insert(last, "<alter>1</alter>")));
        object[] readings = Sent([homr, Reading("copisteria", ["C", "D", "E", "G"], ["G", "A", "B", "F"])]);
        var facade = new MusicCoreFacade();
        JsonElement plain = Join(facade, new { pages = new[] { readings } });
        string score = plain.GetProperty("score").GetString()!;
        Assert.Single(plain.GetProperty("pages")[0].GetProperty("merge").GetProperty("bars")[1].GetProperty("variants").EnumerateArray());

        // No sharps or flats marked: homr's F#5 loses the sharp its key gave it, so copisteria's reading is the notes' own.
        string file = Marked(facade, score, """{"kind": "key", "bar": 1, "value": "0"}""");
        JsonElement bar = Join(facade, new { pages = new[] { readings }, file }).GetProperty("pages")[0].GetProperty("merge").GetProperty("bars")[1];

        Assert.Equal("check", bar.GetProperty("level").GetString());
        Assert.Empty(bar.GetProperty("variants").EnumerateArray());
    }

    private static object[] Sent(ScanReading[] readings) =>
        [.. readings.Select(static reading => new Dictionary<string, string> { ["engine"] = reading.Engine, ["musicxml"] = Encoding.UTF8.GetString(reading.MusicXml) })];

    /// <summary>The project file of notes read from <paramref name="score"/>, open in <paramref name="facade"/> as piece 1, with the player's <paramref name="marks"/>.</summary>
    private static string Marked(MusicCoreFacade facade, string score, params string[] marks)
    {
        byte[] read = Encoding.UTF8.GetBytes(score);
        DateTimeOffset now = DateTimeOffset.Parse("2026-11-27T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        Project project = Project.CreateDocument("p_read_again", "Page.pdf", "%PDF-1.4\n%%EOF\n"u8.ToArray(), now)
            .Recognised(read, "homr + copisteria + audiveris", PageMap.Of(PieceImporter.Import("a.musicxml", read).Piece!, 1), now);
        Assert.DoesNotContain("\"error\"", facade.Open("Page.motiff.json", ProjectFile.Write(project), """{"language": "en"}"""), StringComparison.Ordinal);
        foreach (string mark in marks)
        {
            Assert.DoesNotContain("\"error\"", facade.Call(1, "check.mark", mark), StringComparison.Ordinal);
        }

        return facade.Call(1, "project.save");
    }

    private static JsonElement Join(MusicCoreFacade facade, object request) => Json(facade.Call(0, "scan.join", JsonSerializer.Serialize(request)));

    private static JsonElement Json(string answer) => JsonDocument.Parse(answer).RootElement.Clone();
}