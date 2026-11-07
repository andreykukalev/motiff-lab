using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;

namespace MotiffLab.Acceptance.Tests;

/// <summary>
/// What a player does, through the one facade both apps call (0020). The scenarios use only the facade's
/// calls and answers, never music-core's inner types, so they hold whatever the code inside looks like.
/// </summary>
internal sealed class Player
{
    private readonly MusicCoreFacade _facade = new();

    public static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>Opens a file as piece 1 and answers what the app shows on opening.</summary>
    public static (Player Player, JsonElement Opened) Opens(string fileName, byte[] data, string? request = null)
    {
        var player = new Player();
        JsonElement opened = Parse(player._facade.Open(fileName, data, request));
        Assert.False(opened.TryGetProperty("error", out JsonElement error), $"{fileName} did not open: {error} {opened}");
        return (player, opened);
    }

    public static (Player Player, JsonElement Opened) OpensFixture(string name, string? request = null) => Opens(name, Fixture(name), request);

    /// <summary>A file the app refuses: the answer with its error.</summary>
    public static (Player Player, JsonElement Answer) TriesToOpen(string fileName, byte[] data)
    {
        var player = new Player();
        return (player, Parse(player._facade.Open(fileName, data)));
    }

    public int OpenPieces => _facade.Count;

    public JsonElement Does(string call, string? request = null)
    {
        JsonElement answer = Parse(_facade.Call(1, call, request));
        Assert.False(answer.TryGetProperty("error", out JsonElement error), $"{call}: {error} {answer}");
        return answer;
    }

    /// <summary>The answer as it is, an error included.</summary>
    public string Raw(string call, string? request = null) => _facade.Call(1, call, request);

    /// <summary>The project file as the app saves it, at a fixed time so two saves can be compared.</summary>
    public string Saves() => _facade.Call(1, "project.save", """{"now": "2026-11-07T12:00:00Z"}""");

    /// <summary>A new app session that opens the saved project file.</summary>
    public static (Player Player, JsonElement Opened) Reopens(string projectFile) =>
        Opens("piece.motiff.json", Encoding.UTF8.GetBytes(projectFile));

    /// <summary>The notes as they sound, ordered by when they start and then by pitch.</summary>
    public (long Onset, long Duration, int Pitch)[] Hears(string of = "piece") =>
        [.. Does("playback", $$"""{"of": "{{of}}"}""").GetProperty("notes").EnumerateArray()
            .Select(note => (note.GetProperty("onset").GetInt64(), note.GetProperty("duration").GetInt64(), note.GetProperty("pitch").GetInt32()))
            .OrderBy(note => note.Item1).ThenBy(note => note.Item3)];

    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    public static bool IsRussian(string? text) => text is not null && text.Any(c => c is >= 'А' and <= 'я' or 'ё' or 'Ё');
}
