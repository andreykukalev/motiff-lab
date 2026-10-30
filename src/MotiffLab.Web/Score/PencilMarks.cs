namespace MotiffLab.Web.Score;

/// <summary>
/// Where the player's pencil marks on a score are kept on the device (decision record 0015): one set per piece,
/// in <c>Scripts/storage/pencil-store.ts</c>. Marks are the player's own writing, never read as music.
/// </summary>
public static class PencilMarks
{
    /// <summary>The marks of a saved project, or of a piece open without one (Für Elise) by its title; null when neither is known.</summary>
    public static string? KeyFor(string? projectId, string? title) =>
        projectId is not null ? ProjectKey(projectId)
        : string.IsNullOrEmpty(title) ? null
        : $"piece:{title}";

    public static string ProjectKey(string projectId) => $"project:{projectId}";
}