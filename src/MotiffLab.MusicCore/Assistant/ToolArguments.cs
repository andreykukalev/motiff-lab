using System.Text.Json;

using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Assistant;

/// <summary>The parameters every ranged tool shares: the bars and the hand.</summary>
internal static class ToolArguments
{
    /// <summary>{"from": first bar, "to": last bar}.</summary>
    internal static ToolSchema BarsSchema => ToolSchema.Object(
        ("from", ToolSchema.Integer(0, AssistantTools.MaxBarNumber), "The first bar, as the score numbers it (the pickup is bar 0)."),
        ("to", ToolSchema.Integer(0, AssistantTools.MaxBarNumber), "The last bar, inclusive."));

    /// <summary>"both", "right" or "left".</summary>
    internal static ToolSchema HandSchema => ToolSchema.Choice("both", "right", "left");

    internal static (int From, int To) BarsOf(JsonElement parameters)
    {
        JsonElement bars = parameters.GetProperty("bars");
        return (bars.GetProperty("from").GetInt32(), bars.GetProperty("to").GetInt32());
    }

    internal static Hand? HandOf(JsonElement parameters) => parameters.GetProperty("hand").GetString() switch
    {
        "right" => Hand.Right,
        "left" => Hand.Left,
        _ => null,
    };

    /// <summary>The range's refusal, or null; then <see cref="AssistantPiece.Read"/> may run.</summary>
    internal static string? RangeRefusal(AssistantPiece piece, JsonElement parameters)
    {
        (int from, int to) = BarsOf(parameters);
        return piece.RangeRefusal(from, to);
    }

    /// <summary>Refuses a range the piece does not have; otherwise runs <paramref name="run"/> on it.</summary>
    internal static ToolResult Ranged(AssistantPiece piece, JsonElement parameters, Func<int, int, ToolResult> run)
    {
        (int from, int to) = BarsOf(parameters);
        return piece.RangeRefusal(from, to) is { } refusal ? ToolResult.Refused(refusal) : run(from, to);
    }
}