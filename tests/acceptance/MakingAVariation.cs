using System.Text.Json;

namespace MotiffLab.Acceptance.Tests;

/// <summary>
/// Scenarios e to g: the player selects bars and tries a variation of them (README.md). Since contract 10 the facade
/// runs a variation through a Try chip, a catalogue operation with fixed parameters and a fixed seed.
/// </summary>
public class MakingAVariation
{
    private const string RightHandOfBars1To2 = """{"bars": {"first": 1, "last": 2}, "filter": {"hand": "right"}}""";

    /// <summary>"Half time": the rhythm reshape that makes every note twice as long, keeping the pitches and their order.</summary>
    private const string HalfTime = """{"chip": "v1.halfTime"}""";

    [Fact(DisplayName = "e. The player tries a variation of bars 1–2: the piece's own notes stay as they were, also after saving")]
    public void A_variation_leaves_the_original_notes_untouched()
    {
        (Player player, _) = Player.OpensFixture("fur-elise-original.musicxml");
        JsonElement[] before = player.Notes();
        (long, long, int)[] heardBefore = player.Hears();

        JsonElement selection = player.Does("select", RightHandOfBars1To2).GetProperty("selection");
        // The right hand of bars 1 and 2 as printed: E D♯ E B D C, then A and the run C E A.
        Assert.Equal(10, selection.GetProperty("noteIds").GetArrayLength());
        JsonElement chain = player.Does("try", HalfTime);

        JsonElement result = chain.GetProperty("result");
        Assert.Empty(result.GetProperty("added").EnumerateArray());
        Assert.Empty(result.GetProperty("removed").EnumerateArray());
        int[] varied = [.. result.GetProperty("notes").EnumerateArray().OrderBy(note => note.GetProperty("onset").GetInt64()).Select(note => note.GetProperty("pitch").GetInt32())];
        Assert.Equal([76, 75, 76, 71, 74, 72, 69, 60, 64, 69], varied);
        Assert.NotEqual(heardBefore, player.Hears("inPlace"));

        // The piece itself is untouched: its notes, how it sounds, and the notes the variation started from.
        Assert.Equal(Texts(before), Texts(player.Notes()));
        Assert.Equal(heardBefore, player.Hears());
        string[] original = [.. chain.GetProperty("original").EnumerateArray().Select(note => note.GetRawText())];
        Assert.All(original, note => Assert.Contains(note, Texts(before)));

        (Player reopened, _) = Player.Reopens(player.Saves());
        Assert.Equal(Texts(before), Texts(reopened.Notes()));
        Assert.Equal(heardBefore, reopened.Hears());
    }

    [Fact(DisplayName = "f. The same piece, selection, operation, parameters and seed give the same variation")]
    public void The_same_steps_give_the_same_variation()
    {
        static (string Chain, string Heard) Vary()
        {
            (Player player, _) = Player.OpensFixture("fur-elise-original.musicxml", $$"""{"id": "p_elise", "now": "{{Player.Now}}"}""");
            player.Does("select", RightHandOfBars1To2);
            string chain = player.Does("try", HalfTime).GetRawText();
            return (chain, player.Does("playback", """{"of": "inPlace"}""").GetRawText());
        }

        (string Chain, string Heard) first = Vary();
        (string Chain, string Heard) second = Vary();

        Assert.Equal(first.Chain, second.Chain);
        Assert.Equal(first.Heard, second.Heard);
    }

    [Fact(DisplayName = "g. A variation the catalogue does not have is refused, and the variation stays as it was")]
    public void An_operation_the_catalogue_does_not_have_is_refused()
    {
        (Player player, _) = Player.OpensFixture("fur-elise-original.musicxml");
        player.Does("select", RightHandOfBars1To2);
        string tried = player.Does("try", HalfTime).GetRawText();
        (long, long, int)[] heard = player.Hears("inPlace");

        foreach (string wrong in new[] { """{"chip": "v99.unknown"}""", """{"chip": ""}""", "{}" })
        {
            Assert.Equal("badRequest", player.Tries("try", wrong).GetProperty("error").GetString());
        }

        Assert.Equal(tried, player.Does("variation").GetRawText());
        Assert.Equal(heard, player.Hears("inPlace"));
    }

    private static string[] Texts(IEnumerable<JsonElement> notes) => [.. notes.Select(note => note.GetRawText())];
}