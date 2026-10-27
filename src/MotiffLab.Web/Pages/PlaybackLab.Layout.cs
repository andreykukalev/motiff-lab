using System.Globalization;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Roll;

namespace MotiffLab.Web.Pages;

/// <summary>
/// How the workspace is laid out (0012, batch 2): the score, the roll or both, with a divider between
/// them that the player drags or moves by keyboard; and Focus mode, which leaves the phrase, the loop
/// and the transport on the screen for playing at the instrument. The view and the divider are kept
/// on the device as preferences; Focus is not, so a reload comes back to the full workspace.
/// <c>Scripts/layout/workspace-layout.ts</c> measures and drags; the page owns every choice.
/// </summary>
public partial class PlaybackLab : IAsyncDisposable
{
    private const string ViewKey = "ml-view";
    private const string SplitKey = "ml-split";
    private const double DefaultSplit = 0.6;

    /// <summary>Which of the two views of the piece the canvas shows.</summary>
    private enum ViewMode
    {
        Score,
        Roll,
        Both,
    }

    private ViewMode _view = ViewMode.Both;

    /// <summary>The share of the canvas the score takes while both views show, a fifth to four fifths.</summary>
    private double _split = DefaultSplit;

    private bool _focus;
    private bool _fullscreen;

    private ElementReference _main;
    private ElementReference _canvas;
    private ElementReference _handle;
    private IJSObjectReference? _layoutModule;
    private IJSObjectReference? _layout;
    private DotNetObjectReference<PlaybackLab>? _self;
    private (ViewMode View, bool Focus, bool Divider)? _attached;

    private bool ShowScore => _view != ViewMode.Roll;

    private bool ShowRoll => _view != ViewMode.Score;

    /// <summary>Whether the divider is on the canvas: both views, and a score to show.</summary>
    private bool ShowDivider => _view == ViewMode.Both && ShownScore is not null;

    private (ViewMode Mode, string Label)[] ViewModes =>
    [
        (ViewMode.Score, Text["view.score"]),
        (ViewMode.Roll, Text["view.roll"]),
        (ViewMode.Both, Text["view.both"]),
    ];

    /// <summary>Reads the view and the divider the player left on this device; nothing read keeps the defaults.</summary>
    private async Task ReadLayoutPreferencesAsync()
    {
        if (Enum.TryParse(await Preferences.GetAsync(ViewKey), ignoreCase: false, out ViewMode view))
        {
            _view = view;
        }

        if (double.TryParse(await Preferences.GetAsync(SplitKey), NumberStyles.Float, CultureInfo.InvariantCulture, out double split))
        {
            _split = Math.Clamp(split, 0.2, 0.8);
        }
    }

    private Task SetViewAsync(ViewMode mode)
    {
        _view = mode;
        return Preferences.SetAsync(ViewKey, mode.ToString());
    }

    /// <summary>Focus mode: the header, the inspector and the phrase tools go, and the browser is asked for the whole screen.</summary>
    private async Task EnterFocusAsync()
    {
        _focus = true;
        _fullscreen = await InvokeLayoutAsync<bool>("enterFullscreen");
    }

    private async Task LeaveFocusAsync()
    {
        _focus = false;
        if (_fullscreen)
        {
            _fullscreen = false;
            await InvokeLayoutAsync<object?>("exitFullscreen");
        }
    }

    /// <summary>Called by the layout script when the player lets the divider go or moves it by keyboard.</summary>
    [JSInvokable]
    public Task OnSplitChanged(double share)
    {
        _split = Math.Clamp(share, 0.2, 0.8);
        return Preferences.SetAsync(SplitKey, _split.ToString("0.00", CultureInfo.InvariantCulture));
    }

    /// <summary>A click on a bar in the score selects it, and a Shift-click extends the selection to it, as on the roll (S1-M1).</summary>
    private Task OnScoreBarPickedAsync((Tick Start, bool Extend) bar) =>
        OnRangePickedAsync(new RollRange(bar.Start, bar.Start, true, bar.Extend));

    /// <summary>
    /// Attaches the layout script to the canvas once it is on the page, and again when the view or Focus
    /// changes, since the divider and the Focus bar come and go with them.
    /// </summary>
    private async Task SyncLayoutAsync()
    {
        if (_score is null)
        {
            await DetachLayoutAsync();
            return;
        }

        (ViewMode, bool, bool) key = (_view, _focus, ShowDivider);
        if (_attached == key)
        {
            return;
        }

        await DetachLayoutAsync();
        try
        {
            _self ??= DotNetObjectReference.Create(this);
            _layoutModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/layout/workspace-layout.js");
            _layout = await _layoutModule.InvokeAsync<IJSObjectReference>("attach", _main, _canvas, ShowDivider ? _handle : null, _self, _split);
            _attached = key;
        }
        catch (JSException)
        {
            // Without the script the canvas keeps its own heights; the page still works.
        }
    }

    private async Task DetachLayoutAsync()
    {
        _attached = null;
        if (_layout is { } layout)
        {
            _layout = null;
            try
            {
                await layout.InvokeVoidAsync("dispose");
                await layout.DisposeAsync();
            }
            catch (JSException)
            {
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    private async Task<T?> InvokeLayoutAsync<T>(string function)
    {
        try
        {
            _layoutModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/layout/workspace-layout.js");
            return await _layoutModule.InvokeAsync<T>(function);
        }
        catch (JSException)
        {
            return default;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await DetachLayoutAsync();
            if (_layoutModule is not null)
            {
                await _layoutModule.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The page is going away with the layout.
        }

        _self?.Dispose();
    }
}