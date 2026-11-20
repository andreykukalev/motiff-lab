using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using MotiffLab.Api.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Facade;

namespace MotiffLab.Api.Tests;

/// <summary>
/// The assistant in a draft (decision record 0034, section 6; the plan of version 1.2, answer 7): the relay serves the
/// place assembler, the explainer and the seam chooser, each with its own instructions and only its own tools, and the turn
/// the device sends for a place holds the notes of its base and of the variant built from, never a file's name or a title.
/// </summary>
public sealed class AssistantDraftTests(AssistantApiTests.WithModel api) : IClassFixture<AssistantApiTests.WithModel>
{
    /// <summary>The context of the draft's place made by music-core as the iPad asks for it: Für Elise, a place of bars 1 to <paramref name="last"/> and the bars after them as its variant.</summary>
    private static JsonElement PlaceContext(int last = 4)
    {
        var facade = new MusicCoreFacade();
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "CheckQuestions", "FurElise.musicxml"));
        int piece = JsonDocument.Parse(facade.Open("FurElise.musicxml", data, """{"language": "en"}""")).RootElement.GetProperty("piece").GetInt32();
        string[] calls =
        [
            facade.Call(piece, "drafts.add", $$"""{"device": "ipad", "here": [], "bars": {"first": 1, "last": {{last}}}, "now": "2026-11-20T10:00:00Z"}"""),
            facade.Call(piece, "drafts.add", $$"""{"device": "ipad", "here": [], "bars": {"first": {{last + 1}}, "last": {{2 * last}}}, "draft": "d_0001", "drop": "variant", "place": "pl_0001", "now": "2026-11-20T10:05:00Z"}"""),
        ];
        Assert.All(calls, static answer => Assert.DoesNotContain("\"error\"", answer, StringComparison.Ordinal));
        return JsonDocument.Parse(facade.Call(piece, "drafts.context", """{"draft": "d_0001", "place": "pl_0001"}""")).RootElement.GetProperty("context").Clone();
    }

    private static TurnRequest Turn(AssistantRole role, string question, JsonElement context) =>
        AssistantSession.Start(role).Ask(question).Request("en", context);

    private static TurnRequest WithHistory(AssistantRole role, JsonElement context, string history) =>
        TurnRequest.Read(JsonDocument.Parse($$"""{"version": 1, "role": "{{AssistantRoles.Id(role)}}", "language": "en", "context": {{context.GetRawText()}}, "history": [{{history}}]}""").RootElement, out _)!;

    private HttpClient Client(string player)
    {
        HttpClient client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ProjectApiTests.Api.Token(player, "Projects.ReadWrite", ["Pro"]));
        return client;
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, TurnRequest turn)
    {
        HttpResponseMessage response = await client.PostAsync("/assistant/turn", new ByteArrayContent(turn.ToJson()) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData(AssistantRole.PlaceAssembler, "allowed-d1", "place-assembler-v1", "by calling place.take")]
    [InlineData(AssistantRole.Explainer, "allowed-d2", "explainer-v1", "You may only read")]
    [InlineData(AssistantRole.SeamChooser, "allowed-d3", "seam-chooser-v1", "by calling place.bridge")]
    public async Task Each_draft_role_is_served_with_its_own_instructions_its_tools_and_the_place_beside_the_question(AssistantRole role, string player, string version, string rule)
    {
        string asked = $"What differs in this place? ({version})";
        api.Model.Script(asked, new ModelReply([], "The variant is a recording of other bars [[source.variant]]."));
        JsonElement answer = await PostAsync(Client(player), Turn(role, asked, PlaceContext()));

        Assert.Equal("answer", answer.GetProperty("kind").GetString());
        Assert.False(answer.GetProperty("fallback").GetBoolean());
        ModelTurn seen = api.Model.Seen(asked);
        Assert.Equal(AssistantRoles.ToolsOf(role).Select(static tool => tool.Name), seen.Tools.GetProperty("tools").EnumerateArray().Select(static tool => tool.GetProperty("name").GetString()));
        Assert.Contains(rule, seen.Instructions, StringComparison.Ordinal);
        Assert.Contains("English unless", seen.Instructions, StringComparison.Ordinal);
        Assert.StartsWith("The player is assembling a draft, and the next question is about one place of it", seen.ContextText, StringComparison.Ordinal);
        Assert.Contains(api.Logs, line => line.Contains($"prompt {version}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_assembler_proposes_a_take_through_the_device_and_the_explainer_and_a_wrong_bridge_never_reach_it()
    {
        HttpClient client = Client("allowed-d4");
        JsonElement context = PlaceContext();
        const string asked = "Assemble place 1 as on the recording, without improvisation.";
        JsonElement take = Arguments("""{"techniques": [1], "bar": null}""");

        // The assembler's place.take goes to the device; after the proposal its answer need cite nothing (the engine explains).
        api.Model.Script(asked, new ModelReply([new ToolCall("c1", "place.take", take)], null));
        JsonElement called = await PostAsync(client, Turn(AssistantRole.PlaceAssembler, asked, context));
        Assert.Equal("toolCalls", called.GetProperty("kind").GetString());
        Assert.Equal("place.take", called.GetProperty("entry").GetProperty("calls")[0].GetProperty("name").GetString());

        api.Model.Script(asked, new ModelReply([], "I asked music-core to take technique 1."));
        string history = $$$"""
            {"kind": "question", "text": "{{{asked}}}"},
            {"kind": "toolCalls", "calls": [{"id": "c1", "name": "place.take", "arguments": {{{take.GetRawText()}}}}]},
            {"kind": "toolResults", "results": [{"id": "c1", "name": "place.take", "content": {"tool": "place.take", "kind": "proposeDraft", "ok": true, "drafts": [{"tool": "place.take", "applied": false}]}}]}
            """;
        JsonElement answered = await PostAsync(client, WithHistory(AssistantRole.PlaceAssembler, context, history));
        Assert.False(answered.GetProperty("fallback").GetBoolean());

        // The explainer only reads: its place.take is the engine's template, from the place's facts.
        api.Model.Script(asked, new ModelReply([new ToolCall("c2", "place.take", take)], null));
        JsonElement refused = await PostAsync(Client("allowed-d1"), Turn(AssistantRole.Explainer, asked, context));
        Assert.True(refused.GetProperty("fallback").GetBoolean());
        Assert.Equal(AnswerCheck.ToolNotAllowed, refused.GetProperty("reason").GetString());
        Assert.Contains("[[", refused.GetProperty("text").GetString(), StringComparison.Ordinal);

        // A bridge kind the catalogue does not have never reaches the device.
        api.Model.Script(asked, new ModelReply([new ToolCall("c3", "place.bridge", Arguments("""{"kind": "tritone"}"""))], null));
        JsonElement wrong = await PostAsync(Client("allowed-d2"), Turn(AssistantRole.SeamChooser, asked, context));
        Assert.Equal(AnswerCheck.BadArguments, wrong.GetProperty("reason").GetString());
    }

    [Fact]
    public void A_place_of_32_bars_with_a_variant_of_32_fits_one_turn_and_names_no_file_or_title()
    {
        JsonElement context = PlaceContext(last: 32);
        Assert.Equal(32, context.GetProperty("digest").GetProperty("bars").GetProperty("to").GetInt32());
        Assert.True(context.GetProperty("variantNotes").GetProperty("notes").GetArrayLength() > 0);

        byte[] turn = Turn(AssistantRole.PlaceAssembler, "Assemble this place.", context).ToJson();
        // About 60 KB for 536 notes: a quarter of the turn is left for the conversation and the tools' results.
        Assert.True(turn.Length < TurnRequest.MaxBytes * 3 / 4, $"{turn.Length} bytes");
        string text = Encoding.UTF8.GetString(turn);
        Assert.DoesNotContain("Elise", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("musicxml", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_role_served_has_its_instructions()
    {
        Assert.All(AssistantRoles.Served, static role => Assert.DoesNotContain("{language}", AssistantPrompts.For(role, "ru"), StringComparison.Ordinal));
        Assert.Contains("(Russian unless", AssistantPrompts.For(AssistantRole.SeamChooser, "ru").ReplaceLineEndings(" "), StringComparison.Ordinal);
    }
}