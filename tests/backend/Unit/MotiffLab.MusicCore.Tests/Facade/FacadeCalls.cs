using System.Text.Json;

using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>Calls on piece 1 of a facade, answered as JSON, for the facade's tests.</summary>
internal static class FacadeCalls
{
    public static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name));

    public static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    /// <summary>
    /// The request of «Распознать» for the recording "src_rec": Für Elise as the engine heard it, 60 s of sound sent from
    /// 30 s into the file.
    /// </summary>
    public static string Heard()
    {
        ImportedPiece piece = PieceImporter.Import("fur-elise.musicxml", Fixture("fur-elise-original.musicxml")).Piece!;
        string midi = Convert.ToBase64String(MidiExporter.Export(piece.Notes, [], piece.Meter, "heard"));
        return $$$"""{"original": "src_rec", "engine": "transkun", "milliseconds": 60000, "sentFromMs": 30000, "parts": [{"index": 0, "midi": "{{{midi}}}"}], "now": "2026-11-20T11:00:00Z"}""";
    }

    /// <summary>The answer of a call that must not fail.</summary>
    public static JsonElement Call(MusicCoreFacade facade, string name, string? request = null)
    {
        JsonElement answer = Json(facade.Call(1, name, request));
        Assert.False(answer.TryGetProperty("error", out JsonElement error), $"{name}: {error} {answer}");
        return answer;
    }

    /// <summary>The error of a call that must fail.</summary>
    public static string Error(MusicCoreFacade facade, string name, string? request = null)
    {
        JsonElement answer = Json(facade.Call(1, name, request));
        return answer.TryGetProperty("error", out JsonElement error) ? error.GetString()! : throw new Xunit.Sdk.XunitException($"{name} answered without an error: {answer}");
    }
}