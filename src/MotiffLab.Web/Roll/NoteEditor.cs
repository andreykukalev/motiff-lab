namespace MotiffLab.Web.Roll;

/// <summary>A note as the editor on the roll leaves it (testing item 15): its id (none for a note to add), onset and length in ticks, and pitch.</summary>
public readonly record struct RollNoteEdit(string? Id, long Onset, int Pitch, long Length);

/// <summary>
/// The note editor on the roll (testing item 15): whether it is on and with which tool, whether a step can be undone and
/// whether anything changed since it began, and what the page does with each edit. Nothing is kept until Save; Cancel puts
/// the notes back as they were, Reset takes every correction out, Undo steps back.
/// </summary>
public sealed record NoteEditor(
    bool On,
    string Tool,
    bool CanUndo,
    bool Changed,
    bool CanReset,
    string? Picked,
    Func<Task> Begin,
    Func<string, Task> ChooseTool,
    Func<RollNoteEdit, Task> Change,
    Func<RollNoteEdit, Task> Add,
    Func<string, Task> Remove,
    Func<Task> Undo,
    Func<Task> Reset,
    Func<Task> Cancel,
    Func<Task> Save);