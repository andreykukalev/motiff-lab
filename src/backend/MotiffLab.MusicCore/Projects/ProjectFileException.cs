namespace MotiffLab.MusicCore.Projects;

/// <summary>A project file or saved project that cannot be opened. The message is a plain reason for the player.</summary>
public sealed class ProjectFileException(string message) : Exception(message)
{
    /// <summary>The reason in Russian, where the reader gives one (a file of a newer version, #363); the others are in English only.</summary>
    public string? RussianMessage { get; init; }

    /// <summary>The reason in the language: the Russian when there is one, else the English.</summary>
    public string Text(Words words) => words.IsRussian && RussianMessage is { } russian ? russian : Message;
}