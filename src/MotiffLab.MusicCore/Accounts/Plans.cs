namespace MotiffLab.MusicCore.Accounts;

/// <summary>What a player's account allows (decision record 0018, section 12).</summary>
public enum Plan
{
    /// <summary>Everything that runs on the device. The free tier has no cloud account and sends nothing anywhere.</summary>
    Free,

    /// <summary>Also what needs cloud infrastructure or an AI model.</summary>
    Pro,
}

/// <summary>The features that need Pro. Everything else (facts, Try chips, variations, Piece DNA, marks, playback) is free.</summary>
public enum ProFeature
{
    /// <summary>Reading a page of printed music on Motiff Lab's server (0011, 0014).</summary>
    Scan,

    /// <summary>Hearing the notes of a recording on Motiff Lab's server (0014).</summary>
    Transcribe,

    /// <summary>Saving projects online through the Motiff Lab API (0014).</summary>
    SaveOnline,

    /// <summary>The assistant (0018, section 7).</summary>
    Assistant,
}

/// <summary>A player's plan as the servers see it: the plan in force, and whether a tester chose it.</summary>
/// <param name="Plan">The plan every Pro feature is checked against.</param>
/// <param name="IsTester">The account may switch between Pro and no Pro to test both (preparation item 14).</param>
/// <param name="Chosen">The plan a tester asked for with <see cref="Plans.Header"/>; null when none was asked for or the caller is no tester.</param>
public sealed record PlanState(Plan Plan, bool IsTester, Plan? Chosen)
{
    public bool Allows(ProFeature feature) => Plans.Allows(Plan, feature);
}

/// <summary>
/// The Pro rule (0018, section 12) and how the servers read it from a sign-in. The account's plan is an app role of the
/// Motiff Lab API in the External ID tenant, so it arrives in the access token's <c>roles</c> claim and every server
/// checks it without a database: <see cref="ProRole"/> for Pro, <see cref="TesterRole"/> for a tester. A tester may
/// send <see cref="Header"/> with <c>pro</c> or <c>free</c> to test either mode; for anyone else the header is ignored.
/// Payment comes later and will grant the Pro role.
/// </summary>
/// <remarks>The scan server (Python) applies the same rule with the same names (<c>services/omr/app/auth.py</c>).</remarks>
public static class Plans
{
    /// <summary>The app role that makes an account Pro.</summary>
    public const string ProRole = "Pro";

    /// <summary>The app role of a tester, who may switch between Pro and no Pro.</summary>
    public const string TesterRole = "Tester";

    /// <summary>The request header a tester's choice travels in: <c>pro</c> or <c>free</c>.</summary>
    public const string Header = "Motiff-Plan";

    /// <summary>Every feature that needs Pro, in the order the app lists them.</summary>
    public static IReadOnlyList<ProFeature> Features { get; } = [ProFeature.Scan, ProFeature.Transcribe, ProFeature.SaveOnline, ProFeature.Assistant];

    /// <summary>
    /// How many pieces a free account keeps online (0025, section 8; Andrei, 2026-11-11): a new piece beyond them stays
    /// on the device, and the pieces already online keep syncing. Pro has no such limit.
    /// </summary>
    public const int FreeProjectsOnline = 300;

    /// <summary>Whether the plan allows the feature: Pro allows every feature, and the free tier none of them.</summary>
    public static bool Allows(Plan plan, ProFeature feature) =>
        Enum.IsDefined(feature) ? plan == Plan.Pro : throw new ArgumentOutOfRangeException(nameof(feature), feature, "Not a Pro feature.");

    /// <summary>
    /// The plan in force for a sign-in with these app roles and, for a tester, this choice. A tester without a choice
    /// has the plan of their roles; anyone else's choice changes nothing.
    /// </summary>
    public static PlanState Resolve(IEnumerable<string> roles, string? choice)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var held = new HashSet<string>(roles, StringComparer.Ordinal);
        Plan own = held.Contains(ProRole) ? Plan.Pro : Plan.Free;
        bool tester = held.Contains(TesterRole);
        Plan? chosen = tester ? Parse(choice) : null;
        return new PlanState(chosen ?? own, tester, chosen);
    }

    /// <summary><c>pro</c> or <c>free</c>, in any case; null for anything else.</summary>
    public static Plan? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "pro" => Plan.Pro,
        "free" => Plan.Free,
        _ => null,
    };

    /// <summary>The plan's name in JSON and in <see cref="Header"/>: <c>pro</c>, <c>free</c>.</summary>
    public static string Id(Plan plan) => plan switch
    {
        Plan.Pro => "pro",
        Plan.Free => "free",
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, null),
    };

    /// <summary>The feature's name in JSON: <c>scan</c>, <c>transcribe</c>, <c>saveOnline</c>, <c>assistant</c>.</summary>
    public static string Id(ProFeature feature) => feature switch
    {
        ProFeature.Scan => "scan",
        ProFeature.Transcribe => "transcribe",
        ProFeature.SaveOnline => "saveOnline",
        ProFeature.Assistant => "assistant",
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
    };
}