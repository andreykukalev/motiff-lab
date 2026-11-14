using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Import;

/// <summary>
/// The import plan of a folder (version 0.5, «Импорт папки», behind <c>experimental.libraryImport</c>): from the files the
/// app found in the folder the player chose and the originals the library has, what happens to each file and what its
/// piece is suggested to be. Pure and deterministic: the same files, library and options give the same plan, so the web
/// and the iPad (through the facade's library.plan) plan alike. The plan only reads: nothing in it changes, renames,
/// moves or deletes a file or a folder.
/// </summary>
/// <remarks>
/// A file is known by its fingerprint, not its name: the same PDF in two folders is one piece on both folders' playlists,
/// and a file the library has already adds nothing. A suggestion is a guess (<see cref="Suggestion"/>) that becomes the
/// piece's <see cref="PieceMetadata"/> only with the player's word (<see cref="Confirmed"/>), and never over what the
/// player wrote. Two different files with the same title and composer are possibly the same piece, said and not merged.
/// </remarks>
public static class LibraryImport
{
    /// <summary>A PDF with more pages than this is perhaps several pieces in one score, «В этой партитуре несколько пьес».</summary>
    public const int CollectionPages = 20;

    /// <exception cref="ArgumentException">Two found files have the same path.</exception>
    public static ImportPlan Plan(IEnumerable<FoundFile> found, IEnumerable<LibraryOriginal> library, ImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(options);
        List<FoundFile> files = [.. found.OrderBy(static file => file.Path, StringComparer.Ordinal)];
        if (files.Select(static file => file.Path).Distinct(StringComparer.Ordinal).Count() != files.Count)
        {
            throw new ArgumentException("Each found file has its own path.", nameof(found));
        }

        List<LibraryOriginal> originals = [.. library.OrderBy(static original => original.ProjectId, StringComparer.Ordinal).ThenBy(static original => original.OriginalId, StringComparer.Ordinal)];
        var suggestions = new ImportSuggestions(files, options.Folders);
        List<PlannedFile> planned = [.. files.Select(file => Status(file, options) is { } unsuitable ? new PlannedFile(file, unsuitable) : suggestions.For(file))];
        planned = Fingerprinted(planned, originals);
        List<string> toFingerprint = ToFingerprint(planned, originals);
        HashSet<string> provisional = [.. toFingerprint];
        planned = [.. planned.Select(file => provisional.Contains(file.File.Path) ? file with { Provisional = true } : file)];
        return new ImportPlan(PossiblySame(planned, originals), toFingerprint);
    }

