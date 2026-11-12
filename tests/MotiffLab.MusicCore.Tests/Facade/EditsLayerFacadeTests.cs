using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>«Мои правки: вкл / выкл» through the facade (version 0.5, within contract 12): "edits" on open, corrections.layer and .drop.</summary>
public class EditsLayerFacadeTests
{
    private const string FurElise = "fur-elise-original.musicxml";

    private static (MusicCoreFacade Facade, JsonElement Opened) OpenFile(byte[] file, string? request = null)
    {
        var facade = new MusicCoreFacade();
        return (facade, Json(facade.Open("fur.motiff.json", file, request)));
    }

    /// <summary>Für Elise with the first note of bar 1 taken out, bars 1–2 as the motif and a chip tried on them, saved.</summary>
    private static byte[] Worked()
    {
        var facade = new MusicCoreFacade();
        Assert.False(Json(facade.Open(FurElise, Fixture(FurElise), """{"id": "p_fur"}""")).TryGetProperty("error", out _));
        string id = Call(facade, "corrections", """{"bar": 1}""").GetProperty("notes")[0].GetProperty("id").GetString()!;
        Call(facade, "corrections.remove", $$"""{"bar": 1, "id": "{{id}}"}""");
        Call(facade, "select", """{"bars": {"first": 1, "last": 2}}""");
        JsonElement understood = Call(facade, "understand");
        string chip = understood.GetProperty("motif").GetProperty("facts").EnumerateArray().Concat(understood.GetProperty("character").EnumerateArray())
            .SelectMany(fact => fact.GetProperty("tryChips").EnumerateArray()).First().GetProperty("id").GetString()!;
        Call(facade, "try", $$"""{"chip": "{{chip}}"}""");
        return Encoding.UTF8.GetBytes(Call(facade, "project.save").GetRawText());
    }

    /// <summary>The motifs and variations of a project file, by id, passage, notes and steps.</summary>
    private static string[] Motifs(byte[] file)
    {
        Project project = ProjectFile.Read(file);
        return [.. project.Motifs.Select(motif => $"{motif.Id} {motif.Span} {string.Join(",", motif.NoteIds)}"),
            .. project.Variations.Select(variation => $"{variation.Id} {variation.MotifId} {variation.Steps.Count} {variation.Pending is not null}")];
    }

    [Fact]
    public void Off_then_saved_then_on_keeps_the_motif_its_variation_and_the_corrections()
    {
        byte[] worked = Worked();

        (MusicCoreFacade off, JsonElement opened) = OpenFile(worked, """{"edits": "off"}""");
        JsonElement edits = opened.GetProperty("edits");
        Assert.Equal("off", edits.GetProperty("layer").GetString());
        Assert.Equal(1, edits.GetProperty("count").GetInt32());
        Assert.Equal(1, edits.GetProperty("applied").GetInt32());
        Assert.Contains("m_0001", edits.GetProperty("hidden").EnumerateArray().Select(hidden => hidden.GetString()));
        Assert.False(opened.GetProperty("hasVariation").GetBoolean());
        Assert.Equal("unavailable", Error(off, "corrections.change", """{"bar": 1, "id": "n_0001", "pitch": 1}"""));
        Assert.Equal(0, Call(off, "corrections", """{"bar": 1}""").GetProperty("notes").EnumerateArray().Count(note => note.GetProperty("edited").GetBoolean()));
        Call(off, "select", """{"bars": {"first": 3, "last": 4}}""");
        byte[] saved = Encoding.UTF8.GetBytes(Call(off, "project.save").GetRawText());

        (_, JsonElement strict) = OpenFile(saved);
        Assert.False(strict.TryGetProperty("error", out _), strict.ToString());
        Assert.True(strict.GetProperty("hasVariation").GetBoolean());
        Assert.False(strict.TryGetProperty("edits", out _));
        Assert.Equal(1, strict.GetProperty("selection").GetProperty("firstBar").GetInt32());
        Assert.Equal(Motifs(worked), Motifs(saved));
        Assert.Equal(ProjectFile.Read(worked).Edits, ProjectFile.Read(saved).Edits);
    }

