using System.Diagnostics;
using System.Text.Json;

using MotiffLab.MusicCore;
using MotiffLab.MusicCore.Accounts;
using MotiffLab.MusicCore.Assistant.Conversation;

namespace MotiffLab.Api.Assistant;

/// <summary>
/// The assistant's relay (decision record 0028, section 4; 0003): one turn of a conversation at a time, stateless. The
/// device sends the role, the language, the context of the question (the selected passage's facts digest and notes) and
/// the history; the relay hands it to the model with the role's instructions and tools, and answers with the model's tool
/// calls for the device to run (it never runs a tool) or with its answer, checked: an answer that cites a fact the turn did
/// not give, or none, becomes the engine's template text. Only the mentor (read tools) is served; the proposer (draft tools)
/// is in the list of roles and is refused until a model provider exists.
/// Gates: signed in with the API's scope, on the allow-list (Assistant:Allowed), Pro, a turn of at most
/// <see cref="TurnRequest.MaxBytes"/>, at most Assistant:DailyRequests turns a day. With no model configured it answers
/// "not configured" before reading the turn. It logs the outcome, counts and timing, never the turn's content.
/// </summary>
internal static class AssistantEndpoints
{
    public static void MapAssistant(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder assistant = app.MapGroup("/assistant").RequireAuthorization(Player.Policy);

        // {"configured": bool, "allowed": bool, "pro": bool, "left": n}: what the app needs before it sends anything.
        assistant.MapGet("/status", (HttpContext http, AssistantModelChoice choice, AssistantOptions options, AssistantUsage usage) =>
        {
            string player = Player.Id(http.User);
            return Results.Ok(new
            {
                configured = choice.Model is not null,
                allowed = options.Allowed.Contains(player),
                pro = Player.Plan(http).Allows(ProFeature.Assistant),
                left = usage.Left(player, options.DailyRequests),
            });
        });

        assistant.MapPost("/turn", TurnAsync);
    }

    private static async Task<IResult> TurnAsync(HttpContext http, AssistantModelChoice choice, AssistantOptions options, AssistantUsage usage, ILoggerFactory loggers, CancellationToken cancel)
    {
        ILogger logger = loggers.CreateLogger("MotiffLab.Api.Assistant");
        string player = Player.Id(http.User);
        if (!options.Allowed.Contains(player))
        {
            return ProjectEndpoints.Problem(StatusCodes.Status403Forbidden, "The assistant is not open to this account yet.", "notAllowed");
        }

        if (!Player.Plan(http).Allows(ProFeature.Assistant))
        {
            return ProjectEndpoints.Problem(StatusCodes.Status403Forbidden, "The assistant is part of Pro.", "pro");
        }

        if (choice.Model is not { } model)
        {
            return Reply(TurnReply.NotConfigured);
        }

        (TurnRequest? request, IResult? problem) = await ReadAsync(http, cancel);
        if (request is null)
        {
            return problem!;
        }

        if (!AssistantRoles.Served.Contains(request.Role))
        {
            return ProjectEndpoints.Problem(StatusCodes.Status403Forbidden, $"The {AssistantRoles.Id(request.Role)} is not open yet.", "roleClosed");
        }

        if (!usage.TryTake(player, options.DailyRequests))
        {
            return ProjectEndpoints.Problem(StatusCodes.Status429TooManyRequests, $"At most {options.DailyRequests} questions and steps a day.", "dailyCap");
        }

        var watch = Stopwatch.StartNew();
        (TurnReply reply, string outcome, ModelReply? answered) = await AskAsync(model, request, options.Timeout, cancel);
        logger.LogInformation(
            "Assistant turn: prompt {Prompt}, model {Model}, outcome {Outcome}, history {Entries}, calls {Calls}, tokens {Input}/{Output}, {Milliseconds} ms",
            AssistantPrompts.Version,
            model.Name,
            outcome,
            request.History.Count,
            reply.Calls.Count,
            answered?.InputTokens ?? 0,
            answered?.OutputTokens ?? 0,
            watch.ElapsedMilliseconds);
        return Reply(reply);
    }

    /// <summary>The turn in the body, or the problem with it: too large, not JSON, or not a turn the format allows.</summary>
    private static async Task<(TurnRequest? Request, IResult? Problem)> ReadAsync(HttpContext http, CancellationToken cancel)
    {
        if (http.Request.ContentLength > TurnRequest.MaxBytes)
        {
            return (null, TooLarge());
        }

        using var body = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, cancel)) > 0)
        {
            body.Write(chunk, 0, read);
            if (body.Length > TurnRequest.MaxBytes)
            {
                return (null, TooLarge());
            }
        }

        try
        {
            using JsonDocument json = JsonDocument.Parse(body.ToArray());
            return TurnRequest.Read(json.RootElement, out string? refusal) is { } request
                ? (request, null)
                : (null, ProjectEndpoints.Problem(StatusCodes.Status400BadRequest, refusal!, "badRequest"));
        }
        catch (JsonException)
        {
            return (null, ProjectEndpoints.Problem(StatusCodes.Status400BadRequest, "The turn is JSON.", "badRequest"));
        }
    }

    private static IResult TooLarge() =>
        ProjectEndpoints.Problem(StatusCodes.Status413RequestEntityTooLarge, $"A turn is at most {TurnRequest.MaxBytes / 1024} KB; select fewer bars.", "tooLarge");

    /// <summary>
    /// The model's step, as the device may take it: its tool calls when they are the role's and within the cap, else its
    /// answer checked; the engine's template when it calls a tool the role lacks, calls too often, errs or takes too long.
    /// </summary>
    private static async Task<(TurnReply Reply, string Outcome, ModelReply? Answered)> AskAsync(IAssistantModel model, TurnRequest request, TimeSpan timeout, CancellationToken cancel)
    {
        Words words = Words.For(request.Language);
        TurnReply Template(string reason) => TurnReply.Answered(AnswerCheck.Template(request.Context, words, reason));
        ModelReply answered;
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel))
        {
            limit.CancelAfter(timeout);
            try
            {
                answered = await model.NextAsync(new ModelTurn(AssistantPrompts.For(request.Role, request.Language), request.Language, Catalogue(request.Role), request.Context, request.History), limit.Token);
            }
            catch (Exception error) when (error is OperationCanceledException or HttpRequestException or InvalidOperationException or JsonException)
            {
                return (Template("modelError"), cancel.IsCancellationRequested ? "cancelled" : error is OperationCanceledException ? "timeout" : "modelError", null);
            }
        }

        if (answered.Calls.Count > 0)
        {
            if (answered.Calls.Any(call => !AssistantRoles.Allows(request.Role, call.Name)))
            {
                return (Template("toolNotAllowed"), "toolNotAllowed", answered);
            }

            return request.ToolCallsInQuestion + answered.Calls.Count > AssistantRoles.MostToolCalls
                ? (Template("tooManyCalls"), "tooManyCalls", answered)
                : (TurnReply.Called(answered.Calls), "toolCalls", answered);
        }

        CheckedAnswer checkedAnswer = AnswerCheck.Check(answered.Text, request.KnownIds, request.Context, words);
        return (TurnReply.Answered(checkedAnswer), checkedAnswer.FellBack ? $"fallback:{checkedAnswer.Reason}" : "answer", answered);
    }

    private static JsonElement Catalogue(AssistantRole role)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            AssistantRoles.WriteCatalogue(json, role);
            json.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    private static IResult Reply(TurnReply reply)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            reply.Write(json);
        }

        return Results.Bytes(buffer.ToArray(), "application/json");
    }
}