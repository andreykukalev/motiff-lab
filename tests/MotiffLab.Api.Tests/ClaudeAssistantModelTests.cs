using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using MotiffLab.Api.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;

namespace MotiffLab.Api.Tests;

/// <summary>
/// The Claude adapter (0028 section 4) against a fake provider: how a turn becomes a Messages request on the Claude API and
/// on Microsoft Foundry, how the answer becomes tool calls or text, and which settings give no model. No network, no key.
/// </summary>
public sealed class ClaudeAssistantModelTests
{
    private const string ToolCallReply = """
        {"id": "msg_1", "type": "message", "role": "assistant", "model": "claude-opus-5-5",
         "content": [{"type": "thinking", "thinking": "", "signature": "s"},
                     {"type": "tool_use", "id": "toolu_2", "name": "harmony_chords", "input": {"bars": {"from": 6, "to": 7}}}],
         "stop_reason": "tool_use", "stop_sequence": null,
         "usage": {"input_tokens": 100, "output_tokens": 20, "cache_creation_input_tokens": 50, "cache_read_input_tokens": 30}}
        """;

    private const string TextReply = """
        {"id": "msg_2", "type": "message", "role": "assistant", "model": "claude-opus-5-5",
         "content": [{"type": "text", "text": "Bar 6 is A minor "}, {"type": "text", "text": "[[chord.6.1]]."}],
         "stop_reason": "end_turn", "stop_sequence": null, "usage": {"input_tokens": 12, "output_tokens": 7}}
        """;

    private const string RefusalReply = """
        {"id": "msg_3", "type": "message", "role": "assistant", "model": "claude-opus-5-5",
         "content": [{"type": "text", "text": "I can't help with that."}],
         "stop_reason": "refusal", "stop_sequence": null,
         "stop_details": {"type": "refusal", "category": "cyber", "explanation": null},
         "usage": {"input_tokens": 12, "output_tokens": 3}}
        """;

    private static readonly JsonElement s_context = JsonDocument.Parse("""
        {"digest": {"scope": "range", "items": [{"id": "chord.6.1", "text": "Am, bar 6, beat 1"}], "ids": ["chord.6.1"]}}
        """).RootElement.Clone();

