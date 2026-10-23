using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Motifs;

/// <summary>How much the app suggests before the player acts; chosen when a motif is created and stored with it.</summary>
public enum MotifMode
{
    Discovery,
    Guided,
    Analysis,
}

/// <summary>How the player picked a motif's notes.</summary>
public enum MotifSelectionKind
{
    /// <summary>A range of bars, dragged or typed, with an optional filter (S1-M1, S1-M2).</summary>
    Range,

    /// <summary>Notes clicked one by one (S1-M3).</summary>
    Notes,
}

/// <summary>Where a motif sits in the piece, so "Show in piece" can scroll there and highlight it (S1-M4).</summary>
/// <param name="Span">The motif's range on the canonical timeline.</param>
/// <param name="FirstBar">The bar to scroll to.</param>
/// <param name="LastBar">The last bar to keep in view.</param>
/// <param name="FirstNote">Where the motif's first note is in the source file; null when the note has no source location.</param>
public sealed record MotifSourceReference(TickSpan Span, int FirstBar, int LastBar, SourceLocation? FirstNote);

/// <summary>
/// A motif: the notes the player selected, across one or more voices, plus the harmonic context of
/// the bars it spans (Stage 1 spec, motif selection). It refers to notes by id; it never copies them.
/// </summary>
/// <remarks>
/// Melodic views and operations use the motif's melody voice and accompaniment operations its other
/// voices; <see cref="MotifVoices"/> derives both. Creating a motif is pure: the same selection and
/// notes give the same motif.
/// </remarks>
public sealed record Motif
{
    private readonly string _id = null!;
    private readonly string _name = null!;
    private readonly MotifSelection _selection = null!;

    /// <summary>Stable within a project, for example "m_0001".</summary>
    public required string Id
    {
        get => _id;
        init => _id = !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("A motif id is required.", nameof(Id));
    }

    /// <summary>The player's name for the motif, at first <see cref="DefaultName"/>.</summary>
    public required string Name
    {
        get => _name;
        init => _name = !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException("A motif needs a name.", nameof(Name));
    }

    /// <summary>The range, bars, filter and note ids.</summary>
    public required MotifSelection Selection
    {
        get => _selection;
        init => _selection = value ?? throw new ArgumentNullException(nameof(Selection));
    }

    public required MotifSelectionKind Kind { get; init; }

    public required MotifMode Mode { get; init; }

    public required MotifSourceReference Source { get; init; }

    public IReadOnlyList<string> NoteIds => Selection.NoteIds;

    public TickSpan Span => Selection.Span;

    /// <summary>The filter the notes were picked with: all notes, a hand, a track or a voice.</summary>
    public SelectionFilter Filter => Selection.Filter;

    /// <summary>
    /// Creates a motif from a selection. Throws when the selection holds no notes, or, for notes
    /// picked one by one, fewer than <see cref="MotifSelection.MinNotes"/> or more than
    /// <see cref="MotifSelection.MaxNotes"/>.
    /// </summary>
    /// <param name="notes">The notes of the piece; the source reference is taken from the selection's first note.</param>
    /// <param name="number">The motif's number in the project, used in the default name.</param>
    public static Motif Create(string id, int number, MotifSelection selection, MotifSelectionKind kind, MotifMode mode, IEnumerable<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(notes);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown selection kind.");
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode.");
        }

        if (selection.NoteIds.Count == 0)
        {
            throw new ArgumentException("The selection holds no notes.", nameof(selection));
        }

        if (kind == MotifSelectionKind.Notes && selection.NoteIds.Count is < MotifSelection.MinNotes or > MotifSelection.MaxNotes)
        {
            throw new ArgumentException($"A motif of single notes holds {MotifSelection.MinNotes} to {MotifSelection.MaxNotes} notes.", nameof(selection));
        }

        string first = selection.NoteIds[0];
        SourceLocation? firstNote = notes.FirstOrDefault(note => note.Id == first)?.Source;

        return new Motif
        {
            Id = id,
            Name = DefaultName(number, selection.FirstBar, selection.LastBar),
            Selection = selection,
            Kind = kind,
            Mode = mode,
            Source = new MotifSourceReference(selection.Span, selection.FirstBar, selection.LastBar, firstNote),
        };
    }

    /// <summary>The name a new motif gets, for example "Motif 1 · bars 5–6" or "Motif 2 · bar 3" (N3).</summary>
    public static string DefaultName(int number, int firstBar, int lastBar)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        return firstBar == lastBar ? $"Motif {number} · bar {firstBar}" : $"Motif {number} · bars {firstBar}–{lastBar}";
    }
}