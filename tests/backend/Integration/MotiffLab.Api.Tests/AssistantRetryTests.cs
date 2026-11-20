using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;

using MotiffLab.Api.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;

namespace MotiffLab.Api.Tests;

/// <summary>
/// The assistant's option A in the relay: the proposer's instructions for general questions, one retry when the check
/// refuses an answer it can mend (no citation, an unknown fact, too long) within the turn's time, and the reason of a
/// fallback in the reply.
/// </summary>
public sealed class AssistantRetryTests(AssistantRetryTests.WithQuickModel api) : IClassFixture<AssistantRetryTests.WithQuickModel>
{
    private static readonly string s_context = """
        {"digest": {"scope": "piece", "items": [{"id": "density.32", "kind": "density", "certainty": "detected", "text": "Bar 32, the densest: 7 notes per beat", "bars": {"from": 32, "to": 32}},
                                                {"id": "phrase.7", "kind": "phrase", "certainty": "inferred", "text": "Phrase 7, bars 32–35", "bars": {"from": 32, "to": 35}}],
                    "ids": ["density.32", "phrase.7"]}}
        """;

    private static StringContent Turn(string question, string role = "proposer", string language = "en") =>
        new($$"""{"version": 1, "role": "{{role}}", "language": "{{language}}", "context": {{s_context}}, "history": [{"kind": "question", "text": "{{question}}"}]}""", Encoding.UTF8, "application/json");

    private static ModelReply Says(string text) => new([], text, InputTokens: 100, OutputTokens: 10);

    private async Task<JsonElement> AskAsync(string question, string role = "proposer", string language = "en")
    {
        HttpClient client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ProjectApiTests.Api.Token("allowed-retry", "Projects.ReadWrite", ["Pro"]));
        HttpResponseMessage response = await client.PostAsync("/assistant/turn", Turn(question, role, language));
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private string Outcome() => api.Logs.Last(line => line.StartsWith("Assistant turn", StringComparison.Ordinal)).Split("outcome ")[1].Split(',')[0];

