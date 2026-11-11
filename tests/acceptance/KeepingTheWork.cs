using System.Text.Json;

namespace MotiffLab.Acceptance.Tests;

/// <summary>Scenarios h to j: the player saves and reopens a project, and opens project files of other versions (README.md).</summary>
public class KeepingTheWork
{
    [Fact(DisplayName = "h. The player saves a project with a selection and a variation, reopens it and gets the same project")]
    public void Save_and_reopen_give_the_same_project()
    {
        (Player player, _) = Player.OpensFixture("fur-elise-original.musicxml", $$"""{"id": "p_elise", "now": "{{Player.Now}}"}""");
        JsonElement selection = player.Does("select", """{"bars": {"first": 1, "last": 2}, "filter": {"hand": "right"}}""").GetProperty("selection");
        string variation = player.Does("try", """{"chip": "v1.halfTime"}""").GetRawText();
        string saved = player.Saves();

        (Player reopened, JsonElement opened) = Player.Reopens(saved);

        Assert.Equal("p_elise", opened.GetProperty("project").GetProperty("id").GetString());
        Assert.Equal(selection.GetProperty("noteIds").GetRawText(), opened.GetProperty("selection").GetProperty("noteIds").GetRawText());
        Assert.True(opened.GetProperty("hasVariation").GetBoolean());
        Assert.Equal(Parts(player.Does("piece")), Parts(reopened.Does("piece")));
        Assert.Equal(Steps(variation), Steps(reopened.Does("variation").GetRawText()));
        Assert.Equal(player.Hears("inPlace"), reopened.Hears("inPlace"));
        // Saved again with nothing changed, it is the same file.
        Assert.Equal(saved, reopened.Saves());
    }

    [Theory(DisplayName = "i. The player opens project files saved by earlier builds (versions 12 and 15)")]
    [InlineData("v12-paterlini-all-my-joy.motiff.json")]
    [InlineData("v12-einaudi-santiago.motiff.json")]
    [InlineData("v15-worked.motiff.json")]
    public void The_golden_project_files_open(string name)
    {
        JsonElement file = Player.FixtureJson(name);

        (Player player, JsonElement opened) = Player.OpensFixture(name);

        // What the file holds is what the player gets back.
        JsonElement project = opened.GetProperty("project");
        Assert.Equal(file.GetProperty("id").GetString(), project.GetProperty("id").GetString());
        Assert.Equal(file.GetProperty("fileName").GetString(), project.GetProperty("fileName").GetString());
        Assert.Equal(file.GetProperty("motifs").GetArrayLength(), project.GetProperty("motifs").GetInt32());
        Assert.Equal(file.GetProperty("variations").GetArrayLength(), project.GetProperty("variations").GetInt32());
        Assert.Equal(file.GetProperty("resumePoints").GetArrayLength(), project.GetProperty("resumePoints").GetInt32());
        Assert.Equal(file.GetProperty("bookmarks").GetProperty("items").GetArrayLength(), project.GetProperty("bookmarks").GetInt32());
        Assert.NotEmpty(player.Notes());
        if (file.GetProperty("motifs").GetArrayLength() > 0)
        {
            // The first motif comes back as the selection, with the notes the file names.
            Assert.Equal(Ids(file.GetProperty("motifs")[0]), Ids(opened.GetProperty("selection")));
        }

        if (file.TryGetProperty("transcription", out JsonElement transcription) && transcription.ValueKind == JsonValueKind.Object)
        {
            // A project made from a recording: its notes were heard, not imported.
            Assert.Equal("transcribed", project.GetProperty("origin").GetString());
            Assert.All(player.Notes(), note => Assert.Equal("detected", note.GetProperty("provenance").GetString()));
        }

        if (file.TryGetProperty("performances", out JsonElement performances))
        {
            Assert.Equal(performances.GetArrayLength(), player.Does("performances").GetProperty("performances").GetArrayLength());
        }

        // Saving it again does not fail, and the new file opens too.
        Player.Reopens(player.Saves());
    }

    [Fact(DisplayName = "j. The player opens a project file of a newer version: it is refused with the newer-version message and left as it was")]
    public void A_file_of_a_newer_version_is_neither_damaged_nor_overwritten()
    {
        byte[] future = Player.Fixture("v16-future.motiff.json");
        byte[] copy = [.. future];

        (Player player, JsonElement answer) = Player.TriesToOpen("v16-future.motiff.json", future, """{"language": "en"}""");

        Assert.Equal("import", answer.GetProperty("error").GetString());
        Assert.Contains("newer version of Motiff Lab", answer.GetProperty("message").GetString(), StringComparison.Ordinal);
        // Nothing is open, so nothing can be saved over the file, and the bytes the player gave are as they were.
        Assert.Equal(0, player.OpenPieces);
        Assert.Equal("noPiece", Player.Parse(player.Saves()).GetProperty("error").GetString());
        Assert.Equal(copy, future);
    }

    private static string[] Ids(JsonElement selection) => [.. selection.GetProperty("noteIds").EnumerateArray().Select(id => id.GetString()!)];

    /// <summary>The piece's bars, keys and notes, without the file name the reopened copy is opened under.</summary>
    private static string Parts(JsonElement piece) =>
        string.Join('\n', new[] { "bars", "keys", "notes" }.Select(name => piece.GetProperty(name).GetRawText()));

    /// <summary>A variation's Keep choices, seed and steps, as the chain answer has them.</summary>
    private static string Steps(string chain)
    {
        JsonElement answer = Player.Parse(chain);
        return string.Join('\n', new[] { "keep", "seed", "steps", "result" }.Select(name => answer.GetProperty(name).GetRawText()));
    }
}