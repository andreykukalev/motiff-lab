namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// What the envelope of a project file says, read without its musical content (0026 S2): the format, the version, the
/// project's id and name, when it was last opened, and the counts a library row shows. The API checks a file by it, so it
/// stores a file of any version, a later one too, exactly as sent.
/// </summary>
/// <param name="Version">The file's version, 1 or more; it may be later than this build's.</param>
public sealed record ProjectEnvelope(string Id, int Version, string FileName, DateTimeOffset LastOpened)
{
    public int MotifCount { get; init; }

    public int VariationCount { get; init; }

    public int ResumeCount { get; init; }

    public int IdeaCount { get; init; }

    /// <summary>The library row the envelope gives, as <see cref="Project.Summary"/> gives it for the counts the API keeps.</summary>
    public ProjectSummary Summary => new(Id, FileName, MotifCount, VariationCount, LastOpened, ResumeCount, IdeaCount);
}