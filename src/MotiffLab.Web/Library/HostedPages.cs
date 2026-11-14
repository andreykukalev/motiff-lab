using System.Globalization;

using MotiffLab.Web.Account;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Library;

/// <summary>
/// The pages Motiff Lab's hosted scan server read today for the signed-in player (version 0.5, «Импорт папки»): 1000 a day
/// (Andrei, 2026-11-13; 100 before), counted on this device by player and by local day, as on the iPad; the player's own
/// server has no limit. Kept only here.
/// </summary>
public sealed class HostedPages(Preferences preferences, CloudAccount account)
{
    /// <summary>The pages a day the hosted server reads for one player.</summary>
    public const int Limit = 1000;

    /// <summary>About how long the scan server takes for a page, in seconds, for the estimate shown before sending.</summary>
    public const int SecondsPerPage = 30;

    /// <summary>Pages read today; 0 when nobody is signed in.</summary>
    public async Task<int> UsedAsync() =>
        await KeyAsync() is { } key && int.TryParse(await preferences.GetAsync(key), NumberStyles.None, CultureInfo.InvariantCulture, out int used) ? used : 0;

    /// <summary>Pages that can still be read today.</summary>
    public async Task<int> LeftAsync() => Math.Max(0, Limit - await UsedAsync());

    /// <summary>Counts pages read now.</summary>
    public async Task AddAsync(int pages)
    {
        if (await KeyAsync() is { } key)
        {
            await preferences.SetAsync(key, (await UsedAsync() + pages).ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>About how many minutes the pages take: half a minute a page.</summary>
    public static int Minutes(int pages) => Math.Max(1, ((pages * SecondsPerPage) + 59) / 60);

    private async Task<string?> KeyAsync() => await account.PlayerAsync() is { } player
        ? $"scan.hostedPages.{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.{player}"
        : null;
}