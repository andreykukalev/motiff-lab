using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>«Ноты крупнее» through the facade (version 1.7, contract 17, calls added): largerNotes and largerNotes.set.</summary>
public class LargerNotesFacadeTests
{
    private static MusicCoreFacade Opened(byte[] data, string fileName = "FurElise.musicxml")
    {
        var facade = new MusicCoreFacade();
        Assert.False(Json(facade.Open(fileName, data, """{"id": "p_larger", "now": "2026-11-28T10:00:00Z"}""")).TryGetProperty("error", out _));
        return facade;
    }

    private static byte[] Elise() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));

    [Fact]
    public void A_piece_opens_at_step_0_and_the_step_the_player_sets_is_saved_with_it()
    {
        MusicCoreFacade facade = Opened(Elise());

        JsonElement before = Call(facade, "largerNotes");
        Assert.Equal((0, 100, LargerNotes.MaxStep), (before.GetProperty("step").GetInt32(), before.GetProperty("percent").GetInt32(), before.GetProperty("maxStep").GetInt32()));

        JsonElement set = Call(facade, "largerNotes.set", """{"step": 7}""");
        Assert.Equal((7, 170), (set.GetProperty("step").GetInt32(), set.GetProperty("percent").GetInt32()));
        Assert.Equal(7, Call(facade, "largerNotes").GetProperty("step").GetInt32());
        string saved = facade.Call(1, "project.save", """{"now": "2026-11-28T10:05:00Z"}""");
        Assert.Equal(7, Json(saved).GetProperty("workspace").GetProperty("largerNotes").GetInt32());

        MusicCoreFacade again = Opened(Encoding.UTF8.GetBytes(saved), "p_larger" + ProjectFile.Extension);
        Assert.Equal(7, Call(again, "largerNotes").GetProperty("step").GetInt32());
        Assert.Equal(0, Call(again, "largerNotes.set", """{"step": 0}""").GetProperty("step").GetInt32());
        Assert.False(Json(again.Call(1, "project.save", "{}")).GetProperty("workspace").TryGetProperty("largerNotes", out _));
    }

    [Theory]
    [InlineData("""{"step": 11}""")]
    [InlineData("""{"step": -1}""")]
    [InlineData("""{"step": "big"}""")]
    [InlineData("{}")]
    public void A_step_out_of_range_or_none_is_refused_and_changes_nothing(string request)
    {
        MusicCoreFacade facade = Opened(Elise());
        Call(facade, "largerNotes.set", """{"step": 2}""");

        Assert.Equal("badRequest", Error(facade, "largerNotes.set", request));
        Assert.Equal(2, Call(facade, "largerNotes").GetProperty("step").GetInt32());
    }

    [Fact]
    public void With_no_piece_open_the_step_cannot_be_read() =>
        Assert.Equal("noPiece", Json(new MusicCoreFacade().Call(0, "largerNotes", "{}")).GetProperty("error").GetString());
}