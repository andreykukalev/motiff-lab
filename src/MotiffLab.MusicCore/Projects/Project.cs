using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// A project as it is saved on the device and in an exported file (S1-P1): one source file and
/// what the player made from it. Notes are not stored; they come from importing the source again,
/// which is deterministic, so a saved project reopens exactly as it was (AC-S1-20).
/// </summary>
/// <remarks>
/// Motifs and variations are stored as the choices that made them: a motif's range, filter and note
/// ids, and a variation's operation, parameters and Keep choices. Timing layers join the format with
/// rhythm restoration (#27). Times are passed in, never read from a clock, so building a project is pure.
/// </remarks>
/// <param name="Id">Stable for the life of the project, for example "p_3f9c…".</param>
/// <param name="Source">The imported file's bytes, exactly as picked.</param>
public sealed record Project(
    string Id,
    string FileName,
    byte[] Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastOpened,
    IReadOnlyList<SavedMotif> Motifs,
    IReadOnlyList<SavedVariation> Variations)
{
    /// <summary>A new project for a file the player just imported, with nothing made from it yet.</summary>
    public static Project Create(string id, string fileName, byte[] source, DateTimeOffset now) =>
        new(id, fileName, source, now, now, [], []);

    /// <summary>The row on the projects screen (N1).</summary>
    public ProjectSummary Summary => new(Id, FileName, Motifs.Count, Variations.Count, LastOpened);
}

/// <summary>A project's row on the projects screen (N1).</summary>
public sealed record ProjectSummary(string Id, string FileName, int MotifCount, int VariationCount, DateTimeOffset LastOpened);

/// <summary>A motif as saved: what the player picked, from which the motif is rebuilt on open.</summary>
/// <param name="NoteIds">The selected notes, checked against the source when the project opens.</param>
public sealed record SavedMotif(
    string Id,
    string Name,
    MotifSelectionKind Kind,
    MotifMode Mode,
    TickSpan Span,
    SelectionFilter Filter,
    IReadOnlyList<string> NoteIds)
{
    public static SavedMotif From(Motif motif)
    {
        ArgumentNullException.ThrowIfNull(motif);
        return new(motif.Id, motif.Name, motif.Kind, motif.Mode, motif.Span, motif.Filter, motif.NoteIds);
    }
}

/// <summary>A variation as saved: the step that made it from a motif, which is pure, so it is re-run on open.</summary>
/// <param name="Operation">The catalogue id, for example "V1".</param>
/// <param name="Keep">The properties the player asked to keep, in Keep table order.</param>
public sealed record SavedVariation(
    string Id,
    string MotifId,
    string Operation,
    RhythmReshapeParameters Parameters,
    IReadOnlyList<KeepProperty> Keep)
{
    public static SavedVariation Create(string id, string motifId, RhythmReshapeParameters parameters, IEnumerable<KeepProperty> keep) =>
        new(id, motifId, RhythmReshape.OperationId, parameters, [.. keep.Distinct().Order()]);
}

/// <summary>A project opened for work: the source imported again, and its motifs and variations rebuilt.</summary>
public sealed record OpenedProject(
    Project Project,
    ImportedPiece Piece,
    IReadOnlyList<Motif> Motifs,
    IReadOnlyList<(SavedVariation Saved, Variation Variation)> Variations)
{
    /// <summary>
    /// Imports the project's source and rebuilds what was made from it. Throws
    /// <see cref="ProjectFileException"/> when the source no longer imports or no longer matches.
    /// </summary>
    public static OpenedProject Open(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        PieceImportResult imported = PieceImporter.Import(project.FileName, project.Source);
        if (imported.Piece is not { } piece)
        {
            throw new ProjectFileException($"The piece in this project cannot be read: {imported.Rejection?.Message}");
        }

        var motifs = new List<Motif>();
        foreach (SavedMotif saved in project.Motifs)
        {
            MotifSelection selection = saved.Kind == MotifSelectionKind.Notes
                ? MotifSelector.SelectNotes(piece.Notes, piece.Meter, saved.NoteIds)
                : MotifSelector.Select(piece.Notes, piece.Meter, saved.Span, saved.Filter);
            if (!selection.NoteIds.SequenceEqual(saved.NoteIds, StringComparer.Ordinal))
            {
                throw new ProjectFileException($"\"{saved.Name}\" no longer matches the notes of the piece.");
            }

            motifs.Add(Motif.Create(saved.Id, motifs.Count + 1, selection, saved.Kind, saved.Mode, piece.Notes) with { Name = saved.Name });
        }

        var variations = new List<(SavedVariation, Variation)>();
        foreach (SavedVariation saved in project.Variations)
        {
            Motif motif = motifs.FirstOrDefault(motif => motif.Id == saved.MotifId)
                ?? throw new ProjectFileException($"A variation refers to a motif ({saved.MotifId}) that is not in the project.");
            if (saved.Operation != RhythmReshape.OperationId)
            {
                throw new ProjectFileException($"A variation uses an operation ({saved.Operation}) this version of Motiff Lab does not have.");
            }

            variations.Add((saved, RhythmReshape.Apply(piece.Notes, motif.Selection, saved.Keep.ToHashSet(), saved.Parameters)));
        }

        return new OpenedProject(project, piece, motifs, variations);
    }
}

/// <summary>A project file or saved project that cannot be opened. The message is a plain reason for the player.</summary>
public sealed class ProjectFileException(string message) : Exception(message);