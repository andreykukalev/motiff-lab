namespace MotiffLab.MusicCore.Projects;

/// <summary>A project file or saved project that cannot be opened. The message is a plain reason for the player.</summary>
public sealed class ProjectFileException(string message) : Exception(message);