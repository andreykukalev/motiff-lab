using System.Security.Claims;

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
}