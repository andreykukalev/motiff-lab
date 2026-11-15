using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using MotiffLab.MusicCore.Assistant.Conversation;

namespace MotiffLab.Web.Account;

/// <summary>What the API says about the assistant for the signed-in player: whether a model is on, the account may use it, it is Pro and how many turns are left today.</summary>
public sealed record AssistantStatus(bool Configured, bool Allowed, bool Pro, int Left);

/// <summary>Why the API did not answer a turn, as the conversation says it.</summary>
public enum RelayProblem
{
    None,
    NotSignedIn,
    Unreachable,
    NotAllowed,
    Pro,
    TooLarge,
    DailyCap,
    Refused,
}

/// <summary>
/// The assistant's relay on the Motiff Lab API (0028 section 4): GET /assistant/status before anything is sent, then one
/// POST /assistant/turn at a time with the turn the device built (<see cref="TurnRequest"/>). Nothing goes without a
/// signed-in player's token; the request carries the tester's plan choice like every other API request.
/// </summary>
public sealed class AssistantRelay(CloudAccount account) : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = CloudAccount.AnswerWithin };

    /// <summary>The API's status for the player, or the problem: not signed in or not reachable.</summary>
    public async Task<(AssistantStatus? Status, RelayProblem Problem)> StatusAsync()
    {
        if (await RequestAsync(HttpMethod.Get, "assistant/status") is not { } request)
        {
            return (null, RelayProblem.NotSignedIn);
        }

        (JsonElement? answer, RelayProblem problem) = await SendAsync(request);
        if (answer is not { } json)
        {
            return (null, problem);
        }

        return (new AssistantStatus(Flag(json, "configured"), Flag(json, "allowed"), Flag(json, "pro"), json.TryGetProperty("left", out JsonElement left) && left.TryGetInt32(out int count) ? count : 0), RelayProblem.None);
    }

    /// <summary>The server's reply to the turn, or the problem with it.</summary>
    public async Task<(TurnReply? Reply, RelayProblem Problem)> TurnAsync(TurnRequest turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        if (await RequestAsync(HttpMethod.Post, "assistant/turn") is not { } request)
        {
            return (null, RelayProblem.NotSignedIn);
        }

        request.Content = new ByteArrayContent(turn.ToJson());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        (JsonElement? answer, RelayProblem problem) = await SendAsync(request);
        TurnReply? reply = answer is { } json ? TurnReply.Read(json) : null;
        return (reply, reply is null && problem == RelayProblem.None ? RelayProblem.Unreachable : problem);
    }

    public void Dispose() => _http.Dispose();

    private static bool Flag(JsonElement json, string name) => json.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    /// <summary>A request with the player's token, or null when nobody is signed in: then nothing is sent.</summary>
    private async Task<HttpRequestMessage?> RequestAsync(HttpMethod method, string path)
    {
        if (!account.IsAvailable || await account.TokenAsync(account.Options.ApiScope) is not { } token)
        {
            return null;
        }

        var request = new HttpRequestMessage(method, new Uri(new Uri(account.Options.Api.TrimEnd('/') + "/"), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await account.AddPlanChoiceAsync(request);
        return request;
    }

    private async Task<(JsonElement? Answer, RelayProblem Problem)> SendAsync(HttpRequestMessage request)
    {
        try
        {
            using (request)
            {
                using HttpResponseMessage response = await _http.SendAsync(request);
                byte[] body = await response.Content.ReadAsByteArrayAsync();
                if (!response.IsSuccessStatusCode)
                {
                    return (null, ProblemOf(response.StatusCode, body));
                }

                using JsonDocument answer = JsonDocument.Parse(body);
                return (answer.RootElement.Clone(), RelayProblem.None);
            }
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or TaskCanceledException)
        {
            return (null, RelayProblem.Unreachable);
        }
    }

    /// <summary>The problem the API named in its answer's "code", or what its status says.</summary>
    private static RelayProblem ProblemOf(HttpStatusCode status, byte[] body)
    {
        string? code = null;
        try
        {
            using JsonDocument problem = JsonDocument.Parse(body);
            code = problem.RootElement.ValueKind == JsonValueKind.Object && problem.RootElement.TryGetProperty("code", out JsonElement named) ? named.GetString() : null;
        }
        catch (JsonException)
        {
        }

        return code switch
        {
            "notAllowed" => RelayProblem.NotAllowed,
            "pro" => RelayProblem.Pro,
            "tooLarge" => RelayProblem.TooLarge,
            "dailyCap" => RelayProblem.DailyCap,
            _ when status == HttpStatusCode.Unauthorized => RelayProblem.NotSignedIn,
            _ => RelayProblem.Refused,
        };
    }
}