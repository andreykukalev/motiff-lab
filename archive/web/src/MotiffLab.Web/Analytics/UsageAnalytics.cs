using Microsoft.JSInterop;

using MotiffLab.MusicCore.Analytics;

namespace MotiffLab.Web.Analytics;

/// <summary>
/// Anonymous usage analytics with consent (#64). Off until the player turns them on in Settings;
/// turning them off forgets the device's random ID and the events not yet sent. Events come only
/// from <see cref="AnalyticsCatalogue"/>, so they never hold note content, file names or titles.
/// </summary>
/// <remarks>
/// This version has no analytics endpoint yet, so nothing leaves the device: events wait in memory
/// and Settings shows them, so the player sees exactly what would be sent.
/// </remarks>
public sealed class UsageAnalytics(IJSRuntime js)
{
    /// <summary>The most events held before the oldest are dropped.</summary>
    public const int MaxPending = 200;

    private const string ConsentKey = "motifflab.analytics";
    private const string DeviceKey = "motifflab.analytics.device";

    private readonly List<AnalyticsEvent> _pending = [];
    private Task? _loading;

    /// <summary>Raised after consent changes or an event is recorded.</summary>
    public event Action? Changed;

    /// <summary>Whether the player has turned analytics on.</summary>
    public bool On { get; private set; }

    /// <summary>A random ID for this browser, made when analytics are turned on, so a week of use can be counted; null while off.</summary>
    public string? DeviceId { get; private set; }

    /// <summary>The events recorded and not yet sent, oldest first.</summary>
    public IReadOnlyList<AnalyticsEvent> Pending => _pending;

    /// <summary>Reads the player's choice from this browser. Safe to call more than once.</summary>
    public Task LoadAsync() => _loading ??= LoadCoreAsync();

    public async Task SetOnAsync(bool on)
    {
        await LoadAsync();
        On = on;
        if (on)
        {
            DeviceId ??= Guid.NewGuid().ToString("N");
            await SetItemAsync(DeviceKey, DeviceId);
            await SetItemAsync(ConsentKey, "on");
        }
        else
        {
            DeviceId = null;
            _pending.Clear();
            await RemoveItemAsync(DeviceKey);
            await SetItemAsync(ConsentKey, "off");
        }

        Changed?.Invoke();
    }

    /// <summary>Records an event if the player has turned analytics on; otherwise does nothing.</summary>
    public async Task TrackAsync(AnalyticsEvent analyticsEvent)
    {
        ArgumentNullException.ThrowIfNull(analyticsEvent);
        await LoadAsync();
        if (!On)
        {
            return;
        }

        if (_pending.Count == MaxPending)
        {
            _pending.RemoveAt(0);
        }

        _pending.Add(analyticsEvent);
        Changed?.Invoke();
    }

    private async Task LoadCoreAsync()
    {
        On = await GetItemAsync(ConsentKey) == "on";
        DeviceId = On ? await GetItemAsync(DeviceKey) : null;
        if (On && DeviceId is null)
        {
            DeviceId = Guid.NewGuid().ToString("N");
            await SetItemAsync(DeviceKey, DeviceId);
        }
    }

    // Local storage can be blocked (private windows, strict settings); analytics then stay off.
    private async Task<string?> GetItemAsync(string key)
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", key);
        }
        catch (JSException)
        {
            return null;
        }
    }

    private async Task SetItemAsync(string key, string value)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", key, value);
        }
        catch (JSException)
        {
        }
    }

    private async Task RemoveItemAsync(string key)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", key);
        }
        catch (JSException)
        {
        }
    }
}