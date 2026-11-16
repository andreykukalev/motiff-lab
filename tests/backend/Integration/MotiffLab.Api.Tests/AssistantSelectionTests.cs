using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using MotiffLab.Api.Assistant;
using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.Api.Tests;

/// <summary>
/// The 0.6 bug "the assistant answers but does not see the selected bars" (#582 item 1), traced end to end: the selection
/// on the device, the context music-core makes of it, the turn's body, the relay, and the request the model's provider
/// receives (the Azure OpenAI adapter, as on the dev environment). The selected bars' notes and facts must stand in the
/// model's input next to the question about them, also when the conversation asked about other bars or the whole piece first.
/// </summary>
public sealed class AssistantSelectionTests(AssistantSelectionTests.WithAzure api) : IClassFixture<AssistantSelectionTests.WithAzure>
{
    /// <summary>Four bars of 3/4, a melody over a bass.</summary>
    private static ImportedPiece Piece()
    {
        static string N(string step, int octave, int duration, int staff) =>
            $"<note><pitch><step>{step}</step><octave>{octave}</octave></pitch><duration>{duration}</duration><staff>{staff}</staff></note>";
        const string Back = "<backup><duration>6</duration></backup>";
        const string Head = "<attributes><divisions>2</divisions><key><fifths>0</fifths></key><staves>2</staves><time><beats>3</beats><beat-type>4</beat-type></time></attributes>";
        string xml = "<score-partwise><part-list><score-part id=\"P1\"><part-name>Piano</part-name></score-part></part-list><part id=\"P1\">"
            + $"<measure number=\"1\">{Head}{N("E", 5, 2, 1)}{N("D", 5, 2, 1)}{N("C", 5, 2, 1)}{Back}{N("C", 3, 6, 2)}</measure>"
            + $"<measure number=\"2\">{N("D", 5, 2, 1)}{N("E", 5, 2, 1)}{N("F", 5, 2, 1)}{Back}{N("G", 2, 6, 2)}</measure>"
            + $"<measure number=\"3\">{N("G", 5, 2, 1)}{N("A", 5, 2, 1)}{N("B", 5, 2, 1)}{Back}{N("G", 2, 6, 2)}</measure>"
            + $"<measure number=\"4\">{N("C", 6, 6, 1)}{Back}{N("C", 3, 6, 2)}</measure></part></score-partwise>";
        return PieceImporter.Import("score.musicxml", Encoding.UTF8.GetBytes(xml)).Piece!;
    }

