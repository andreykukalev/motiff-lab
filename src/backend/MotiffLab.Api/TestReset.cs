using System.Globalization;
using System.Text.RegularExpressions;

namespace MotiffLab.Api;

/// <summary>
/// «Очистить всё» for a new testing cycle (Andrei, 2026-11-13; infra/README.md, "A fresh testing cycle"), only in the
/// develop deployment. <c>POST /me/reset</c> deletes everything the player keeps in the test database and records a reset
/// stamp in their partition; the list of projects then carries the stamp in the <see cref="Header"/> header, so each of
/// the player's devices clears its own test library at its next sync and sends nothing from before it. A save that says
/// it saw an older stamp (<see cref="SeenHeader"/>) is refused with the code "reset". The players' API (main) maps none of
/// this: the endpoint is 404 there, and no header is ever sent.
/// </summary>
internal static partial class TestReset
{
    /// <summary>The player's last reset stamp, on the list of projects and on a refused save.</summary>
    public const string Header = "Motiff-Reset";

    /// <summary>The stamp a device last applied (0 for none), on a save; absent from apps before «Очистить всё».</summary>
    public const string SeenHeader = "Motiff-Reset-Seen";

    /// <summary>
    /// True only when the API's database is develop's own: the account <c>cosmos-motifflab-dev-&lt;8 characters&gt;</c>
    /// that infra/dev-env.bicep makes and infra/azure/reset-dev-data.ps1 also insists on. The players' account is
    /// <c>cosmos-motifflab-&lt;subscription&gt;</c>, so the players' API never resets anything.
    /// </summary>
    public static bool IsEnabled(IConfiguration config) =>
        Uri.TryCreate(config["Cosmos:Endpoint"], UriKind.Absolute, out Uri? endpoint)
        && endpoint.Scheme == Uri.UriSchemeHttps
        && DevelopAccount().IsMatch(endpoint.Host);

    /// <summary>A new stamp, later than the one before: the server's time in milliseconds since 1970.</summary>
    public static long Next(long? previous, DateTimeOffset now) => Math.Max(now.ToUnixTimeMilliseconds(), (previous ?? 0) + 1);

    /// <summary>The stamp in a header's value; null when it is absent or not a whole number from 0.</summary>
    public static long? Parse(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long stamp) ? stamp : null;

    public static void MapTestReset(this IEndpointRouteBuilder app)
    {
        if (!IsEnabled(app.ServiceProvider.GetRequiredService<IConfiguration>()))
        {
            return;
        }

        app.MapPost("/me/reset", async (HttpContext http, IProjectStore store, CancellationToken cancel) =>
        {
            string owner = Player.Id(http.User);
            long stamp = Next(await store.GetResetAsync(owner, cancel), DateTimeOffset.UtcNow);
            int deleted = await store.ResetAsync(owner, stamp, cancel);
            http.Response.Headers[Header] = stamp.ToString(CultureInfo.InvariantCulture);
            return Results.Ok(new { deletedProjects = deleted, reset = stamp });
        }).RequireAuthorization(Player.Policy);
    }

    /// <summary>Adds the player's stamp to the answer, when resets are on here and the player has one.</summary>
    public static async Task<long?> AddStampAsync(HttpContext http, IProjectStore store, bool enabled, CancellationToken cancel)
    {
        if (!enabled || await store.GetResetAsync(Player.Id(http.User), cancel) is not { } stamp)
        {
            return null;
        }

        http.Response.Headers[Header] = stamp.ToString(CultureInfo.InvariantCulture);
        return stamp;
    }

    /// <summary>
    /// True when the save comes from a device that has not applied the player's latest reset yet: it says which stamp it
    /// saw, and that one is older. A save without the header (an app before «Очистить всё») is never refused.
    /// </summary>
    public static async Task<bool> IsStaleAsync(HttpContext http, IProjectStore store, bool enabled, CancellationToken cancel)
    {
        if (!enabled || Parse(http.Request.Headers[SeenHeader].ToString()) is not { } seen)
        {
            return false;
        }

        return await AddStampAsync(http, store, enabled, cancel) is { } stamp && stamp > seen;
    }

    [GeneratedRegex(@"^cosmos-motifflab-dev-[a-z0-9]{8}\.documents\.azure\.com$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DevelopAccount();
}