namespace MotiffLab.Web.Score;

/// <summary>
/// What a selection gesture on the score or the roll tells the page that owns the selection's history (#511 sections 2
/// and 11), raised before the change it names: <see cref="Change"/> (the next change belongs to <see cref="Gesture"/>),
/// <see cref="Cleared"/> (a hold outside took the selection away), <see cref="Cancelled"/> (the gesture was cancelled
/// after a hold cleared the selection) or <see cref="Hint"/> (a pointer that cannot scroll moved off the selection
/// before the hold).
/// </summary>
public readonly record struct SelectionGestureNote(string Kind, string? Gesture)
{
    public const string Change = "change";
    public const string Cleared = "cleared";
    public const string Cancelled = "cancelled";
    public const string Hint = "hint";
}