    [Fact]
    public async Task The_selected_bars_notes_and_facts_reach_the_models_input_beside_the_question()
    {
        (AssistantPiece piece, TurnRequest turn) = SelectedTurn();
        HttpResponseMessage response = await Client().PostAsync("/assistant/turn", new ByteArrayContent(turn.ToJson()) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The context stands just before the question it belongs to, after the earlier one, and says which bars were selected.
        JsonElement given = ContextBeside("What happens here?", "What is the form?", "The player selected bars 3-4 in the score");

        // Every note of bars 3-4, by address and pitch, and every fact of their digest, by id.
        string[] addresses = [.. piece.Notes.Select(note => piece.Addresses.AddressOf(note.Id)!.Value).Where(static address => address.Bar is 3 or 4).Select(static address => address.ToString())];
        Assert.Equal(6, addresses.Length);
        Assert.Equal(addresses.Order(StringComparer.Ordinal), given.GetProperty("notes").GetProperty("notes").EnumerateArray().Select(static note => note.GetProperty("address").GetString()!).Order(StringComparer.Ordinal));
        Assert.Contains(given.GetProperty("notes").GetProperty("notes").EnumerateArray(), static note => note.GetProperty("pitch").GetString() == "C6");
        string[] ids = [.. FactsDigest.OfRange(piece, 3, 4).Ids];
        Assert.NotEmpty(ids);
        Assert.Equal(ids, given.GetProperty("digest").GetProperty("ids").EnumerateArray().Select(static id => id.GetString()!));
        Assert.Contains("never ask the player which bars they mean", api.Provider.Body.GetProperty("instructions").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The turn the device sends: the assistant opened with nothing selected and asked about the whole piece, then the
    /// player selected bars 3-4 on the score, mid-bar to mid-bar, with the assistant still open, and asked about them.
    /// </summary>
    private static (AssistantPiece Piece, TurnRequest Turn) SelectedTurn()
    {
        ImportedPiece imported = Piece();
        var piece = new AssistantPiece(imported, imported.Notes, []);

        AskScope scope = AskScope.Choose(false, null);
        JsonElement whole = AssistantContext.OfQuestion(piece, null, scope)!.Value;
        AssistantSession session = AssistantSession.Start(AssistantRole.Mentor).Ask("What is the form?").Answered("One phrase [[x]].");

        var selection = TickSpan.FromBounds(new Tick(imported.Meter.GetBar(3).Span.Onset.Value + 240), new Tick(imported.Meter.GetBar(4).Span.End.Value - 240));
        Assert.False(scope.IsWholePiece(selection));
        JsonElement context = AssistantContext.OfQuestion(piece, selection, scope)!.Value;
        Assert.NotEqual(whole.GetRawText(), context.GetRawText());
        return (piece, session.Ask("What happens here?").Request("en", context));

    }

    /// <summary>
    /// The context as the provider received it: the input item just before <paramref name="question"/>, after the earlier
    /// question, starting with <paramref name="about"/>; the JSON music-core wrote.
    /// </summary>
    private JsonElement ContextBeside(string question, string earlier, string about)
    {
        string[] texts = [.. api.Provider.Body.GetProperty("input").EnumerateArray().Select(static item => item.TryGetProperty("content", out JsonElement content) ? content[0].GetProperty("text").GetString()! : "")];
        int at = Array.LastIndexOf(texts, question);
        Assert.True(at > Array.IndexOf(texts, earlier));
        string seen = texts[at - 1];
        Assert.StartsWith(about, seen, StringComparison.Ordinal);
        JsonElement given = JsonDocument.Parse(seen[(seen.IndexOf('\n', StringComparison.Ordinal) + 1)..]).RootElement.Clone();
        Assert.Equal(3, given.GetProperty("digest").GetProperty("bars").GetProperty("from").GetInt32());
        Assert.Equal(4, given.GetProperty("digest").GetProperty("bars").GetProperty("to").GetInt32());
        return given;
    }

    private HttpClient Client()
    {
        HttpClient client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ProjectApiTests.Api.Token("allowed-sel", "Projects.ReadWrite", ["Pro"]));
        return client;
    }

    /// <summary>The API with the Azure OpenAI adapter of the dev environment, its provider played by a fake that keeps the request.</summary>
    public sealed class WithAzure : ProjectApiTests.Api
    {
        private const string Reply = """
            {"id": "resp_1", "object": "response", "created_at": 1763200000, "status": "completed", "model": "gpt-5-mini",
             "output": [{"type": "message", "id": "msg_1", "role": "assistant", "status": "completed",
                         "content": [{"type": "output_text", "text": "It climbs to C6.", "annotations": []}]}],
             "usage": {"input_tokens": 12, "input_tokens_details": {"cached_tokens": 0},
                       "output_tokens": 7, "output_tokens_details": {"reasoning_tokens": 0}, "total_tokens": 19}}
            """;

        internal FakeProvider Provider { get; } = new(Reply);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Assistant:Allowed", "allowed-sel");
            var settings = new Dictionary<string, string?>
            {
                ["Assistant:Provider"] = "azure-openai",
                ["Assistant:Key"] = "azure-key",
                ["Assistant:Endpoint"] = "motifflab-ai",
                ["Assistant:Model"] = "gpt-5-mini",
            };
            AssistantModelChoice model = AssistantModels.Create(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), NullLogger.Instance, Provider);
            builder.ConfigureTestServices(services => services.AddSingleton(model));
        }
    }
}