namespace MotiffLab.Web.Score;

/// <summary>
/// What the mouse and a finger do on the score and the roll (version 0.5, the experiment Two playheads and modes; product
/// spec interaction-modes): one group in the bar, chosen by a tap or by the keys H, S, E and P.
/// </summary>
public enum InteractionMode
{
    /// <summary>A drag moves the score; a click places a playhead; no selection is made.</summary>
    Navigate,

    /// <summary>A drag outside the selection selects beats, replacing the selection; Shift and a click extend it.</summary>
    Select,

    /// <summary>A click on a note opens its correction; the selection stays as it is.</summary>
    Edit,

    /// <summary>The pen writes; the selection stays as it is.</summary>
    Pen,
}

/// <summary>The names the scripts use for the modes (Scripts/score/score-modes.ts).</summary>
public static class InteractionModes
{
    public static IReadOnlyList<InteractionMode> All { get; } = [InteractionMode.Navigate, InteractionMode.Select, InteractionMode.Edit, InteractionMode.Pen];

    /// <summary>The mode's name in the scripts: "navigate", "select", "edit" or "pen".</summary>
    public static string Name(this InteractionMode mode) => mode switch
    {
        InteractionMode.Select => "select",
        InteractionMode.Edit => "edit",
        InteractionMode.Pen => "pen",
        _ => "navigate",
    };

    /// <summary>The mode a script names, or null for a name it does not know.</summary>
    public static InteractionMode? FromName(string? name)
    {
        foreach (InteractionMode mode in All)
        {
            if (mode.Name() == name)
            {
                return mode;
            }
        }

        return null;
    }
}