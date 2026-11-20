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
/// <param name="Context">What the question is about: the passage's facts digest and notes, a draft's place (its digest, its base's and its variant's notes), or the piece's digest.</param>
/// <param name="History">The conversation so far, ending on the question or on the results of the model's last calls.</param>
public sealed record ModelTurn(string Instructions, string Language, JsonElement Tools, JsonElement Context, IReadOnlyList<TurnEntry> History)
{
    /// <summary>
    /// Where the context goes among the history's entries: just before the question it belongs to, the player's last one, so
    /// earlier questions about other bars come before it and the bars the player selected now stand beside the question about
    /// them. Fixed when the turn is made, from the history it is made with: a turn extended by <c>with { History = … }</c>
    /// keeps it (as <see cref="AnswerRetry"/> wants: its note is not the player's question), so a copy whose history holds
    /// another player's question must set it again, <c>ContextAt = TurnRequest.LastQuestion(history)</c>.
    /// </summary>
    public int ContextAt { get; init; } = TurnRequest.LastQuestion(History);

    /// <summary>
    /// The context as the model reads it: what the question is about (the bars selected, a draft's place, or the whole
    /// piece), then music-core's JSON.
    /// </summary>
    public string ContextText
    {
        get
        {
            string? scope = Context.TryGetProperty("digest", out JsonElement digest) && digest.TryGetProperty("scope", out JsonElement named) ? named.GetString() : null;
            string about = scope switch
            {
                "selection" when digest.TryGetProperty("bars", out JsonElement bars) && bars.TryGetProperty("from", out JsonElement from) && bars.TryGetProperty("to", out JsonElement to) =>
                    $"The player selected {(from.GetInt32() == to.GetInt32() ? $"bar {from.GetInt32()}" : $"bars {from.GetInt32()}-{to.GetInt32()}")} in the score, and the next question is about them (\"here\", \"this\", \"these bars\" mean them). What music-core gives for them: their facts digest and their notes by address",
                "draftPlace" =>
                    "The player is assembling a draft, and the next question is about one place of it (\"here\", \"this place\" mean it). What music-core gives for it: the place's facts digest (where its base and the variant built from came from, the techniques of that variant, the seams either side), the base's notes by address and the variant's notes as variantNotes, each in its own bars from 1",
                _ => "The next question is about the whole piece. What music-core gives for it: the piece's facts digest",
            };
            return $"{about}:\n{Context.GetRawText()}";
        }
    }
}

/// <summary>The model's step: tool calls, or the answer's text when there are none; and what it cost, in tokens.</summary>
public sealed record ModelReply(IReadOnlyList<ToolCall> Calls, string? Text, int InputTokens = 0, int OutputTokens = 0);