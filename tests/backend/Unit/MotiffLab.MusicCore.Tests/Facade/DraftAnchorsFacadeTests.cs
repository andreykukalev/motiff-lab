using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>Anchors through the facade (the plan's answer 4): confirmed at a tick or at the recording's time, shown over the draft's bars, taken away, undone, saved.</summary>
public class DraftAnchorsFacadeTests
{
    private const string Variant = """ "draft": "d_0001", "place": "pl_0001", "variant": "v_0002" """;

    /// <summary>The example open in Russian with bars 1–8 as a draft's place and bars 1–4 as its variant.</summary>
    private static MusicCoreFacade Drafted()
    {
        var facade = new MusicCoreFacade();
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));
        Json(facade.Open("FurElise.musicxml", data, """{"language": "ru", "id": "p_anchors"}"""));
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 8}, "now": "2026-11-20T10:00:00Z"}""");
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 4}, "draft": "d_0001", "drop": "variant", "place": "pl_0001", "now": "2026-11-20T10:05:00Z"}""");
        return facade;
    }

    /// <summary>A request on the variant with <paramref name="more"/> fields.</summary>
    private static string With(string more) => "{" + Variant + ", " + more + "}";

    private static IReadOnlyList<DraftAnchor> Saved(MusicCoreFacade facade, int piece = 1) =>
        ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(piece, "project.save"))).Drafts.Single().Places[0].Find("v_0002")!.Anchors;

    [Fact]
    public void A_confirmed_anchor_is_saved_shown_over_its_bar_and_taken_away()
    {
        MusicCoreFacade facade = Drafted();

        JsonElement confirmed = Call(facade, "drafts.anchor", With(""" "anchor": {"base": 2880, "variant": 0} """));

        Assert.Equal(("pl_0001", "v_0002"), (confirmed.GetProperty("place").GetString(), confirmed.GetProperty("variant").GetString()));
        Assert.Equal([new DraftAnchor(new Tick(2880), Tick.Zero)], Saved(facade));
        JsonElement shown = Assert.Single(Call(facade, "drafts.open", """{"draft": "d_0001"}""").GetProperty("draft").GetProperty("places")[0].GetProperty("variants")[1].GetProperty("anchors").EnumerateArray());
        Assert.Equal((2880, 0, 2880), (shown.GetProperty("base").GetInt64(), shown.GetProperty("variant").GetInt64(), shown.GetProperty("at").GetInt64()));
        Assert.True(shown.GetProperty("bar").GetInt32() >= 1);

        Call(facade, "drafts.unanchor", With(""" "base": 2880 """));
        Assert.Empty(Saved(facade));
        Assert.True(Call(facade, "drafts.undo", """{"draft": "d_0001"}""").GetProperty("structure").GetBoolean());
        Assert.Single(Saved(facade));
    }

    [Fact]
    public void The_приёмы_after_an_anchor_read_the_same_once_saved_and_opened_again()
    {
        MusicCoreFacade facade = Drafted();
        Call(facade, "drafts.anchor", With(""" "anchor": {"base": 2880, "variant": 0} """));
        string techniques = facade.Call(1, "drafts.techniques", """{"draft": "d_0001", "place": "pl_0001"}""");
        var reopened = new MusicCoreFacade();
        Json(reopened.Open("FurElise.motiff.json", Encoding.UTF8.GetBytes(facade.Call(1, "project.save")), """{"language": "ru"}"""));

        Assert.Equal(techniques, reopened.Call(1, "drafts.techniques", """{"draft": "d_0001", "place": "pl_0001"}"""));
        Assert.NotEqual(techniques, Drafted().Call(1, "drafts.techniques", """{"draft": "d_0001", "place": "pl_0001"}"""));
    }

    [Fact]
    public void An_anchor_on_the_base_crossing_another_outside_or_at_a_recording_the_variant_has_not_is_refused()
    {
        MusicCoreFacade facade = Drafted();
        Call(facade, "drafts.anchor", With(""" "anchor": {"base": 2880, "variant": 960} """));

        Assert.Equal("badRequest", Error(facade, "drafts.anchor", With(""" "anchor": {"base": 5760, "variant": 0} """)));
        Assert.Equal("badRequest", Error(facade, "drafts.anchor", """{"draft": "d_0001", "place": "pl_0001", "variant": "v_0001", "anchor": {"base": 0, "variant": 0}}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.anchor", With(""" "anchor": {"base": 999999, "variant": 0} """)));
        Assert.Equal("badRequest", Error(facade, "drafts.anchor", With(""" "anchor": {"base": 0} """)));
        Assert.Equal("unavailable", Error(facade, "drafts.anchor", With(""" "anchor": {"base": 0, "recording": 1000} """)));
        Assert.Equal("badRequest", Error(facade, "drafts.unanchor", With(""" "base": 0 """)));
    }

    [Fact]
    public void An_anchor_at_the_time_of_the_recording_is_the_variant_tick_played_then()
    {
        var facade = new MusicCoreFacade();
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));
        Json(facade.Open("FurElise.musicxml", data, """{"language": "en", "id": "p_heard"}"""));
        Call(facade, "sources.original.add", """{"kind": "audio", "fileName": "Live.m4a", "kept": "device", "now": "2026-11-20T10:30:00Z"}""");
        Call(facade, "sources.notes.hear", Heard());
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 4}, "now": "2026-11-20T10:00:00Z"}""");
        int shown = Call(facade, "sources.notes.open", """{"id": "n_2"}""").GetProperty("piece").GetInt32();
        Json(facade.Call(shown, "drafts.add", """{"bars": {"first": 1, "last": 4}, "draft": "d_0001", "drop": "variant", "place": "pl_0001", "now": "2026-11-20T10:40:00Z"}"""));
        JsonElement occurrence = Call(facade, "drafts.techniques", """{"draft": "d_0001", "place": "pl_0001"}""").GetProperty("techniques")[0].GetProperty("occurrences")[0];
        long from = occurrence.GetProperty("recording").GetProperty("from").GetInt64();
        long onset = occurrence.GetProperty("inVariant").GetProperty("onset").GetInt64();

        Call(facade, "drafts.anchor", With($$""" "anchor": {"base": {{occurrence.GetProperty("inBase").GetProperty("onset").GetInt64()}}, "recording": {{from}}} """));

        Assert.InRange(Assert.Single(Saved(facade)).Variant.Value, onset - 4, onset);
    }
}