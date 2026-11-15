using System.Text;
using System.Text.Json;

using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

using MotiffLab.MusicCore.Assistant.Conversation;

namespace MotiffLab.Api.Assistant;

/// <summary>How the adapter asks Claude: the name for the logs, the model or deployment, the effort, and whether a declined request is retried on the provider's fallback model.</summary>
internal sealed record ClaudeSettings(string Name, string Model, Effort Effort, bool Fallbacks);

/// <summary>
/// The assistant's model on Claude, through the official Anthropic SDK (0028 section 4), the same for the Claude API and for
/// Claude on Microsoft Foundry: only the client differs (<see cref="AssistantModels"/>). One turn is one Messages request:
/// the role's instructions as the system prompt and its tools, both cached (they are the same for every turn of a role and
/// language); the question's context and the history as messages, tool calls as tool_use and their results as tool_result.
/// Thinking is left to the model (adaptive) and effort is a setting. A declined request (stop reason "refusal") or one cut off
/// by the token limit answers no text, so the relay shows the engine's template, never the model's refusal. It logs only the
/// stop reason and the error's type, never the turn's content, and stores nothing.
/// </summary>
internal sealed class ClaudeAssistantModel(AnthropicClient client, ClaudeSettings settings, ILogger logger) : IAssistantModel
{
    /// <summary>The longest reply in tokens, thinking included: an answer is at most 2000 characters, the rest is room to think.</summary>
    public const int MaxTokens = 16000;

    public string Name => settings.Name;

    public async Task<ModelReply> NextAsync(ModelTurn turn, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(turn);
        var names = ToolNames.From(turn.Tools);
        BetaMessage message;
        try
        {
            message = await client.Beta.Messages.Create(ClaudeRequest.Build(turn, settings, names), cancel);
        }
        catch (AnthropicException error)
        {
            cancel.ThrowIfCancellationRequested();
            logger.LogWarning("The model provider failed the turn: {Error}.", error.GetType().Name);
            throw new HttpRequestException($"The model provider failed the turn ({error.GetType().Name}).", error);
        }

        return ClaudeReply.Read(message, names, logger);
    }
}

/// <summary>A turn as a Claude Messages request.</summary>
internal static class ClaudeRequest
{
    public static MessageCreateParams Build(ModelTurn turn, ClaudeSettings settings, ToolNames names)
    {
        var request = new MessageCreateParams
        {
            Model = settings.Model,
            MaxTokens = ClaudeAssistantModel.MaxTokens,
            System = new List<BetaTextBlockParam> { new() { Text = turn.Instructions, CacheControl = new BetaCacheControlEphemeral() } },
            Tools = [.. Tools(turn.Tools, names)],
            ToolChoice = new BetaToolChoiceAuto(),
            Messages = Messages(turn, names),
            OutputConfig = new BetaOutputConfig { Effort = settings.Effort },
            CacheControl = new BetaCacheControlEphemeral(),
        };

        // A field set to null is sent as null, so the fallback fields are only set where they are wanted.
        return settings.Fallbacks ? request with { Betas = [AnthropicBeta.ServerSideFallback2026_07_01], Fallbacks = new Default() } : request;
    }

    /// <summary>The role's tools, strict, named as Claude allows (<see cref="ToolNames"/>).</summary>
    private static IEnumerable<BetaToolUnion> Tools(JsonElement catalogue, ToolNames names) =>
        ToolNames.Entries(catalogue).Select(tool => (BetaToolUnion)new BetaTool
        {
            Name = names.Wire(tool.GetProperty("name").GetString()!),
            Description = ToolNames.Description(tool),
            InputSchema = new InputSchema(StrictToolSchemas.ForClaude(tool.GetProperty("parameters"))),
            Strict = true,
        });