    private static IReadOnlyList<TurnEntry> History() =>
    [
        TurnEntry.Question("Why does bar 6 sound so dark?"),
        TurnEntry.Called([new ToolCall("toolu_1", "harmony.chords", Json("""{"bars": {"from": 6, "to": 6}}"""))]),
        TurnEntry.Resulted([new ToolOutcome("toolu_1", "harmony.chords", Json("""{"tool": "harmony.chords", "ok": false, "refusal": "No bar 6."}"""))]),
    ];

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement MentorTools()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            AssistantRoles.WriteCatalogue(json, AssistantRole.Mentor);
            json.WriteEndObject();
        }

        return Json(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private static ModelTurn Turn() => new("You are the mentor.", "en", MentorTools(), s_context, History());

    private static IAssistantModel? Model(Dictionary<string, string?> settings, FakeProvider? provider = null) =>
        AssistantModels.Create(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), NullLogger.Instance, provider).Model;

    private static IAssistantModel Anthropic(FakeProvider provider, string? effort = null) =>
        Model(new() { ["Assistant:Provider"] = "anthropic", ["Assistant:Key"] = "test-key", ["Assistant:Effort"] = effort }, provider)!;

    [Fact]
    public async Task On_the_Claude_API_a_turn_is_one_cached_strict_request_with_the_history_as_messages()
    {
        var provider = new FakeProvider(ToolCallReply);
        IAssistantModel model = Anthropic(provider);
        Assert.Equal($"anthropic/{AssistantModels.DefaultModel}", model.Name);

        await model.NextAsync(Turn(), CancellationToken.None);

        Assert.Equal("https://api.anthropic.com/v1/messages", provider.Uri!.GetLeftPart(UriPartial.Path));
        Assert.Equal("Bearer test-key", provider.Header("Authorization"));
        Assert.Contains("server-side-fallback-2026-07-01", provider.Header("anthropic-beta"), StringComparison.Ordinal);
        JsonElement body = provider.Body;
        Assert.Equal(AssistantModels.DefaultModel, body.GetProperty("model").GetString());
        Assert.Equal("default", body.GetProperty("fallbacks").GetString());
        Assert.Equal("medium", body.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal("auto", body.GetProperty("tool_choice").GetProperty("type").GetString());
        Assert.False(body.TryGetProperty("thinking", out _));
        Assert.False(body.TryGetProperty("temperature", out _));
        Assert.Equal("ephemeral", body.GetProperty("cache_control").GetProperty("type").GetString());

        JsonElement system = body.GetProperty("system")[0];
        Assert.Equal("You are the mentor.", system.GetProperty("text").GetString());
        Assert.Equal("ephemeral", system.GetProperty("cache_control").GetProperty("type").GetString());

        JsonElement[] tools = [.. body.GetProperty("tools").EnumerateArray()];
        Assert.Equal(AssistantRoles.ToolsOf(AssistantRole.Mentor).Select(static tool => tool.Name.Replace('.', '_')), tools.Select(static tool => tool.GetProperty("name").GetString()));
        Assert.All(tools, static tool => Assert.True(tool.GetProperty("strict").GetBoolean()));
        JsonElement chords = tools.Single(static tool => tool.GetProperty("name").GetString() == "harmony_chords").GetProperty("input_schema");
        Assert.False(chords.GetProperty("additionalProperties").GetBoolean());
        JsonElement from = chords.GetProperty("properties").GetProperty("bars").GetProperty("properties").GetProperty("from");
        Assert.False(from.TryGetProperty("minimum", out _));
        Assert.False(from.TryGetProperty("maximum", out _));
        Assert.Contains("(Limits: minimum 0, maximum", from.GetProperty("description").GetString(), StringComparison.Ordinal);

        JsonElement[] messages = [.. body.GetProperty("messages").EnumerateArray()];
        Assert.Equal(["user", "assistant", "user"], messages.Select(static message => message.GetProperty("role").GetString()));
        JsonElement[] first = [.. messages[0].GetProperty("content").EnumerateArray()];
        Assert.Contains("\"chord.6.1\"", first[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal("Why does bar 6 sound so dark?", first[1].GetProperty("text").GetString());
        JsonElement use = messages[1].GetProperty("content")[0];
        Assert.Equal("tool_use", use.GetProperty("type").GetString());
        Assert.Equal("toolu_1", use.GetProperty("id").GetString());
        Assert.Equal("harmony_chords", use.GetProperty("name").GetString());
        Assert.Equal(6, use.GetProperty("input").GetProperty("bars").GetProperty("from").GetInt32());
        JsonElement result = messages[2].GetProperty("content")[0];
        Assert.Equal("tool_result", result.GetProperty("type").GetString());
        Assert.Equal("toolu_1", result.GetProperty("tool_use_id").GetString());
        Assert.True(result.GetProperty("is_error").GetBoolean());
        Assert.Contains("No bar 6.", result.GetProperty("content").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Claudes_tool_calls_come_back_under_the_catalogues_names_with_the_tokens_they_cost()
    {
        ModelReply reply = await Anthropic(new FakeProvider(ToolCallReply)).NextAsync(Turn(), CancellationToken.None);

        ToolCall call = Assert.Single(reply.Calls);
        Assert.Equal("toolu_2", call.Id);
        Assert.Equal("harmony.chords", call.Name);
        Assert.Equal(7, call.Arguments.GetProperty("bars").GetProperty("to").GetInt32());
        Assert.Null(reply.Text);
        Assert.Equal(180, reply.InputTokens);
        Assert.Equal(20, reply.OutputTokens);
    }

    [Fact]
    public async Task Claudes_answer_comes_back_as_its_text_and_a_refusal_as_no_text()
    {
        ModelReply answer = await Anthropic(new FakeProvider(TextReply)).NextAsync(Turn(), CancellationToken.None);
        Assert.Empty(answer.Calls);
        Assert.Equal("Bar 6 is A minor [[chord.6.1]].", answer.Text);
        Assert.Equal((12, 7), (answer.InputTokens, answer.OutputTokens));

        ModelReply refused = await Anthropic(new FakeProvider(RefusalReply)).NextAsync(Turn(), CancellationToken.None);
        Assert.Empty(refused.Calls);
        Assert.Null(refused.Text);
    }

    [Fact]
    public async Task On_Foundry_the_request_goes_to_the_resource_with_the_deployment_and_without_fallbacks()
    {
        var provider = new FakeProvider(TextReply);
        IAssistantModel model = Model(
            new()
            {
                ["Assistant:Provider"] = "foundry",
                ["Assistant:Key"] = "foundry-key",
                ["Assistant:Endpoint"] = "https://motifflab-ai.services.ai.azure.com/anthropic/",
                ["Assistant:Model"] = "mentor-deployment",
                ["Assistant:Effort"] = "high",
            },
            provider)!;
        Assert.Equal("foundry/mentor-deployment", model.Name);

        await model.NextAsync(Turn(), CancellationToken.None);

        Assert.Equal("https://motifflab-ai.services.ai.azure.com/anthropic/v1/messages", provider.Uri!.GetLeftPart(UriPartial.Path));
        Assert.Equal("foundry-key", provider.Header("x-api-key"));
        Assert.DoesNotContain("server-side-fallback", provider.Header("anthropic-beta"), StringComparison.Ordinal);
        Assert.Equal("mentor-deployment", provider.Body.GetProperty("model").GetString());
        Assert.Equal("high", provider.Body.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.False(provider.Body.TryGetProperty("fallbacks", out _));
    }

    [Fact]
    public async Task A_provider_error_is_a_failed_request_the_relay_answers_with_the_template()
    {
        var provider = new FakeProvider("""{"type": "error", "error": {"type": "invalid_request_error", "message": "Bad."}}""", HttpStatusCode.BadRequest);
        await Assert.ThrowsAsync<HttpRequestException>(() => Anthropic(provider).NextAsync(Turn(), CancellationToken.None));
    }

    [Theory]
    [InlineData("", "k", null)]
    [InlineData("anthropic", "", null)]
    [InlineData("anthropic", "   ", null)]
    [InlineData("openai", "k", null)]
    [InlineData("foundry", "k", null)]
    [InlineData("foundry", "k", "not a resource!")]
    public void Without_a_provider_a_key_or_Foundrys_resource_there_is_no_model(string provider, string key, string? endpoint)
    {
        Assert.Null(Model(new() { ["Assistant:Provider"] = provider, ["Assistant:Key"] = key, ["Assistant:Endpoint"] = endpoint }));
    }

    [Theory]
    [InlineData("motifflab-ai", "motifflab-ai")]
    [InlineData("https://motifflab-ai.services.ai.azure.com/anthropic", "motifflab-ai")]
    [InlineData("https://motifflab-ai.services.ai.azure.com/anthropic/v1/messages", "motifflab-ai")]
    [InlineData("", null)]
    [InlineData("bad name", null)]
    public void Foundrys_resource_is_its_name_or_the_first_part_of_its_address(string endpoint, string? resource)
    {
        Assert.Equal(resource, AssistantModels.FoundryResource(endpoint));
    }

    [Fact]
    public void The_schema_keeps_what_strict_tool_use_takes_and_moves_the_rest_into_descriptions()
    {
        Dictionary<string, JsonElement> schema = ClaudeToolSchemas.Strict(Json("""
            {"type": "object", "properties": {
               "picks": {"description": "Which.", "anyOf": [{"type": "array", "items": {"type": "integer", "enum": [1, 2]}, "minItems": 1, "maxItems": 4, "uniqueItems": true}, {"type": "null"}]},
               "many": {"type": "array", "items": {"type": "string", "pattern": "^[0-9]+$"}, "minItems": 2}},
             "required": ["picks", "many"], "additionalProperties": false}
            """));

        JsonElement list = schema["properties"].GetProperty("picks").GetProperty("anyOf")[0];
        Assert.Equal(1, list.GetProperty("minItems").GetInt32());
        Assert.False(list.TryGetProperty("maxItems", out _));
        Assert.False(list.TryGetProperty("uniqueItems", out _));
        Assert.Equal("(Limits: maxItems 4, uniqueItems true.)", list.GetProperty("description").GetString());
        JsonElement many = schema["properties"].GetProperty("many");
        Assert.False(many.TryGetProperty("minItems", out _));
        Assert.Equal("^[0-9]+$", many.GetProperty("items").GetProperty("pattern").GetString());
        Assert.Equal("Which.", schema["properties"].GetProperty("picks").GetProperty("description").GetString());
    }

    /// <summary>A provider that answers every request with one reply and keeps the last request.</summary>
    private sealed class FakeProvider(string reply, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }

        public JsonElement Body { get; private set; }

        private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

        public string Header(string name) => _headers.GetValueOrDefault(name, "");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            foreach ((string name, IEnumerable<string> values) in request.Headers)
            {
                _headers[name] = string.Join(",", values);
            }

            Body = Json(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }
}