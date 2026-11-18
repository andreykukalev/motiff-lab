using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// Notes of the piece that are not the working notes and not a version compared with them (decision record 0030,
/// section 1): the notes read again from a PDF next to the earlier ones, or the working notes before other Notes were made
/// the working ones. Each keeps what the working notes keep, so it can be made the working notes again with nothing lost.
/// </summary>
/// <param name="Id">The Notes' id among the piece's Notes.</param>
/// <param name="FileName">
/// The name its music is read by, as the piece's own <see cref="Project.FileName"/>: MusicXML or MIDI by the extension. Made the
/// working notes, they keep the piece's name with this extension, and kept again as other Notes, they take the piece's name.
/// </param>
/// <param name="Music">The notes' bytes, as an import or a scan gave them; never changed.</param>
/// <param name="Provenance">How the notes were made and from which originals.</param>
public sealed record OtherNotes(string Id, string FileName, byte[] Music, NotesProvenance Provenance)
{
    /// <summary>The player's name for them; null for the name their operation gives.</summary>
    public string? Name { get; init; }

    /// <summary>The player's corrections of them, kept with them.</summary>
    public NoteEditLog Edits { get; init; } = NoteEditLog.Empty;

    /// <summary>The bars to check, as the working notes keep them; null while not worked out yet.</summary>
    public IReadOnlyList<CheckFlag>? Check { get; init; }

    /// <summary>The scan they were read by, when a scan read them.</summary>
    public ScanOrigin? Scan { get; init; }

    /// <summary>The transcription they were heard by, when one heard them.</summary>
    public TranscriptionOrigin? Transcription { get; init; }

    /// <summary>Where each page of the piece's PDF starts in them, when they were read from it.</summary>
    public PageMap? Pages { get; init; }

    /// <summary>The other piece's PDF they were read from, before PDFs were kept in one piece (0023).</summary>
    public ReadFromPdf? ReadFrom { get; init; }

    /// <summary>
    /// The player's marks on the score drawn while they were the working notes (strokes, margin notes, highlights,
    /// fingering), which stay with the notes they were drawn on (sources sheet, 2026-11-14); none for notes never working.
    /// </summary>
    public Marks Marks { get; init; } = Marks.Empty;

    public bool Equals(OtherNotes? other) =>
        other is not null && Id == other.Id && FileName == other.FileName && Music.AsSpan().SequenceEqual(other.Music) && Provenance.Equals(other.Provenance)
        && Name == other.Name && SameWork(other) && Equals(Scan, other.Scan) && Equals(Transcription, other.Transcription);

    /// <summary>The same corrections, Check flags, page maps and marks.</summary>
    private bool SameWork(OtherNotes other) =>
        Edits.Edits.SequenceEqual(other.Edits.Edits) && (Check ?? []).SequenceEqual(other.Check ?? []) && (Check is null) == (other.Check is null)
        && Equals(Pages, other.Pages) && Equals(ReadFrom, other.ReadFrom) && Marks.Equals(other.Marks);

    public override int GetHashCode() => HashCode.Combine(Id, FileName, Music.Length);
}

/// <summary>
/// What version 16 adds to the piece beside its working notes and versions (decision record 0030): the tombstones of the
/// originals the player removed, the other Notes, and the bytes of the originals kept in the file other than the first PDF.
/// </summary>
public sealed record SourcesLayer
{
    /// <summary>None of them: a piece as every file before version 16 reads.</summary>
    public static SourcesLayer None { get; } = new();

    /// <summary>The originals the player removed, in the order they were removed.</summary>
    public IReadOnlyList<RemovedOriginal> Removed { get; init; } = [];

    /// <summary>The Notes that are neither the working notes nor a version, in the order they were added.</summary>
    public IReadOnlyList<OtherNotes> Notes { get; init; } = [];

    /// <summary>The bytes of each original kept in the file (kept "file") other than the first PDF, by the original's id.</summary>
    public IReadOnlyDictionary<string, byte[]> Data { get; init; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);

    /// <summary>
    /// The player's marks on the pages of each live PDF or photos other than the first (version 0.6; the first PDF's are
    /// <see cref="Project.DocumentMarks"/>), by the original's id; an original with none is left out.
    /// </summary>
    public IReadOnlyDictionary<string, DocumentMarks> Marks { get; init; } = new Dictionary<string, DocumentMarks>(StringComparer.Ordinal);

    /// <summary>The tombstone of the original <paramref name="id"/>; null while it is not removed.</summary>
    public RemovedOriginal? RemovedWith(string id) => Removed.FirstOrDefault(removed => removed.Original.Id == id);

    public bool Equals(SourcesLayer? other) =>
        other is not null && Removed.SequenceEqual(other.Removed) && Notes.SequenceEqual(other.Notes)
        && Data.Count == other.Data.Count && Data.All(pair => other.Data.TryGetValue(pair.Key, out byte[]? bytes) && bytes.AsSpan().SequenceEqual(pair.Value))
        && Marks.Count == other.Marks.Count && Marks.All(pair => other.Marks.TryGetValue(pair.Key, out DocumentMarks? marks) && marks.Equals(pair.Value));

    public override int GetHashCode() => HashCode.Combine(Removed.Count, Notes.Count, Data.Count, Marks.Count);
}