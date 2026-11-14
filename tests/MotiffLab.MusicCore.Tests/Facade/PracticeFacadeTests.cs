using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The calls the iPad's practice experiments make (#327, #328, #349; within contract 12): a selection that snaps to beats,
/// one hand heard alone, and a resume point that keeps where the player stopped, also with no passage selected.
/// </summary>
public class PracticeFacadeTests
{
    private const string FurElise = "fur-elise-original.musicxml";

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name));

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    /// <summary>Für Elise, opened as piece 1 for each test.</summary>
    private readonly MusicCoreFacade _facade = new();

    public PracticeFacadeTests() =>
        Assert.Equal(1, Json(_facade.Open(FurElise, Fixture(FurElise), """{"id": "p_practice"}""")).GetProperty("piece").GetInt32());

    /// <summary>A call on the piece that must answer without an error.</summary>
    private JsonElement Call(string name, string? request = null)
    {
        JsonElement answer = Json(_facade.Call(1, name, request));
        Assert.False(answer.TryGetProperty("error", out _), $"{name}: {answer}");
        return answer;
    }

    /// <summary>The error code a refused call answers.</summary>
    private string Error(string name, string request) => Json(_facade.Call(1, name, request)).GetProperty("error").GetString()!;

    /// <summary>The first two full bars after the start, as the playback score gives them: where each starts and its beat.</summary>
    private (long First, long Second, long Beat) TwoBars()
    {
        JsonElement[] bars = [.. Call("playback").GetProperty("bars").EnumerateArray().Where(bar => !bar.GetProperty("pickup").GetBoolean())];
        long beat = bars[2].GetProperty("beatTicks").GetInt64();
        Assert.True(bars[2].GetProperty("beatsPerBar").GetInt32() >= 3, "the test needs three beats or more to a bar");
        return (bars[2].GetProperty("onset").GetInt64(), bars[3].GetProperty("onset").GetInt64(), beat);
    }

    [Fact]
    public void A_span_snapped_to_beats_starts_and_ends_mid_bar_and_names_its_beats()
    {
        (long first, long second, long beat) = TwoBars();

        // From inside beat 2 of one bar to inside beat 2 of the next: beat 2 to the end of beat 2.
        string request = $$"""{"span": {"onset": {{first + beat + 10}}, "end": {{second + beat + 5}}}, "snap": "beat"}""";
        JsonElement selection = Call("select", request).GetProperty("selection");
        Assert.Equal(first + beat, selection.GetProperty("onset").GetInt64());
        Assert.Equal(second + (2 * beat), selection.GetProperty("end").GetInt64());
        Assert.Equal((2, 2), (selection.GetProperty("firstBeat").GetInt32(), selection.GetProperty("lastBeat").GetInt32()));
        Assert.Equal(selection.GetProperty("firstBar").GetInt32() + 1, selection.GetProperty("lastBar").GetInt32());

        // Without "snap" the span is taken as sent, as before.
        JsonElement exact = Call("select", $$$"""{"span": {"onset": {{{first}}}, "end": {{{second}}}}}""").GetProperty("selection");
        Assert.Equal((first, second), (exact.GetProperty("onset").GetInt64(), exact.GetProperty("end").GetInt64()));
        Assert.Equal(1, exact.GetProperty("firstBeat").GetInt32());
        Assert.Equal("badRequest", Error("select", $$"""{"span": {"onset": {{first}}, "end": {{second}}}, "snap": "bar"}"""));
    }

    [Fact]
    public void The_playback_beats_and_bars_name_their_bar_and_beat_as_a_selection_counts_them()
    {
        // Version 0.5.2, the iPad's one playback position: the pill and the roll write a place as bar.beat ("16.2").
        JsonElement playback = Call("playback");
        JsonElement[] bars = [.. playback.GetProperty("bars").EnumerateArray()];
        JsonElement[] beats = [.. playback.GetProperty("beats").EnumerateArray()];
        (long first, _, long beat) = TwoBars();

        JsonElement second = beats.Single(each => each.GetProperty("at").GetInt64() == first + beat);
        JsonElement full = bars.First(bar => bar.GetProperty("onset").GetInt64() == first);
        Assert.Equal(full.GetProperty("number").GetInt32(), second.GetProperty("bar").GetInt32());
        Assert.Equal(2, second.GetProperty("beat").GetInt32());
        Assert.Equal(1, full.GetProperty("firstBeat").GetInt32());
        Assert.True(beats.Where(each => each.GetProperty("downbeat").GetBoolean()).All(each => each.GetProperty("beat").GetInt32() == 1));

        // Für Elise starts with a pickup, counted from the end of a full bar: its first beat is not beat 1.
        JsonElement pickup = bars[0];
        Assert.True(pickup.GetProperty("pickup").GetBoolean());
        Assert.Equal(0, beats[0].GetProperty("bar").GetInt32());
        Assert.True(pickup.GetProperty("firstBeat").GetInt32() > 1);

        // The same count as a selection's: the beat a selection starts on is the beat its onset falls in.
        JsonElement selection = Call("select", $$"""{"span": {"onset": {{first + beat}}, "end": {{first + (2 * beat)}}}, "snap": "beat"}""").GetProperty("selection");
        Assert.Equal((second.GetProperty("bar").GetInt32(), second.GetProperty("beat").GetInt32()), (selection.GetProperty("firstBar").GetInt32(), selection.GetProperty("firstBeat").GetInt32()));
    }

    [Fact]
    public void One_hand_plays_alone_and_the_answer_says_which_hands_the_piece_has()
    {
        JsonElement both = Call("playback");
        JsonElement hands = both.GetProperty("hands");
        Assert.True(hands.GetProperty("right").GetBoolean());
        Assert.True(hands.GetProperty("left").GetBoolean());
        Assert.False(hands.GetProperty("inferred").GetBoolean(), "a grand staff's hands are read from the file");

        int all = both.GetProperty("notes").GetArrayLength();
        int right = Call("playback", """{"hands": "right"}""").GetProperty("notes").GetArrayLength();
        int left = Call("playback", """{"hands": "left"}""").GetProperty("notes").GetArrayLength();
        Assert.InRange(right, 1, all - 1);
        Assert.Equal(all, right + left);
        Assert.Equal(all, Call("playback", """{"hands": "both"}""").GetProperty("notes").GetArrayLength());
        // The bars and beats stay the whole piece's, so the timeline does not change under the player.
        Assert.Equal(both.GetProperty("end").GetInt64(), Call("playback", """{"hands": "left"}""").GetProperty("end").GetInt64());
        Assert.Equal("badRequest", Error("playback", """{"hands": "up"}"""));
    }

    [Fact]
    public void A_resume_point_keeps_the_passage_tempo_hands_and_loop_through_the_project_file_and_can_keep_no_passage()
    {
        (long first, long second, long beat) = TwoBars();
        string passage = $$$"""{"span": {"onset": {{{first + beat}}}, "end": {{{second}}}}}""";
        JsonElement saved = Call("resume.save", $$"""{"passage": {{passage}}, "tempoPercent": 70, "hands": "left", "loop": true, "focused": true, "now": "2026-11-12T03:00:00Z"}""").GetProperty("saved");
        string id = saved.GetProperty("id").GetString()!;

        // Saved as the web app saves it, the project file opens with the same point first.
        byte[] file = Encoding.UTF8.GetBytes(_facade.Call(1, "project.save"));
        var again = new MusicCoreFacade();
        Assert.False(Json(again.Open("fur.motiff.json", file)).TryGetProperty("error", out _));
        JsonElement latest = Json(again.Call(1, "resume")).GetProperty("resumePoints")[0];
        Assert.Equal(id, latest.GetProperty("id").GetString());
        Assert.Equal(first + beat, latest.GetProperty("passage").GetProperty("span").GetProperty("onset").GetInt64());
        Assert.Equal((70, "left", true, true), (latest.GetProperty("tempoPercent").GetInt32(), latest.GetProperty("hands").GetString(), latest.GetProperty("loop").GetBoolean(), latest.GetProperty("focused").GetBoolean()));

        // Hands not sent stay as they were; "passage": null leaves the point at its place with no passage.
        JsonElement cleared = Call("resume.save", $$"""{"id": "{{id}}", "passage": null, "at": {{second}}, "loop": false, "now": "2026-11-12T04:00:00Z"}""").GetProperty("saved");
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("passage").ValueKind);
        Assert.Equal(second, cleared.GetProperty("at").GetInt64());
        Assert.Equal("left", cleared.GetProperty("hands").GetString());
        Assert.False(cleared.GetProperty("loop").GetBoolean());
    }
}