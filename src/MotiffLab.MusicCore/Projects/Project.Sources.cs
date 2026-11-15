namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// An original the player removed (decision record 0030, section 4): its description and the marks on its pages stay, so
/// the Notes made from it keep their provenance and show it removed, and adding the same file again (the same fingerprint)
/// revives it with its marks. Its bytes are gone from this device; a linked file was only unlinked, never touched.
/// </summary>
/// <param name="Original">The description as it was when removed.</param>
/// <param name="At">When it was removed.</param>
public sealed record RemovedOriginal(PieceOriginal Original, DateTimeOffset At)
{
    /// <summary>The player's marks on its pages when it was a PDF or photos, kept for its revival.</summary>
    public DocumentMarks Marks { get; init; } = DocumentMarks.Empty;
}

/// <summary>
/// The layers of decision record 0030 over the shape both apps use (project file version 16): the working notes are
/// <see cref="Source"/> with <see cref="Scan"/> or <see cref="Transcription"/>, <see cref="Edits"/> and <see cref="Check"/>;
/// the compared Notes are <see cref="Performances"/>; the first PDF is <see cref="Document"/>. What only version 16 says
/// lives here: the working notes' id and provenance, the tombstones of removed originals and the fields kept unknown.
/// </summary>
public sealed partial record Project
{
    /// <summary>The id the working notes get when none was read: the notes of a file of an earlier version, or of a new piece.</summary>
    public const string FirstNotesId = "n_1";

    /// <summary>The working notes' id among the piece's Notes (version 16); <see cref="FirstNotesId"/> until a file says otherwise.</summary>
    public string NotesId { get; init => field = !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Notes have an id.", nameof(NotesId)); } = FirstNotesId;

    /// <summary>
    /// The working notes' provenance as the file last said it (version 16), kept while it still tells of the notes' scan,
    /// transcription or import (<see cref="NotesProvenance.Kept"/>); null for a piece this build made, whose provenance is the
    /// one its data shows (<see cref="DerivedProvenance"/>).
    /// </summary>
    public NotesProvenance? KeptProvenance { get; init; }

    /// <summary>The working notes' provenance: the kept one while it still tells of the notes, else the one their data shows.</summary>
    public NotesProvenance WorkingProvenance => NotesProvenance.Kept(KeptProvenance, DerivedProvenance);

    /// <summary>
    /// The working notes' provenance as their data shows it: a scan's (<see cref="NotesProvenance.PdfRecognition"/>), a
    /// transcription's (<see cref="NotesProvenance.AudioRecognition"/>), else an import, from the imported file when the piece
    /// has one; each original with its fingerprint as the piece knows it now.
    /// </summary>
    public NotesProvenance DerivedProvenance => NotesProvenance.OfWorkingNotes(this);

    /// <summary>A compared Notes' provenance: the kept one while it still tells of the version, else the one its data shows.</summary>
    public NotesProvenance ProvenanceOf(SavedPerformance performance) => NotesProvenance.Kept(performance?.Provenance, NotesProvenance.OfVersion(this, performance!));

    /// <summary>
    /// The views the player hid from the switch at the top of the score (version 16; Andrei, 2026-11-14): ids of originals
    /// and Notes, distinct, in the order of their ids. The working notes are never hidden (<see cref="ViewHidden"/>); an id
    /// this build does not know is kept, so a later build's view stays hidden.
    /// </summary>
    public IReadOnlyList<string> HiddenViews
    {
        get;
        init => field = value.Any(string.IsNullOrWhiteSpace) ? throw new ArgumentException("A view has an id.", nameof(HiddenViews))
            : [.. value.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    } = [];

    /// <summary>Whether the view <paramref name="id"/> is hidden from the switch: never the working notes.</summary>
    public bool IsHidden(string id) => id != NotesId && HiddenViews.Contains(id, StringComparer.Ordinal);

    /// <summary>
    /// The piece with the view <paramref name="id"/> hidden from the switch or shown again; the working notes cannot be
    /// hidden while they are the working ones.
    /// </summary>
    public Project ViewHidden(string id, bool hidden)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (hidden && HasNotes && id == NotesId)
        {
            throw new InvalidOperationException("The working notes are always in the switch.");
        }

        return HiddenViews.Contains(id, StringComparer.Ordinal) == hidden ? this
            : this with { HiddenViews = hidden ? [.. HiddenViews, id] : [.. HiddenViews.Where(view => view != id)] };
    }

    /// <summary>The originals the player removed, in the order they were removed (version 16).</summary>
    public IReadOnlyList<RemovedOriginal> RemovedOriginals { get; init; } = [];

    /// <summary>
    /// The fields of the file this build does not know, by where they stand: "" for the top level, "workspace", "notes/" and
    /// "originals/" and the entity's id (0026 S6; version 16). Written back after the fields this build writes there.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<UnknownField>> UnknownFields { get; init; } = s_noFields;

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<UnknownField>> s_noFields = new Dictionary<string, IReadOnlyList<UnknownField>>(StringComparer.Ordinal);

    /// <summary>The unknown fields kept at <paramref name="path"/>; none when there are none.</summary>
    public IReadOnlyList<UnknownField> UnknownFieldsAt(string path) => UnknownFields.TryGetValue(path, out IReadOnlyList<UnknownField>? fields) ? fields : [];

    /// <summary>
    /// The piece's notes the way its file keeps provenance from now on: each with the provenance its data shows now kept, so
    /// a later change of an original (another PDF under the same id, before version 16) shows as "file changed". The
    /// reader does this to every file it reads.
    /// </summary>
    public Project WithProvenanceKept() => this with
    {
        KeptProvenance = HasNotes ? WorkingProvenance : KeptProvenance,
        Performances = [.. Performances.Select(performance => performance with { Provenance = ProvenanceOf(performance) })],
    };
}