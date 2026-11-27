using System.Text;
using System.Text.Json;

namespace MotiffLab.Acceptance.Tests;

/// <summary>
/// Scenarios m to p (README.md; docs/ux-baseline.md NOTE-01, VIEW-11, NOTE-02, NOTE-03): the player's work on notes read
/// from a page goes with them when the page is read again or other notes are made the working ones, doubt about a bar to
/// check reaches the facts until it is checked, «Выглядит верно» takes a recording bar's length marks away, and the note
/// editor opens at the piece's first bar.
/// </summary>
public class ReadingAgain
{
    /// <summary>The scan result's first note, C5 on the downbeat of bar 1 (the fixture).</summary>
    private const int C5 = 72;

    /// <summary>The blank page's notes read by a scan as <c>scan-result.musicxml</c> says: piece 1, its Notes n_1 the working ones.</summary>
    private static Player Scanned(string language = "en")
    {
        (Player pdf, _) = Player.OpensFixture("blank-page.pdf", $$"""{"id": "p_again", "now": "{{Player.Now}}", "language": "{{language}}"}""");
        JsonElement answer = pdf.Does("document.read", Read(Player.Fixture("scan-result.musicxml")));
        (Player player, _) = Player.Reopens(answer.GetProperty("file").GetString()!, $$"""{"language": "{{language}}"}""");
        return player;
    }

    private static string Read(byte[] musicXml) =>
        JsonSerializer.Serialize(new { musicXml = Convert.ToBase64String(musicXml), engine = "scan server", pages = 1, now = Player.Now });

    /// <summary>The page read again from its PDF in Sources («Распознать ноты»), as <paramref name="musicXml"/>.</summary>
    private static JsonElement ReadsAgain(Player player, byte[] musicXml) =>
        player.Does("sources.notes.recognise", JsonSerializer.Serialize(new { original = "src_pdf", musicXml = Convert.ToBase64String(musicXml), engine = "scan server", pages = 1, now = Player.Now }));

