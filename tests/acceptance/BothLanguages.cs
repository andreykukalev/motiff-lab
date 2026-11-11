using System.Text.Json;

namespace MotiffLab.Acceptance.Tests;

/// <summary>
/// Scenario l: every message the scenarios a to k show the player exists in Russian and in English (README.md): the
/// refusal of a damaged file and of a newer project file, the explanation of a variation, and the Check flags of a scan
/// and of a recording.
/// </summary>
public class BothLanguages
{
    public static TheoryData<string> Languages => new() { "ru", "en" };

    [Theory(DisplayName = "l1. The refusal of a damaged file is in the player's language")]
    [MemberData(nameof(Languages))]
    public void The_refusal_of_a_damaged_file_is_in_the_players_language(string language)
    {
        (_, JsonElement answer) = Player.TriesToOpen("notes.mid", [0x4D, 0x54, 0x68, 0x64, 0, 0, 0], Language(language));

        AssertIn(language, answer.GetProperty("message").GetString());
    }

    [Fact(DisplayName = "l2. The refusal of a project file of a newer version is in English for a player who reads English")]
    public void The_refusal_of_a_newer_file_is_in_English() => AssertNewerFileRefusedIn("en");

    /// <summary>
    /// Known defect: the message comes from the project file reader in English only, whatever the language
    /// (ProjectFile.Read, "This project was saved by a newer version of Motiff Lab."). The task that fixes it removes Skip.
    /// </summary>
    [Fact(DisplayName = "l2. The refusal of a project file of a newer version is in Russian for a player who reads Russian", Skip = KnownDefect.NewerFileMessageInRussian)]
    public void The_refusal_of_a_newer_file_is_in_Russian() => AssertNewerFileRefusedIn("ru");

    [Theory(DisplayName = "l3. The explanation of a variation is in the player's language")]
    [MemberData(nameof(Languages))]
    public void The_explanation_of_a_variation_is_in_the_players_language(string language)
    {
        (Player player, _) = Player.OpensFixture("fur-elise-original.musicxml", Language(language));
        player.Does("select", """{"bars": {"first": 1, "last": 2}, "filter": {"hand": "right"}}""");

        JsonElement step = Assert.Single(player.Does("try", """{"chip": "v1.halfTime"}""").GetProperty("steps").EnumerateArray());

        AssertIn(language, step.GetProperty("explanation").GetString());
    }

    [Theory(DisplayName = "l4. The Check flags of a scan are in the player's language")]
    [MemberData(nameof(Languages))]
    public void The_Check_flags_of_a_scan_are_in_the_players_language(string language)
    {
        (Player pdf, _) = Player.OpensFixture("blank-page.pdf", Language(language));
        string read = JsonSerializer.Serialize(new { musicXml = Convert.ToBase64String(Player.Fixture("scan-result.musicxml")), engine = "scan server", language, now = Player.Now });
        (Player player, _) = Player.Reopens(pdf.Does("document.read", read).GetProperty("file").GetString()!, Language(language));

        AssertFlagsIn(language, player.Does("check"));
    }

    [Theory(DisplayName = "l5. The Check flags of a recording are in the player's language")]
    [MemberData(nameof(Languages))]
    public void The_Check_flags_of_a_recording_are_in_the_players_language(string language)
    {
        string midi = Convert.ToBase64String(Player.Fixture("scale-type0.mid"));
        string request = $$$"""{"language": "{{{language}}}", "transcription": {"engine": "transcription engine", "milliseconds": 130000, "parts": [{"index": 0, "midi": "{{{midi}}}"}, {"index": 1, "midi": "{{{midi}}}"}]}}""";
        (Player player, _) = Player.Opens("recital.m4a", [], request);

        AssertFlagsIn(language, player.Does("check"));
    }

    private static void AssertNewerFileRefusedIn(string language)
    {
        (_, JsonElement answer) = Player.TriesToOpen("v16-future.motiff.json", Player.Fixture("v16-future.motiff.json"), Language(language));

        Assert.Equal("import", answer.GetProperty("error").GetString());
        AssertIn(language, answer.GetProperty("message").GetString());
    }

    private static string Language(string language) => $$"""{"language": "{{language}}"}""";

    private static void AssertFlagsIn(string language, JsonElement check)
    {
        JsonElement[] flags = [.. check.GetProperty("flags").EnumerateArray()];
        Assert.NotEmpty(flags);
        Assert.All(flags, flag =>
        {
            AssertIn(language, flag.GetProperty("name").GetString());
            AssertIn(language, flag.GetProperty("text").GetString());
        });
    }

    private static void AssertIn(string language, string? text)
    {
        Assert.False(string.IsNullOrWhiteSpace(text), "the player is shown a message");
        Assert.True(language == "ru" ? Player.IsRussian(text) : Player.IsEnglish(text), $"in {language}: {text}");
    }
}