using Microsoft.JSInterop;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The workspace's two visibilities (#582 item 10) over practice view (#533 11, Scripts/layout/practice-view.ts), each changed
/// without the other: the selection controls (the selection's menu and its hints: cleared · Undo, how to hold) and the
/// playback controls (practice view as it was: the pill, the hint back to the playing and the blue playhead of the
/// selection). The bar's «Практика» and F hide both in one press and show both again while either is hidden; the chevron
/// lists the two, each on its own; the selection menu's Hide hides the selection controls only; the controls' Leave shows
/// the playback controls again, and Escape in practice view shows both. The notes, the selection's band and the black
/// playhead stay, and with the playback controls hidden a tap still places the playhead and shows Play/Pause and Leave for
/// a few seconds (the script times them). Nothing else changes: playback, the loop, the selection and the position go on. Not saved, not a step of
/// the selection's history.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The playback controls hidden: practice view.</summary>
    private bool _practice;

    /// <summary>The selection controls hidden (#582 item 10).</summary>
    private bool _selectionHidden;

    private IJSObjectReference? _practiceView;

    /// <summary>What the script was last told: whether practice view is on.</summary>
    private bool? _practiceShown;

    /// <summary>Practice view in force: only on the score page itself, not in Expand or while comparing, which have their own bars.</summary>
    private bool Practicing => _practice && !Focused && !_assembling;

    /// <summary>
    /// The selection controls visible (#582 item 10): the selection's menu and its hints, whatever the playback controls do.
    /// In Expand and while comparing they are as normal, as those have their own bars.
    /// </summary>
    private bool SelectionControlsShown => !_selectionHidden || Focused || _assembling;

    /// <summary>
    /// The bar's «Практика» lit, the kit's active state (#543 7): while both groups show, the normal workspace; dark while
    /// either is hidden. Read from the same two states every way in and out changes, as the iPad's PracticeView.lit.
    /// </summary>
    private bool PracticeLit => !Practicing && SelectionControlsShown;

    /// <summary>The bar's «Практика» and F: both hidden from the normal workspace, else both shown; nothing else changes.</summary>
    private async Task TogglePracticeAsync()
    {
        bool hide = !_practice && !_selectionHidden;
        SetSelectionControls(!hide);
        await SetPlaybackControlsAsync(!hide);
    }

    /// <summary>The selection controls shown or hidden on their own (the bar's list, the menu's Hide); the playback controls stay as they are.</summary>
    private void SetSelectionControls(bool shown)
    {
        _selectionHidden = !shown;
        if (!shown)
        {
            // The menu's More goes with the menu; the selection stays.
            _menuMore = false;
        }
    }

    /// <summary>The playback controls shown or hidden on their own (the bar's list, the controls' Leave); the selection controls stay as they are.</summary>
    private async Task SetPlaybackControlsAsync(bool shown)
    {
        _practice = !shown;
        if (!shown)
        {
            // The pill's popovers go with the pill; playback and the loop stay.
            _pillMenu = PillMenu.None;
        }

        await SyncPracticeAsync();
    }

    /// <summary>F hides and shows both groups, and Escape in practice view shows both (practice-view.ts practiceKey).</summary>
    [JSInvokable]
    public async Task OnPracticeKeyAsync(bool leave)
    {
        if (_score is null || Focused || _assembling || (leave && !_practice))
        {
            return;
        }

        // F is the bar's press; Escape, only while practice view is on, is too: it shows both groups again, as on the iPad.
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