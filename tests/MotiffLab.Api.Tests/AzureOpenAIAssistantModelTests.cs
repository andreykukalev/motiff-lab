using System.Net;
using System.Text.Json;

using MotiffLab.Api.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;

namespace MotiffLab.Api.Tests;

/// <summary>
/// The Azure OpenAI adapter (0028 section 4) against a fake provider: how a turn becomes a stateless Responses request on
/// the resource's v1 API, how the response becomes tool calls or text, and which settings give no model. No network, no key.
/// </summary>
public sealed class AzureOpenAIAssistantModelTests
{
    private const string ToolCallReply = """
        {"id": "resp_1", "object": "response", "created_at": 1763200000, "status": "completed", "model": "gpt-6.1-sol",
         "output": [{"type": "reasoning", "id": "rs_1", "summary": []},
                    {"type": "function_call", "id": "fc_1", "call_id": "call_2", "name": "harmony_chords",
                     "arguments": "{\"bars\": {\"from\": 6, \"to\": 7}}", "status": "completed"}],
         "usage": {"input_tokens": 180, "input_tokens_details": {"cached_tokens": 30},
                   "output_tokens": 20, "output_tokens_details": {"reasoning_tokens": 12}, "total_tokens": 200}}
        """;

    private const string TextReply = """
        {"id": "resp_2", "object": "response", "created_at": 1763200000, "status": "completed", "model": "gpt-6.1-sol",
         "output": [{"type": "message", "id": "msg_1", "role": "assistant", "status": "completed",
                     "content": [{"type": "output_text", "text": "Bar 6 is A minor ", "annotations": []},
                                 {"type": "output_text", "text": "[[chord.6.1]].", "annotations": []}]}],
         "usage": {"input_tokens": 12, "input_tokens_details": {"cached_tokens": 0},
                   "output_tokens": 7, "output_tokens_details": {"reasoning_tokens": 0}, "total_tokens": 19}}
        """;

    private const string RefusalReply = """
        {"id": "resp_3", "object": "response", "created_at": 1763200000, "status": "completed", "model": "gpt-6.1-sol",
         "output": [{"type": "message", "id": "msg_2", "role": "assistant", "status": "completed",
                     "content": [{"type": "refusal", "refusal": "I can't help with that."}]}],
         "usage": {"input_tokens": 12, "input_tokens_details": {"cached_tokens": 0},
                   "output_tokens": 3, "output_tokens_details": {"reasoning_tokens": 0}, "total_tokens": 15}}
        """;

    private const string IncompleteReply = """
        {"id": "resp_4", "object": "response", "created_at": 1763200000, "status": "incomplete",
         "incomplete_details": {"reason": "max_output_tokens"}, "model": "gpt-6.1-sol",
         "output": [{"type": "message", "id": "msg_3", "role": "assistant", "status": "incomplete",
                     "content": [{"type": "output_text", "text": "Bar 6 is", "annotations": []}]}],
         "usage": {"input_tokens": 12, "input_tokens_details": {"cached_tokens": 0},
                   "output_tokens": 16000, "output_tokens_details": {"reasoning_tokens": 15990}, "total_tokens": 16012}}
        """;

    private static IAssistantModel Azure(FakeProvider provider, string? effort = null, string endpoint = "https://motifflab-ai.openai.azure.com") =>
        ModelTurns.Model(
            new()
            {
                ["Assistant:Provider"] = "azure-openai",
                ["Assistant:Key"] = "azure-key",
                ["Assistant:Endpoint"] = endpoint,
                ["Assistant:Model"] = "gpt-6.1-sol",
                ["Assistant:Effort"] = effort,
            },
            provider)!;

    [Fact]
    public async Task Every_tool_of_the_proposer_goes_to_the_provider_as_a_strict_tool()
    {
        var provider = new FakeProvider(TextReply);
        await Azure(provider).NextAsync(ModelTurns.ProposerTurn(), CancellationToken.None);

        JsonElement[] tools = [.. provider.Body.GetProperty("tools").EnumerateArray()];
        Assert.Equal(AssistantRoles.ToolsOf(AssistantRole.Proposer).Select(static tool => tool.Name.Replace('.', '_')), tools.Select(static tool => tool.GetProperty("name").GetString()));
        Assert.All(tools, static tool => Assert.True(tool.GetProperty("strict").GetBoolean()));
        Assert.All(tools, static tool => Assert.False(tool.GetProperty("parameters").GetProperty("additionalProperties").GetBoolean()));
    }

