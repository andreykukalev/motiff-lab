using System.Text.Json;

using MotiffLab.MusicCore.Facade;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>The assistant's calls on the facade (version 0.6, within contract 12).</summary>
public class AssistantFacadeTests
{
    private const string FurElise = "fur-elise-original.musicxml";

    private static MusicCoreFacade Opened()
    {
        var facade = new MusicCoreFacade();
        Json(facade.Open(FurElise, Fixture(FurElise), """{"id": "p_assist", "now": "2026-11-14T10:00:00Z"}"""));
        return facade;
    }

    [Fact]
    public void The_tool_registry_answers_without_a_piece()
    {
        JsonElement tools = Json(new MusicCoreFacade().Call(0, "assistant.tools")).GetProperty("tools");

        Assert.Contains(tools.EnumerateArray(), tool => tool.GetProperty("name").GetString() == "variation.simplify");
        Assert.All(tools.EnumerateArray(), tool => Assert.Equal("object", tool.GetProperty("parameters").GetProperty("type").GetString()));
    }

    [Fact]
    public void A_tool_call_answers_its_drafts_and_a_refusal_is_an_answer_not_an_error()
    {
        MusicCoreFacade facade = Opened();
        string before = facade.Call(1, "piece");

        JsonElement drafted = Call(facade, "assistant.call", """
            {"tool": "variation.simplify", "parameters": {"bars": {"from": 1, "to": 4}, "hand": "both", "scope": "all", "keep": [], "seed": 0, "level": 2}}
            """);
        JsonElement refused = Call(facade, "assistant.call", """{"tool": "variation.simplify", "parameters": {"bars": {"from": 1, "to": 4}}}""");

        Assert.True(drafted.GetProperty("ok").GetBoolean());
        Assert.False(drafted.GetProperty("drafts")[0].GetProperty("applied").GetBoolean());
        Assert.False(refused.GetProperty("ok").GetBoolean());
        Assert.Contains("missing", refused.GetProperty("refusal").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, facade.Call(1, "piece"));
    }

    [Fact]
    public void The_digest_and_addresses_answer_on_an_open_piece()
    {
        MusicCoreFacade facade = Opened();

        JsonElement range = Call(facade, "assistant.digest", """{"bars": {"from": 1, "to": 4}}""").GetProperty("digest");
        JsonElement piece = Call(facade, "assistant.digest").GetProperty("digest");
        JsonElement address = Call(facade, "assistant.address", """{"address": "1.R.1"}""");
        JsonElement back = Call(facade, "assistant.address", $$"""{"noteId": "{{address.GetProperty("noteId").GetString()}}"}""");

        Assert.Equal("selection", range.GetProperty("scope").GetString());
        Assert.Equal("piece", piece.GetProperty("scope").GetString());
        Assert.Equal("1.R.1", back.GetProperty("address").GetString());
        Assert.Equal("badRequest", Error(facade, "assistant.address", """{"address": "1.R.999"}"""));
        Assert.Equal("badRequest", Error(facade, "assistant.digest", """{"bars": {"from": 1, "to": 99}}"""));
        Assert.Equal("unknownCall", Error(facade, "assistant.apply"));
    }

