using Microsoft.JSInterop;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Practice view (#533 11, Scripts/layout/practice-view.ts): one state of the view that keeps the page clean while the player
/// plays. The bar's «Практика», the selection menu's Hide and F enter it; the same button, F, Escape and the controls' Leave
/// leave it. It hides the selection's menu, the pill, the blue playhead of the selection and the hints of the selection and
/// of playback; the notes, the selection's band and the black playhead stay, and a tap still places the playhead. A tap on
/// the score shows Play/Pause and Leave for a few seconds (the script times them). Nothing else changes: playback, the loop,
/// the selection and the position go on, so leaving shows them as they are. Not saved, not a step of the selection's history.
/// </summary>
public partial class PlaybackLab
{
    private bool _practice;

    private IJSObjectReference? _practiceView;

    /// <summary>What the script was last told: whether practice view is on.</summary>
    private bool? _practiceShown;

    /// <summary>Practice view in force: only on the score page itself, not in Expand or while comparing, which have their own bars.</summary>
    private bool Practicing => _practice && !Focused && !_assembling;

    /// <summary>The bar's «Практика» and the selection menu's Hide: in or out, and nothing else changes.</summary>
    private async Task TogglePracticeAsync()
    {
        _practice = !_practice;
        if (_practice)
        {
            // The pill's popovers and the menu's More go with the pill and the menu; the selection, playback and the loop stay.
            _pillMenu = PillMenu.None;
            _menuMore = false;
        }

        await SyncPracticeAsync();
    }

    /// <summary>F enters and leaves practice view, Escape leaves it (practice-view.ts practiceKey).</summary>
    [JSInvokable]
    public async Task OnPracticeKeyAsync(bool leave)
    {
        if (_score is null || Focused || _assembling || (leave && !_practice))
        {
            return;
        }

        await TogglePracticeAsync();
        StateHasChanged();
    }

    /// <summary>Tells the script whether practice view is on, when that changed.</summary>
    private async Task SyncPracticeAsync()
    {
        if (_practiceView is null || _practiceShown == Practicing)
        {
            return;
        }

        _practiceShown = Practicing;
        try
        {
            await _practiceView.InvokeVoidAsync("set", Practicing);
        }
        catch (JSException)
        {
            // Without the script the page shows as practice view does, without the timed controls; the bar's button leaves it.
        }
    }
}