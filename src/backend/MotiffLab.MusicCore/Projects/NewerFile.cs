namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The version and "minReader" of a project file a later build wrote and this one reads read-only (0026 S6, #315;
/// docs/project-file.md, "Older builds and later files").
/// </summary>
/// <param name="Version">The file's "version", above <see cref="ProjectFile.Version"/>.</param>
/// <param name="MinReader">The file's "minReader", at most <see cref="ProjectFile.Version"/>.</param>
public sealed record NewerFile(int Version, int MinReader);