    /// <summary>
    /// The piece's metadata once the player confirmed the suggestions (or their own changes to them): the title and
    /// composer the player already set stay, the confirmed ones fill only what is empty, and the tags and playlists are
    /// added to the piece's own.
    /// </summary>
    public static PieceMetadata Confirmed(PieceMetadata existing, string? title, string? composer, IEnumerable<string> tags, IEnumerable<string> playlists)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(playlists);
        return new PieceMetadata(
            existing.Title ?? (title is null ? null : NameText.Composed(title)),
            existing.Composer ?? (composer is null ? null : NameText.Composed(composer)),
            [.. existing.Tags, .. tags.Select(NameText.Composed)],
            [.. existing.Playlists, .. playlists.Select(NameText.Composed)]);
    }

    /// <summary>Why a file is not added, or null when it can be: its kind, whether it could be read, its size.</summary>
    private static ImportStatus? Status(FoundFile file, ImportOptions options) => file.Kind switch
    {
        null => ImportStatus.Unsupported,
        _ when file.Unreadable || file.Bytes == 0 => ImportStatus.Unreadable,
        OriginalKind.Pdf when !options.Linked && file.Bytes > PdfDocument.MaxFileBytes => ImportStatus.TooBig,
        OriginalKind.Midi or OriginalKind.MusicXml when file.Bytes > PieceImporter.MaxFileBytes => ImportStatus.TooBig,
        _ => null,
    };

    /// <summary>
    /// The files the library has by fingerprint, and the duplicates in the folder: a later file with an earlier one's
    /// fingerprint is that piece, whose playlists and tags then take its folders too.
    /// </summary>
    private static List<PlannedFile> Fingerprinted(List<PlannedFile> planned, List<LibraryOriginal> originals)
    {
        Dictionary<string, string> pieceBySha = [];
        foreach (LibraryOriginal original in originals.Where(static original => original.Sha256 is not null))
        {
            pieceBySha.TryAdd(original.Sha256!, original.ProjectId);
        }

        var first = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < planned.Count; index++)
        {
            PlannedFile file = planned[index];
            if (file.Status != ImportStatus.New || file.File.Sha256 is not { } sha)
            {
                continue;
            }

            if (pieceBySha.TryGetValue(sha, out string? piece))
            {
                planned[index] = file with { Status = ImportStatus.InLibrary, Project = piece };
            }
            else if (first.TryGetValue(sha, out int earlier))
            {
                planned[index] = file with { Status = ImportStatus.Duplicate, SameAs = planned[earlier].File.Path };
                planned[earlier] = planned[earlier] with
                {
                    Playlists = Union(planned[earlier].Playlists, file.Playlists),
                    Tags = Union(planned[earlier].Tags, file.Tags),
                };
            }
            else
            {
                first[sha] = index;
            }
        }

        return planned;
    }

    /// <summary>
    /// The new files with no fingerprint yet whose size is another file's or an original's: only they can be the same
    /// file, so only they are worth reading whole before the plan is sure. In path order.
    /// </summary>
    private static List<string> ToFingerprint(List<PlannedFile> planned, List<LibraryOriginal> originals)
    {
        List<PlannedFile> suitable = [.. planned.Where(static file => file.Status is ImportStatus.New or ImportStatus.InLibrary or ImportStatus.Duplicate)];
        var sizes = suitable.GroupBy(static file => file.File.Bytes).ToDictionary(static group => group.Key, static group => group.Count());
        HashSet<long> kept = [.. originals.Select(static original => original.Bytes).OfType<long>()];
        return [.. suitable
            .Where(file => file.Status == ImportStatus.New && file.File.Sha256 is null && (sizes[file.File.Bytes] > 1 || kept.Contains(file.File.Bytes)))
            .Select(static file => file.File.Path)];
    }

    /// <summary>The new files with the same title and composer as another new file or a piece of the library.</summary>
    private static PlannedFile[] PossiblySame(List<PlannedFile> planned, List<LibraryOriginal> originals)
    {
        ILookup<string, string> files = planned.Where(static file => file.Status == ImportStatus.New && Key(file.Title?.Value, file.Composer?.Value) is not null)
            .ToLookup(static file => Key(file.Title?.Value, file.Composer?.Value)!, static file => file.File.Path, StringComparer.Ordinal);
        ILookup<string, string> pieces = originals.Where(static original => Key(original.Title, original.Composer) is not null)
            .ToLookup(static original => Key(original.Title, original.Composer)!, static original => original.ProjectId, StringComparer.Ordinal);
        return [.. planned.Select(file => file.Status == ImportStatus.New && Key(file.Title?.Value, file.Composer?.Value) is { } key
            ? file with
            {
                PossiblySameFiles = [.. files[key].Where(path => path != file.File.Path)],
                PossiblySamePieces = [.. pieces[key].Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            }
            : file)];
    }

    /// <summary>A title and composer as they are compared: folded, so letter case, marks and punctuation do not count.</summary>
    private static string? Key(string? title, string? composer) =>
        title is not null && NameText.Folded(title) is { Length: > 0 } folded ? $"{folded}\n{NameText.Folded(composer ?? string.Empty)}" : null;

    private static string[] Union(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return [.. first.Concat(second).Where(seen.Add)];
    }
}