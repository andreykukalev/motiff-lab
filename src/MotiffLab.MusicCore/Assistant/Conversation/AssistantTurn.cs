using System.Text.Json;

namespace MotiffLab.MusicCore.Assistant.Conversation;

/// <summary>What one entry of a conversation is.</summary>
public enum TurnEntryKind
{
    /// <summary>The player's words.</summary>
    Question,

    /// <summary>The tools the model asked the device to run.</summary>
    ToolCalls,

    /// <summary>What the device's music-core answered to them.</summary>
    ToolResults,

    /// <summary>The model's answer, as checked (<see cref="AnswerCheck"/>).</summary>
    Answer,
}

/// <summary>One tool call the model asks for: its id in the turn, the tool's name and its arguments.</summary>
public sealed record ToolCall(string Id, string Name, JsonElement Arguments);

/// <summary>What the device answered to a call: the call's id, the tool and the answer as <see cref="AssistantTools.WriteResult"/> writes it.</summary>
public sealed record ToolOutcome(string Id, string Name, JsonElement Content);

/// <summary>One entry of a conversation: a question, tool calls, their results, or an answer.</summary>
public sealed record TurnEntry(TurnEntryKind Kind)
{
    /// <summary>The question's or the answer's text; null for calls and results.</summary>
    public string? Text { get; init; }

    public IReadOnlyList<ToolCall> Calls { get; init; } = [];

    public IReadOnlyList<ToolOutcome> Results { get; init; } = [];

    public static TurnEntry Question(string text) => new(TurnEntryKind.Question) { Text = text };

    public static TurnEntry Called(IReadOnlyList<ToolCall> calls) => new(TurnEntryKind.ToolCalls) { Calls = calls };

    public static TurnEntry Resulted(IReadOnlyList<ToolOutcome> results) => new(TurnEntryKind.ToolResults) { Results = results };

    public static TurnEntry Answered(string text) => new(TurnEntryKind.Answer) { Text = text };

    internal void Write(Utf8JsonWriter json)
    {
        json.WriteStartObject();
        json.WriteString("kind", ToolSchema.Camel(Kind.ToString()));
        switch (Kind)
        {
            case TurnEntryKind.ToolCalls:
                json.WriteStartArray("calls");
                foreach (ToolCall call in Calls)
                {
                    json.WriteStartObject();
                    json.WriteString("id", call.Id);
                    json.WriteString("name", call.Name);
                    json.WritePropertyName("arguments");
                    call.Arguments.WriteTo(json);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                break;
            case TurnEntryKind.ToolResults:
                json.WriteStartArray("results");
                foreach (ToolOutcome result in Results)
                {
                    json.WriteStartObject();
                    json.WriteString("id", result.Id);
                    json.WriteString("name", result.Name);
                    json.WritePropertyName("content");
                    result.Content.WriteTo(json);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                break;
            default:
                json.WriteString("text", Text);
                break;
        }

        json.WriteEndObject();
    }

    /// <summary>The entry written in <paramref name="json"/>, or null when it is not one.</summary>
    internal static TurnEntry? Read(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("kind", out JsonElement kind)
            || Enum.GetValues<TurnEntryKind>().Cast<TurnEntryKind?>().FirstOrDefault(value => ToolSchema.Camel(value!.Value.ToString()) == kind.GetString()) is not { } read)
        {
            return null;
        }

        return read switch
        {
            TurnEntryKind.ToolCalls => Items(json, "calls") is { } calls && calls.All(static call => StringOf(call, "id") is not null && StringOf(call, "name") is not null && call.TryGetProperty("arguments", out _))
                ? Called([.. calls.Select(static call => new ToolCall(StringOf(call, "id")!, StringOf(call, "name")!, call.GetProperty("arguments").Clone()))])
                : null,
            TurnEntryKind.ToolResults => Items(json, "results") is { } results && results.All(static result => StringOf(result, "id") is not null && StringOf(result, "name") is not null && result.TryGetProperty("content", out _))
                ? Resulted([.. results.Select(static result => new ToolOutcome(StringOf(result, "id")!, StringOf(result, "name")!, result.GetProperty("content").Clone()))])
                : null,
            _ => StringOf(json, "text") is { } text ? new TurnEntry(read) { Text = text } : null,
        };
    }

    private static JsonElement[]? Items(JsonElement json, string name) =>
        json.TryGetProperty(name, out JsonElement items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0 ? [.. items.EnumerateArray()] : null;

    private static string? StringOf(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}