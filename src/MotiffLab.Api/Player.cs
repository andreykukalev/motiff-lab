using System.Security.Claims;

using MotiffLab.MusicCore.Accounts;

namespace MotiffLab.Api;

/// <summary>Who is calling: a player signed in through External ID, with the API's scope.</summary>
internal static class Player
{
    public const string Policy = "player";

    /// <summary>The delegated scope the web and iOS apps ask for.</summary>
    public const string Scope = "Projects.ReadWrite";

    public static bool HasScope(ClaimsPrincipal user) =>
        user.FindAll("scp").SelectMany(claim => claim.Value.Split(' ')).Contains(Scope, StringComparer.Ordinal);

    /// <summary>
    /// The player's object id in the tenant: stable for the account, the same from every app, and the partition
    /// all of their documents sit in.
    /// </summary>
    public static string Id(ClaimsPrincipal user) =>
        user.FindFirstValue("oid") ?? throw new InvalidOperationException("The token names no player (oid); the policy requires it.");

    /// <summary>
    /// The player's plan (redesign preparation item 14): the API's app roles in the token, and for a tester the plan
    /// asked for in <see cref="Plans.Header"/>.
    /// </summary>
    public static PlanState Plan(HttpContext http) =>
        Plans.Resolve(
            http.User.FindAll("roles").Select(claim => claim.Value),
            http.Request.Headers.TryGetValue(Plans.Header, out var choice) ? choice.ToString() : null);
}