using Microsoft.JSInterop;

using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Playback;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The playback model of version 0.5.2 on the web (#495 UX-10, the architect's design playback-model-v0.5.2, slice W1; the
/// experiment Two playheads and modes, Settings · Experimental). One position in the engine, two playheads on screen: the
/// black one is the position, the blue one the position held to the selection. One Play button in the pill, cobalt where
/// the selection plays (or, stopped, will play) and ink where the piece goes on, with the place as bar.beat; and Цикл with
/// the selection's span, which turns on with a new selection and, turned on, takes the position to its start. The script's
/// player owns the model (<see cref="Player"/>, <c>Scripts/playback/playback-owner.ts</c>); the page sends it the events and
/// keeps what is heard in step: while Цикл loops the selection, the selection's notes alone, else the piece. In focus, and
/// while versions are assembled, playing stays as it was.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>Цикл as the page last followed it, so a change the model made by itself (a tap outside the selection) is heard.</summary>
    private bool? _cycleFollowed;

    /// <summary>Whether the playback model holds: the experiment is on, the page not in focus and not assembling versions.</summary>
    private bool ModelHolds => PlayheadsOn && !Focused && !_assembling;

    /// <summary>The selection as the range the model plays: what plays for it, or its span when it holds no notes.</summary>
    private TickSpan? ModelRange => _selection is { } selection ? (selection.NoteIds.Count > 0 ? PlayRange(selection) : selection.Span) : null;

    /// <summary>The place the Play button names, as bar.beat: the position, or where Play will start.</summary>
    private string PlayPlace => _meter is null ? "" : BeatText(Player.PlayFrom ?? Player.Position);

    /// <summary>The selection's span as Цикл names it: its first and its last beat, as bar.beat.</summary>
    private (string First, string Last)? CycleSpan => _selection is { } selection && _meter is not null
        ? (BeatText(selection.Span.Onset), BeatText(new Tick(Math.Max(selection.Span.Onset.Value, selection.Span.End.Value - 1))))
        : null;

    private string BeatText(Tick tick)
    {
        MetricPosition at = _meter!.ToPosition(tick);
        return FormattableString.Invariant($"{at.Bar}.{at.Beat}");
    }

    /// <summary>The model holds or lets go as the page enters or leaves focus or the experiment is switched; holding, it takes the selection.</summary>
    private async Task SyncModelAsync()
    {
        if (!Player.IsReady || _score is null || ModelHolds == Player.ModelHeld)
        {
            return;
        }

        await Player.HoldModelAsync(ModelHolds);
        if (ModelHolds)
        {
            await Player.SetSelectionAsync(ModelRange);
            await Player.HoldLoopAsync();
            await FollowModelAsync();
        }
    }

    /// <summary>The one Play button, Space or a pedal (T1–T3); while notes are shown in another key, they are heard there.</summary>
    private async Task PressAsync()
    {
        if (Transposed is not null)
        {
            await TogglePlayAsync();
            return;
        }

        await SyncModelAsync();
        bool starts = Player.State == PlaybackState.Stopped;
        _pillKept = true;
        await Player.PressAsync();
        if (starts)
        {
            await TrackPlaybackAsync();
        }
    }

    /// <summary>Цикл in the pill (T7, T8): on, the position goes to the selection's start and it repeats; off, playback goes on past it.</summary>
    private async Task ToggleCycleAsync()
    {
        await SyncModelAsync();
        await Player.SetSelectionLoopAsync(!Player.SelectionLoop);
        await FollowModelAsync();
    }

    /// <summary>The selection changed (T9–T11): the model takes it, Цикл turning on with a new one, and what is heard follows.</summary>
    private async Task SelectInModelAsync()
    {
        await SyncModelAsync();
        await Player.SetSelectionAsync(ModelRange);
        if (_looping && Player.SelectionLoop && _selection is { NoteIds.Count: > 0 } selection && _answerLoop is null && !_scaleHeard)
        {
            // The new selection's notes loop in place of the old ones.
            await LoopSelectionAsync(selection);
            return;
        }

        await FollowModelAsync();
    }

    /// <summary>
    /// What is heard follows the model: while Цикл loops a selection of notes, those notes alone (and the variation as chosen),
    /// else the piece. Another owner of the loop (the answer, the circle's scale) keeps it until it ends.
    /// </summary>
    private async Task FollowModelAsync()
    {
        _cycleFollowed = Player.SelectionLoop;
        if (!ModelHolds || !Player.ModelHeld || _answerLoop is not null || _scaleHeard)
        {
            return;
        }

        bool loops = Player.SelectionLoop && _selection is { NoteIds.Count: > 0 };
        if (loops == _looping)
        {
            return;
        }

        if (loops)
        {
            _looping = true;
            await LoopSelectionAsync(_selection!);
            return;
        }

        _looping = false;
        _alternating = false;
        await Player.SelectSourcesAsync(_hear);
    }

    /// <summary>The model changed Цикл by itself (a tap outside the selection, from the score or the roll): what is heard follows.</summary>
    private Task FollowCycleAsync() => ModelHolds && _cycleFollowed != Player.SelectionLoop ? FollowModelAsync() : Task.CompletedTask;

    /// <summary>The model's part of looping the selection: its range, Цикл on, and the engine loops it unless the passes alternate.</summary>
    private async Task LoopInModelAsync(TickSpan range)
    {
        await Player.SetSelectionAsync(range);
        if (!Player.SelectionLoop)
        {
            await Player.SetSelectionLoopAsync(true);
            _cycleFollowed = true;
        }

        if (!_alternating)
        {
            await Player.HoldLoopAsync();
        }
    }

    /// <summary>The model's part of stopping a loop: another owner's loop ends, the model's comes back, and what is heard follows it.</summary>
    private async Task StopInModelAsync()
    {
        _looping = false;
        _alternating = false;
        _answerLoop = null;
        await Player.HoldLoopAsync();
        await Player.SelectSourcesAsync(_hear);
        await FollowModelAsync();
    }

    /// <summary>A click on a beat (T12): the black playhead goes there; out of the selection Цикл turns off.</summary>
    private async Task PlacePlayheadAsync(Tick at)
    {
        await SyncModelAsync();
        await Player.MoveAsync(at);
        await FollowModelAsync();
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
}