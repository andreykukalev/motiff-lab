namespace MotiffLab.MusicCore.Pieces;

/// <summary>
/// A field of the project file this build does not know (0026 S6; version 16), kept to be written back unchanged after
/// the fields it knows: at the top level, in the workspace, in a Notes, an original or a provenance.
/// </summary>
/// <param name="Name">The field's name.</param>
/// <param name="Json">Its value as the file held it.</param>
public sealed record UnknownField(string Name, string Json);