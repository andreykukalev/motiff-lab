using MotiffLab.MusicCore.Accounts;

namespace MotiffLab.MusicCore.Tests.Accounts;

public sealed class PlansTests
{
    [Fact]
    public void Pro_allows_every_Pro_feature_and_the_free_tier_only_saving_online()
    {
        Assert.All(Plans.Features, feature => Assert.True(Plans.Allows(Plan.Pro, feature)));
        Assert.All(Plans.Features, feature => Assert.Equal(feature == ProFeature.SaveOnline, Plans.Allows(Plan.Free, feature)));
        Assert.Equal(300, Plans.FreeProjectsOnline);
        Assert.Equal(Enum.GetValues<ProFeature>(), Plans.Features);
    }

    [Theory]
    [InlineData(new string[0], null, Plan.Free)]
    [InlineData(new[] { "Pro" }, null, Plan.Pro)]
    [InlineData(new[] { "pro" }, null, Plan.Free)]
    [InlineData(new[] { "Pro" }, "free", Plan.Pro)]
    [InlineData(new string[0], "pro", Plan.Free)]
    public void A_player_who_is_no_tester_has_the_plan_of_their_roles(string[] roles, string? choice, Plan expected)
    {
        PlanState state = Plans.Resolve(roles, choice);
        Assert.Equal(expected, state.Plan);
        Assert.False(state.IsTester);
        Assert.Null(state.Chosen);
    }

    [Theory]
    [InlineData(new[] { "Tester" }, null, Plan.Free, null)]
    [InlineData(new[] { "Tester", "Pro" }, null, Plan.Pro, null)]
    [InlineData(new[] { "Tester" }, "pro", Plan.Pro, Plan.Pro)]
    [InlineData(new[] { "Tester", "Pro" }, "free", Plan.Free, Plan.Free)]
    [InlineData(new[] { "Tester", "Pro" }, " FREE ", Plan.Free, Plan.Free)]
    [InlineData(new[] { "Tester", "Pro" }, "gold", Plan.Pro, null)]
    public void A_tester_has_the_plan_they_chose(string[] roles, string? choice, Plan expected, Plan? chosen)
    {
        PlanState state = Plans.Resolve(roles, choice);
        Assert.Equal(expected, state.Plan);
        Assert.True(state.IsTester);
        Assert.Equal(chosen, state.Chosen);
        Assert.Equal(expected == Plan.Pro, state.Allows(ProFeature.Assistant));
        Assert.True(state.Allows(ProFeature.SaveOnline));
    }

    [Fact]
    public void Names_in_JSON_are_fixed()
    {
        Assert.Equal(["scan", "transcribe", "saveOnline", "assistant"], Plans.Features.Select(Plans.Id));
        Assert.Equal(("pro", "free"), (Plans.Id(Plan.Pro), Plans.Id(Plan.Free)));
        Assert.Equal(Plan.Pro, Plans.Parse(Plans.Id(Plan.Pro)));
        Assert.Equal(Plan.Free, Plans.Parse(Plans.Id(Plan.Free)));
    }
}