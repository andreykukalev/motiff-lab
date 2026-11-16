using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assistant.Conversation;

/// <summary>
/// What a question is about (0028 section 5; the 0.6 bug "the assistant does not see the selected bars", #582): the
/// selection, unless the player chose the whole piece while that very selection stood. A new selection made with the
/// assistant open is what the next question is about, whatever the scope was before: the scope follows the selection, so
/// the bars the player has just selected always reach the model. With nothing selected it is the whole piece. Immutable;
/// both apps keep one and ask it.
/// </summary>
/// <param name="WholePieceChosen">Whether the player chose the whole piece.</param>
/// <param name="ChosenFor">The selection that stood when they chose it; the choice holds only while it stands.</param>
public sealed record AskScope(bool WholePieceChosen, TickSpan? ChosenFor)
{
    /// <summary>The selection, whichever it is (the scope when the assistant opens on a selection).</summary>
    public static AskScope Selection { get; } = new(false, null);

    /// <summary>The player's choice: the whole piece (true) or the selection, while <paramref name="selection"/> stands.</summary>
    public static AskScope Choose(bool wholePiece, TickSpan? selection) => wholePiece && selection is not null ? new(true, selection) : Selection;

    /// <summary>Whether the question is about the whole piece while <paramref name="selection"/> is selected (null: nothing is).</summary>
    public bool IsWholePiece(TickSpan? selection) => selection is not { } span || (WholePieceChosen && ChosenFor == span);
}