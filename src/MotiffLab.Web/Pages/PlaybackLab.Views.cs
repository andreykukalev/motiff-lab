using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Pages;

/// <summary>
/// PDF | Notes keep each view as the player left it (#511 section 10): the PDF and the notes are two pages of the app, so the
/// "Notes" view is written to the device as the player leaves it, for the PDF, the library or anywhere else (the
/// selection, also a bar with no notes, where the score was scrolled to as a system and a share of it, where the music
/// stood and whether it looped, and the roll's drawer with its zoom and view), and brought back in place of the landing on
/// the PDF's page when the player comes back to it from the PDF ("&amp;view=notes"). Playback stops, as the PDF has its own
/// metronome. The landing by the PDF's pages stays for the first switch, when nothing was kept yet.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The handler that keeps the "Notes" view as the player leaves this page.</summary>
    private IDisposable? _leavingNotes;

    /// <summary>The roll's script, for its zoom and view kept beside the score.</summary>
    private IJSObjectReference? _rollModule;

    /// <summary>The place the score goes back to once its pages are laid out, if they were not yet; null for none.</summary>
    private ViewPlace? _placeToShow;

    /// <summary>
    /// The score's pages are held out of sight while they land (#533 6): from a PDF's page ("&amp;at=") or back to the view
    /// kept ("&amp;view=notes"), the place is reached only after the pages are drawn, and the top of the first page used to
    /// show on the way there.
    /// </summary>
    private bool _landing;

    /// <summary>The arrival (the resume point and the "Notes" view kept) is still being put in place.</summary>
    private bool _arriving;

    /// <summary>The score has told which pages it shows since the piece began to land.</summary>
    private bool _pagesSeen;

    /// <summary>A piece opens: held out of sight when it opens at a place, shown as drawn when not.</summary>
    private void StartLanding()
    {
        _landing = At is not null || View == ViewStates.NotesAsLeft;
        _arriving = _landing;
        _pagesSeen = false;
    }

    /// <summary>The arrival is in place: a view kept that the pages shown could not take yet is given up rather than waited for.</summary>
    private async Task EndArrivalAsync()
    {
        _arriving = false;
        if (_pagesSeen)
        {
            await ShowKeptPlaceAsync(last: true);
        }

        Land();
    }

    /// <summary>
    /// Shows the pages once nothing is left to bring them to: the arrival done, the view kept shown (or given up), and the
    /// PDF's page shown, or no page to show it on (the strip).
    /// </summary>
    private void Land()
    {
        bool placed = At is not { } at || at == _shownAt || (_pagesSeen && _pages.Count == 0);
        if (_landing && !_arriving && _placeToShow is null && placed)
        {
            _landing = false;
            StateHasChanged();
        }
    }

    private void WatchLeavingNotes() => _leavingNotes ??= Navigation.RegisterLocationChangingHandler(OnLeavingNotesAsync);

    /// <summary>Another page opens (not this one with another address): the "Notes" view is kept before it goes.</summary>
    private async ValueTask OnLeavingNotesAsync(LocationChangingContext context)
    {
        if (Navigation.ToAbsoluteUri(context.TargetLocation).AbsolutePath != new Uri(Navigation.Uri).AbsolutePath)
        {
            await KeepNotesViewAsync();
        }
    }

    /// <summary>Writes the "Notes" view of the project shown to the device; nothing before the score is shown.</summary>
    private async Task KeepNotesViewAsync()
    {
        if (_project is not { HasNotes: true } project || _scoreView is null || _score is null || CurrentPiece.Project?.Id != project.Id)
        {
            return;
        }

        try
        {
            ViewPlace? place = _placeToShow ?? await _scoreView.ViewPlaceAsync();
            RollFrame? roll = await (await RollModuleAsync()).InvokeAsync<RollFrame?>("keptRollFrame");
            TickSpan? span = _selection?.Span;
            bool loops = ModelHolds ? Player.SelectionLoop && span is not null : Looping;
            NotesView view = new(span?.Onset.Value, span?.End.Value, place, Player.Position.Value, loops, _rollOpen, roll);
            await Preferences.KeepNotesViewAsync(project.Id, view);
        }
        catch (JSException)
        {
            // The score's script is gone already: the view kept before stays.
        }
    }

    /// <summary>
    /// Back from the PDF ("&amp;view=notes"): the "Notes" view as it was left, in place of the resume point's passage. Nothing
    /// starts playing.
    /// </summary>
    private async Task ReturnToNotesViewAsync()
    {
        if (View != ViewStates.NotesAsLeft || _project is not { } project || _score is null || await Preferences.NotesViewAsync(project.Id) is not { } kept)
        {
            return;
        }

        if (kept.Roll is { } frame)
        {
            await (await RollModuleAsync()).InvokeAsync<bool>("restoreRollFrame", frame);
        }

        _rollOpen = kept.RollOpen;
        TickSpan? span = kept is { SelectionOnset: { } onset, SelectionEnd: { } end } && end > onset && end <= _score.End.Value
            ? TickSpan.FromBounds(new Tick(onset), new Tick(end))
            : null;
        await PutSelectionAsync(span);
        await ReturnToPositionAsync(kept);
        _placeToShow = kept.Place;
        await ShowKeptPlaceAsync(last: false);
    }

    /// <summary>Where the music stood, and Цикл as it was: under the playback model the model's, else the selection's loop.</summary>
    private async Task ReturnToPositionAsync(NotesView kept)
    {
        Tick at = new(Math.Clamp(kept.Position, 0, _score!.End.Value));
        if (ModelHolds)
        {
            await SyncModelAsync();
            if (Player.SelectionLoop != kept.Loop)
            {
                await ToggleCycleAsync();
            }

            await Player.MoveAsync(at);
            await FollowModelAsync();
            return;
        }

        if (kept.Loop && !Looping)
        {
            await ToggleLoopSelectionAsync();
        }

        await Player.SeekAsync(at);
    }

    /// <summary>The score goes back to the place kept, once its pages can be: at arrival, else when they are first shown.</summary>
    private async Task ShowKeptPlaceAsync(bool last)
    {
        if (_placeToShow is not { } place || _scoreView is null)
        {
            return;
        }

        if (await _scoreView.ShowPlaceAsync(place) || last)
        {
            _placeToShow = null;
        }
    }

    private async Task<IJSObjectReference> RollModuleAsync() =>
        _rollModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/roll/piano-roll.js");

    private async Task DisposeViewsAsync()
    {
        _leavingNotes?.Dispose();
        _leavingNotes = null;
        if (_rollModule is not null)
        {
            await _rollModule.DisposeAsync();
            _rollModule = null;
        }
    }
}