    [Fact]
    public void The_context_of_a_question_is_the_selections_bars_or_the_whole_piece()
    {
        MusicCoreFacade facade = Opened();

        // Bars 1-2 of Für Elise's 3/8 after the pickup: from tick 240, two bars of 720.
        JsonElement selected = Call(facade, "assistant.context", """{"selection": {"onset": 300, "end": 1500}}""").GetProperty("context");
        JsonElement whole = Call(facade, "assistant.context", """{"selection": {"onset": 300, "end": 1500}, "wholePiece": true}""").GetProperty("context");
        JsonElement none = Call(facade, "assistant.context").GetProperty("context");

        Assert.Equal("selection", selected.GetProperty("digest").GetProperty("scope").GetString());
        Assert.True(selected.GetProperty("notes").GetProperty("notes").GetArrayLength() > 0);
        Assert.Equal("piece", whole.GetProperty("digest").GetProperty("scope").GetString());
        Assert.Equal("piece", none.GetProperty("digest").GetProperty("scope").GetString());
        Assert.Equal("badRequest", Error(facade, "assistant.context", """{"selection": {"onset": 500, "end": 400}}"""));
        Assert.Contains("notes.join", Json(new MusicCoreFacade().Call(0, "assistant.tools")).GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()));
    }

    [Fact]
    public void Accept_adds_the_drafts_edits_as_corrections_made_again_from_its_call()
    {
        MusicCoreFacade facade = Opened();
        const string Parameters = """
            {"bars": {"from": 1, "to": 1}, "edits": [{"kind": "remove", "address": "1.R.1", "bar": null, "hand": null, "start": null, "length": null, "pitch": null, "velocity": null}]}
            """;
        JsonElement drafted = Call(facade, "assistant.call", $$"""{"tool": "notes.edit", "parameters": {{Parameters}}}""");
        Assert.True(drafted.GetProperty("ok").GetBoolean(), drafted.ToString());
        int before = Call(facade, "corrections", """{"bar": 1}""").GetProperty("notes").GetArrayLength();

        JsonElement accepted = Call(facade, "corrections.acceptDraft", $$"""{"bar": 1, "tool": "notes.edit", "parameters": {{Parameters}}, "index": 0}""");

        Assert.Equal(before - 1, accepted.GetProperty("notes").GetArrayLength());
        // Accept needs the call that made the draft.
        Assert.Equal("badRequest", Error(facade, "corrections.acceptDraft", """{"bar": 1, "tool": "notes.edit"}"""));
        // One undo per edit, as every correction.
        Assert.Equal(before, Call(facade, "corrections.undo", """{"bar": 1}""").GetProperty("notes").GetArrayLength());
    }

    [Fact]
    public void Undo_draft_takes_the_whole_draft_back_while_nothing_came_after_it()
    {
        MusicCoreFacade facade = Opened();
        const string Parameters = """
            {"bars": {"from": 1, "to": 1}, "edits": [{"kind": "remove", "address": "1.R.1", "bar": null, "hand": null, "start": null, "length": null, "pitch": null, "velocity": null}]}
            """;
        int before = Call(facade, "corrections", """{"bar": 1}""").GetProperty("notes").GetArrayLength();
        Assert.Equal("unavailable", Error(facade, "corrections.undoDraft", """{"bar": 1}"""));
        Call(facade, "corrections.acceptDraft", $$"""{"bar": 1, "tool": "notes.edit", "parameters": {{Parameters}}, "index": 0}""");

        JsonElement undone = Call(facade, "corrections.undoDraft", """{"bar": 1}""");

        Assert.Equal(before, undone.GetProperty("notes").GetArrayLength());
        Assert.Equal("unavailable", Error(facade, "corrections.undoDraft", """{"bar": 1}"""));
    }

    [Fact]
    public void A_call_with_its_role_is_refused_as_the_relay_refuses_it()
    {
        MusicCoreFacade facade = Opened();
        const string Parameters = """
            {"bars": {"from": 1, "to": 1}, "edits": [{"kind": "remove", "address": "1.R.1", "bar": null, "hand": null, "start": null, "length": null, "pitch": null, "velocity": null}]}
            """;

        JsonElement mentor = Call(facade, "assistant.call", $$"""{"tool": "notes.edit", "parameters": {{Parameters}}, "role": "mentor"}""");
        JsonElement proposer = Call(facade, "assistant.call", $$"""{"tool": "notes.edit", "parameters": {{Parameters}}, "role": "proposer"}""");

        Assert.False(mentor.GetProperty("ok").GetBoolean());
        Assert.False(mentor.TryGetProperty("drafts", out _));
        Assert.True(proposer.GetProperty("ok").GetBoolean(), proposer.ToString());
        Assert.Equal("badRequest", Error(facade, "assistant.call", """{"tool": "notes.edit", "parameters": {}, "role": "other"}"""));
    }

    [Fact]
    public void The_contract_stays_13()
    {
        // The assistant's calls came within 12; 13 is the merge calls removed (version 1.1, ADR 0034 section 8).
        Assert.Equal(13, Json(MusicCoreFacade.Version()).GetProperty("contract").GetInt32());
    }
}