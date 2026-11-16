using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using MotiffLab.Api.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;

namespace MotiffLab.Api.Tests;

/// <summary>What the model adapters' tests share: a mentor's turn with a refused tool call in its history, and the model the settings give.</summary>
internal static class ModelTurns
{
    private static readonly JsonElement s_context = Json("""
        {"digest": {"scope": "range", "items": [{"id": "chord.6.1", "text": "Am, bar 6, beat 1"}], "ids": ["chord.6.1"]}}
        """);

    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>The mentor's turn: the question, a call of harmony.chords and its refused result.</summary>
    public static ModelTurn Turn() => new("You are the mentor.", "en", Tools(AssistantRole.Mentor), s_context, History());

    /// <summary>The proposer's turn: the same history, its tools those that make drafts too.</summary>
    public static ModelTurn ProposerTurn() => new("You are the assistant.", "en", Tools(AssistantRole.Proposer), s_context, History());

    /// <summary>The model the settings give, its provider's requests answered by <paramref name="provider"/>.</summary>
    public static IAssistantModel? Model(Dictionary<string, string?> settings, FakeProvider? provider = null) =>
        AssistantModels.Create(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), NullLogger.Instance, provider).Model;

    private static IReadOnlyList<TurnEntry> History() =>
    [
        TurnEntry.Question("Why does bar 6 sound so dark?"),
        TurnEntry.Called([new ToolCall("toolu_1", "harmony.chords", Json("""{"bars": {"from": 6, "to": 6}}"""))]),
        TurnEntry.Resulted([new ToolOutcome("toolu_1", "harmony.chords", Json("""{"tool": "harmony.chords", "ok": false, "refusal": "No bar 6."}"""))]),
    ];

    private static JsonElement Tools(AssistantRole role)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            AssistantRoles.WriteCatalogue(json, role);
            json.WriteEndObject();
        }

        return Json(Encoding.UTF8.GetString(buffer.ToArray()));
    }
}

/// <summary>A provider that answers every request with one reply and keeps the last request.</summary>
internal sealed class FakeProvider(string reply, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

    public Uri? Uri { get; private set; }

    public JsonElement Body { get; private set; }

    public int Requests { get; private set; }

    public string Header(string name) => _headers.GetValueOrDefault(name, "");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        Uri = request.RequestUri;
        foreach ((string name, IEnumerable<string> values) in request.Headers)
        {
            _headers[name] = string.Join(",", values);
        }

        Body = ModelTurns.Json(await request.Content!.ReadAsStringAsync(cancellationToken));
        return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
    }
}