using MotiffLab.MusicCore.Import;

namespace MotiffLab.MusicCore.Projects;

/// <summary>The library's originals, for a folder import to know a file it has already (<see cref="LibraryOriginal"/>).</summary>
public static class LibraryOriginals
{
    /// <summary>The originals of a piece, each with the piece's title and composer.</summary>
    public static IEnumerable<LibraryOriginal> Of(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        PieceDetails details = project.Details;
        return project.Originals.Select(original => new LibraryOriginal(project.Id, original.Id, original.Sha256)
        {
            Bytes = original.Bytes,
            Pages = original.Pages,
            FileName = original.FileName,
            Folder = original.Folder,
            Title = details.Title.Value,
            Composer = details.Composer?.Value,
        });
    }
}