using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The operations on a piece's originals (decision record 0030, section 4): add an original, revive a removed one with
/// the same file, and remove one with or without its notes (<see cref="NotesOperations"/> for the Notes). Each is pure:
/// the same piece and arguments give the same piece. Nothing here touches a file of the player's: a linked original is
/// only unlinked.
/// </summary>
public static class OriginalOperations
{
    /// <summary>
    /// A free id for a new original: <paramref name="stem"/> itself ("src_pdf") while the piece uses it nowhere (an original,
    /// a removed one, a version, an unknown original), else the stem with the first free number ("src_pdf_2").
    /// </summary>
    public static string NextOriginalId(this Project project, string stem)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(stem);
        HashSet<string> taken = TakenOriginalIds(project);
        if (!taken.Contains(stem))
        {
            return stem;
        }

        for (int number = 2; ; number++)
        {
            string candidate = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{stem}_{number}");
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static HashSet<string> TakenOriginalIds(Project project) =>
    [
        .. project.Originals.Select(static original => original.Id),
        .. project.Sources.Removed.Select(static removed => removed.Original.Id),
        .. project.Performances.Select(static performance => performance.Id),
        .. Project.UnknownIds(project.Unknown, "originals"),
    ];

    /// <summary>
    /// The piece with an original added without recognising it (0030, section 4): a PDF (any number of them), photos, a
    /// recording, MIDI or MusicXML. Its fingerprint and size are worked out from <paramref name="data"/> when the
    /// description has none. Kept <see cref="OriginalKept.File"/>, the bytes go into the file (the first PDF as
    /// <see cref="Project.Document"/>); kept on the device or linked, only the description does. The same file as a removed
    /// original (the same fingerprint) revives it under its own id, with the marks on its pages; a revived first PDF of a
    /// piece with notes needs <paramref name="pages"/>, where its pages start in the working notes.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The id is taken, the first PDF's bytes are not a PDF, a file kept in the file has no bytes, or a revived first PDF
    /// of a piece with notes has no pages.
    /// </exception>
    public static Project AddOriginal(this Project project, PieceOriginal original, byte[]? data, PageMap? pages = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(original);
        if (data is not null)
        {
            original = original with { Sha256 = original.Sha256 ?? PieceOriginal.Fingerprint(data), Bytes = original.Bytes ?? data.Length };
        }

        (project, RemovedOriginal? tombstone) = Reviving(project, ref original);
        if (tombstone is null && TakenOriginalIds(project).Contains(original.Id))
        {
            throw new ArgumentException($"The piece has an original {original.Id} already.", nameof(original));
        }

        if (original.Kept == OriginalKept.File && data is null)
        {
            throw new ArgumentException("An original kept in the file comes with its bytes.", nameof(data));
        }

        bool firstPdf = original.Id == PieceOriginal.DocumentId && original.Kept == OriginalKept.File;
        if (firstPdf)
        {
            return WithFirstPdf(project, original, data!, tombstone?.Marks ?? DocumentMarks.Empty, pages);
        }

        project = original.Kept == OriginalKept.File ? project with { Sources = project.Sources with { Data = With(project.Sources.Data, original.Id, data!) } } : project;
        project = project.WithOriginal(original);
        return tombstone is null ? project : project.WithPageMarks(original.Id, tombstone.Marks);
    }

    /// <summary>
    /// The marks on the pages of the original <paramref name="id"/>: the first PDF's (<see cref="Project.DocumentMarks"/>), another
    /// PDF's or photos' (version 0.6); none for an original without them.
    /// </summary>
    public static DocumentMarks PageMarksOf(this Project project, string id)
    {
        ArgumentNullException.ThrowIfNull(project);
        return id == PieceOriginal.DocumentId ? project.DocumentMarks : project.Sources.Marks.GetValueOrDefault(id) ?? DocumentMarks.Empty;
    }

    /// <summary>The piece with the marks on the pages of the original <paramref name="id"/> in place of the ones it had.</summary>
    /// <exception cref="KeyNotFoundException">No live original has the id.</exception>
    public static Project WithPageMarks(this Project project, string id, DocumentMarks marks)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(marks);
        _ = project.FindOriginal(id) ?? throw new KeyNotFoundException($"There is no original {id}.");
        if (id == PieceOriginal.DocumentId)
        {
            return project with { DocumentMarks = marks };
        }

        Dictionary<string, DocumentMarks> all = new(project.Sources.Marks.Where(pair => pair.Key != id), StringComparer.Ordinal);
        if (!marks.IsEmpty || marks.Unknown.Count > 0)
        {
            all[id] = marks;
        }

        return project with { Sources = project.Sources with { Marks = all } };
    }

    /// <summary>The piece without the tombstone of the same file as <paramref name="original"/>, which takes its id and name; the tombstone, or null when none.</summary>
    private static (Project Project, RemovedOriginal? Tombstone) Reviving(Project project, ref PieceOriginal original)
    {
        string? sha = original.Sha256;
        RemovedOriginal? tombstone = sha is null ? null : project.Sources.Removed.FirstOrDefault(removed => removed.Original.Matches(sha));
        if (tombstone is null)
        {
            return (project, null);
        }

        original = (original.Id == tombstone.Original.Id ? original : PieceOriginals.Renamed(original, tombstone.Original.Id)) with { Name = original.Name ?? tombstone.Original.Name };
        return (project with { Sources = project.Sources with { Removed = [.. project.Sources.Removed.Where(removed => removed != tombstone)] } }, tombstone);
    }

