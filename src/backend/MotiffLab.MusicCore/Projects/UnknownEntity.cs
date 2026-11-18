namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// An entity of the project file this build cannot read, kept as the file held it (decision record 0026, S6: the tolerant
/// reader): it holds a value a later build added to one of the file's enums, or it belongs with such an entity (a motif
/// and its variations, a bookmark category and its bookmarks). The project's own lists do not hold it, so it takes no part
/// in playback, analysis or variations and an app never meets it; <see cref="ProjectFile.Write"/> writes it back unchanged
/// where it stood. docs/project-file.md, "Unknown enum values", says which entities are kept this way.
/// </summary>
/// <param name="List">The array of the file it stands in, as a path: "motifs", "marks.ink", "bookmarks.items".</param>
/// <param name="Index">Its place in that array as the file held it.</param>
/// <param name="Id">Its "id", when it has one as text.</param>
/// <param name="Owner">The id of the entity it belongs with and goes with: a variation's motif, a bookmark's category.</param>
/// <param name="Json">The entity as the file held it, a JSON object.</param>
public sealed record UnknownEntity(string List, int Index, string? Id, string? Owner, string Json)
{
    /// <summary>
    /// The enum whose value this build does not know, by its type name ("MotifMode"); null for an entity kept only because
    /// it belongs with an unknown one.
    /// </summary>
    public string? Enum { get; init; }
}