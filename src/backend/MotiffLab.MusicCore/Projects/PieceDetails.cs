using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The title, composer, tags and playlists a library card shows: the player's where they set them (<see cref="PieceMetadata"/>,
/// provenance user-edited), else what the imported file says (a MusicXML score's work or movement title and its composer,
/// provenance imported), else for the title the file's name without its extensions (provenance inferred). A scanned or
/// transcribed piece and a PDF have no title or composer from the file: what a scan or a recording gives is not read as
/// one.
/// </summary>
public sealed record PieceDetails(Attributed<string> Title, Attributed<string>? Composer, IReadOnlyList<string> Tags, IReadOnlyList<string> Playlists)
{
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
            : new(PieceMetadata.TitleOf(project.FileName), Attribution.Inferred(1));
        Attributed<string>? composer = metadata.Composer is { } player ? new(player, Attribution.UserEdited)
            : header.Composer is { } named ? new(named, Attribution.Imported)
            : null;
        return new(title, composer, metadata.Tags, metadata.Playlists);
    }
}