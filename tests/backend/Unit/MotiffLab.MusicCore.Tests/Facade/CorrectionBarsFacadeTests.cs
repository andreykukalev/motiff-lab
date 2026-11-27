using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>The bar a corrections call names (every one answers a bar's notes), or none for the piece's first: a pickup's is bar 0.</summary>
public class CorrectionBarsFacadeTests
{
    private const string Pickup = """<?xml version="1.0"?><score-partwise version="3.1"><part-list><score-part id="P1"><part-name>P</part-name></score-part></part-list><part id="P1"><measure number="0" implicit="yes"><attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes><note><pitch><step>E</step><octave>5</octave></pitch><duration>1</duration><type>quarter</type></note></measure><measure number="1"><note><pitch><step>A</step><octave>4</octave></pitch><duration>4</duration><type>whole</type></note></measure></part></score-partwise>""";

    [Fact]
    public void A_corrections_call_with_no_bar_answers_for_the_first_bar_a_pickups_bar_0()
    {
        // The iPad's note editor with no selection sends no bar (within contract 16, v1.5 r-2a), never a guessed bar 1.
        var facade = new MusicCoreFacade();
        Assert.False(Json(facade.Open("pickup.musicxml", Encoding.UTF8.GetBytes(Pickup))).TryGetProperty("error", out _));

        Assert.Equal(0, Call(facade, "corrections.begin").GetProperty("bar").GetInt32());
        JsonElement pickup = Call(facade, "corrections", """{"bar": 0}""").GetProperty("notes")[0];
        Assert.Equal(76, pickup.GetProperty("pitch").GetInt32());
        Assert.True(Call(facade, "corrections.remove", $$"""{"bar": 0, "id": "{{pickup.GetProperty("id").GetString()}}"}""").GetProperty("changed").GetBoolean());
        Assert.Equal(1, Call(facade, "corrections.save").GetProperty("count").GetInt32());
        Assert.Equal(1, Call(facade, "corrections.undo", """{"bar": 1}""").GetProperty("bar").GetInt32());
        Assert.Equal("badRequest", Error(facade, "corrections.begin", """{"bar": -1}"""));
        Assert.Equal("badRequest", Error(facade, "corrections.begin", """{"bar": "first"}"""));
    }
}