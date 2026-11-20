using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MotiffLab.Api.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;

namespace MotiffLab.Api.Tests;

/// <summary>The assistant's relay (0028 section 4): its gates, "not configured", tool calls and checked answers.</summary>
public sealed class AssistantApiTests : IClassFixture<AssistantApiTests.WithModel>, IClassFixture<AssistantApiTests.WithoutModel>
{
    private const string Question = "Why does bar 6 sound so dark?";

    private static readonly string s_context = """
        {"digest": {"scope": "range", "items": [{"id": "chord.6.1", "kind": "chord", "certainty": "inferred", "text": "Am, bar 6, beat 1"}], "ids": ["chord.6.1"]},
         "notes": {"notes": [{"address": "6.R.1", "pitch": "A4"}]}}
        """;

    private readonly WithModel _api;
    private readonly WithoutModel _bare;

    public AssistantApiTests(WithModel api, WithoutModel bare)
    {
        _api = api;
        _bare = bare;
    }

    private static string Turn(string question = Question) => Raw($$"""{"kind": "question", "text": "{{question}}"}""");

    private static string Raw(string history) =>
        $$"""{"version": 1, "role": "mentor", "language": "en", "context": {{s_context}}, "history": [{{history}}]}""";

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static HttpClient Client(ProjectApiTests.Api api, string player, string[]? roles = null)
    {
        HttpClient client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ProjectApiTests.Api.Token(player, "Projects.ReadWrite", roles ?? ["Pro"]));
        return client;
    }

    [Fact]
    public async Task Without_signing_in_the_assistant_answers_nothing()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/assistant/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().PostAsync("/assistant/turn", Body(Turn()))).StatusCode);
    }

    [Fact]
    public async Task An_account_not_on_the_allow_list_or_without_pro_is_refused()
    {
        HttpClient other = Client(_api, "someone-else");
        JsonElement status = await JsonOf(await other.GetAsync("/assistant/status"));
        Assert.False(status.GetProperty("allowed").GetBoolean());
        HttpResponseMessage refused = await other.PostAsync("/assistant/turn", Body(Turn()));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("notAllowed", (await JsonOf(refused)).GetProperty("code").GetString());

        HttpResponseMessage free = await Client(_api, "allowed-free", roles: []).PostAsync("/assistant/turn", Body(Turn()));
        Assert.Equal("pro", (await JsonOf(free)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task With_no_model_configured_it_says_so_and_sends_nothing()
    {
        HttpClient client = Client(_bare, "allowed-1");
        JsonElement status = await JsonOf(await client.GetAsync("/assistant/status"));
        Assert.False(status.GetProperty("configured").GetBoolean());
        Assert.True(status.GetProperty("allowed").GetBoolean());

        HttpResponseMessage response = await client.PostAsync("/assistant/turn", Body(Turn()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("notConfigured", (await JsonOf(response)).GetProperty("kind").GetString());
        Assert.Null(AssistantModels.Create(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Assistant:Provider"] = "any", ["Assistant:Key"] = "k" }).Build(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).Model);
    }

    [Fact]
    public async Task The_models_tool_calls_go_back_to_the_device_and_its_cited_answer_comes_through()
    {
        HttpClient client = Client(_api, "allowed-2");
        const string asked = "What is the chord in bar 6?";
        _api.Model.Script(asked, new ModelReply([new ToolCall("c1", "harmony.chords", JsonDocument.Parse("""{"bars": {"from": 6, "to": 6}}""").RootElement.Clone())], null));
        JsonElement called = await JsonOf(await client.PostAsync("/assistant/turn", Body(Turn(asked))));
        Assert.Equal("toolCalls", called.GetProperty("kind").GetString());
        Assert.Equal("harmony.chords", called.GetProperty("entry").GetProperty("calls")[0].GetProperty("name").GetString());

        ModelTurn seen = _api.Model.Seen(asked);
        Assert.Equal(["piece.describe", "facts.get", "notes.list", "harmony.chords", "check.flags"], seen.Tools.GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()));
        Assert.Contains("[[chord.6.1]]", seen.Instructions, StringComparison.Ordinal);
        Assert.Contains("English", seen.Instructions, StringComparison.Ordinal);

        _api.Model.Script(asked, new ModelReply([], "Bar 6 rests on A minor [[chord.6.1]] and its result [[chord.6.1~2]]."));
        string history = $$$$"""
            {"kind": "question", "text": "{{{{asked}}}}"},
            {"kind": "toolCalls", "calls": [{"id": "c1", "name": "harmony.chords", "arguments": {"bars": {"from": 6, "to": 6}}}]},
            {"kind": "toolResults", "results": [{"id": "c1", "name": "harmony.chords", "content": {"ok": true, "result": {"ids": ["chord.6.1~2"]}}}]}
            """;
        JsonElement answer = await JsonOf(await client.PostAsync("/assistant/turn", Body(Raw(history))));
        Assert.Equal("answer", answer.GetProperty("kind").GetString());
        Assert.False(answer.GetProperty("fallback").GetBoolean());
        Assert.StartsWith("Bar 6 rests on A minor", answer.GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_citing_an_unknown_fact_a_tool_the_mentor_lacks_or_an_error_becomes_the_template()
    {
        HttpClient client = Client(_api, "allowed-3");
        const string asked = "Does it modulate?";
        _api.Model.Script(asked, new ModelReply([], "It modulates [[key.2]]."));
        JsonElement invented = await JsonOf(await client.PostAsync("/assistant/turn", Body(Turn(asked))));
        Assert.True(invented.GetProperty("fallback").GetBoolean());
        Assert.EndsWith("Am, bar 6, beat 1 [[chord.6.1]]", invented.GetProperty("text").GetString(), StringComparison.Ordinal);

        _api.Model.Script(asked, new ModelReply([new ToolCall("c1", "notes.edit", JsonDocument.Parse("{}").RootElement.Clone())], null));
        Assert.True((await JsonOf(await client.PostAsync("/assistant/turn", Body(Turn(asked))))).GetProperty("fallback").GetBoolean());

        _api.Model.Script(asked, (ModelReply?)null);
        Assert.True((await JsonOf(await client.PostAsync("/assistant/turn", Body(Turn(asked))))).GetProperty("fallback").GetBoolean());
    }

    [Fact]
    public async Task A_turn_too_large_malformed_or_beyond_the_days_cap_is_refused()
    {
        HttpClient client = Client(_api, "allowed-4");
        string large = Turn(new string('x', TurnRequest.MaxBytes));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/assistant/turn", Body(large))).StatusCode);
        HttpResponseMessage bad = await client.PostAsync("/assistant/turn", Body(Raw("""{"kind": "answer", "text": "x"}""")));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("out of order", (await JsonOf(bad)).GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/assistant/turn", Body("not json"))).StatusCode);

        const string asked = "How many times may I ask?";
        _api.Model.Script(asked, new ModelReply([], "[[chord.6.1]]"));
        for (int turn = 0; turn < WithModel.DailyRequests; turn++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/assistant/turn", Body(Turn(asked)))).StatusCode);
        }

        HttpResponseMessage capped = await client.PostAsync("/assistant/turn", Body(Turn(asked)));
        Assert.Equal(HttpStatusCode.TooManyRequests, capped.StatusCode);
        Assert.Equal(0, (await JsonOf(await client.GetAsync("/assistant/status"))).GetProperty("left").GetInt32());
    }

    [Fact]
    public async Task The_proposer_is_offered_the_draft_tools_and_a_call_out_of_range_never_reaches_the_device()
    {
        HttpClient client = Client(_api, "allowed-6");
        const string asked = "Move the A up an octave.";
        string proposer = Turn(asked).Replace("\"role\": \"mentor\"", "\"role\": \"proposer\"", StringComparison.Ordinal);
        static JsonElement Edit(int pitch) => JsonDocument.Parse($$"""{"bars": {"from": 6, "to": 6}, "edits": [{"kind": "change", "address": "6.R.1", "bar": null, "hand": null, "start": null, "length": null, "pitch": {{pitch}}, "velocity": null}]}""").RootElement.Clone();

        // In range: the call goes to the device, which runs it in music-core.
        _api.Model.Script(asked, new ModelReply([new ToolCall("c1", "notes.edit", Edit(81))], null));
        JsonElement called = await JsonOf(await client.PostAsync("/assistant/turn", Body(proposer)));
        Assert.Equal("toolCalls", called.GetProperty("kind").GetString());
        string[] offered = [.. _api.Model.Seen(asked).Tools.GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()!)];
        Assert.Contains("notes.edit", offered);
        Assert.Contains("notes.join", offered);
        Assert.Contains("repair.candidates", offered);
        Assert.Contains("variation.simplify", offered);
        Assert.Contains("practice.suggest", offered);
        Assert.Contains("never write notes", _api.Model.Seen(asked).Instructions, StringComparison.Ordinal);

        // Out of range (MIDI 200): the engine's template, and nothing for the device to run.
        _api.Model.Script(asked, new ModelReply([new ToolCall("c2", "notes.edit", Edit(200))], null));
        JsonElement refused = await JsonOf(await client.PostAsync("/assistant/turn", Body(proposer)));
        Assert.Equal("answer", refused.GetProperty("kind").GetString());
        Assert.True(refused.GetProperty("fallback").GetBoolean());
        Assert.Contains(_api.Logs, line => line.Contains("outcome badArguments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_logs_hold_no_question_and_no_note()
    {
        HttpClient client = Client(_api, "allowed-5");
        _api.Model.Script(Question, new ModelReply([], "A minor [[chord.6.1]]."));
        await client.PostAsync("/assistant/turn", Body(Turn()));

        string[] lines = [.. _api.Logs];
        Assert.Contains(lines, line => line.Contains("Assistant turn", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains(Question, StringComparison.Ordinal) || line.Contains("6.R.1", StringComparison.Ordinal) || line.Contains("A minor", StringComparison.Ordinal));
    }

    /// <summary>
    /// A model that answers what each test scripts for the player's question (the one the context stands beside, so a retry
    /// the relay adds is answered from the same script): the replies in turn, the last one again once they run out, each
    /// after the scripted delay; a null reply is the provider's error. It remembers every turn it was given.
    /// </summary>
    public sealed class FakeAssistantModel : IAssistantModel
    {
        private readonly ConcurrentDictionary<string, (TimeSpan Delay, ConcurrentQueue<ModelReply?> Replies)> _next = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ConcurrentQueue<ModelTurn>> _seen = new(StringComparer.Ordinal);

        public string Name => "fake/model";

        public void Script(string question, params ModelReply?[] replies) => Script(question, TimeSpan.Zero, replies);

        public void Script(string question, TimeSpan delay, params ModelReply?[] replies)
        {
            _next[question] = (delay, new ConcurrentQueue<ModelReply?>(replies));
            _seen[question] = new ConcurrentQueue<ModelTurn>();
        }

        /// <summary>The last turn given for <paramref name="question"/>.</summary>
        public ModelTurn Seen(string question) => _seen[question].Last();

        /// <summary>Every turn given for <paramref name="question"/> since it was scripted.</summary>
        public IReadOnlyList<ModelTurn> Turns(string question) => [.. _seen[question]];

        public async Task<ModelReply> NextAsync(ModelTurn turn, CancellationToken cancel)
        {
            string question = turn.History[turn.ContextAt].Text!;
            _seen.GetOrAdd(question, static _ => new ConcurrentQueue<ModelTurn>()).Enqueue(turn);
            if (!_next.TryGetValue(question, out var script))
            {
                throw new HttpRequestException("The fake provider is down.");
            }

            ModelReply? reply = script.Replies.Count > 1 && script.Replies.TryDequeue(out ModelReply? next) ? next : script.Replies.LastOrDefault();
            await Task.Delay(script.Delay, cancel);
            return reply ?? throw new HttpRequestException("The fake provider is down.");
        }
    }

    /// <summary>The API with the fake model, an allow-list of test accounts, a small daily cap and the logs kept.</summary>
    public class WithModel : ProjectApiTests.Api
    {
        public const int DailyRequests = 3;

        public FakeAssistantModel Model { get; } = new();

        public ConcurrentQueue<string> Logs { get; } = new();

        /// <summary>The model the API is given: the fake; null leaves the one the settings choose (<see cref="AssistantModels"/>).</summary>
        protected virtual AssistantModelChoice? Choice => new(Model);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Assistant:Allowed", "allowed-1, allowed-2, allowed-3, allowed-4, allowed-5, allowed-6, allowed-free");
            builder.UseSetting("Assistant:DailyRequests", DailyRequests.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureLogging(logging => logging.AddProvider(new ListLogger(Logs)));
            if (Choice is { } choice)
            {
                builder.ConfigureTestServices(services => services.AddSingleton(choice));
            }
        }
    }

    public sealed class WithoutModel : WithModel
    {
        protected override AssistantModelChoice? Choice => new(null);
    }

    private sealed class ListLogger(ConcurrentQueue<string> lines) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue(formatter(state, exception));

        public void Dispose()
        {
        }
    }
}