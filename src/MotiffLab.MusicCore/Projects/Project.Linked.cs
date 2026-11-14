using MotiffLab.MusicCore.Import;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// A piece whose PDF stays in the player's folder (version 0.5, «Импорт папки», within version 15): its PDF original is kept
/// <see cref="OriginalKept.Link"/>, so the bytes are never written into the project file, whatever the app holds in memory
/// while the piece is open; the file is read in place and never changed.
/// </summary>
public sealed partial record Project
{
    /// <summary>Whether the piece's PDF stays in the player's folder, linked, and is never written into the file.</summary>
    public bool LinksDocument => FindOriginal(PieceOriginal.DocumentId) is { Kept: OriginalKept.Link };

    /// <summary>
    /// Whether the file describes the piece's PDF (<see cref="PieceOriginal.DocumentId"/>), known to this build or kept
    /// whole as unknown: a piece with no notes and no PDF in the file is still a piece then, whose PDF is in the player's
    /// folder or on another device, listed and opened once the file is linked again.
    /// </summary>
    public bool DescribesDocument =>
        KeptOriginals.Any(static original => original.Id == PieceOriginal.DocumentId)
        || Unknown.Any(static entity => entity.List == "originals" && entity.Id == PieceOriginal.DocumentId);

    /// <summary>The project as its file holds it: without the PDF's bytes and its page map when the PDF is linked.</summary>
    public Project AsFiled() => LinksDocument && Document is not null ? this with { Document = null, DocumentPages = null } : this;

    /// <summary>The project without its PDF's bytes and page map, every original described, as the online copy holds it.</summary>
    private static Project WithoutDocument(Project project) =>
        project with { Document = null, DocumentPages = null, KeptOriginals = [.. project.Originals] };

    /// <summary>
    /// A new piece for a PDF that stays in the player's folder (version 0.5): no notes and no PDF bytes, its original
    /// described as linked with where it was in the folder, its fingerprint, size and pages, held by the device that links it.
    /// </summary>
    /// <param name="folder">Where the file is inside the chosen folder, "Бах/ХТК"; empty for a file right in it.</param>
    public static Project CreateLinkedDocument(string id, string fileName, string folder, string sha256, long bytes, int? pages, HoldingDevice device, DateTimeOffset now)
    {
        var original = new PieceOriginal(PieceOriginal.DocumentId, OriginalKind.Pdf, fileName, OriginalKept.Link, now)
        {
            Folder = folder,
            Sha256 = sha256,
            Bytes = bytes,
            Pages = pages,
        };
        return new Project(id, fileName, [], now, now, [], []) { KeptOriginals = [original.HeldBy(device, now)] };
    }

    /// <summary>
    /// The piece with an original found again in the player's folder (version 0.5, «Проверить папку»): at its new place
    /// (<paramref name="folder"/>, <paramref name="fileName"/>), where it moved or was renamed with the same content, as
    /// <see cref="Import.LibraryRelink"/> found it by place or fingerprint. With <paramref name="sha256"/>, the player said a
    /// file whose content changed is this piece («Похоже, это та же пьеса»): its fingerprint, size and pages become the
    /// original's. Nothing else of the piece changes; the file itself is only read.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No original has the id.</exception>
    /// <exception cref="ArgumentException">The folder is not a path inside the chosen folder, or the name is not a file name.</exception>
    public Project RelinkedAt(string id, string folder, string fileName, string? sha256 = null, long? bytes = null, int? pages = null)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.Contains('/') || fileName.Contains('\\'))
        {
            throw new ArgumentException("A file name names one file, without folders.", nameof(fileName));
        }

        PieceOriginal original = FindOriginal(id) ?? throw new KeyNotFoundException($"There is no original {id}.");
        PieceOriginal moved = new PieceOriginal(original.Id, original.Kind, fileName, original.Kept, original.AddedAt)
        {
            Name = original.Name,
            Folder = folder,
            Sha256 = original.Sha256,
            Bytes = original.Bytes,
            Pages = original.Pages,
            DurationMs = original.DurationMs,
            Held = original.Held,
        };
        moved = sha256 is null ? moved : moved with { Sha256 = sha256, Bytes = bytes ?? moved.Bytes, Pages = pages ?? moved.Pages };
        return moved == original ? this : WithOriginal(moved);
    }

    /// <summary>
    /// The piece opened with its linked PDF read from the player's folder on this device: the bytes match the original's
    /// fingerprint, so the PDF shows while the piece is open and is never written into its file (<see cref="AsFiled"/>);
    /// null when the file is another one.
    /// </summary>
    /// <exception cref="InvalidOperationException">The piece's PDF is not linked, or is here already.</exception>
    public Project? WithLinkedDocument(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (!LinksDocument || Document is not null)
        {
            throw new InvalidOperationException("Only a piece whose PDF is linked and not open yet reads it from the folder.");
        }

        return PdfDocument.IsPdf(pdf) && FindOriginal(PieceOriginal.DocumentId)!.Matches(PieceOriginal.Fingerprint(pdf)) ? this with { Document = pdf } : null;
    }
}