using System.Globalization;
using System.Text;

using Microsoft.AspNetCore.Hosting;

using MotiffLab.Api.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;

using Xunit.Abstractions;

namespace MotiffLab.Api.Tests.CheckQuestions;

/// <summary>
/// The check-question set with a fake model, in CI: every kept question makes a turn within the limits in its language,
/// and when the model answers it with advice that cites nothing, twice, the player gets the engine's facts nearest to the
/// question, with the reason. No network, nothing costs anything.
/// </summary>
public sealed class CheckQuestionsTests(CheckQuestionsTests.WithFakeModel api) : IClassFixture<CheckQuestionsTests.WithFakeModel>
{
    public static TheoryData<string> Questions => CheckQuestionSet.Ids;

    [Fact]
    public void The_set_holds_general_and_specific_questions_in_both_languages()
    {
        Assert.Equal(CheckQuestionSet.All.Count, CheckQuestionSet.All.Select(static question => question.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (string kind in new[] { "general", "specific" })
        {
            Assert.All(new[] { "en", "ru" }, language => Assert.Contains(CheckQuestionSet.All, question => question.Kind == kind && question.Language == language));
        }

        Assert.Contains(CheckQuestionSet.All, static question => question.From is not null);
    }

    [Theory]
    [MemberData(nameof(Questions))]
    public async Task An_uncited_answer_is_retried_once_then_the_template_answers_near_the_question(string id)
    {
        CheckQuestion question = CheckQuestionSet.Named(id);
        string advice = question.Language == "ru" ? "Учите медленно, по рукам." : "Practise slowly, hands apart.";
        api.Model.Script(question.Question, new ModelReply([], advice));

        CheckRun run = await CheckQuestionSet.RunAsync(CheckQuestionSet.Client(api), api.Logs, question);

        Assert.InRange(run.LargestTurn, 1, TurnRequest.MaxBytes);
        Assert.Equal((true, "noCitation"), (run.Reply.Fallback, run.Reply.Reason));
        Assert.Equal(["retried:noCitation;fallback:noCitation"], run.Outcomes);
        Assert.Equal(2, api.Model.Turns(question.Question).Count);
        Assert.StartsWith(question.Language == "ru" ? "Ответ ассистента" : "The assistant's answer", run.Reply.Text, StringComparison.Ordinal);
        IReadOnlySet<string> known = AnswerCheck.Known(CheckQuestionSet.Context(CheckQuestionSet.Piece(question), question), []);
        Assert.NotEmpty(run.Cited);
        Assert.All(run.Cited, cited => Assert.Contains(cited, known));
        if (question.Near.Count > 0)
        {
            Assert.Contains(run.Cited, cited => question.Near.Any(prefix => cited.StartsWith(prefix, StringComparison.Ordinal)));
        }
    }

    /// <summary>The API with the fake model, the check account allowed and no daily cap to speak of.</summary>
    public sealed class WithFakeModel : AssistantApiTests.WithModel
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Assistant:Allowed", "allowed-check");
            builder.UseSetting("Assistant:DailyRequests", "1000");
        }
    }
}

/// <summary>
/// The check-question set against the real model, run only by hand (README.md): it costs money, so it never runs in CI.
/// It writes a report of every answer and holds the model to 0003 section 8: at most 5 % of the answers fall back.
/// </summary>
public sealed class CheckQuestionsRealModelTests(CheckQuestionsRealModelTests.WithRealModel api, ITestOutputHelper output) : IClassFixture<CheckQuestionsRealModelTests.WithRealModel>
{
    /// <summary>The switch that runs it: MOTIFF_CHECK_QUESTIONS=real, with the model's settings as Assistant__* variables.</summary>
    public const string Switch = "MOTIFF_CHECK_QUESTIONS";

    [RealModelFact]
    public async Task The_real_model_answers_the_check_questions_with_cited_facts()
    {
        var runs = new List<CheckRun>();
        foreach (CheckQuestion question in CheckQuestionSet.All)
        {
            runs.Add(await CheckQuestionSet.RunAsync(CheckQuestionSet.Client(api), api.Logs, question));
        }

        string report = Report(runs);
        string path = Environment.GetEnvironmentVariable("MOTIFF_CHECK_QUESTIONS_REPORT") is { Length: > 0 } named ? named : Path.Combine(AppContext.BaseDirectory, "check-questions-report.md");
        await File.WriteAllTextAsync(path, report);
        output.WriteLine($"Report: {path}");
        output.WriteLine(report);
        Assert.InRange(runs.Count(static run => run.Reply.Fallback), 0, runs.Count * 5 / 100);
    }

    private static string Report(IReadOnlyList<CheckRun> runs)
    {
        var text = new StringBuilder("| Question | Outcomes | Reason | Tool calls | Characters | Cited | Answer |\n| --- | --- | --- | --- | --- | --- | --- |\n");
        foreach (CheckRun run in runs)
        {
            string answer = (run.Reply.Text ?? "").ReplaceLineEndings(" ").Replace("|", "/", StringComparison.Ordinal);
            text.Append(CultureInfo.InvariantCulture, $"| {run.Question.Id} | {string.Join(" → ", run.Outcomes)} | {run.Reply.Reason ?? "—"} | {run.ToolCalls} | {run.Reply.Text?.Length ?? 0} | {string.Join(" ", run.Cited)} | {answer} |\n");
        }

        return text.Append(CultureInfo.InvariantCulture, $"\nFell back: {runs.Count(static run => run.Reply.Fallback)} of {runs.Count}; retried: {runs.Count(static run => run.Outcomes.Any(static outcome => outcome.StartsWith("retried", StringComparison.Ordinal)))}.\n").ToString();
    }

    /// <summary>A test that runs only when <see cref="Switch"/> is "real"; skipped otherwise, as in CI.</summary>
    private sealed class RealModelFactAttribute : FactAttribute
    {
        public RealModelFactAttribute()
        {
            if (Environment.GetEnvironmentVariable(Switch) != "real")
            {
                Skip = $"By hand only, against the real model: {Switch}=real (CheckQuestions/README.md).";
            }
        }
    }

    /// <summary>The API with the model its settings choose (the Assistant__* environment variables), the check account allowed.</summary>
    public sealed class WithRealModel : AssistantApiTests.WithModel
    {
        protected override AssistantModelChoice? Choice => null;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Assistant:Allowed", "allowed-check");
            builder.UseSetting("Assistant:DailyRequests", "1000");
        }
    }
}