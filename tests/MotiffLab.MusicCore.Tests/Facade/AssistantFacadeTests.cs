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
    public void The_contract_stays_12()
    {
        Assert.Equal(12, Json(MusicCoreFacade.Version()).GetProperty("contract").GetInt32());
    }
}