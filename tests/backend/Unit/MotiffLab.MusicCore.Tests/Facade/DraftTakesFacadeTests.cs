using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>A place's приёмы through the facade (decision record 0034, section 4; the iPad's step 6): saved as steps, refused where nothing is left to do.</summary>
public class DraftTakesFacadeTests
{
    private const string Place = """ "draft": "d_0001", "place": "pl_0001" """;

    /// <summary>The example open in Russian with bars 1–4 as a draft's place and bars 5–8 as its variant: one sure приём, a simplification in bar 4.</summary>
    private static MusicCoreFacade Drafted()
    {
        var facade = new MusicCoreFacade();
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));
        Json(facade.Open("FurElise.musicxml", data, """{"language": "ru", "id": "p_takes"}"""));
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 4}, "now": "2026-11-20T10:00:00Z"}""");
        Call(facade, "drafts.add", """{"bars": {"first": 5, "last": 8}, "draft": "d_0001", "drop": "variant", "place": "pl_0001", "now": "2026-11-20T10:05:00Z"}""");
        return facade;
    }

    private static Draft Saved(MusicCoreFacade facade) => ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save"))).Drafts.Single();

    [Fact]
    public void The_techniques_are_labelled_in_the_language_of_the_piece_and_marked_inferred()
    {
        JsonElement technique = Call(Drafted(), "drafts.techniques", $$"""{{{Place}}}""").GetProperty("techniques")[0];

        Assert.Equal(("simplify", "упрощение", true, true), (technique.GetProperty("kind").GetString(), technique.GetProperty("label").GetString(), technique.GetProperty("inferred").GetBoolean(), technique.GetProperty("sure").GetBoolean()));
    }

    [Fact]
    public void An_occurrence_gives_the_notes_the_sure_rule_counts_so_a_sure_one_under_85_per_cent_says_why()
    {
        JsonElement occurrence = Call(Drafted(), "drafts.techniques", $$"""{{{Place}}}""").GetProperty("techniques")[0].GetProperty("occurrences")[0];

        // Sure by 0024's «all but one»: 3 of 4 notes in place.
        Assert.Equal((3, 4, 75), (occurrence.GetProperty("matched").GetInt32(), occurrence.GetProperty("total").GetInt32(), occurrence.GetProperty("confidence").GetInt32()));
    }

    [Fact]
    public void A_take_is_saved_as_a_step_of_its_notes_with_its_technique_as_a_label()
    {
        MusicCoreFacade facade = Drafted();

        Call(facade, "drafts.take", $$"""{{{Place}}, "technique": 1, "now": "2026-11-20T10:10:00Z"}""");

        DraftTake take = Assert.IsType<DraftTake>(Assert.Single(Saved(facade).Steps));
        Assert.Equal((TechniqueKind.Simplify, 4), (take.Technique!.Kind, Assert.Single(take.Technique.Bars)));
        Assert.NotEmpty(take.Removed);
    }

    [Fact]
    public void An_occurrence_of_notes_not_heard_is_played_from_the_variant_and_has_no_recording()
    {
        JsonElement occurrence = Call(Drafted(), "drafts.techniques", $$"""{{{Place}}}""").GetProperty("techniques")[0].GetProperty("occurrences")[0];

        JsonElement inVariant = occurrence.GetProperty("inVariant");
        Assert.Equal((2160, 2880), (inVariant.GetProperty("onset").GetInt32(), inVariant.GetProperty("end").GetInt32()));
        Assert.Equal(JsonValueKind.Null, occurrence.GetProperty("recording").ValueKind);
    }

    [Fact]
    public void The_base_side_of_an_occurrence_is_played_from_the_base_variant_even_once_the_technique_is_taken()
    {
        MusicCoreFacade facade = Drafted();
        Call(facade, "drafts.takeSure", $$"""{{{Place}}, "now": "2026-11-20T10:10:00Z"}""");

        JsonElement techniques = Call(facade, "drafts.techniques", $$"""{{{Place}}}""");

        JsonElement occurrence = techniques.GetProperty("techniques")[0].GetProperty("occurrences")[0];
        Assert.Equal(("v_0001", "taken"), (techniques.GetProperty("base").GetString(), occurrence.GetProperty("state").GetString()));
        JsonElement inBase = occurrence.GetProperty("inBase");
        Assert.Equal((2160, 2880), (inBase.GetProperty("onset").GetInt32(), inBase.GetProperty("end").GetInt32()));
        Assert.Equal("v_0001", Call(facade, "drafts.open", """{"draft": "d_0001", "variant": "v_0001"}""").GetProperty("variant").GetString());
    }

    [Fact]
    public void Sources_give_each_draft_how_many_of_its_techniques_are_taken()
    {
        MusicCoreFacade facade = Drafted();
        static JsonElement Tally(JsonElement sources) => sources.GetProperty("drafts")[0].GetProperty("techniques");

        Assert.Equal((0, 1), (Tally(Call(facade, "sources.layers")).GetProperty("taken").GetInt32(), Tally(Call(facade, "sources.layers")).GetProperty("total").GetInt32()));
        JsonElement taken = Tally(Call(facade, "drafts.takeSure", $$"""{{{Place}}, "now": "2026-11-20T10:10:00Z"}"""));
        Assert.Equal((1, 1), (taken.GetProperty("taken").GetInt32(), taken.GetProperty("total").GetInt32()));
    }

    [Fact]
    public void What_is_not_there_or_left_to_do_is_refused()
    {
        MusicCoreFacade facade = Drafted();

        Assert.Equal("badRequest", Error(facade, "drafts.take", $$"""{{{Place}}, "technique": 7}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.take", """{"draft": "d_0001", "place": "pl_9", "technique": 1}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.leave", $$"""{{{Place}}}"""));
        Assert.Equal("unavailable", Error(facade, "drafts.undo", """{"draft": "d_0001"}"""));
        Assert.Equal("unavailable", Error(facade, "drafts.take", $$"""{{{Place}}, "technique": 1, "bar": 2}"""));
        Call(facade, "drafts.takeSure", $$"""{{{Place}}, "now": "2026-11-20T10:10:00Z"}""");
        Assert.Equal("unavailable", Error(facade, "drafts.takeSure", $$"""{{{Place}}}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.takeFrom", $$"""{{{Place}}, "variant": "v_0001"}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.takeFrom", $$"""{{{Place}}, "variant": "v_0002", "span": {"onset": 9000, "end": 9600} }"""));
    }
}