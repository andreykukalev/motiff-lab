using System.Globalization;

using Microsoft.JSInterop;

namespace MotiffLab.Web.Account;

/// <summary>
/// «Очистить всё» on the web test site (Andrei, 2026-11-13; infra/README.md, "A fresh testing cycle"): develop's API
/// keeps the player's last reset stamp and sends it with the list of projects (<see cref="Header"/>). This browser keeps the
/// stamp it last applied (Scripts/storage/test-library.ts, outside the library's entries); a newer one means the player
/// cleared everything from another device, so this browser empties its own test library and loads again before it
/// syncs or sends anything. Its saves say which stamp it applied (<see cref="SeenHeader"/>), so the API refuses one from
/// before a reset. Only on develop's API (<see cref="OnlineProjects.IsDevelopApi"/>) and on the test site.
/// </summary>
public sealed class TestReset(IJSRuntime js)
{
    /// <summary>The player's last reset stamp, on the list of projects (src/MotiffLab.Api/TestReset.cs).</summary>
    public const string Header = "Motiff-Reset";

    /// <summary>The stamp this browser last applied, sent with a save.</summary>
    public const string SeenHeader = "Motiff-Reset-Seen";

    private IJSObjectReference? _module;

    /// <summary>The stamp of the answer, or null when it carries none.</summary>
    public static string? StampOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues(Header, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;

    /// <summary>The last stamp this browser applied, as the save sends it: "0" when none.</summary>
    public async Task<string> AppliedAsync()
    {
        try
        {
            return (await (await ModuleAsync()).InvokeAsync<long>("appliedReset")).ToString(CultureInfo.InvariantCulture);
        }
        catch (JSException)
        {
            return "0";
        }
    }

    /// <summary>Keeps the stamp of the reset this browser made itself, so it does not clear again at its next sync.</summary>
    public async Task RecordAsync(HttpResponseMessage response)
    {
        if (StampOf(response) is not { } stamp)
        {
            return;
        }

        try
        {
            await (await ModuleAsync()).InvokeVoidAsync("recordReset", stamp);
        }
        catch (JSException)
        {
            // Not kept: the next sync clears this library once more, which leaves it as empty as it is now.
        }
    }

    /// <summary>
    /// Clears this browser's test library when the answer's stamp is newer than the one applied here, keeps the stamp and
    /// loads the page again. True when it did; false anywhere but on the test site, or when there was nothing new.
    /// </summary>
    public async Task<bool> ApplyAsync(HttpResponseMessage response)
    {
        if (StampOf(response) is not { } stamp)
        {
            return false;
        }

        try
        {
            IJSObjectReference module = await ModuleAsync();
            if (!await module.InvokeAsync<bool>("applyReset", stamp))
            {
                return false;
            }

            await module.InvokeVoidAsync("reloadPage");
            return true;
        }
        catch (JSException)
        {
            return false;
        }
    }

    // The module stays for the page's life, as the page loads again after a clear.
    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/storage/test-library.js");
}