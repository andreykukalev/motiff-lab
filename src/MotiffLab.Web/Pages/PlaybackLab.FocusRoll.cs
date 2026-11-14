namespace MotiffLab.Web.Pages;

/// <summary>
/// The roll in phrase focus (testing item 12): Notes | Piano roll in the focus bar shows the passage in the card as notation
/// or as a roll laid across the card and bound to the passage (#521 19): it moves only within it, and nothing on it selects,
/// edits or steps to another phrase. Expand on the roll drawer's head goes there, only with a selection (without one it is
/// dimmed and says why), and its way back is Collapse (#521 17), which opens the drawer again as it was; the choice of
/// Piano roll is kept for the session.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>Whether phrase focus shows the phrase as a roll; kept until the page is left.</summary>
    private bool _focusRoll;

    /// <summary>Whether the roll drawer was open when Expand took the phrase into focus, so Back opens it again.</summary>
    private bool _rollBeforeFocus;

    /// <summary>Whether Expand was opened from the roll, so its way back is Collapse rather than Back to page (#521 17).</summary>
    private bool _focusFromRoll;

    /// <summary>Whether the card shows the phrase as a roll now; the drawer's roll is not shown with it.</summary>
    private bool FocusShowsRoll => Focused && _focusRoll;

    /// <summary>Notes | Piano roll in the focus bar.</summary>
    private void ShowFocusAs(bool roll)
    {
        _focusRoll = roll;
        _pointed = null;
    }

    /// <summary>Expand on the roll drawer's head: the selection in focus as a roll; without a selection it does nothing (#521 19.1).</summary>
    private async Task ExpandRollAsync()
    {
        if (_selection is null || _penOn)
        {
            return;
        }

        _focusRoll = true;
        _rollBeforeFocus = _rollOpen;
        _rollOpen = false;
        _pointed = null;
        await EnterFocusAsync(fromRoll: true);
    }

    /// <summary>Back from focus opens the roll drawer again when Expand closed it.</summary>
    private void ReopenRollAfterFocus()
    {
        if (_rollBeforeFocus)
        {
            _rollBeforeFocus = false;
            _rollOpen = true;
        }
    }
}