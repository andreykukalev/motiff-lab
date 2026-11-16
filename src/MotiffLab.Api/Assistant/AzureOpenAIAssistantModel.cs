using System.ClientModel;
using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Assistant.Conversation;

using OpenAI.Responses;

namespace MotiffLab.Api.Assistant;

/// <summary>How the adapter asks Azure OpenAI: the name for the logs, the deployment, and the reasoning effort.</summary>
internal sealed record AzureOpenAISettings(string Name, string Deployment, ResponseReasoningEffortLevel Effort);

/// <summary>
/// The assistant's model on Azure OpenAI in Microsoft Foundry (a GPT deployment), through the official OpenAI SDK on the
/// v1 Responses API (0028 section 4). One turn is one stateless Responses request (store false, no previous response): the
/// role's instructions as the instructions, its tools as strict function tools; the question's context and the history as
/// input items, tool calls as function_call items and their results as function_call_output items. Effort is a setting. A
/// refused or incomplete response (the token limit, the content filter) answers no text, so the relay shows the engine's
/// template, never the model's refusal. It logs only the status and the error's type, never the turn's content.
/// </summary>
internal sealed class AzureOpenAIAssistantModel(ResponsesClient client, AzureOpenAISettings settings, ILogger logger) : IAssistantModel
{
    public string Name => settings.Name;

    public async Task<ModelReply> NextAsync(ModelTurn turn, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(turn);
        var names = ToolNames.From(turn.Tools);
        ResponseResult response;
        try
        {
            response = await client.CreateResponseAsync(AzureOpenAIRequest.Build(turn, settings, names), cancel);
        }
        catch (ClientResultException error)
        {
            cancel.ThrowIfCancellationRequested();
            logger.LogWarning("The model provider failed the turn: {Error} ({Status}).", error.GetType().Name, error.Status);
            throw new HttpRequestException($"The model provider failed the turn ({error.GetType().Name} {error.Status}).", error);
        }

        return AzureOpenAIReply.Read(response, names, logger);
    }
}

/// <summary>A turn as a Responses request.</summary>
internal static class AzureOpenAIRequest
{
    public static CreateResponseOptions Build(ModelTurn turn, AzureOpenAISettings settings, ToolNames names)
    {
        var request = new CreateResponseOptions
        {
            Model = settings.Deployment,
            Instructions = turn.Instructions,
            StoredOutputEnabled = false,
            MaxOutputTokenCount = ClaudeAssistantModel.MaxTokens,
            ReasoningOptions = new ResponseReasoningOptions { ReasoningEffortLevel = settings.Effort },
            ToolChoice = ResponseToolChoice.CreateAutoChoice(),
        };
        foreach (JsonElement tool in ToolNames.Entries(turn.Tools))
        {
            request.Tools.Add(ResponseTool.CreateFunctionTool(
                functionName: names.Wire(tool.GetProperty("name").GetString()!),
                functionParameters: BinaryData.FromString(StrictToolSchemas.ForOpenAI(tool.GetProperty("parameters"))),
                strictModeEnabled: true,
                functionDescription: ToolNames.Description(tool)));
        }

        foreach (ResponseItem item in Input(turn, names))
        {
            request.InputItems.Add(item);
        }

        return request;
    }

    /// <summary>The history in order, the context just before the question it belongs to (<see cref="ModelTurn.ContextAt"/>).</summary>
    private static IEnumerable<ResponseItem> Input(ModelTurn turn, ToolNames names)
    {
        int contextAt = turn.ContextAt;
        for (int index = 0; index <= turn.History.Count; index++)
        {
            if (index == contextAt)
            {
                yield return ResponseItem.CreateUserMessageItem(turn.ContextText);
            }

            if (index == turn.History.Count)
            {
                yield break;
            }

            foreach (ResponseItem item in Items(turn.History[index], names))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<ResponseItem> Items(TurnEntry entry, ToolNames names) => entry.Kind switch
    {
        TurnEntryKind.Question => Message(entry.Text, static text => ResponseItem.CreateUserMessageItem(text)),
        TurnEntryKind.ToolCalls => entry.Calls.Select(call => (ResponseItem)ResponseItem.CreateFunctionCallItem(
            call.Id, names.Wire(call.Name), BinaryData.FromString(call.Arguments.ValueKind == JsonValueKind.Object ? call.Arguments.GetRawText() : "{}"))),
        TurnEntryKind.ToolResults => entry.Results.Select(static result => (ResponseItem)ResponseItem.CreateFunctionCallOutputItem(result.Id, result.Content.GetRawText())),
        _ => Message(entry.Text, static text => ResponseItem.CreateAssistantMessageItem(text)),
    };

    private static IEnumerable<ResponseItem> Message(string? text, Func<string, MessageResponseItem> create) =>
        string.IsNullOrWhiteSpace(text) ? [] : [create(text)];
}

/// <summary>The response as the relay takes it: its function calls, else its text; and the tokens it cost.</summary>
internal static class AzureOpenAIReply
{
    public static ModelReply Read(ResponseResult response, ToolNames names, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(response);
        int input = (int)Math.Min(response.Usage?.InputTokenCount ?? 0, int.MaxValue);
        int output = (int)Math.Min(response.Usage?.OutputTokenCount ?? 0, int.MaxValue);
        if (response.Status != ResponseStatus.Completed)
        {
            logger.LogWarning("The model gave no usable answer: status {Status}, {Reason}.", response.Status, response.IncompleteStatusDetails?.Reason);
            return new ModelReply([], null, input, output);
        }

        var calls = new List<ToolCall>();
        var text = new StringBuilder();
        foreach (ResponseItem item in response.OutputItems)
        {
            if (item is FunctionCallResponseItem call)
            {
                calls.Add(new ToolCall(call.CallId, names.Catalogue(call.FunctionName), Arguments(call.FunctionArguments)));
            }
            else if (item is MessageResponseItem message)
            {
                if (message.Content.Any(static part => part.Kind == ResponseContentPartKind.Refusal))
                {
                    logger.LogWarning("The model gave no usable answer: refusal.");
                    return new ModelReply([], null, input, output);
                }

                text.AppendJoin("", message.Content.Where(static part => part.Kind == ResponseContentPartKind.OutputText).Select(static part => part.Text));
            }
        }

        return calls.Count > 0 ? new ModelReply(calls, null, input, output) : new ModelReply([], text.ToString(), input, output);
    }

    /// <summary>The call's arguments as JSON; arguments that are not a JSON object are an empty one, which the device refuses.</summary>
    private static JsonElement Arguments(BinaryData arguments)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(arguments.ToMemory());
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                return json.RootElement.Clone();
            }
        }
        catch (JsonException)
        {
        }

        return JsonSerializer.SerializeToElement(new Dictionary<string, object>());
    }
}