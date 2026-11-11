using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.MusicXml;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// What the player wrote about the piece in the library (version 0.2): its title, its composer, their own tags and the
/// playlists it is on. Each is the player's, never copied from the file: a title or composer the file gives is shown by
/// <see cref="PieceDetails"/> until the player sets one. Values are cleaned on the way in: white space runs become one space
/// and the ends are trimmed, so an empty value is none; tags and playlists keep the player's order and drop a name already
/// there in any letter case.
/// </summary>
public sealed record PieceMetadata
{
    public PieceMetadata(string? title, string? composer, IEnumerable<string?>? tags, IEnumerable<string?>? playlists)
    {
        Title = Clean(title);
        Composer = Clean(composer);
        Tags = Names(tags);
        Playlists = Names(playlists);
    }

    /// <summary>No title, composer, tags or playlists: what every project had before the library kept them.</summary>
    public static PieceMetadata None { get; } = new(null, null, null, null);

    /// <summary>The player's title; null shows the file's (<see cref="PieceDetails.Title"/>).</summary>
    public string? Title { get; }

    /// <summary>The player's composer; null shows the file's, if it names one.</summary>
    public string? Composer { get; }

    /// <summary>The player's tags in their order, distinct regardless of letter case.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>The playlists the piece is on, by name, in the player's order, distinct regardless of letter case.</summary>
    public IReadOnlyList<string> Playlists { get; }

    public bool IsEmpty => Title is null && Composer is null && Tags.Count == 0 && Playlists.Count == 0;

    public bool Equals(PieceMetadata? other) =>
        other is not null && Title == other.Title && Composer == other.Composer
        && Tags.SequenceEqual(other.Tags, StringComparer.Ordinal) && Playlists.SequenceEqual(other.Playlists, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Title, Composer, Tags.Count, Playlists.Count);

    /// <summary>One line of text: runs of white space as one space, trimmed; null when nothing is left.</summary>
    public static string? Clean(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string cleaned = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length > 0 ? cleaned : null;
    }

    /// <summary>Names cleaned, in their order, the first spelling kept of those equal in any letter case, empty ones dropped.</summary>
    private static string[] Names(IEnumerable<string?>? names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return [.. (names ?? []).Select(Clean).OfType<string>().Where(seen.Add)];
    }
}

/// <summary>
/// The title, composer, tags and playlists a library card shows: the player's where they set them (<see cref="PieceMetadata"/>,
/// provenance user-edited), else what the imported file says (a MusicXML score's work or movement title and its composer,
/// provenance imported), else for the title the file's name without its extensions (provenance inferred). A scanned or
/// transcribed piece and a PDF have no title or composer from the file: what a scan or a recording gives is not read as
/// one.
/// </summary>
public sealed record PieceDetails(Attributed<string> Title, Attributed<string>? Composer, IReadOnlyList<string> Tags, IReadOnlyList<string> Playlists)
{
    private static readonly string[] s_extensions = [".mid", ".midi", ".kar", ".musicxml", ".xml", ".mxl", ".json", ".motiff", ".pdf"];

    /// <summary>The project's details: its metadata, over what its source file says, over its file name.</summary>
    public static PieceDetails Of(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        PieceMetadata metadata = project.Metadata;
        ScoreHeader header = metadata is { Title: not null, Composer: not null } || !project.HasNotes || project.Scan is not null || project.Transcription is not null
            ? ScoreHeader.None
            : ScoreHeader.Read(project.Source);
        Attributed<string> title = metadata.Title is { } own ? new(own, Attribution.UserEdited)
            : header.Title is { } written ? new(written, Attribution.Imported)
            : new(TitleOf(project.FileName), Attribution.Inferred(1));
        Attributed<string>? composer = metadata.Composer is { } player ? new(player, Attribution.UserEdited)
            : header.Composer is { } named ? new(named, Attribution.Imported)
            : null;
        return new(title, composer, metadata.Tags, metadata.Playlists);
    }

    /// <summary>A file's name without the extensions of the files a piece comes from: "Für Elise.motiff.json" is "Für Elise".</summary>
    public static string TitleOf(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        string name = fileName.Trim();
        for (int pass = 0; pass < 2; pass++)
        {
            string extension = Path.GetExtension(name);
            if (extension.Length == 0 || extension.Length == name.Length || !s_extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                break;
            }

            name = name[..^extension.Length];
        }

        return PieceMetadata.Clean(name) ?? fileName;
    }
}