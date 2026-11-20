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
    public void The_techniques_are_labelled_in_the_language_of_the_piece()
    {
        JsonElement technique = Call(Drafted(), "drafts.techniques", $$"""{{{Place}}}""").GetProperty("techniques")[0];

        Assert.Equal(("simplify", "упрощено", true), (technique.GetProperty("kind").GetString(), technique.GetProperty("label").GetString(), technique.GetProperty("sure").GetBoolean()));
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