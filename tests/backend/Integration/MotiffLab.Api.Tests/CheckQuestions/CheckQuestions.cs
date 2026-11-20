using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;

using MotiffLab.MusicCore;
using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.Api.Tests.CheckQuestions;

/// <summary>One question of the kept set: its language, whether it is general or about given bars, and the facts the template must stand near.</summary>
/// <param name="From">The selected bars' first, or null for a question about the whole piece.</param>
/// <param name="Near">Id prefixes: the engine's template for the question cites at least one fact whose id starts with one of them (none: no expectation).</param>
public sealed record CheckQuestion(string Id, string Language, string Kind, int? From, int? To, string Question, IReadOnlyList<string> Near);

/// <summary>What one question gave: the reply that answered it, every turn's outcome in the API's log, the tool calls run and the largest turn sent.</summary>
public sealed record CheckRun(CheckQuestion Question, TurnReply Reply, IReadOnlyList<string> Outcomes, int ToolCalls, int LargestTurn)
{
    public IReadOnlyList<string> Cited => AnswerCheck.Cited(Reply.Text ?? "");
}

/// <summary>
/// The check-question set the assistant's prompts are measured with (0028, Consequences; 0003 section 8): the questions of
/// check-questions.json about the example piece, Für Elise, each asked as the iPad asks it: the proposer, the context
/// music-core makes for the piece or the selected bars, the model's tool calls run on the device by music-core, turn after
/// turn through the relay until an answer. With a fake model in CI (<see cref="CheckQuestionsTests"/>), by hand with the
/// real one (<see cref="CheckQuestionsRealModelTests"/>, README.md).
/// </summary>
internal static class CheckQuestionSet
{
    private static readonly string s_folder = Path.Combine(AppContext.BaseDirectory, "CheckQuestions");

    public static IReadOnlyList<CheckQuestion> All { get; } = Load();

    /// <summary>The ids of the set, for a theory's data.</summary>
    public static TheoryData<string> Ids => [.. All.Select(static question => question.Id)];

    public static CheckQuestion Named(string id) => All.Single(question => question.Id == id);

    /// <summary>Für Elise, as the player sees it in the question's language.</summary>
    public static AssistantPiece Piece(CheckQuestion question)
    {
        ImportedPiece piece = PieceImporter.Import("FurElise.musicxml", File.ReadAllBytes(Path.Combine(s_folder, "FurElise.musicxml"))).Piece!;
        return new AssistantPiece(piece, piece.Notes, [], Words.For(question.Language));
    }

    /// <summary>The question's context: the selected bars' digest and notes, or the piece's digest.</summary>
    public static JsonElement Context(AssistantPiece piece, CheckQuestion question) =>
        question.From is { } from ? AssistantContext.OfRange(piece, from, question.To!.Value) : AssistantContext.OfPiece(piece);

    /// <summary>Asks <paramref name="question"/> through the relay as the iPad does, the tool calls run here, until the answer.</summary>
    public static async Task<CheckRun> RunAsync(HttpClient client, ConcurrentQueue<string> logs, CheckQuestion question)
    {
        AssistantPiece piece = Piece(question);
        JsonElement context = Context(piece, question);
        AssistantSession session = AssistantSession.Start(AssistantRole.Proposer).Ask(question.Question);
        var outcomes = new List<string>();
        int largest = 0;
        for (int step = 0; step <= AssistantRoles.MostToolCalls; step++)
        {
            byte[] turn = session.Request(question.Language, context).ToJson();
            largest = Math.Max(largest, turn.Length);
            int logged = logs.Count;
            HttpResponseMessage response = await client.PostAsync("/assistant/turn", new ByteArrayContent(turn) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } });
            outcomes.AddRange(logs.Skip(logged).Where(static line => line.StartsWith("Assistant turn", StringComparison.Ordinal)).Select(static line => line.Split("outcome ")[1].Split(',')[0]));
            TurnReply reply = TurnReply.Read(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement)
                ?? throw new InvalidOperationException($"{question.Id}: the relay answered {response.StatusCode}.");
            if (reply.Kind != TurnReplyKind.ToolCalls)
            {
                return new CheckRun(question, reply, outcomes, session.ToolCallsInQuestion, largest);
            }

            session = session.Called(reply.Calls);
            session = session.Resulted(TurnRunner.Run(piece, AssistantRole.Proposer, reply.Calls, session.ToolCallsInQuestion - reply.Calls.Count));
        }

        throw new InvalidOperationException($"{question.Id}: no answer after {AssistantRoles.MostToolCalls} tool calls.");
    }

    /// <summary>A client of the account the check fixtures allow.</summary>
    public static HttpClient Client(ProjectApiTests.Api api)
    {
        HttpClient client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ProjectApiTests.Api.Token("allowed-check", "Projects.ReadWrite", ["Pro"]));
        return client;
    }

    private static List<CheckQuestion> Load()
    {
        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(s_folder, "check-questions.json")));
        return [.. json.RootElement.GetProperty("questions").EnumerateArray().Select(static entry =>
        {
            JsonElement bars = entry.GetProperty("bars");
            bool selected = bars.ValueKind == JsonValueKind.Object;
            return new CheckQuestion(
                entry.GetProperty("id").GetString()!,
                entry.GetProperty("language").GetString()!,
                entry.GetProperty("kind").GetString()!,
                selected ? bars.GetProperty("from").GetInt32() : null,
                selected ? bars.GetProperty("to").GetInt32() : null,
                entry.GetProperty("question").GetString()!,
                [.. entry.GetProperty("near").EnumerateArray().Select(static prefix => prefix.GetString()!)]);
        })];
    }
}