    [Fact]
    public async Task A_turn_is_one_stateless_strict_Responses_request_with_the_history_as_input_items()
    {
        var provider = new FakeProvider(ToolCallReply);
        IAssistantModel model = Azure(provider);
        Assert.Equal("azure-openai/gpt-6.1-sol", model.Name);

        await model.NextAsync(ModelTurns.Turn(), CancellationToken.None);

        Assert.Equal("https://motifflab-ai.openai.azure.com/openai/v1/responses", provider.Uri!.GetLeftPart(UriPartial.Path));
        Assert.Equal("Bearer azure-key", provider.Header("Authorization"));
        JsonElement body = provider.Body;
        Assert.Equal("gpt-6.1-sol", body.GetProperty("model").GetString());
        Assert.Equal("You are the mentor.", body.GetProperty("instructions").GetString());
        Assert.False(body.GetProperty("store").GetBoolean());
        Assert.False(body.TryGetProperty("previous_response_id", out _));
        Assert.Equal(ClaudeAssistantModel.MaxTokens, body.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("medium", body.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("auto", body.GetProperty("tool_choice").GetString());
        Assert.False(body.TryGetProperty("temperature", out _));

        JsonElement[] tools = [.. body.GetProperty("tools").EnumerateArray()];
        Assert.Equal(AssistantRoles.ToolsOf(AssistantRole.Mentor).Select(static tool => tool.Name.Replace('.', '_')), tools.Select(static tool => tool.GetProperty("name").GetString()));
        Assert.All(tools, static tool => Assert.Equal("function", tool.GetProperty("type").GetString()));
        Assert.All(tools, static tool => Assert.True(tool.GetProperty("strict").GetBoolean()));
        JsonElement chords = tools.Single(static tool => tool.GetProperty("name").GetString() == "harmony_chords").GetProperty("parameters");
        Assert.False(chords.GetProperty("additionalProperties").GetBoolean());
        JsonElement bars = chords.GetProperty("properties").GetProperty("bars");
        Assert.False(bars.GetProperty("additionalProperties").GetBoolean());
        JsonElement from = bars.GetProperty("properties").GetProperty("from");
        Assert.False(from.TryGetProperty("minimum", out _));
        Assert.Contains("(Limits: minimum 0, maximum", from.GetProperty("description").GetString(), StringComparison.Ordinal);

        JsonElement[] input = [.. body.GetProperty("input").EnumerateArray()];
        Assert.Equal(["message", "message", "function_call", "function_call_output"], input.Select(static item => item.GetProperty("type").GetString()));
        Assert.Equal(["user", "user"], input[..2].Select(static item => item.GetProperty("role").GetString()));
        Assert.Contains("\"chord.6.1\"", input[0].GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal("Why does bar 6 sound so dark?", input[1].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("toolu_1", input[2].GetProperty("call_id").GetString());
        Assert.Equal("harmony_chords", input[2].GetProperty("name").GetString());
        Assert.False(input[2].TryGetProperty("id", out _));
        Assert.Equal(6, ModelTurns.Json(input[2].GetProperty("arguments").GetString()!).GetProperty("bars").GetProperty("from").GetInt32());
        Assert.Equal("toolu_1", input[3].GetProperty("call_id").GetString());
        Assert.Contains("No bar 6.", input[3].GetProperty("output").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_models_function_calls_come_back_under_the_catalogues_names_with_the_tokens_they_cost()
    {
        ModelReply reply = await Azure(new FakeProvider(ToolCallReply)).NextAsync(ModelTurns.Turn(), CancellationToken.None);

        ToolCall call = Assert.Single(reply.Calls);
        Assert.Equal("call_2", call.Id);
        Assert.Equal("harmony.chords", call.Name);
        Assert.Equal(7, call.Arguments.GetProperty("bars").GetProperty("to").GetInt32());
        Assert.Null(reply.Text);
        Assert.Equal((180, 20), (reply.InputTokens, reply.OutputTokens));
    }

    [Fact]
    public async Task The_models_answer_comes_back_as_its_text_and_a_refusal_or_a_cut_answer_as_no_text()
    {
        ModelReply answer = await Azure(new FakeProvider(TextReply)).NextAsync(ModelTurns.Turn(), CancellationToken.None);
        Assert.Empty(answer.Calls);
        Assert.Equal("Bar 6 is A minor [[chord.6.1]].", answer.Text);
        Assert.Equal((12, 7), (answer.InputTokens, answer.OutputTokens));

        foreach (string reply in new[] { RefusalReply, IncompleteReply })
        {
            ModelReply none = await Azure(new FakeProvider(reply)).NextAsync(ModelTurns.Turn(), CancellationToken.None);
            Assert.Empty(none.Calls);
            Assert.Null(none.Text);
        }
    }

    [Theory]
    [InlineData("motifflab-ai", null, "https://motifflab-ai.openai.azure.com/openai/v1/responses", "medium")]
    [InlineData("https://motifflab-ai.openai.azure.com/", "low", "https://motifflab-ai.openai.azure.com/openai/v1/responses", "low")]
    [InlineData("https://motifflab-ai.services.ai.azure.com/openai/v1/", "max", "https://motifflab-ai.services.ai.azure.com/openai/v1/responses", "xhigh")]
    public async Task The_endpoint_is_the_resource_and_the_effort_its_reasoning_effort(string endpoint, string? effort, string address, string reasoning)
    {
        var provider = new FakeProvider(TextReply);
        await Azure(provider, effort, endpoint).NextAsync(ModelTurns.Turn(), CancellationToken.None);

        Assert.Equal(address, provider.Uri!.GetLeftPart(UriPartial.Path));
        Assert.Equal(reasoning, provider.Body.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    [Fact]
    public async Task A_provider_error_is_a_failed_request_the_relay_answers_with_the_template()
    {
        var provider = new FakeProvider("""{"error": {"code": "DeploymentNotFound", "message": "Bad."}}""", HttpStatusCode.NotFound);
        await Assert.ThrowsAsync<HttpRequestException>(() => Azure(provider).NextAsync(ModelTurns.Turn(), CancellationToken.None));
        Assert.Equal(1, provider.Requests);
    }

    [Theory]
    [InlineData("", "https://motifflab-ai.openai.azure.com", "gpt-6.1-sol")]
    [InlineData("k", "", "gpt-6.1-sol")]
    [InlineData("k", "not a resource!", "gpt-6.1-sol")]
    [InlineData("k", "http://motifflab-ai.openai.azure.com", "gpt-6.1-sol")]
    [InlineData("k", "https://example.com", "gpt-6.1-sol")]
    [InlineData("k", "https://motifflab-ai.openai.azure.com", "")]
    [InlineData("k", "motifflab-ai", "  ")]
    public void Without_a_key_the_resource_or_the_deployment_there_is_no_model(string key, string endpoint, string deployment)
    {
        Assert.Null(ModelTurns.Model(new()
        {
            ["Assistant:Provider"] = "azure-openai",
            ["Assistant:Key"] = key,
            ["Assistant:Endpoint"] = endpoint,
            ["Assistant:Model"] = deployment,
        }));
    }

    [Fact]
    public void The_schema_moves_every_keyword_strict_mode_does_not_take_and_closes_every_object()
    {
        JsonElement schema = ModelTurns.Json(StrictToolSchemas.ForOpenAI(ModelTurns.Json("""
            {"type": "object", "properties": {
               "picks": {"description": "Which.", "anyOf": [{"type": "array", "items": {"type": "integer", "enum": [1, 2]}, "minItems": 1, "maxItems": 4}, {"type": "null"}]},
               "span": {"type": "object", "properties": {"name": {"type": "string", "pattern": "^[0-9]+$", "maxLength": 8}}, "required": ["name"]}},
             "required": ["picks", "span"]}
            """)));

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        JsonElement list = schema.GetProperty("properties").GetProperty("picks").GetProperty("anyOf")[0];
        Assert.False(list.TryGetProperty("minItems", out _));
        Assert.Equal("(Limits: minItems 1, maxItems 4.)", list.GetProperty("description").GetString());
        JsonElement span = schema.GetProperty("properties").GetProperty("span");
        Assert.False(span.GetProperty("additionalProperties").GetBoolean());
        JsonElement name = span.GetProperty("properties").GetProperty("name");
        Assert.False(name.TryGetProperty("pattern", out _));
        Assert.Equal("(Limits: maxLength 8, pattern \"^[0-9]\\u002B$\".)", name.GetProperty("description").GetString());
    }
}