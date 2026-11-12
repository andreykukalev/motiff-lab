using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Roll;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The note editor on the roll (testing item 15): the roll becomes the place to correct the notes, chiefly what a scan or a
/// transcription got wrong. A note dragged moves by sixteenths and rows, its end changes its length, Add puts in a note
/// where the player presses, Delete takes one out. Every edit sounds, shows and is engraved at once, as a correction in the
/// edit log (S1-I7), but the project keeps nothing until ✓ Save; Cancel puts the notes back as they were, Undo steps back
/// one edit, Reset takes every correction out, back to the notes as imported.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The corrections when the editor was opened; null while it is closed. The project is saved with these until Save.</summary>
    private NoteEditLog? _editFrom;

    /// <summary>The corrections before each edit made in the editor, the last first undone.</summary>
    private readonly Stack<NoteEditLog> _editSteps = new();

    private string _editTool = "move";

    /// <summary>The note just added, picked in the editor so the arrows and Delete act on it.</summary>
    private string? _editPicked;

    private bool NoteEditing => _editFrom is not null;

    /// <summary>The editor the roll shows; none until a piece has notes, or while the player's corrections are off (version 0.5).</summary>
    private NoteEditor? RollEditor => _notes is null || EditsOff ? null : new NoteEditor(
        NoteEditing,
        _editTool,
        CanUndo: _editSteps.Count > 0,
        Changed: _editFrom is { } from && !from.Equals(_edits),
        CanReset: !_edits.IsEmpty,
        Picked: _editPicked,
        Begin: Rendered(BeginNoteEditAsync),
        ChooseTool: Rendered<string>(tool =>
        {
            _editTool = tool;
            return Task.CompletedTask;
        }),
        Change: Rendered<RollNoteEdit>(ChangeEditedNoteAsync),
        Add: Rendered<RollNoteEdit>(AddEditedNoteAsync),
        Remove: Rendered<string>(RemoveEditedNoteAsync),
        Undo: Rendered(UndoNoteEditAsync),
        Reset: Rendered(() => EditStepAsync(NoteEditLog.Empty)),
        Cancel: Rendered(CancelNoteEditAsync),
        Save: Rendered(SaveNoteEditAsync));

    /// <summary>The page renders again after an editor action, which the roll raises, not the page.</summary>
    private Func<Task> Rendered(Func<Task> action) => async () =>
    {
        await action();
        StateHasChanged();
    };

    private Func<T, Task> Rendered<T>(Func<T, Task> action) => async value =>
    {
        await action(value);
        StateHasChanged();
    };

    private Task BeginNoteEditAsync()
    {
        _editFrom = _edits;
        _editSteps.Clear();
        _editTool = "move";
        _editPicked = null;
        return Task.CompletedTask;
    }

    private Task EditStepAsync(NoteEditLog next)
    {
        if (next.Equals(_edits))
        {
            return Task.CompletedTask;
        }

        _editSteps.Push(_edits);
        return ApplyEditsAsync(next);
    }

    private Task ChangeEditedNoteAsync(RollNoteEdit edit)
    {
        if (_notes?.FirstOrDefault(note => note.Id == edit.Id) is not { } note || RefuseTakenNote(note))
        {
            return Task.CompletedTask;
        }

        _editPicked = note.Id;
        NoteValues after = new(
            new Pitch(Math.Clamp(edit.Pitch, 0, Pitch.MaxMidiNumber)),
            new TickSpan(new Tick(Math.Max(0, edit.Onset)), Math.Max(1, edit.Length)),
            note.Velocity);
        return EditStepAsync(_edits.Change(_imported, note.Id, after));
    }

    /// <summary>A note added where the player pressed, with the track, hand and loudness of the note nearest to it.</summary>
    private Task AddEditedNoteAsync(RollNoteEdit edit)
    {
        if (edit.Pitch is < 0 or > Pitch.MaxMidiNumber || edit.Onset < 0 || edit.Length <= 0)
        {
            return Task.CompletedTask;
        }

        Note? near = _notes!.OrderBy(note => Math.Abs(note.Span.Onset.Value - edit.Onset) + (Math.Abs(note.Pitch.MidiNumber - edit.Pitch) * CanonicalTime.TicksPerQuarter)).FirstOrDefault();
        Hand? hand = near?.Hand is null ? null : edit.Pitch < 60 ? Hand.Left : Hand.Right;
        NoteValues values = new(new Pitch(edit.Pitch), new TickSpan(new Tick(edit.Onset), edit.Length), near?.Velocity ?? 80);
        NoteEditLog next = _edits.Add(_imported, values, near?.Track ?? 0, hand, out string id);
        _editPicked = id;
        return EditStepAsync(next);
    }

    private Task RemoveEditedNoteAsync(string id)
    {
        if (_notes?.FirstOrDefault(note => note.Id == id) is not { } note || RefuseTakenNote(note) || PerformanceTake.IsTaken(id))
        {
            return Task.CompletedTask;
        }

        _editPicked = null;
        return EditStepAsync(_edits.Remove(_imported, id));
    }

    private Task UndoNoteEditAsync() => _editSteps.TryPop(out NoteEditLog? before) ? ApplyEditsAsync(before) : Task.CompletedTask;

    private async Task CancelNoteEditAsync()
    {
        NoteEditLog from = _editFrom ?? _edits;
        CloseNoteEditor();
        if (!from.Equals(_edits))
        {
            await ApplyEditsAsync(from);
        }
    }

    private async Task SaveNoteEditAsync()
    {
        CloseNoteEditor();
        await SaveProjectAsync();
    }

    private void CloseNoteEditor()
    {
        _editFrom = null;
        _editSteps.Clear();
        _editPicked = null;
    }
}