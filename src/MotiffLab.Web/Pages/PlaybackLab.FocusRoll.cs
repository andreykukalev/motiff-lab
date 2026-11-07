using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The roll in phrase focus (testing item 12): Notes | Piano roll in the focus bar shows the phrase in the card as notation or as
/// a roll laid across the card whole. It is a way of focusing, not another view: previous and next walk the phrases, the
/// loop, the tempo and the hands stay as they are, Back goes to the page. Expand on the roll drawer's head goes there, with
/// the phrase under the playhead when nothing is selected; the choice of Piano roll is kept for the session.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>Whether phrase focus shows the phrase as a roll; kept until the page is left.</summary>
    private bool _focusRoll;

    /// <summary>Whether the roll drawer was open when Expand took the phrase into focus, so Back opens it again.</summary>
    private bool _rollBeforeFocus;

    /// <summary>Whether the card shows the phrase as a roll now; the drawer's roll is not shown with it.</summary>
    private bool FocusShowsRoll => Focused && _focusRoll;

    /// <summary>Notes | Piano roll in the focus bar.</summary>
    private void ShowFocusAs(bool roll)
    {
        _focusRoll = roll;
        _pointed = null;
    }

    /// <summary>
    /// Expand on the roll drawer's head: the selection, or with none the phrase the playhead is in (the first when nothing has
    /// played), in focus as a roll.
    /// </summary>
    private async Task ExpandRollAsync()
    {
        if (_selection is null && _levels is not null && _levels.Units(_level).Count > 0)
        {
            PhraseUnit unit = _levels.At(_level, Player.Position);
            await SelectAsync(_levels.Selectable(unit), note: null);
        }

        if (_selection is null || _penOn)
        {
            return;
        }

        _focusRoll = true;
        _rollBeforeFocus = _rollOpen;
        _rollOpen = false;
        _pointed = null;
        EnterFocus();
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