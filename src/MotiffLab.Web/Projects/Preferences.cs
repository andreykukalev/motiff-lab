using Microsoft.JSInterop;

namespace MotiffLab.Web.Projects;

/// <summary>
/// Preferences kept on the device (0012): how the workspace is laid out and whether Focus mode was on.
/// A thin handle on <c>Scripts/storage/preferences.ts</c>; blocked storage reads as nothing and
/// writes are dropped, so the app works the same without it. Nothing leaves the device.
/// </summary>
public sealed class Preferences(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task<string?> GetAsync(string key)
    {
        try
        {
            IJSObjectReference module = await ModuleAsync();
            return await module.InvokeAsync<string?>("getPreference", key);
        }
        catch (JSException)
        {
            return null;
        }
    }

    public async Task SetAsync(string key, string? value)
    {
        try
        {
            IJSObjectReference module = await ModuleAsync();
            await module.InvokeVoidAsync("setPreference", key, value);
        }
        catch (JSException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/storage/preferences.js");
}