    private static Project WithFirstPdf(Project project, PieceOriginal original, byte[] pdf, DocumentMarks marks, PageMap? pages)
    {
        if (!PdfDocument.IsPdf(pdf))
        {
            throw new ArgumentException("The file is not a PDF.", nameof(pdf));
        }

        if (project.HasNotes && pages is null)
        {
            throw new ArgumentException("The PDF of a piece with notes comes with where its pages start in them.", nameof(pages));
        }

        // A page map kept as the file held it while the PDF was gone gives way to the one given now.
        Dictionary<string, IReadOnlyList<UnknownField>> fields = new(project.UnknownFields, StringComparer.Ordinal);
        foreach (string path in fields.Keys.Where(static path => path.StartsWith(ProjectFile.NotesPath, StringComparison.Ordinal)).ToList())
        {
            fields[path] = [.. fields[path].Where(static field => field.Name != "pageMap")];
        }

        return project with
        {
            Document = pdf,
            DocumentMarks = marks,
            DocumentPages = project.HasNotes ? pages : null,
            UnknownFields = fields,
            KeptOriginals = [.. project.KeptOriginals.Where(static kept => kept.Id != PieceOriginal.DocumentId), original],
        };
    }

    /// <summary>
    /// The piece with an original removed (0030, section 4). By default its notes stay: the bytes leave the piece and a
    /// tombstone keeps its description and the marks on its pages, so the Notes made from it show it removed and the same
    /// file added again revives it. <paramref name="withNotes"/> removes the Notes made from it too, with their corrections.
    /// A linked original is only unlinked: the player's own file is never touched.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No original has the id.</exception>
    /// <exception cref="InvalidOperationException">
    /// The original is the only PDF or recording a piece with no notes has (remove the piece instead), or it is to go with
    /// its notes and the working notes were made from it.
    /// </exception>
    public static Project RemoveOriginal(this Project project, string id, DateTimeOffset at, bool withNotes = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        PieceOriginal original = project.FindOriginal(id) ?? throw new KeyNotFoundException($"There is no original {id}.");
        if (!project.HasNotes && project.Sources.Notes.Count == 0 && id == PieceOriginal.DocumentId)
        {
            throw new InvalidOperationException("A piece that is only its PDF is removed as a piece.");
        }

        // Version 17 (finding 29): nor is the last recording of a piece that is only its recording.
        if (project.Sources.Notes.Count == 0 && project.IsOnlyRecording && original.Kind == OriginalKind.Audio && project.Originals.Count(static each => each.Kind == OriginalKind.Audio) == 1)
        {
            throw new InvalidOperationException("A piece that is only its recording is removed as a piece.");
        }

        if (withNotes && project.HasNotes && MadeFrom(project.WorkingProvenance, id))
        {
            throw new InvalidOperationException("The working notes were made from this original; they cannot be removed while they are working.");
        }

        Project removed = Gone(project, original.Id) with
        {
            HiddenViews = [.. project.HiddenViews.Where(view => view != id)],
        };
        RemovedOriginal tombstone = new(original, at) { Marks = project.PageMarksOf(id) };
        removed = removed with { Sources = removed.Sources with { Removed = [.. removed.Sources.Removed.Where(each => each.Original.Id != id), tombstone] } };
        return withNotes ? NotesFromGone(removed, id) : removed;
    }

    /// <summary>
    /// The piece without an original's description and bytes, as its tombstone leaves it: the first PDF's bytes, page map
    /// and marks go with it (the tombstone keeps the marks), and the page maps other Notes have of it.
    /// </summary>
    internal static Project Gone(Project project, string id)
    {
        Project gone = project with
        {
            KeptOriginals = [.. project.KeptOriginals.Where(kept => kept.Id != id)],
            Sources = project.Sources with
            {
                Data = Without(project.Sources.Data, id),
                Marks = Without(project.Sources.Marks, id),
                Notes = id == PieceOriginal.DocumentId ? [.. project.Sources.Notes.Select(static notes => notes with { Pages = null })] : project.Sources.Notes,
            },
        };
        return id == PieceOriginal.DocumentId ? gone with { Document = null, DocumentMarks = DocumentMarks.Empty, DocumentPages = null } : gone;
    }

    /// <summary>Whether Notes of this <paramref name="provenance"/> were made from the original <paramref name="id"/> of their own piece.</summary>
    public static bool MadeFrom(NotesProvenance provenance, string id)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        return provenance.From.Any(source => source.Project is null && source.Original == id);
    }

    private static Project NotesFromGone(Project project, string id)
    {
        foreach (OtherNotes notes in project.Sources.Notes.Where(notes => MadeFrom(notes.Provenance, id)).ToList())
        {
            project = project.DeleteNotes(notes.Id);
        }

        foreach (SavedPerformance performance in project.Performances.Where(performance => MadeFrom(project.ProvenanceOf(performance), id) || performance.Id == id).ToList())
        {
            project = project.DeleteNotes(performance.Id);
        }

        return project;
    }

    private static Dictionary<string, T> With<T>(IReadOnlyDictionary<string, T> values, string key, T value) =>
        new(values.Where(pair => pair.Key != key), StringComparer.Ordinal) { [key] = value };

    internal static Dictionary<string, T> Without<T>(IReadOnlyDictionary<string, T> values, string key) =>
        new(values.Where(pair => pair.Key != key), StringComparer.Ordinal);
}