    /// <summary>The scan result with its text changed, as another reading of the same page.</summary>
    private static byte[] ReadOtherwise(string from, string to)
    {
        string xml = Encoding.UTF8.GetString(Player.Fixture("scan-result.musicxml"));
        int at = xml.IndexOf(from, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the fixture has {from}");
        return Encoding.UTF8.GetBytes(string.Concat(xml.AsSpan(0, at), to, xml.AsSpan(at + from.Length)));
    }

    private static JsonElement FirstNote(Player player) => player.Notes()[0];

    [Fact(DisplayName = "m1. The player corrects a note and has the page read again: the correction goes with the new reading, and the toast's line says so")]
    public void A_correction_is_carried_to_the_page_read_again()
    {
        Player player = Scanned();
        JsonElement first = FirstNote(player);
        Assert.Equal(C5, first.GetProperty("pitch").GetInt32());
        player.Does("corrections.set", $$"""{"bar": 1, "id": "{{first.GetProperty("id").GetString()}}", "pitch": {{C5 + 1}}}""");

        JsonElement again = ReadsAgain(player, Player.Fixture("scan-result.musicxml"));

        JsonElement carried = again.GetProperty("carried");
        Assert.Equal((1, 0, 0), (carried.GetProperty("carried").GetInt32(), carried.GetProperty("byReading").GetInt32(), carried.GetProperty("notCarried").GetInt32()));
        Assert.Equal("1 correction carried over", carried.GetProperty("text").GetString());
        player.Does("sources.makeWorking", $$"""{"id": "{{again.GetProperty("id").GetString()}}"}""");
        Assert.Equal(C5 + 1, FirstNote(player).GetProperty("pitch").GetInt32());
    }

    [Theory(DisplayName = "m2. A correction the new reading reads otherwise is not carried: its bar is flagged in Check, in the player's language")]
    [InlineData("en")]
    [InlineData("ru")]
    public void A_correction_that_does_not_fit_the_new_reading_is_flagged(string language)
    {
        Player player = Scanned(language);
        player.Does("corrections.set", $$"""{"bar": 1, "id": "{{FirstNote(player).GetProperty("id").GetString()}}", "pitch": {{C5 + 1}}}""");

        // The page read again has B4 where C5 was read and the player wrote C♯5: neither the value found nor the corrected one.
        JsonElement again = ReadsAgain(player, ReadOtherwise("<step>C</step><octave>5</octave>", "<step>B</step><octave>4</octave>"));

        JsonElement carried = again.GetProperty("carried");
        Assert.Equal(1, carried.GetProperty("notCarried").GetInt32());
        Assert.Equal([1], carried.GetProperty("bars").EnumerateArray().Select(static bar => bar.GetInt32()));
        player.Does("sources.makeWorking", $$"""{"id": "{{again.GetProperty("id").GetString()}}"}""");
        JsonElement flag = Assert.Single(player.Does("check").GetProperty("flags").EnumerateArray(), static flag => flag.GetProperty("reason").GetString() == "correctionUnfit");
        Assert.Equal((1, false), (flag.GetProperty("bar").GetInt32(), flag.GetProperty("resolved").GetBoolean()));
        Assert.Equal(language == "ru" ? "Исправление не подошло" : "Your correction did not fit", flag.GetProperty("name").GetString());
    }

    [Fact(DisplayName = "n. The player makes notes read again the working ones: a bookmark follows its bar's music, and the toast's line says so")]
    public void A_bookmark_follows_its_music_to_the_notes_made_working()
    {
        Player player = Scanned();
        // Bar 2 of the page as first read: G F E D C.
        player.Does("bookmark.add", $$"""{"bars": {"first": 2}, "title": "Mine", "now": "{{Player.Now}}"}""");
        // Read again with a bar before the first, so that music is bar 3 of the new reading.
        const string Bar1 = "<measure number=\"1\">";
        byte[] longer = ReadOtherwise(Bar1, """<measure number="0"><attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time></attributes><note><rest/><duration>4</duration><voice>1</voice><type>whole</type></note></measure>""" + Bar1);
        JsonElement again = ReadsAgain(player, longer);

        JsonElement followed = player.Does("sources.makeWorking", $$"""{"id": "{{again.GetProperty("id").GetString()}}"}""").GetProperty("followed");

        Assert.Equal((1, 0), (followed.GetProperty("moved").GetInt32(), followed.GetProperty("near").GetInt32()));
        Assert.Equal("moved with their bars: 1 bookmark", followed.GetProperty("text").GetString());
        JsonElement bookmark = Assert.Single(Player.Parse(player.Saves()).GetProperty("workspace").GetProperty("bookmarks").GetProperty("items").EnumerateArray());
        // 4/4 at 480 a quarter: bar 3 of the new reading starts at 3840.
        Assert.Equal((3840, "Mine"), (bookmark.GetProperty("onset").GetInt64(), bookmark.GetProperty("title").GetString()));
    }

    [Fact(DisplayName = "o1. Facts about a bar still to check say they rest on it until the player checks it")]
    public void Facts_on_a_bar_to_check_are_doubted_until_it_is_checked()
    {
        Player player = Scanned();
        player.Does("select", """{"bars": {"first": 1, "last": 3}}""");

        JsonElement[] doubted = Doubted(player.Does("understand"));

        Assert.NotEmpty(doubted);
        Assert.All(doubted, fact => Assert.Equal("Rests on recognised notes still to check in bar 2.", fact.GetProperty("doubt").GetString()));
        player.Does("check.resolve", """{"bar": 2}""");
        Assert.Empty(Doubted(player.Does("understand")));
        Assert.DoesNotContain("\"doubt\"", player.Does("dna").GetRawText(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "o2. «Выглядит верно» on a recording's bar takes its length marks away, and taking it back brings them back")]
    public void Looks_right_takes_a_recording_bars_length_marks_away()
    {
        // Für Elise heard in two parts by Transkun: lengths held by the pedal or longer than a half note are uncertain.
        string midi = Convert.ToBase64String(Player.Fixture("transkun-fur-elise.mid"));
        (Player player, _) = Player.Opens("recital.m4a", [], $$$"""{"id": "p_heard", "now": "{{{Player.Now}}}", "transcription": {"engine": "transkun", "milliseconds": 130000, "parts": [{"index": 0, "midi": "{{{midi}}}"}, {"index": 1, "midi": "{{{midi}}}"}]}}""");
        JsonElement check = player.Does("check");
        int[] flagged = [.. check.GetProperty("flags").EnumerateArray().Select(static flag => flag.GetProperty("bar").GetInt32())];
        int bar = Lengths(check).First(flagged.Contains);

        Assert.DoesNotContain(bar, Lengths(player.Does("check.resolve", $$"""{"bar": {{bar}}}""")));
        Assert.Contains(bar, Lengths(player.Does("check.resolve", $$"""{"bar": {{bar}}, "resolved": false}""")));
    }

    [Fact(DisplayName = "p. With nothing selected the note editor opens at the piece's first bar, its pickup")]
    public void The_note_editor_opens_at_the_first_bar_a_pickup_too()
    {
        (Player player, _) = Player.OpensFixture("fur-elise-original.musicxml");

        JsonElement editor = player.Does("corrections.begin", "{}");

        // Für Elise starts with a pickup of two sixteenths: bar 0, E5 D♯5.
        Assert.Equal(0, editor.GetProperty("bar").GetInt32());
        Assert.Equal([76, 75], editor.GetProperty("notes").EnumerateArray().Select(static note => note.GetProperty("pitch").GetInt32()));
        JsonElement changed = player.Does("corrections.set", $$"""{"id": "{{editor.GetProperty("notes")[0].GetProperty("id").GetString()}}", "pitch": 77}""");
        Assert.Equal((0, 77), (changed.GetProperty("bar").GetInt32(), changed.GetProperty("notes")[0].GetProperty("pitch").GetInt32()));
    }

    /// <summary>The facts an answer of <c>understand</c> gives that carry a doubt.</summary>
    private static JsonElement[] Doubted(JsonElement understood) =>
        [.. new[] { "motif", "structure", "harmony" }
            .Where(part => understood.TryGetProperty(part, out JsonElement each) && each.TryGetProperty("facts", out _))
            .SelectMany(part => understood.GetProperty(part).GetProperty("facts").EnumerateArray())
            .Where(static fact => fact.TryGetProperty("doubt", out _))];

    /// <summary>The bars Check lists with lengths to check.</summary>
    private static int[] Lengths(JsonElement check) => [.. check.GetProperty("lengths").EnumerateArray().Select(static bar => bar.GetProperty("bar").GetInt32())];
}