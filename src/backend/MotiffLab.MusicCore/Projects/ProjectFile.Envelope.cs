using System.Text.Json;

using MotiffLab.MusicCore.Projects.File;

using static MotiffLab.MusicCore.Projects.File.JsonRead;

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

public static partial class ProjectFile
{
    /// <summary>
    /// The envelope of a project file (0026 S2): the format's name, a version of 1 or more (a later one too), the id and
    /// file name, a time it was last opened, and how many motifs, variations, resume points and ideas it keeps (the
    /// workspace's from version 16, the top level's before; none where a list is not there). Nothing else is read, so a file
    /// with a value this build does not know, or of a later version, passes.
    /// </summary>
    /// <exception cref="ProjectFileException">Not JSON, not a project file, or its envelope is damaged.</exception>
    public static ProjectEnvelope ReadEnvelope(ReadOnlySpan<byte> data)
    {
        using JsonDocument document = Parse(data);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out JsonElement format) || format.ValueKind != JsonValueKind.String || format.GetString() != FormatName)
        {
            throw new ProjectFileException("This is not a Motiff Lab project file.");
        }

        int version = Integer(root, "version");
        if (version < 1)
        {
            throw Damaged("version");
        }

        JsonElement work = version >= Version && root.TryGetProperty("workspace", out JsonElement workspace) && workspace.ValueKind == JsonValueKind.Object ? workspace : root;
        return new ProjectEnvelope(Text(root, "id"), version, Text(root, "fileName"), JsonRead.Time(root, "lastOpened"))
        {
            MotifCount = Count(work, "motifs"),
            VariationCount = Count(work, "variations"),
            ResumeCount = Count(work, "resumePoints"),
            IdeaCount = Count(work, "observations"),
        };
    }

    private static int Count(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement list) && list.ValueKind == JsonValueKind.Array ? list.GetArrayLength() : 0;
}