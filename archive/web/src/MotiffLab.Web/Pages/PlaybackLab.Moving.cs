namespace MotiffLab.Web.Pages;

/// <summary>
/// The playhead moving (#419): the engine reports the position many times a second. The score and the rolls follow it in
/// the browser on their own, so the page draws itself again only when what it shows from the position changes: the bar
/// and beat (the pill, Split here), or at every report while the DNA map plays or a recording's wave is shown.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The bar and beat the page last drew itself again for.</summary>
    private (int Bar, int Beat)? _beatShown;

    private void OnPlayerMoved() => _ = InvokeAsync(() =>
    {
        (int Bar, int Beat)? beat = _meter?.ToPosition(Player.Position) is { } at ? (at.Bar, at.Beat) : null;
        if (beat != _beatShown || FollowsEveryMove)
        {
            _beatShown = beat;
            StateHasChanged();
        }
    });

    /// <summary>Whether something on the page draws the position itself: the DNA map's playhead, a recording's wave and time.</summary>
    private bool FollowsEveryMove => _dnaPlaying || _recordingShown?.Id == _assemblyShown;
}