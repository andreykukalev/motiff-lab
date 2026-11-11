using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The call project.metadata (contract 12): the title, composer and tags the player sets in the library. "title" and
/// "composer" are text, "tags" a list of text that replaces the tags; a field left out stays as it is, and null (or text
/// with nothing in it) clears it, so the card shows the file's title or composer again.
/// </summary>
internal static class MetadataRequest
{
    public static Project Apply(Project project, Request request)
    {
        PieceMetadata current = project.Metadata;
        return project with
        {
            Metadata = new PieceMetadata(
                request.Sends("title") ? request.Text("title") : current.Title,
                request.Sends("composer") ? request.Text("composer") : current.Composer,
                request.Sends("tags") ? request.Texts("tags") : current.Tags),
        };
    }
}