    [Fact]
    public void Switching_the_layer_off_and_on_again_brings_the_variation_back()
    {
        (MusicCoreFacade facade, JsonElement opened) = OpenFile(Worked(), """{"edits": "on"}""");
        Assert.Equal("on", opened.GetProperty("edits").GetProperty("layer").GetString());
        Assert.True(opened.GetProperty("hasVariation").GetBoolean());

        Assert.Equal("off", Call(facade, "corrections.layer", """{"on": false}""").GetProperty("layer").GetString());
        JsonElement on = Call(facade, "corrections.layer", """{"on": true, "bar": 1}""");

        Assert.Equal("on", on.GetProperty("layer").GetString());
        Assert.Equal(1, on.GetProperty("applied").GetInt32());
        Assert.Empty(on.GetProperty("unfit").EnumerateArray());
        Assert.NotEqual(JsonValueKind.Null, Call(facade, "variation").GetProperty("result").ValueKind);
        (_, JsonElement again) = OpenFile(Encoding.UTF8.GetBytes(Call(facade, "project.save").GetRawText()));
        Assert.True(again.GetProperty("hasVariation").GetBoolean());
        Assert.Equal("badRequest", Error(facade, "corrections.layer", "{}"));
    }

    [Fact]
    public void A_correction_that_no_longer_fits_is_listed_and_dropped_and_the_piece_opens_strictly_again()
    {
        Project project = ProjectFile.Read(Worked());
        NoteEdit kept = project.Edits.Edits[0];
        Project stale = project with { Edits = new NoteEditLog([.. project.Edits.Edits, new NoteEdit("n_9999", kept.Before, null)]) };
        byte[] file = ProjectFile.Write(stale);

        (_, JsonElement refused) = OpenFile(file);
        Assert.Equal("import", refused.GetProperty("error").GetString());

        (MusicCoreFacade facade, JsonElement opened) = OpenFile(file, """{"edits": "on"}""");
        JsonElement unfit = Assert.Single(opened.GetProperty("edits").GetProperty("unfit").EnumerateArray());
        Assert.Equal(("n_9999", "missing", 1), (unfit.GetProperty("noteId").GetString(), unfit.GetProperty("reason").GetString(), unfit.GetProperty("bar").GetInt32()));
        Assert.True(opened.GetProperty("hasVariation").GetBoolean());

        JsonElement dropped = Call(facade, "corrections.drop", """{"noteIds": ["n_9999"]}""");
        Assert.Empty(dropped.GetProperty("unfit").EnumerateArray());
        Assert.Equal(1, dropped.GetProperty("count").GetInt32());
        (_, JsonElement strict) = OpenFile(Encoding.UTF8.GetBytes(Call(facade, "project.save").GetRawText()));
        Assert.False(strict.TryGetProperty("error", out _), strict.ToString());
        Assert.Equal("badRequest", Error(facade, "corrections.drop", "{}"));
    }

    [Fact]
    public void The_piece_remembers_the_corrections_off_in_its_file_and_opens_so_as_the_file_says()
    {
        (MusicCoreFacade facade, _) = OpenFile(Worked(), """{"edits": "on"}""");
        Assert.True(Call(facade, "corrections.layer", """{"on": false}""").GetProperty("editsOff").GetBoolean());
        byte[] off = Encoding.UTF8.GetBytes(Call(facade, "project.save").GetRawText());
        Assert.True(ProjectFile.Read(off).EditsOff);

        (MusicCoreFacade piece, JsonElement opened) = OpenFile(off, """{"edits": "piece"}""");
        Assert.Equal("off", opened.GetProperty("edits").GetProperty("layer").GetString());
        Assert.Equal("on", OpenFile(off, """{"edits": "on"}""").Opened.GetProperty("edits").GetProperty("layer").GetString());

        // A build that opens it strictly, as the iPad does, keeps the field when it saves.
        (MusicCoreFacade strict, _) = OpenFile(off);
        Assert.True(ProjectFile.Read(Encoding.UTF8.GetBytes(Call(strict, "project.save").GetRawText())).EditsOff);

        Assert.False(Call(piece, "corrections.layer", """{"on": true}""").GetProperty("editsOff").GetBoolean());
        byte[] on = Encoding.UTF8.GetBytes(Call(piece, "project.save").GetRawText());
        Assert.False(ProjectFile.Read(on).EditsOff);
        Assert.Equal("on", OpenFile(on, """{"edits": "piece"}""").Opened.GetProperty("edits").GetProperty("layer").GetString());
    }

    [Fact]
    public void An_open_without_edits_answers_as_before_and_a_wrong_value_is_refused()
    {
        byte[] worked = Worked();

        (MusicCoreFacade facade, JsonElement opened) = OpenFile(worked);
        Assert.False(opened.TryGetProperty("edits", out _));
        Assert.False(Call(facade, "corrections", """{"bar": 1}""").TryGetProperty("layer", out _));
        (_, JsonElement wrong) = OpenFile(worked, """{"edits": "maybe"}""");
        Assert.Equal("badRequest", wrong.GetProperty("error").GetString());
    }
}