using System.Text.Json;

using MotiffLab.MusicCore.Assistant.Conversation;

namespace MotiffLab.Api.Assistant;

/// <summary>
/// The language model behind the assistant, through one replaceable interface (decision record 0003, section 3; 0028
/// section 4). An adapter turns one turn into the provider's request (instructions, the tools, the context and the
/// history) and the provider's answer into tool calls or text. It never runs a tool, never stores anything and never
/// logs the turn's content: the relay checks what it answers.
/// </summary>
public interface IAssistantModel
{
    /// <summary>The provider and model, such as "provider/model-name", for the status and the logs.</summary>
    string Name { get; }

    /// <summary>The model's next step for this turn: tool calls for the device to run, or the answer's text.</summary>
    Task<ModelReply> NextAsync(ModelTurn turn, CancellationToken cancel);
}

/// <summary>One turn as the model is given it.</summary>
/// <param name="Instructions">The role's instructions, a versioned file of this repository (Assistant/Prompts).</param>
/// <param name="Tools">The role's tools, {"tools": [{name, kind, description, parameters}]}, parameters as strict JSON schemas.</param>
/// <param name="Context">What the question is about: the passage's facts digest and notes, or the piece's digest.</param>
/// <param name="History">The conversation so far, ending on the question or on the results of the model's last calls.</param>
public sealed record ModelTurn(string Instructions, string Language, JsonElement Tools, JsonElement Context, IReadOnlyList<TurnEntry> History);

/// <summary>The model's step: tool calls, or the answer's text when there are none; and what it cost, in tokens.</summary>
public sealed record ModelReply(IReadOnlyList<ToolCall> Calls, string? Text, int InputTokens = 0, int OutputTokens = 0);