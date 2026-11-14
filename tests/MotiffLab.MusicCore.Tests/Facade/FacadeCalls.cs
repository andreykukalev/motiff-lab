using System.Text.Json;

using MotiffLab.MusicCore.Facade;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>Calls on piece 1 of a facade, answered as JSON, for the facade's tests.</summary>
internal static class FacadeCalls
{
    public static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name));

    public static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

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