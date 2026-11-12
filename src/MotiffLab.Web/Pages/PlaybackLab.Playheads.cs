using Microsoft.JSInterop;

using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Playback;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The two playheads of version 0.5 (the experiment Two playheads and modes, Settings · Experimental; product spec
/// two-playheads). One player, two places: the global playhead plays the piece from the bar's Play and never stops at the
/// selection; the selection playhead plays only inside the selection from Play first in its menu, round on Loop or once
/// without it, back at its start after. A click outside the selection places the global one and inside it the selection's;
/// Space plays the selection when there is one, else the piece; when the selection goes the global playhead takes the
/// selection playhead's place. In focus, playing stays as it was.
/// </summary>
public partial class PlaybackLab
{
    private const string PlayheadsFadeSetting = "experimental.playheadsFade";

    /// <summary>Loop in the selection's menu: the selection Play goes round, or plays once and stops.</summary>
    private bool _loopChosen = true;

    /// <summary>Where the global playhead waits while the selection has the player (<see cref="_looping"/>).</summary>
    private Tick _globalAt;

    /// <summary>Where the selection playhead waits while the piece has the player; null for the selection's start.</summary>
    private Tick? _selectionAt;

    /// <summary>Whether the two playheads hold: the experiment is on and the page is not in focus, which plays as before.</summary>
    private bool TwoPlayheads => PlayheadsOn && !Focused;

    /// <summary>Whether the global Play plays: the player plays and the selection does not have it.</summary>
    private bool GlobalPlaying => Player.State != PlaybackState.Stopped && !(TwoPlayheads && _looping);

    /// <summary>Whether the selection Play plays.</summary>
    private bool SelectionPlaying => TwoPlayheads && _looping && Player.State != PlaybackState.Stopped;

    /// <summary>Loop as its button shows it: the choice for the selection Play, or the loop in force before version 0.5.</summary>
    private bool LoopShown => TwoPlayheads ? _loopChosen : Looping;

    /// <summary>The selection playhead's place while the piece has the player: where it was left, inside the selection, else its start.</summary>
    private Tick? SelectionWaitsAt => _selection is not { } selection ? null
        : _selectionAt is { } at && selection.Span.Contains(at) ? at : selection.Span.Onset;

    /// <summary>The playheads the score draws, or null while the experiment is off (the one playhead as before) or in focus.</summary>
    private ScorePlayheads? Playheads => TwoPlayheads
        ? new ScorePlayheads(_looping, _globalAt.Value, _looping ? _selection?.Span.Onset.Value : SelectionWaitsAt?.Value, SettingsStore.IsOn(PlayheadsFadeSetting))
        : null;

    /// <summary>The bar's Play: the piece from the global playhead. While the selection has the player, the piece takes it back.</summary>
    private async Task ToggleGlobalPlayAsync()
    {
        if (!TwoPlayheads || !_looping)
        {
            await TogglePlayAsync();
            return;
        }

        await StopLoopingAsync();
        await PlayAsync();
    }

    /// <summary>Play first in the selection's menu, or Space with a selection: the selection from its playhead, or a pause.</summary>
    private async Task ToggleSelectionPlayAsync()
    {
        if (_selection is not { NoteIds.Count: > 0 } selection)
        {
            return;
        }

        if (_looping)
        {
            await TogglePlayAsync();
            return;
        }

        Tick from = SelectionWaitsAt ?? selection.Span.Onset;
        if (Player.State != PlaybackState.Stopped)
        {
            await Player.PauseAsync();
        }

        _looping = true;
        TickSpan range = await LoopSelectionAsync(selection);
        await Player.SeekAsync(range.Contains(from) ? from : range.Onset);
        await PlayAsync();
    }

    /// <summary>Loop in the selection's menu: whether the selection Play goes round; a selection playing now follows at once.</summary>
    private async Task ToggleLoopChosenAsync()
    {
        _loopChosen = !_loopChosen;
        if (_looping && _selection is { NoteIds.Count: > 0 } selection)
        {
            await LoopSelectionAsync(selection);
        }
    }

    /// <summary>Loop in the selection's menu: the choice for the selection Play, or the loop in force before version 0.5.</summary>
    private Task ToggleLoopShownAsync() => TwoPlayheads ? ToggleLoopChosenAsync() : ToggleLoopSelectionAsync();

    /// <summary>The selection plays once rather than round: Loop is off in its menu, outside focus.</summary>
    private bool SelectionPlaysOnce => TwoPlayheads && !_loopChosen;

    /// <summary>The global playhead's place is kept when the selection takes the player from the piece.</summary>
    private void KeepGlobalPlace()
    {
        if (Player.Loop is null)
        {
            _globalAt = Player.Position;
        }
    }

    /// <summary>
    /// The selection gives the player back to the piece: its playhead stays where the player was and the global one comes
    /// back to its place, stopped. Before version 0.5 (or in focus) the player just stops looping. Returns whether it did.
    /// </summary>
    private async Task<bool> HandBackAsync()
    {
        if (!TwoPlayheads || Player.Loop is null)
        {
            return false;
        }

        if (_selection is not null)
        {
            _selectionAt = Player.Position;
        }

        if (Player.State != PlaybackState.Stopped)
        {
            await Player.PauseAsync();
        }

        return true;
    }

    /// <summary>Where the global playhead goes when the selection is removed: the selection playhead's place, unless the piece plays.</summary>
    private Tick? HandOverAt => TwoPlayheads && _selection is not null && !GlobalPlaying ? (_looping ? Player.Position : SelectionWaitsAt) : null;

    /// <summary>The selection removed: the global playhead takes the place the selection playhead had (spec two-playheads, section 3).</summary>
    private async Task HandOverAsync(Tick? at)
    {
        _selectionAt = null;
        if (at is { } place)
        {
            await Player.SeekAsync(place);
        }
    }

    /// <summary>A click on a beat inside the selection places its playhead; anywhere else, the global one.</summary>
    private async Task PlacePlayheadAsync(Tick at)
    {
        if (_selection is { } selection && selection.Span.Contains(at))
        {
            _selectionAt = at;
            if (_looping)
            {
                await Player.SeekAsync(at);
            }

            return;
        }

        if (_looping)
        {
            _globalAt = at;
        }
        else
        {
            await Player.SeekAsync(at);
        }
    }

    /// <summary>Space outside the roll (Scripts/layout/mode-keys.ts): as the roll's Space.</summary>
    [JSInvokable]
    public async Task OnSpaceKeyAsync()
    {
        if (PlayheadsOn && _score is not null)
        {
            await PlayPauseAsync();
            StateHasChanged();
        }
    }

    /// <summary>Space under the two playheads: the selection Play with a selection of notes, else the global Play.</summary>
    private Task TwoPlayheadsSpaceAsync() =>
        _selection is { NoteIds.Count: > 0 } ? ToggleSelectionPlayAsync() : ToggleGlobalPlayAsync();
}