using System.Text.Json;

using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Assistant.Drafts;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Assistant;

/// <summary>
/// The assistant's tools in a draft (decision record 0034, section 6): they read the приёмы of the variant built from and
/// propose only the draft's own operations, the same notes the player's buttons give, as the assistant's steps in one group;
/// they refuse outside a draft, and each role has only its own.
/// </summary>
public class DraftToolsTests
{
    private static readonly Words Ru = Words.For("ru");

    /// <summary>A piece whose draft has eight bars of notes as its base and a recording of them with octaves in bars 2 and 6 and another bass in bar 4.</summary>
    private static DraftScope Scope()
    {
        SourceMusic score = Score();
        Project project = Project.Create("p_1", "a.mid", MidiExporter.Export(score.Notes, score.Tempos, score.Meter, "a"), At).WithNewDraft(Drafted(OctavesAndBass()));
        return DraftScope.Of(project, "d_1", "pl_1", null, At, Ru);
    }

    private static JsonElement Parameters(string json) => JsonDocument.Parse(json).RootElement;

    private static string Written(AssistantPiece piece, string tool, ToolResult result)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            AssistantTools.WriteResult(json, piece, tool, result);
            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    [Fact]
    public void The_techniques_of_the_variant_are_read_with_ids_to_cite()
    {
        DraftScope scope = Scope();

        ToolResult result = AssistantTools.Call(scope.Base, "place.techniques", Parameters("{}"));

        JsonElement read = JsonDocument.Parse(Written(scope.Base, "place.techniques", result)).RootElement.GetProperty("result");
        Assert.Equal(read.GetProperty("ids").EnumerateArray().Select(static id => id.GetString()), read.GetProperty("techniques").EnumerateArray().Select(static each => each.GetProperty("id").GetString()));
        Assert.Equal(2, read.GetProperty("techniques").GetArrayLength());
    }

    [Fact]
    public void A_take_of_two_is_the_players_take_of_each_marked_as_the_assistants_in_one_group()
    {
        DraftScope scope = Scope();
        Draft players = DraftTakes.Take(DraftTakes.Take(scope.Draft, "pl_1", 1, null, At), "pl_1", 2, null, At);

        DraftProposal proposal = AssistantTools.Call(scope.Base, "place.take", Parameters("""{"techniques": [1, 2], "bar": null}""")).Proposal!;

        Assert.Equal(players.ResultOf("pl_1").Music.Notes, proposal.After.ResultOf("pl_1").Music.Notes);
        Assert.Equal(2, proposal.Steps.Count);
        Assert.All(proposal.Steps, step => Assert.Equal(DraftProposal.Origin, step.Origin));
        Assert.Single(proposal.Steps.Select(static step => step.Group).Distinct());
        Assert.NotNull(proposal.Steps[0].Group);
        Assert.Empty(scope.Draft.Steps);
        Assert.Empty(proposal.After.Undone().Steps);
    }

    [Fact]
    public void The_same_call_proposes_the_same()
    {
        DraftScope scope = Scope();
        JsonElement take = Parameters("""{"techniques": [2], "bar": 4}""");

        Assert.Equal(Written(scope.Base, "place.take", AssistantTools.Call(scope.Base, "place.take", take)), Written(Scope().Base, "place.take", AssistantTools.Call(Scope().Base, "place.take", take)));
    }

    [Theory]
    [InlineData("""{"techniques": [9], "bar": null}""", "нет приёма 9")]
    [InlineData("""{"techniques": [1], "bar": 3}""", "не открыт в такте 3")]
    public void A_take_music_core_cannot_make_is_refused_in_words(string parameters, string reason)
    {
        ToolResult result = AssistantTools.Call(Scope().Base, "place.take", Parameters(parameters));

        Assert.Contains(reason, result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_place_of_more_than_32_bars_is_not_sent()
    {
        SourceMusic score = Score(bars: 33);
        Project project = Project.Create("p_1", "a.mid", MidiExporter.Export(score.Notes, score.Tempos, score.Meter, "a"), At)
            .WithNewDraft(Draft.Start("d_1", "Предложение", At, "pl_1", Variant("v_1", score, 1, 33)));

        Assert.Throws<ArgumentException>(() => DraftScope.Of(project, "d_1", "pl_1", null, At, Ru));
    }

    [Fact]
    public void Outside_a_draft_the_draft_tools_refuse()
    {
        AssistantPiece piece = Scope().Heard!;

        Assert.False(AssistantTools.Call(piece, "place.techniques", Parameters("{}")).Ok);
    }

    [Fact]
    public void Without_differing_keys_no_bridge_is_offered()
    {
        Assert.False(AssistantTools.Call(Scope().Base, "place.bridges", Parameters("{}")).Ok);
    }

    [Fact]
    public void Each_role_has_its_own_tools_and_the_piece_roles_none_of_the_draft()
    {
        Assert.DoesNotContain(AssistantRoles.ToolsOf(AssistantRole.Proposer), static tool => tool.Name.StartsWith("place.", StringComparison.Ordinal));
        Assert.DoesNotContain(AssistantRoles.ToolsOf(AssistantRole.Mentor), static tool => tool.Name.StartsWith("place.", StringComparison.Ordinal));
        Assert.True(AssistantRoles.Allows(AssistantRole.PlaceAssembler, "place.take"));
        Assert.False(AssistantRoles.Allows(AssistantRole.PlaceAssembler, "variation.simplify"));
        Assert.DoesNotContain(AssistantRoles.ToolsOf(AssistantRole.Explainer), static tool => tool.Kind != ToolKind.Read);
        Assert.True(AssistantRoles.Allows(AssistantRole.SeamChooser, "place.bridge"));
        Assert.Equal(Enum.GetValues<AssistantRole>(), AssistantRoles.Served);
    }

    [Fact]
    public void The_place_digest_names_its_sources_and_techniques_in_words_and_marks_them_inferred()
    {
        FactsDigest digest = FactsDigest.OfDraftPlace(Scope());

        Assert.Equal(["place", "source.base", "source.variant", "technique.1", "technique.2"], digest.Ids);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            digest.Write(json);
        }

        JsonElement items = JsonDocument.Parse(buffer.ToArray()).RootElement.GetProperty("items");
        JsonElement technique = items.EnumerateArray().First(static item => item.GetProperty("id").GetString() == "technique.1");
        Assert.Equal("inferred", technique.GetProperty("certainty").GetString());
        Assert.StartsWith("Приём 1: ", technique.GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_proposal_is_a_draft_so_the_answer_about_it_need_cite_no_fact()
    {
        DraftScope scope = Scope();
        string written = Written(scope.Base, "place.take", AssistantTools.Call(scope.Base, "place.take", Parameters("""{"techniques": [1], "bar": null}""")));

        Assert.True(AnswerCheck.Drafted([new ToolOutcome("c1", "place.take", JsonDocument.Parse(written).RootElement)]));
    }
}