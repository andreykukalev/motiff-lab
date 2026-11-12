using Microsoft.JSInterop;

using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Roll;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The modes and the rules of the selection in version 0.5 (the experiment Two playheads and modes, Settings · Experimental;
/// product spec interaction-modes, «Правила выделения по режимам»). One group in the bar for the score and the roll:
/// Navigate, Select, Edit notes and Pen, also by the keys H, S, E and P. The selection goes by beats and changes only by
/// its handles, in any mode; a click never changes it but places the playhead on the beat under it; a drag that starts
/// inside it leaves it alone; only Select starts a new one with a drag, and Shift and a click there extend it; only ✕ in
/// its menu or Escape removes it. Off, the score behaves as before.
/// </summary>
public partial class PlaybackLab
{
    private const string PlayheadsSetting = "experimental.playheads";

    /// <summary>The mode chosen in the bar, other than Pen: Pen is the pencil being on (<see cref="_penOn"/>).</summary>
    private InteractionMode _mode = InteractionMode.Navigate;

    private IJSObjectReference? _modeKeys;

    /// <summary>Whether the keys of the modes were last told they work.</summary>
    private bool? _modeKeysOn;

    /// <summary>The beat lines of <see cref="_beatLinesOf"/>, in ticks, which the selection on the score snaps to.</summary>
    private long[]? _beatLines;

    private PlaybackScore? _beatLinesOf;

    /// <summary>Whether the experiment is on.</summary>
    private bool PlayheadsOn => SettingsStore.IsOn(PlayheadsSetting);

    /// <summary>The mode in force: Pen while the pencil is on.</summary>
    private InteractionMode Mode => _penOn ? InteractionMode.Pen : _mode;

    /// <summary>The mode the score is told, or null while the experiment is off, which keeps the score's rules as they were.</summary>
    private string? ScoreMode => PlayheadsOn ? Mode.Name() : null;

    /// <summary>music-core's beat lines of the piece (its playback score), while the experiment is on.</summary>
    private IReadOnlyList<long>? ScoreBeatLines
    {
        get
        {
            if (!PlayheadsOn || _score is null)
            {
                return null;
            }

            if (!ReferenceEquals(_beatLinesOf, _score))
            {
                _beatLinesOf = _score;
                _beatLines = [.. _score.Beats.Select(beat => beat.At.Value)];
            }

            return _beatLines;
        }
    }

    /// <summary>A mode chosen in the bar or by its key: Pen turns the pencil on, any other turns it off.</summary>
    private async Task ChooseModeAsync(InteractionMode mode)
    {
        if (mode == InteractionMode.Pen)
        {
            if (!_penOn && _scoreView?.CanDraw == true)
            {
                await TogglePenAsync();
            }

            return;
        }

        _mode = mode;
        if (_penOn)
        {
            await TogglePenAsync();
        }
    }

    /// <summary>H, S, E or P (Scripts/layout/mode-keys.ts).</summary>
    [JSInvokable]
    public async Task OnModeKeyAsync(string name)
    {
        if (PlayheadsOn && !Focused && _score is not null && InteractionModes.FromName(name) is { } mode)
        {
            await ChooseModeAsync(mode);
            StateHasChanged();
        }
    }

    /// <summary>Escape removes the selection, unless it closes something else first: a popover, a card, focus.</summary>
    [JSInvokable]
    public async Task OnEscapeKeyAsync()
    {
        if (PlayheadsOn && _selection is not null && !Focused && _popover == Popover.None && _pillMenu == PillMenu.None && _bookmarkDraft is null && _noteDraft is null)
        {
            await CloseSelectionAsync();
            StateHasChanged();
        }
    }

    /// <summary>✕ in the selection's menu, or Escape.</summary>
    private Task CloseSelectionAsync() => ClearSelectionAsync();

    /// <summary>Beats chosen on the score: a drag across them in Select, or a handle moved; music-core makes them a passage.</summary>
    private Task OnScoreSpanChosenAsync((Tick Start, Tick End, bool ByHandle) chosen) => SelectBeatsAsync(chosen.Start, chosen.End);

    /// <summary>The beats from <paramref name="start"/> to <paramref name="end"/> selected, kept to the bars a selection holds.</summary>
    private Task SelectBeatsAsync(Tick start, Tick end)
    {
        if (_meter is null || _score is null || end <= start)
        {
            return Task.CompletedTask;
        }

        Tick last = new(end.Value - 1);
        TickSpan span = MotifSelector.Drag(_meter, start, last, _score.End);
        bool capped = _meter.BarAt(last).Number - _meter.BarAt(start).Number + 1 > MotifSelection.MaxBars;
        return SelectAsync(span, capped ? Text.Format("selection.dragCapped", MotifSelection.MaxBars) : null);
    }

    /// <summary>
    /// A click on a beat of the score: Shift and a click in Select extend the selection to it, as its nearer handle would;
    /// any other click places the playhead there and leaves the selection as it is.
    /// </summary>
    private async Task OnScoreBeatClickedAsync((Tick Start, Tick End, bool Extend) beat)
    {
        if (beat.Extend && Mode == InteractionMode.Select && _selection is { } selection)
        {
            TickSpan span = selection.Span;
            await (beat.Start < span.Onset ? SelectBeatsAsync(beat.Start, span.End) : SelectBeatsAsync(span.Onset, beat.End > span.End ? beat.End : span.End));
            return;
        }

        await PlacePlayheadAsync(beat.Start);
    }

    /// <summary>The playhead put at a beat: while a loop holds it elsewhere, the loop lets it go.</summary>
    private async Task PlacePlayheadAsync(Tick at)
    {
        if (Player.Loop is { } loop && !loop.Contains(at))
        {
            await StopLoopingAsync();
        }

        await Player.SeekAsync(at);
    }

    /// <summary>A note clicked in Edit notes: the roll opens on it with the editor on and the note picked.</summary>
    private async Task OnScoreNoteToEditAsync(NotePoint point)
    {
        _rollOpen = true;
        _popover = Popover.None;
        _rollPoint = point;
        if (RollEditor is null)
        {
            return;
        }

        if (!NoteEditing)
        {
            await BeginNoteEditAsync();
        }

        _editPicked = _notes?.FirstOrDefault(note => note.Span.Onset.Value == point.Tick && (point.Pitch < 0 || note.Pitch.MidiNumber == point.Pitch))?.Id;
    }

    /// <summary>The keys of the modes work while the experiment is on (Scripts/layout/mode-keys.ts).</summary>
    private async Task SyncModeKeysAsync()
    {
        if (_modeKeys is null || _modeKeysOn == PlayheadsOn)
        {
            return;
        }

        _modeKeysOn = PlayheadsOn;
        try
        {
            await _modeKeys.InvokeVoidAsync("setOn", PlayheadsOn);
        }
        catch (JSException)
        {
            // Without the script the bar's buttons still choose the modes.
        }
    }
}