using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Score;

using Microsoft.JSInterop;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The selection's Undo and Redo (#511 section 11): music-core's <see cref="SelectionHistory{T}"/> of the piece open now,
/// fed by every change of the selection the player makes, with the gesture that made it so that a hold, the selection
/// gone and a drag after it are one step. A clear or a replace shows the toast «Выделение снято · Вернуть» for six
/// seconds; Undo and Redo sit in the Select row of the mode bar and on ⌘Z and ⇧⌘Z. Undo neither scrolls nor zooms.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>How long the toast after a clear or a replace stays (the evaluation of #511, section 2).</summary>
    private static readonly TimeSpan s_selectionToastFor = TimeSpan.FromSeconds(6);

    /// <summary>How long the hint to hold stays.</summary>
    private static readonly TimeSpan s_holdHintFor = TimeSpan.FromSeconds(2);

    private SelectionHistory<TickSpan> _selectionHistory = new();

    /// <summary>The gesture the next change of the selection belongs to; null for a change of its own.</summary>
    private string? _selectionGesture;

    /// <summary>While Undo, Redo or a cancelled gesture puts a selection back: that is no step.</summary>
    private bool _historyQuiet;

    private bool _selectionToast;
    private bool _holdHint;
    private CancellationTokenSource? _selectionToastTimer;
    private CancellationTokenSource? _holdHintTimer;

    /// <summary>The device preference for Select's «По тактам / По долям»: a selection under the modes goes by beats unless "bars".</summary>
    private const string SelectByPreference = "ml-select-by";

    /// <summary>Whether a selection under the modes goes by beats (the default) or by bars, as last chosen on this device.</summary>
    private bool SelectsByBeats => _selectBy != "bars";

    private string? _selectBy;

    private bool _selectByRead;

    /// <summary>Reads the device's choice of bars or beats once, when the page has its scripts.</summary>
    private async Task ReadSelectByAsync()
    {
        if (_selectByRead)
        {
            return;
        }

        _selectByRead = true;
        _selectBy = await Preferences.GetAsync(SelectByPreference);
    }

    private async Task ChooseSelectByBeatsAsync(bool byBeats)
    {
        _selectBy = byBeats ? "beats" : "bars";
        await Preferences.SetAsync(SelectByPreference, _selectBy);
    }

    /// <summary>A new piece starts its own history.</summary>
    private void ForgetSelectionHistory()
    {
        _selectionHistory = new SelectionHistory<TickSpan>();
        _selectionGesture = null;
        _selectionToast = false;
    }

    /// <summary>The selection has changed from <paramref name="before"/>: a step of the history, unless it was put back or restored.</summary>
    private void NoteSelectionStep(TickSpan? before)
    {
        string? gesture = _selectionGesture;
        _selectionGesture = null;
        if (_restoring || _historyQuiet)
        {
            return;
        }

        if (_selectionHistory.Record(before, _selection?.Span, gesture) is not { } step)
        {
            return;
        }

        if (step.Kind is SelectionStepKind.Clear or SelectionStepKind.Replace)
        {
            ShowTimed(ref _selectionToastTimer, s_selectionToastFor, show => _selectionToast = show);
        }
        else
        {
            _selectionToast = false;
        }
    }

    /// <summary>What a gesture on the score or the roll says before its change.</summary>
    private async Task OnSelectionGestureAsync(SelectionGestureNote note)
    {
        switch (note.Kind)
        {
            case SelectionGestureNote.Change:
                _selectionGesture = note.Gesture;
                break;
            case SelectionGestureNote.Cleared when _selection is not null:
                _selectionGesture = note.Gesture;
                await ClearSelectionAsync();
                break;
            case SelectionGestureNote.Cancelled when note.Gesture is { } gesture && _selectionHistory.Revert(gesture) is { } step:
                await PutSelectionAsync(step.Before);
                _selectionToast = false;
                break;
            case SelectionGestureNote.Hint:
                ShowTimed(ref _holdHintTimer, s_holdHintFor, show => _holdHint = show);
                break;
        }
    }

    private async Task UndoSelectionAsync()
    {
        if (_selectionHistory.Undo() is { } step)
        {
            _selectionToast = false;
            await PutSelectionAsync(step.Before);
        }
    }

    private async Task RedoSelectionAsync()
    {
        if (_selectionHistory.Redo() is { } step)
        {
            _selectionToast = false;
            await PutSelectionAsync(step.After);
        }
    }

    /// <summary>⌘Z and ⇧⌘Z (mode-keys.ts): the selection's, unless the assembly has its own or the pen is writing.</summary>
    [JSInvokable]
    public async Task OnUndoKeyAsync(bool redo)
    {
        if (_assembling || _penOn || _score is null || NoteEditing)
        {
            return;
        }

        await (redo ? RedoSelectionAsync() : UndoSelectionAsync());
        StateHasChanged();
    }

    /// <summary>Selects a span again, or nothing, as no step: where the view is stays as it is.</summary>
    private async Task PutSelectionAsync(TickSpan? span)
    {
        _historyQuiet = true;
        try
        {
            if (span is { } selected)
            {
                await SelectAsync(selected, note: null);
            }
            else
            {
                await ClearSelectionAsync();
            }
        }
        finally
        {
            _historyQuiet = false;
        }
    }

    /// <summary>Shows something for a while: a new showing starts the time again.</summary>
    private void ShowTimed(ref CancellationTokenSource? timer, TimeSpan time, Action<bool> show)
    {
        timer?.Cancel();
        timer = new CancellationTokenSource();
        show(true);
        CancellationToken token = timer.Token;
        _ = Task.Delay(time, token).ContinueWith(
            _ => InvokeAsync(() =>
            {
                show(false);
                StateHasChanged();
            }),
            token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }
}