    [Fact]
    public async Task An_answer_without_a_citation_is_rewritten_once_and_the_rewrite_comes_through()
    {
        const string asked = "How should I learn this piece?";
        api.Model.Script(asked, Says("Practise slowly, hands apart."), Says("Start with bar 32, the hardest place [[density.32]]."));

        JsonElement reply = await AskAsync(asked);

        Assert.Equal("Start with bar 32, the hardest place [[density.32]].", reply.GetProperty("text").GetString());
        Assert.False(reply.GetProperty("fallback").GetBoolean());
        Assert.False(reply.TryGetProperty("reason", out _));

        // The second turn is the first with the refused answer and the reason after it; the context stays beside the question.
        IReadOnlyList<ModelTurn> turns = api.Model.Turns(asked);
        Assert.Equal(2, turns.Count);
        ModelTurn retry = turns[1];
        Assert.Equal(
            [(TurnEntryKind.Question, asked), (TurnEntryKind.Answer, "Practise slowly, hands apart.")],
            retry.History.Take(2).Select(static entry => (entry.Kind, entry.Text)));
        Assert.Equal((3, TurnEntryKind.Question), (retry.History.Count, retry.History[^1].Kind));
        Assert.Contains("it cites no fact", retry.History[^1].Text, StringComparison.Ordinal);
        Assert.Equal(0, retry.ContextAt);
        Assert.Equal(turns[0].Instructions, retry.Instructions);
        Assert.Equal("retried:noCitation", Outcome());
        Assert.Contains(api.Logs, line => line.Contains("tokens 200/20", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_answer_that_fails_twice_is_the_template_with_its_reason()
    {
        const string asked = "Which bars are new?";
        api.Model.Script(asked, Says("Bars 7 to 15 [[new.7]]."), Says("Still bars 7 to 15 [[new.7]]."));

        JsonElement reply = await AskAsync(asked);

        Assert.True(reply.GetProperty("fallback").GetBoolean());
        Assert.Equal("unknownFact", reply.GetProperty("reason").GetString());
        Assert.Contains("[[density.32]]", reply.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal(2, api.Model.Turns(asked).Count);
        Assert.Contains("new.7", api.Model.Turns(asked)[1].History[^1].Text, StringComparison.Ordinal);
        Assert.Equal("retried:unknownFact;fallback:unknownFact", Outcome());

        string longer = new string('a', AnswerCheck.MaxLength) + " [[density.32]]";
        api.Model.Script(asked, Says(longer), Says(longer));
        JsonElement tooLong = await AskAsync(asked);
        Assert.Equal("tooLong", tooLong.GetProperty("reason").GetString());
        Assert.Contains($"it is {longer.Length} characters long", api.Model.Turns(asked)[1].History[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timeout_an_error_or_an_empty_answer_is_not_retried_and_says_why()
    {
        const string asked = "Why is bar 32 hard?";
        api.Model.Script(asked, Timeout.InfiniteTimeSpan, Says("Never sent [[density.32]]."));
        JsonElement late = await AskAsync(asked);
        Assert.Equal(("timeout", 1), (late.GetProperty("reason").GetString(), api.Model.Turns(asked).Count));
        Assert.True(late.GetProperty("fallback").GetBoolean());
        Assert.Equal("timeout", Outcome());

        api.Model.Script(asked, (ModelReply?)null);
        JsonElement failed = await AskAsync(asked);
        Assert.Equal(("modelError", 1), (failed.GetProperty("reason").GetString(), api.Model.Turns(asked).Count));

        api.Model.Script(asked, Says(" "));
        JsonElement empty = await AskAsync(asked);
        Assert.Equal(("empty", 1), (empty.GetProperty("reason").GetString(), api.Model.Turns(asked).Count));
    }

    [Fact]
    public async Task The_retry_fits_in_the_turns_time_or_the_template_answers()
    {
        // Each step alone is within the turn's time, both together are not: the retry is cut off, the reply says timeout.
        const string asked = "What should I practise first?";
        TimeSpan step = WithQuickModel.Timeout * 0.6;
        api.Model.Script(asked, step, Says("Practise slowly."), Says("Bar 32 first [[density.32]]."));

        JsonElement reply = await AskAsync(asked);

        Assert.Equal("timeout", reply.GetProperty("reason").GetString());
        Assert.Equal(2, api.Model.Turns(asked).Count);
        Assert.Equal("retried:noCitation;timeout", Outcome());
    }

    [Fact]
    public async Task The_proposer_is_told_to_answer_general_questions_from_cited_facts_in_both_languages()
    {
        const string asked = "Как разучить эту пьесу?";
        api.Model.Script(asked, Says("Начните с такта 32 [[density.32]]."));

        JsonElement reply = await AskAsync(asked, language: "ru");

        Assert.False(reply.GetProperty("fallback").GetBoolean());
        string instructions = api.Model.Seen(asked).Instructions;
        Assert.Equal("proposer-v2", AssistantPrompts.VersionOf(AssistantRole.Proposer));
        Assert.Contains(api.Logs, line => line.Contains("prompt proposer-v2", StringComparison.Ordinal));
        Assert.Contains("how to learn,\npractise or play it", instructions.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("An answer without a citation", instructions, StringComparison.Ordinal);
        Assert.Contains("is not shown to the player", instructions, StringComparison.Ordinal);
        Assert.Contains("practice.suggest", instructions, StringComparison.Ordinal);
        Assert.Contains("[[density.32]]", instructions, StringComparison.Ordinal);
        Assert.Contains("2000 characters", instructions, StringComparison.Ordinal);
        Assert.Contains("(Russian unless the question is in another)", instructions.ReplaceLineEndings(" "), StringComparison.Ordinal);
        Assert.DoesNotContain("{language}", instructions, StringComparison.Ordinal);
        Assert.Contains("(English unless", AssistantPrompts.For(AssistantRole.Proposer, "en").ReplaceLineEndings(" "), StringComparison.Ordinal);
    }

    /// <summary>The API with the fake model and a turn's time of <see cref="Timeout"/>, so a slow model is cut off quickly.</summary>
    public sealed class WithQuickModel : AssistantApiTests.WithModel
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Assistant:Allowed", "allowed-retry");
            builder.UseSetting("Assistant:DailyRequests", "100");
            builder.UseSetting("Assistant:TimeoutSeconds", ((int)Timeout.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}