    /// <summary>The context first, then the history; entries of the same side in a row share one message.</summary>
    private static List<BetaMessageParam> Messages(ModelTurn turn, ToolNames names)
    {
        var messages = new ClaudeMessages();
        messages.Add(Role.User, Text(Context(turn.Context)));
        foreach (TurnEntry entry in turn.History)
        {
            switch (entry.Kind)
            {
                case TurnEntryKind.Question:
                    messages.Add(Role.User, Text(entry.Text));
                    break;
                case TurnEntryKind.ToolCalls:
                    messages.Add(Role.Assistant, [.. entry.Calls.Select(call => ToolUse(call, names))]);
                    break;
                case TurnEntryKind.ToolResults:
                    messages.Add(Role.User, [.. entry.Results.Select(ToolResult)]);
                    break;
                default:
                    messages.Add(Role.Assistant, Text(entry.Text));
                    break;
            }
        }

        return messages.Build();
    }

    private static string Context(JsonElement context) =>
        $"The context of the question, as music-core gives it (its facts digest and, for a passage, its notes):\n{context.GetRawText()}";

    private static BetaContentBlockParam[] Text(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : [new BetaTextBlockParam { Text = text }];

    private static BetaContentBlockParam ToolUse(ToolCall call, ToolNames names) => new BetaToolUseBlockParam
    {
        ID = call.Id,
        Name = names.Wire(call.Name),
        Input = call.Arguments.ValueKind == JsonValueKind.Object
            ? call.Arguments.EnumerateObject().ToDictionary(static property => property.Name, static property => property.Value.Clone(), StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal),
    };

    private static BetaContentBlockParam ToolResult(ToolOutcome result) => new BetaToolResultBlockParam
    {
        ToolUseID = result.Id,
        Content = result.Content.GetRawText(),
        IsError = result.Content.ValueKind == JsonValueKind.Object && result.Content.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.False,
    };

    private enum Role
    {
        User,
        Assistant,
    }

    /// <summary>Builds the messages, joining blocks of the same side in a row (Claude alternates user and assistant).</summary>
    private sealed class ClaudeMessages
    {
        private readonly List<(Role Role, List<BetaContentBlockParam> Blocks)> _messages = [];

        public void Add(Role role, IReadOnlyList<BetaContentBlockParam> blocks)
        {
            if (blocks.Count == 0)
            {
                return;
            }

            if (_messages.Count > 0 && _messages[^1].Role == role)
            {
                _messages[^1].Blocks.AddRange(blocks);
            }
            else
            {
                _messages.Add((role, [.. blocks]));
            }
        }

        public List<BetaMessageParam> Build() =>
            [.. _messages.Select(static message => message.Role == Role.User
                ? (BetaMessageParam)new BetaUserMessageParam { Content = message.Blocks }
                : new BetaAssistantMessageParam { Content = message.Blocks })];
    }
}

/// <summary>Claude's answer as the relay takes it: its tool calls, else its text; and the tokens it cost.</summary>
internal static class ClaudeReply
{
    public static ModelReply Read(BetaMessage message, ToolNames names, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(message);
        (int input, int output) = Tokens(message.Usage);
        string stop = message.StopReason?.Raw() ?? "";
        if (stop is "refusal" or "max_tokens")
        {
            logger.LogWarning("The model gave no usable answer: stop reason {Stop}.", stop);
            return new ModelReply([], null, input, output);
        }

        var calls = new List<ToolCall>();
        var text = new StringBuilder();
        foreach (BetaContentBlock block in message.Content)
        {
            if (block.TryPickToolUse(out BetaToolUseBlock? use))
            {
                calls.Add(new ToolCall(use.ID, names.Catalogue(use.Name), JsonSerializer.SerializeToElement(use.Input)));
            }
            else if (block.TryPickText(out BetaTextBlock? part))
            {
                text.Append(part.Text);
            }
        }

        return calls.Count > 0 ? new ModelReply(calls, null, input, output) : new ModelReply([], text.ToString(), input, output);
    }

    /// <summary>The input tokens with those written to and read from the cache, and the output tokens.</summary>
    private static (int Input, int Output) Tokens(BetaUsage usage)
    {
        long input = usage.InputTokens + (usage.CacheCreationInputTokens ?? 0) + (usage.CacheReadInputTokens ?? 0);
        return ((int)Math.Min(input, int.MaxValue), (int)Math.Min(usage.OutputTokens, int.MaxValue));
    }
}