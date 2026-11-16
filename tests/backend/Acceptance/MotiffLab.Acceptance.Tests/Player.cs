using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;

namespace MotiffLab.Acceptance.Tests;

/// <summary>
/// What a player does, through the facade's JSON contract (0020). The scenarios send only the facade's calls and read
/// only its answers, never music-core's inner types, so they hold whatever the code behind the contract looks like.
/// </summary>
internal sealed class Player
{
    /// <summary>A fixed time, so that two saves of the same work can be compared.</summary>
    public const string Now = "2026-11-11T12:00:00Z";

    private readonly MusicCoreFacade _facade = new();

    private Player()
    {
    }

    /// <summary>How many pieces this app session has open.</summary>
    public int OpenPieces => _facade.Count;

    public static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>A fixture read as JSON, for scenarios whose expected values are what the fixture itself holds.</summary>
    public static JsonElement FixtureJson(string name) => Parse(Encoding.UTF8.GetString(Fixture(name)));

    /// <summary>A new app session that opens a file as piece 1 and answers what the app shows on opening.</summary>
    public static (Player Player, JsonElement Opened) Opens(string fileName, byte[] data, string? request = null)
    {
        var player = new Player();
        JsonElement opened = Parse(player._facade.Open(fileName, data, request));
        Assert.False(opened.TryGetProperty("error", out JsonElement error), $"{fileName} did not open: {error} {opened}");
        return (player, opened);
    }

    public static (Player Player, JsonElement Opened) OpensFixture(string name, string? request = null) => Opens(name, Fixture(name), request);

    /// <summary>A new app session that tries to open a file: the answer as it is, a refusal included.</summary>
    public static (Player Player, JsonElement Answer) TriesToOpen(string fileName, byte[] data, string? request = null)
    {
        var player = new Player();
        return (player, Parse(player._facade.Open(fileName, data, request)));
    }

    /// <summary>A step on piece 1 that must succeed.</summary>
    public JsonElement Does(string call, string? request = null)
    {
        JsonElement answer = Tries(call, request);
        Assert.False(answer.TryGetProperty("error", out JsonElement error), $"{call}: {error} {answer}");
        return answer;
    }

    /// <summary>A step on piece 1: the answer as it is, a refusal included.</summary>
    public JsonElement Tries(string call, string? request = null) => Parse(_facade.Call(1, call, request));

    /// <summary>The project file as the app saves it, at <see cref="Now"/>.</summary>
    public string Saves() => _facade.Call(1, "project.save", $$"""{"now": "{{Now}}"}""");

    /// <summary>A new app session that opens a saved project file.</summary>
    public static (Player Player, JsonElement Opened) Reopens(string projectFile, string? request = null) =>
        Opens("piece.motiff.json", Encoding.UTF8.GetBytes(projectFile), request);

    /// <summary>The piece's notes as the score holds them, ordered by onset and then by pitch.</summary>
    public JsonElement[] Notes() =>
        [.. Does("piece").GetProperty("notes").EnumerateArray()
            .OrderBy(note => note.GetProperty("onset").GetInt64()).ThenBy(note => note.GetProperty("pitch").GetInt32())];

    /// <summary>The notes as they sound, ordered by when they start and then by pitch.</summary>
    public (long Onset, long Duration, int Pitch)[] Hears(string of = "piece") =>
        [.. Does("playback", $$"""{"of": "{{of}}"}""").GetProperty("notes").EnumerateArray()
            .Select(note => (note.GetProperty("onset").GetInt64(), note.GetProperty("duration").GetInt64(), note.GetProperty("pitch").GetInt32()))
            .OrderBy(note => note.Item1).ThenBy(note => note.Item3)];

    public static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>Text in Russian: it has Cyrillic letters.</summary>
    public static bool IsRussian(string? text) => text is not null && text.Any(IsCyrillic);

    /// <summary>Text in English: it has Latin letters and no Cyrillic ones.</summary>
    public static bool IsEnglish(string? text) => text is not null && text.Any(char.IsAsciiLetter) && !text.Any(IsCyrillic);

    private static bool IsCyrillic(char c) => c is >= 'Ѐ' and <= 'ӿ';
}