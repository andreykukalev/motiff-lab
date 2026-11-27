namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The phrases as objects of the project (0026 C1, #318): the anchors what the player keeps is attached to, minted, carried
/// and attached again by <see cref="ProjectAnchors"/>. A file without anchors reads as before: none until something attaches.
/// </summary>
public sealed partial record Project
{
    /// <summary>The anchors as minted, those nothing names any more among them; <see cref="Anchors"/> leaves those out.</summary>
    internal IReadOnlyList<PhraseAnchor> MintedAnchors { get; init; } = [];

    /// <summary>
    /// The phrase anchors something is attached to, in the order they were minted: an idea, a resume point, or an entity of
    /// a later build kept as the file held it that names one.
    /// </summary>
    public IReadOnlyList<PhraseAnchor> Anchors => [.. MintedAnchors.Where(anchor => Names(anchor.Id))];

    /// <summary>Whether an idea, a resume point or an entity kept as a later build wrote it names the anchor <paramref name="id"/>.</summary>
    private bool Names(string id) =>
        Observations.Any(observation => observation.Anchor == id) || ResumePoints.Any(point => point.Anchor == id)
        || Unknown.Any(entity => entity.Json.Contains(id, StringComparison.Ordinal));

    /// <summary>The project with an idea or a resume point that names an anchor the file does not keep attached to none, as in a file without anchors.</summary>
    internal Project WithKnownAnchors()
    {
        HashSet<string> ids = [.. MintedAnchors.Select(static anchor => anchor.Id)];
        string? Known(string? id) => id is not null && ids.Contains(id) ? id : null;
        return this with
        {
            Observations = [.. Observations.Select(observation => observation with { Anchor = Known(observation.Anchor) })],
            ResumePoints = [.. ResumePoints.Select(point => point with { Anchor = Known(point.Anchor) })],
        };
    }
}