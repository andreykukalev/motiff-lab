using System.Text.Json;

namespace MotiffLab.MusicCore.Assistant.Conversation;

/// <summary>
/// Runs the model's tool calls on the device (0028 section 4): each through the registry (<see cref="AssistantTools.Call"/>),
/// only the role's tools, and no more than <see cref="AssistantRoles.MostToolCalls"/> for one question. A call it will not
/// run is answered with a refusal the model reads, so the turn still answers every call. Nothing is applied to the piece.
/// </summary>
public static class TurnRunner
{
    /// <summary>The results of <paramref name="calls"/>, in their order, when <paramref name="callsBefore"/> calls were already made for this question.</summary>
    public static IReadOnlyList<ToolOutcome> Run(AssistantPiece piece, AssistantRole role, IReadOnlyList<ToolCall> calls, int callsBefore)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(calls);
        Words w = piece.Words;
        var outcomes = new List<ToolOutcome>();
        foreach ((ToolCall call, int index) in calls.Select((call, index) => (call, index)))
        {
            ToolResult result = callsBefore + index >= AssistantRoles.MostToolCalls
                ? ToolResult.Refused(w.T(
                    $"A question takes at most {AssistantRoles.MostToolCalls} tool calls; answer with what you have.",
                    $"На один вопрос — не больше {AssistantRoles.MostToolCalls} вызовов инструментов; ответьте по тому, что уже есть."))
                : !AssistantRoles.Allows(role, call.Name)
                    ? ToolResult.Refused(w.T($"The {AssistantRoles.Id(role)} has no tool \"{call.Name}\".", $"У роли «{AssistantRoles.Id(role)}» нет инструмента «{call.Name}»."))
                    : AssistantTools.Call(piece, call.Name, call.Arguments);
            outcomes.Add(new ToolOutcome(call.Id, call.Name, Written(piece, call.Name, result)));
        }

        return outcomes;
    }

    private static JsonElement Written(AssistantPiece piece, string name, ToolResult result)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            AssistantTools.WriteResult(json, piece, name, result);
            json.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }
}