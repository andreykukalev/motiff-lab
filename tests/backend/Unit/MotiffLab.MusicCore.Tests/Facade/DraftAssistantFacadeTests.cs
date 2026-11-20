using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The assistant in a draft through the facade (decision record 0034, section 6; the plan's answer 7): what a turn sends about
/// a place and nothing more, the place's tools, a proposal heard after and accepted as the assistant's step or a bridge, undone.
/// </summary>
public class DraftAssistantFacadeTests
{
    private const string Place = """ "draft": "d_0001", "place": "pl_0001" """;

    private const string Take = """ "tool": "place.take", "parameters": {"techniques": [1], "bar": null}, "role": "placeAssembler", "now": "2026-11-20T10:10:00Z" """;

    /// <summary>The example open in Russian with bars 1–4 as a draft's place and bars 5–8 as its variant: one sure приём, a simplification in bar 4.</summary>
    private static MusicCoreFacade Drafted()
    {
        var facade = new MusicCoreFacade();
        Json(facade.Open("FurElise.musicxml", FurElise(), """{"language": "ru", "id": "p_assist"}"""));
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 4}, "now": "2026-11-20T10:00:00Z"}""");
        Call(facade, "drafts.add", """{"bars": {"first": 5, "last": 8}, "draft": "d_0001", "drop": "variant", "place": "pl_0001", "now": "2026-11-20T10:05:00Z"}""");
        return facade;
    }

    private static byte[] FurElise() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));

    private static Draft Saved(MusicCoreFacade facade) => ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save"))).Drafts.Single();

    [Fact]
    public void The_context_holds_the_place_its_two_variants_notes_and_the_techniques_and_no_name_or_file()
    {
        MusicCoreFacade facade = Drafted();
        JsonElement context = Call(facade, "drafts.context", $$"""{{{Place}}}""").GetProperty("context");

        Assert.Equal(["place", "source.base", "source.variant", "technique.1"], context.GetProperty("digest").GetProperty("ids").EnumerateArray().Select(static id => id.GetString()));
        Assert.NotEmpty(context.GetProperty("notes").GetProperty("notes").EnumerateArray());
        Assert.NotEmpty(context.GetProperty("variantNotes").GetProperty("notes").EnumerateArray());
        string sent = context.GetRawText();
        Assert.DoesNotContain("FurElise", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("Elise", sent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("p_assist", sent, StringComparison.Ordinal);
        Assert.DoesNotContain(Saved(facade).Name, sent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_context_is_one_a_turn_sends()
    {
        JsonElement context = Call(Drafted(), "drafts.context", $$"""{{{Place}}}""").GetProperty("context");
        var request = new TurnRequest(AssistantRole.PlaceAssembler, "ru", context, [TurnEntry.Question("Собери место ① как на записи без импровизации")]);

        Assert.NotNull(TurnRequest.Read(JsonDocument.Parse(request.ToJson()).RootElement, out string? refusal));
        Assert.Null(refusal);
        Assert.Contains("technique.1", request.KnownIds);
    }

    [Fact]
    public void A_take_is_proposed_without_changing_the_draft_and_its_after_is_heard_as_a_view()
    {
        MusicCoreFacade facade = Drafted();

        JsonElement answer = Call(facade, "drafts.call", $$"""{{{Place}}, {{Take}}}""");

        JsonElement proposal = Assert.Single(answer.GetProperty("drafts").EnumerateArray());
        Assert.Equal((false, "pl_0001", "Взять приём 1"), (proposal.GetProperty("applied").GetBoolean(), proposal.GetProperty("place").GetString(), proposal.GetProperty("title").GetString()));
        JsonElement technique = Assert.Single(proposal.GetProperty("steps").EnumerateArray()).GetProperty("technique");
        Assert.Equal(("simplify", 4), (technique.GetProperty("kind").GetString(), Assert.Single(technique.GetProperty("bars").EnumerateArray()).GetInt32()));
        Assert.Empty(Saved(facade).Steps);
        JsonElement after = Call(facade, "drafts.proposal", $$"""{{{Place}}, {{Take}}}""");
        Assert.True(after.GetProperty("readOnly").GetBoolean());
        Assert.Empty(Saved(facade).Steps);
    }

    [Fact]
    public void Accepted_the_take_is_the_assistants_step_of_the_same_notes_as_the_players_and_undone_as_one()
    {
        MusicCoreFacade facade = Drafted();
        MusicCoreFacade player = Drafted();
        Call(player, "drafts.take", $$"""{{{Place}}, "technique": 1, "now": "2026-11-20T10:10:00Z"}""");

        JsonElement accepted = Call(facade, "drafts.accept", $$"""{{{Place}}, {{Take}}}""");

        Assert.Equal([1], accepted.GetProperty("steps").EnumerateArray().Select(static step => step.GetInt32()));
        DraftTake take = Assert.IsType<DraftTake>(Assert.Single(Saved(facade).Steps));
        DraftTake players = Assert.IsType<DraftTake>(Assert.Single(Saved(player).Steps));
        Assert.Equal(("assistant", (string?)null), (take.Origin, players.Origin));
        Assert.Equal(players.Removed, take.Removed);
        Assert.Equal(players.Added, take.Added);
        Call(facade, "drafts.undo", """{"draft": "d_0001"}""");
        Assert.Empty(Saved(facade).Steps);
    }

    [Fact]
    public void A_proposal_that_no_longer_holds_is_not_accepted()
    {
        MusicCoreFacade facade = Drafted();
        Call(facade, "drafts.take", $$"""{{{Place}}, "technique": 1, "now": "2026-11-20T10:08:00Z"}""");

        Assert.Equal("badRequest", Error(facade, "drafts.accept", $$"""{{{Place}}, {{Take}}}"""));
        Assert.Single(Saved(facade).Steps);
    }

    [Fact]
    public void A_role_without_the_tool_is_refused_as_the_relay_refuses_it()
    {
        JsonElement answer = Call(Drafted(), "drafts.call", $$"""{{{Place}}, "tool": "place.take", "parameters": {"techniques": [1], "bar": null}, "role": "explainer"}""");

        Assert.False(answer.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void A_bridge_is_proposed_accepted_as_a_place_between_and_undone()
    {
        var facade = new MusicCoreFacade();
        Json(facade.Open("FurElise.musicxml", FurElise(), """{"language": "ru", "id": "p_assist"}"""));
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 4}, "now": "2026-11-20T10:00:00Z"}""");
        Call(facade, "drafts.add", """{"bars": {"first": 5, "last": 8}, "draft": "d_0001", "drop": "place", "now": "2026-11-20T10:05:00Z"}""");
        Call(facade, "drafts.transpose", """{"draft": "d_0001", "place": "pl_0002", "semitones": 2, "now": "2026-11-20T10:10:00Z"}""");
        const string Second = """ "draft": "d_0001", "place": "pl_0002", "role": "seamChooser", "now": "2026-11-20T10:15:00Z" """;

        JsonElement candidates = Call(facade, "drafts.call", $$"""{{{Second}}, "tool": "place.bridges"}""").GetProperty("result").GetProperty("candidates");
        JsonElement accepted = Call(facade, "drafts.accept", $$"""{{{Second}}, "tool": "place.bridge", "parameters": {"kind": "bassWalk"} }""");

        Assert.InRange(candidates.GetArrayLength(), 2, 3);
        Assert.True(accepted.GetProperty("structure").GetBoolean());
        Draft draft = Saved(facade);
        Assert.Equal(3, draft.Places.Count);
        Assert.True(draft.Places[1].Bridge);
        Assert.Equal(BridgeKind.BassWalk, DraftBridges.KindOf(draft.Places[1].BaseVariant));
        Call(facade, "drafts.undo", """{"draft": "d_0001"}""");
        Assert.Equal(2, Saved(facade).Places.Count);
    }
}