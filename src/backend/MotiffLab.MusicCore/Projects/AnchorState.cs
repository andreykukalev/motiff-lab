namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The phrase anchors of a project at one moment (#318), as a phrases step left them or found them, so its Undo and Redo put
/// them back exactly (<see cref="ProjectAnchors.WithAnchorState"/>): the anchors at their spans, and the anchor each idea and
/// each resume point was attached to, by their ids (null for none).
/// </summary>
internal sealed record AnchorState(IReadOnlyList<PhraseAnchor> Anchors, IReadOnlyDictionary<string, string?> Ideas, IReadOnlyDictionary<string, string?> Points);