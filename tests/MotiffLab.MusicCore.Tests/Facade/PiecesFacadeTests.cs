using System.Text.Json;

using MotiffLab.MusicCore.Facade;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// pieces.find and pieces.save (version 0.5, within contract 12): where the pieces of a file begin, proposed over the notes
/// as they are, and the pieces the player confirmed kept as the pieces layer, which the project file carries as bookmarks.
/// </summary>
public class PiecesFacadeTests
{
    private const string TwoPieces = "two-pieces.musicxml";

    private static byte[] Fixture() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Analysis", "Fixtures", TwoPieces));

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    private readonly MusicCoreFacade _facade = new();

    public PiecesFacadeTests() =>
        Assert.Equal(1, Json(_facade.Open(TwoPieces, Fixture(), """{"id": "p_pieces", "language": "en"}""")).GetProperty("piece").GetInt32());

    private JsonElement Call(string name, string? request = null)
    {
        JsonElement answer = Json(_facade.Call(1, name, request));
        Assert.False(answer.TryGetProperty("error", out _), $"{name}: {answer}");
        return answer;
    }

    private string Error(string name, string request) => Json(_facade.Call(1, name, request)).GetProperty("error").GetString()!;

    [Fact]
    public void Find_proposes_the_two_pieces_with_their_bars_and_reasons_and_no_layer_yet()
    {
        JsonElement found = Call("pieces.find");

        JsonElement[] proposals = [.. found.GetProperty("proposals").EnumerateArray()];
        Assert.Equal(2, proposals.Length);
        Assert.Equal((0, 105), (proposals[0].GetProperty("firstBar").GetInt32(), proposals[0].GetProperty("lastBar").GetInt32()));
        JsonElement ode = proposals[1];
        Assert.Equal("Ode to Joy", ode.GetProperty("title").GetString());
        Assert.Equal(106, ode.GetProperty("firstBar").GetInt32());
        Assert.Equal("likely", ode.GetProperty("certainty").GetString());
        Assert.StartsWith("final bar line", ode.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.False(ode.GetProperty("added").GetBoolean());
        Assert.Equal(JsonValueKind.Null, ode.GetProperty("pages").ValueKind);
        Assert.Empty(found.GetProperty("layer").EnumerateArray());

        Assert.Single(Call("pieces.find", """{"count": 1}""").GetProperty("proposals").EnumerateArray());
        Assert.Equal("badRequest", Error("pieces.find", """{"count": 0}"""));
    }

    [Fact]
    public void Save_keeps_the_confirmed_pieces_as_the_layer_which_the_project_file_carries()
    {
        JsonElement saved = Call("pieces.save", """{"pieces": [{"title": "Prelude", "firstBar": 0}, {"title": "Ode to Joy", "firstBar": 106}], "now": "2026-11-12T15:00:00Z"}""");

        JsonElement[] layer = [.. saved.GetProperty("layer").EnumerateArray()];
        Assert.Equal(["Prelude", "Ode to Joy"], layer.Select(piece => piece.GetProperty("title").GetString()));
        Assert.Equal((106, 121), (layer[1].GetProperty("firstBar").GetInt32(), layer[1].GetProperty("lastBar").GetInt32()));
        Assert.Equal(2, Call("pieces.find").GetProperty("layer").GetArrayLength());
        Assert.Contains(Call("bookmarks").GetProperty("categories").EnumerateArray(), category => category.GetProperty("id").GetString() == "pieces");

        byte[] file = System.Text.Encoding.UTF8.GetBytes(_facade.Call(1, "project.save"));
        var again = new MusicCoreFacade();
        Assert.Equal(1, Json(again.Open("pieces.motiff.json", file)).GetProperty("piece").GetInt32());
        Assert.Equal(2, Json(again.Call(1, "pieces.find")).GetProperty("layer").GetArrayLength());
    }

    [Fact]
    public void Save_refuses_pieces_out_of_order_or_without_a_first_bar()
    {
        Assert.Equal("badRequest", Error("pieces.save", """{"pieces": [{"firstBar": 50}, {"firstBar": 20}]}"""));
        Assert.Equal("badRequest", Error("pieces.save", """{"pieces": [{"title": "A"}]}"""));
        Assert.Equal("badRequest", Error("pieces.save", "{}"));
        Assert.Equal("badRequest", Error("pieces.save", """{"pieces": [{"firstBar": 500}]}"""));
    }
}