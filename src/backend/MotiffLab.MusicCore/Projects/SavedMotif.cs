using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

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
    /// <summary>
    /// The Notes the motif was made on, when they are not the working notes any more (version 16): other Notes were made
    /// the working ones, so the motif waits, kept in the file, until its own are working again. Null for the working notes.
    /// </summary>
    public string? Notes { get; init; }

    public static SavedMotif From(Motif motif)
    {
        ArgumentNullException.ThrowIfNull(motif);
        return new(motif.Id, motif.Name, motif.Kind, motif.Mode, motif.Span, motif.Filter, motif.NoteIds);
    }
}