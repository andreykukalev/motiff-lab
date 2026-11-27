namespace MotiffLab.MusicCore.Projects;

/// <summary>A project file or saved project that cannot be opened. The message is a plain reason for the player.</summary>
public sealed class ProjectFileException(string message) : Exception(message)
{
    /// <summary>The reason in Russian, where the reader gives one (a file of a newer version, #363); the others are in English only.</summary>
    public string? RussianMessage { get; init; }

    /// <summary>Whether the project was refused because it is read-only: read from a later version, never saved here (0026 S6).</summary>
    public bool ReadOnly { get; init; }

    /// <summary>The reason in the language: the Russian when there is one, else the English.</summary>
    public string Text(Words words) => words.IsRussian && RussianMessage is { } russian ? russian : Message;
}