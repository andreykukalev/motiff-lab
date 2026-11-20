using System.Text.Json;

namespace MotiffLab.MusicCore.Assistant.Conversation;

/// <summary>What the server answers to a turn.</summary>
public enum TurnReplyKind
{
    /// <summary>The model asks the device to run tools and send their results in the next turn.</summary>
    ToolCalls,

    /// <summary>The model's answer, checked against the facts it may cite, or the engine's own template text when it failed the check.</summary>
    Answer,

    /// <summary>No model is switched on: nothing was sent to any provider.</summary>
    NotConfigured,
}

/// <summary>The server's answer to a <see cref="TurnRequest"/>: tool calls to run, an answer, or that no model is switched on.</summary>
public sealed record TurnReply(TurnReplyKind Kind)
{
    public IReadOnlyList<ToolCall> Calls { get; init; } = [];

    /// <summary>The answer's text, citing facts as [[id]]; null for the other kinds.</summary>
    public string? Text { get; init; }

    /// <summary>True when the model's answer failed the check and <see cref="Text"/> is the engine's template instead.</summary>
    public bool Fallback { get; init; }

    /// <summary>
    /// Why the answer is the template, with <see cref="Fallback"/>; null otherwise. The answer's check (<see cref="AnswerCheck"/>):
    /// empty, tooLong, noCitation, unknownFact; the relay: timeout, modelError, toolNotAllowed, badArguments, tooManyCalls.
    /// Written as "reason" beside "fallback" (added in version 1.2; a device that does not read it shows the template as before).
    /// </summary>
    public string? Reason { get; init; }

    public static TurnReply NotConfigured { get; } = new(TurnReplyKind.NotConfigured);

    public static TurnReply Called(IReadOnlyList<ToolCall> calls) => new(TurnReplyKind.ToolCalls) { Calls = calls };

    public static TurnReply Answered(CheckedAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return new(TurnReplyKind.Answer) { Text = answer.Text, Fallback = answer.FellBack, Reason = answer.FellBack ? answer.Reason : null };
    }

    public void Write(Utf8JsonWriter json)
    {
        ArgumentNullException.ThrowIfNull(json);
        json.WriteStartObject();
        json.WriteString("kind", ToolSchema.Camel(Kind.ToString()));
        if (Kind == TurnReplyKind.ToolCalls)
        {
            json.WritePropertyName("entry");
            TurnEntry.Called(Calls).Write(json);
        }
        else if (Kind == TurnReplyKind.Answer)
        {
            json.WriteString("text", Text);
            json.WriteBoolean("fallback", Fallback);
            if (Fallback && Reason is not null)
            {
                json.WriteString("reason", Reason);
            }
        }

        json.WriteEndObject();
    }

    /// <summary>The reply in <paramref name="json"/>, or null when it is not one.</summary>
    public static TurnReply? Read(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("kind", out JsonElement kind) || kind.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return kind.GetString() switch
        {
            "notConfigured" => NotConfigured,
            "answer" when json.TryGetProperty("text", out JsonElement text) && text.ValueKind == JsonValueKind.String =>
                new TurnReply(TurnReplyKind.Answer)
                {
                    Text = text.GetString(),
                    Fallback = json.TryGetProperty("fallback", out JsonElement fallback) && fallback.ValueKind == JsonValueKind.True,
                    Reason = json.TryGetProperty("reason", out JsonElement reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null,
                },
            "toolCalls" when json.TryGetProperty("entry", out JsonElement entry) && TurnEntry.Read(entry) is { Kind: TurnEntryKind.ToolCalls } called => Called(called.Calls),
            _ => null,
        };
    }
}