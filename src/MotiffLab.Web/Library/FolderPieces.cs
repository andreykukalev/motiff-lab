using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Web.Library;

/// <summary>
/// A file of the player's folder made into a piece (version 0.5, «Импорт папки»), as Open a file makes one: a PDF read as it
/// is, linked where it is (<see cref="Project.CreateLinkedDocument"/>) or copied into the piece; a MIDI or MusicXML file
/// read into notes. Each original says where it was in the folder. The title, composer, tags and playlists the player
/// added it with go into its metadata, never over what the player wrote (<see cref="LibraryImport.Confirmed"/>).
/// </summary>
public static class FolderPieces
{
    /// <summary>The piece, or null with why (a key of the words, or the importer's own words).</summary>
    public static (Project? Project, string? Problem) Make(string id, JobFile file, byte[] data, bool linked, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(data);
        string name = file.Path[(file.Path.LastIndexOf('/') + 1)..];
        string folder = file.Path.LastIndexOf('/') is var slash and >= 0 ? file.Path[..slash] : string.Empty;
        string sha = PieceOriginal.Fingerprint(data);
        Project? project;
        if (FolderScan.IsPdf(name))
        {
            if (!PdfDocument.IsPdf(data))
            {
                return (null, "folder.problemDamaged");
            }

            if (!linked && PdfDocument.CheckSize(data.Length) is not null)
            {
                return (null, "folder.problemBig");
            }

            project = linked
                ? Project.CreateLinkedDocument(id, name, folder, sha, data.Length, file.Pages, HoldingDevice.Web, now)
                : Project.CreateDocument(id, name, data, now).RelinkedAt(PieceOriginal.DocumentId, folder, name, sha, data.Length, file.Pages);
        }
        else
        {
            PieceImportResult imported = PieceImporter.Import(name, data);
            if (imported.Piece is null)
            {
                return (null, imported.Rejection?.Reason == ImportRejectionReason.TooLarge ? "folder.problemBig" : "folder.problemNotes");
            }

            project = Project.Create(id, name, data, now);
            if (project.FindOriginal(PieceOriginal.SourceFileId) is not null)
            {
                project = project.RelinkedAt(PieceOriginal.SourceFileId, folder, name, sha, data.Length);
            }
        }

        return (project with { Metadata = LibraryImport.Confirmed(project.Metadata, file.Title, file.Composer, file.Tags, file.Playlists) }, null);
    }
}