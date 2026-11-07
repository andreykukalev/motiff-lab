using System.Text.Json;

namespace MotiffLab.Acceptance.Tests;

/// <summary>Selecting bars and making a variation of them that keeps what the player asked to keep.</summary>
public class MakingAVariation
{
    private const string Rhythm = """{"operation": "V1", "parameters": {"kind": "HalfTime", "eighths": 1}}""";

    [Fact(DisplayName = "B1. Выделение тактов 1–2 правой руки берёт ровно 10 нот этих тактов")]
    public void Selecting_bars_takes_the_notes_of_those_bars()
    {
        (Player player, _) = Player.OpensFixture("fur-elise-original.musicxml");

        JsonElement selection = player.Does("select", """{"bars": {"first": 1, "last": 2}, "filter": {"hand": "right"}}""").GetProperty("selection");

        Assert.Equal((1, 2), (selection.GetProperty("firstBar").GetInt32(), selection.GetProperty("lastBar").GetInt32()));
        Assert.Equal(10, selection.GetProperty("noteIds").GetArrayLength());
    }

    [Fact(DisplayName = "B2. Вариация ритма с «сохранить высоты»: те же высоты в том же порядке, меняется только время")]
    public void A_rhythm_variation_keeps_the_pitches_and_their_order()
    {
        (Player player, _) = Player.OpensFixture("fur-elise-original.musicxml");
        player.Does("select", """{"bars": {"first": 1, "last": 2}, "filter": {"hand": "right"}}""");
        player.Does("variation.start", """{"keep": ["pitches", "pitchOrder"], "seed": 3}""");

        JsonElement result = player.Does("variation.propose", Rhythm).GetProperty("result");

        Assert.Empty(result.GetProperty("added").EnumerateArray());
        Assert.Empty(result.GetProperty("removed").EnumerateArray());
        JsonElement[] changes = [.. result.GetProperty("changes").EnumerateArray()];
        Assert.NotEmpty(changes);
        Assert.All(changes, change => Assert.Equal(change.GetProperty("before").GetProperty("pitch").GetInt32(), change.GetProperty("after").GetProperty("pitch").GetInt32()));
        Assert.All(changes, change => Assert.True(
            change.GetProperty("before").GetProperty("onset").GetInt64() != change.GetProperty("after").GetProperty("onset").GetInt64()
            || change.GetProperty("before").GetProperty("duration").GetInt64() != change.GetProperty("after").GetProperty("duration").GetInt64()));

        // The right hand of bars 1–2 in order of time, as written: E D♯ E B D C, then A and the run C E A.
        int[] varied = [.. result.GetProperty("notes").EnumerateArray().OrderBy(note => note.GetProperty("onset").GetInt64()).Select(note => note.GetProperty("pitch").GetInt32())];
        Assert.Equal([76, 75, 76, 71, 74, 72, 69, 60, 64, 69], varied);
    }

    [Fact(DisplayName = "B3. Вариация не трогает оригинал: пьеса звучит как прежде, отмена возвращает всё")]
    public void A_variation_leaves_the_piece_as_it_was_and_undo_takes_it_back()
    {
        (Player player, _) = Player.OpensFixture("fur-elise-original.musicxml");
        (long, long, int)[] before = player.Hears();
        player.Does("select", """{"bars": {"first": 1, "last": 2}}""");
        player.Does("variation.start", """{"keep": ["pitches"], "seed": 3}""");
        player.Does("variation.propose", Rhythm);
        player.Does("variation.accept");

        Assert.Equal(before, player.Hears());
        Assert.NotEqual(before, player.Hears("inPlace"));

        player.Does("variation.undo");
        Assert.Empty(player.Does("variation").GetProperty("steps").EnumerateArray());
        Assert.Equal(before, player.Hears());
    }

    [Fact(DisplayName = "B4. Одинаковые действия дают одинаковую вариацию: тот же seed — те же ноты")]
    public void The_same_steps_give_the_same_variation()
    {
        static string Vary()
        {
            (Player player, _) = Player.OpensFixture("ode-to-joy.musicxml", """{"id": "p_ode", "now": "2026-11-07T10:00:00Z"}""");
            player.Does("select", """{"bars": {"first": 1, "last": 4}}""");
            player.Does("variation.start", """{"keep": ["pitches"], "seed": 7}""");
            return player.Does("variation.propose", Rhythm).GetProperty("result").GetRawText();
        }

        Assert.Equal(Vary(), Vary());
    }

    [Fact(DisplayName = "B5. Операция с недопустимым параметром отклоняется, вариация не меняется")]
    public void An_operation_out_of_range_is_refused()
    {
        (Player player, _) = Player.OpensFixture("fur-elise-original.musicxml");
        player.Does("select", """{"bars": {"first": 1, "last": 2}}""");
        player.Does("variation.start", """{"keep": ["pitches"], "seed": 3}""");

        foreach (string wrong in new[] { """{"operation": "V99", "parameters": {}}""", """{"operation": "V1", "parameters": {"kind": "HalfTime", "eighths": 99}}""" })
        {
            JsonElement refused = Player.Parse(player.Raw("variation.propose", wrong));
            Assert.Equal("badRequest", refused.GetProperty("error").GetString());
        }

        Assert.Empty(player.Does("variation").GetProperty("steps").EnumerateArray());
    }
}
