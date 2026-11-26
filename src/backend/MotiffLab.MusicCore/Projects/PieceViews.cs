using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Projects;

/// <summary>Where an original's bytes are, or where the original of Notes is, as the Sources sheet says it (0030, section 3).</summary>
internal enum SourceState
{
    /// <summary>The bytes are on this device: in the project file, or held here.</summary>
    Here,

    /// <summary>Not here; another device holds them («Только на iPad»).</summary>
    OnAnotherDevice,

    /// <summary>Not here and held by no device the piece knows of.</summary>
    Nowhere,

    /// <summary>For Notes: the original's fingerprint is not the one the notes were made from («Оригинал изменился»).</summary>
    FileChanged,

    /// <summary>The player removed the original; its tombstone stays («Оригинал удалён · ноты остаются»).</summary>
    Removed,
}

/// <summary>What Notes are to the piece.</summary>
internal enum NotesRole
{
    /// <summary>The working notes, everything the player makes stands on.</summary>
    Working,

    /// <summary>Other Notes beside them: read again, or the working notes until others were made working.</summary>
    Other,

    /// <summary>A version compared with the working notes (0022).</summary>
    Version,
}

/// <summary>What a view of the switch at the top of the score shows.</summary>
internal enum ViewKind
{
    /// <summary>A PDF or photos as they are («PDF»).</summary>
    Original,

    /// <summary>Notes («Ноты»).</summary>
    Notes,

    /// <summary>A recording's sound with the notes heard in it («Исполнение»).</summary>
    Performance,

    /// <summary>A draft, its places' results as one score (decision record 0034, «вкладка черновика»).</summary>
    Draft,
}

/// <summary>An original as the Sources sheet lists it: live or removed, with where its bytes are and the Notes made from it.</summary>
/// <param name="RemovedAt">When the player removed it; null while it is live.</param>
/// <param name="Notes">The ids of the Notes made from it, in the order the sheet lists them.</param>
/// <param name="PageMarks">How many marks its pages have (strokes and bookmarks), kept with its tombstone when removed.</param>
internal sealed record SourceOriginal(PieceOriginal Original, SourceState State, DateTimeOffset? RemovedAt, bool Hidden, IReadOnlyList<string> Notes, int PageMarks);

/// <summary>Notes as the Sources sheet lists them: their role, provenance, corrections and the state of their original.</summary>
/// <param name="Name">The player's name for them, or a version's name; null for the working notes and unnamed Notes.</param>
/// <param name="Original">The original they were made from in this piece; null when none (another piece's PDF, an assembly, a manual entry).</param>
/// <param name="State">Their original's state; null without an original.</param>
/// <param name="Edits">How many corrections they carry.</param>
/// <param name="Taken">For a version, how many bars were taken from it into the working notes.</param>
internal sealed record SourceNotes(string Id, NotesRole Role, string? Name, NotesProvenance Provenance, string? Original, SourceState? State, int Edits, int Taken, bool Hidden);

/// <summary>A view of the switch: what it shows, the original it is grouped under, and whether it is working or hidden.</summary>
/// <param name="Id">The original's id for an original or a recording's sound, the Notes' id for Notes, the draft's for a draft.</param>
/// <param name="Group">The original whose group it is in («PDF» and the notes read from it, the working ones too); null for Notes of no original here.</param>
/// <param name="Operation">What made it, for its pictogram: the Notes' operation (<see cref="NotesProvenance.Op"/>), "pdf", "recording" or "draft".</param>
internal sealed record SourceView(string Id, ViewKind Kind, string? Group, bool Working, bool Hidden, string Operation);

/// <summary>
/// The piece's sources for the Sources sheet and the switch of views (decision record 0030, sections 3 and 7.5; sources
/// sheet, 2026-11-14), worked out on every call, never stored: the originals live and removed with where their bytes are,
/// the Notes with the state of their originals, and the ordered views of the switch.
/// </summary>
internal sealed record PieceViews(IReadOnlyList<SourceOriginal> Originals, IReadOnlyList<SourceNotes> Notes, IReadOnlyList<SourceView> Views)
{
    /// <summary>How many views the player hid from the switch.</summary>
    public int HiddenCount => Views.Count(static view => view.Hidden);

    /// <summary>
    /// The sources of <paramref name="project"/> as <paramref name="device"/> sees them. An original is here when its bytes
    /// are in the project file, the device holds it, or it is one of <paramref name="here"/> (the ids of the originals whose
    /// bytes the app has on this device, such as a recording's sound it keeps beside the file). The views, PDF before Notes
    /// (Andrei, 0.6.1 #582 item 2): by original, a PDF here (or photos) followed by the Notes read from it, the working ones
    /// first, a recording here followed by its sound and the Notes heard from it; then Notes of no original here, the
    /// working ones first; an assembly that is not working last; then the drafts in the order they were made (version 17).
    /// A recording or PDF that is not here, and a removed original, are no views.
    /// </summary>
    public static PieceViews Of(Project project, HoldingDevice device, IReadOnlyCollection<string>? here = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        here ??= [];
        List<SourceNotes> notes = NotesOf(project, device, here);
        List<SourceOriginal> originals =
        [
            .. project.Originals.Select(original => new SourceOriginal(original, Where(project, original, device, here), null, project.IsHidden(original.Id), MadeFrom(notes, original.Id), PageMarks(project, original.Id))),
            .. project.Sources.Removed.Select(removed => new SourceOriginal(removed.Original, SourceState.Removed, removed.At, Hidden: false, MadeFrom(notes, removed.Original.Id), removed.Marks.Ink.Count + removed.Marks.Bookmarks.Count)),
        ];
        List<SourceView> views = ViewsOf(notes, originals);
        views.AddRange(project.Drafts.Select(draft => new SourceView(draft.Id, ViewKind.Draft, null, Working: false, project.IsHidden(draft.Id), "draft")));
        return new PieceViews(originals, notes, views);
    }

    private static List<SourceNotes> NotesOf(Project project, HoldingDevice device, IReadOnlyCollection<string> here)
    {
        List<SourceNotes> notes = [];
        if (project.HasNotes)
        {
            notes.Add(Listed(project, project.NotesId, NotesRole.Working, null, project.WorkingProvenance, project.Edits.Edits.Count, 0, device, here));
        }

        notes.AddRange(project.Sources.Notes.Select(other => Listed(project, other.Id, NotesRole.Other, other.Name, other.Provenance, other.Edits.Edits.Count, 0, device, here)));
        notes.AddRange(project.Performances.Select(version => Listed(project, version.Id, NotesRole.Version, version.Name, project.ProvenanceOf(version), 0, version.Taken.Count, device, here)));
        return notes;
    }

    private static SourceNotes Listed(Project project, string id, NotesRole role, string? name, NotesProvenance provenance, int edits, int taken, HoldingDevice device, IReadOnlyCollection<string> here)
    {
        string? original = provenance.Original?.Original;
        SourceState? state = original is null ? null : StateOf(project, provenance.Original!, device, here);
        return new SourceNotes(id, role, name, provenance, original, state, edits, taken, role != NotesRole.Working && project.IsHidden(id));
    }

    /// <summary>The state of the original Notes were made from: removed, changed since, or where its bytes are.</summary>
    private static SourceState StateOf(Project project, ProvenanceSource source, HoldingDevice device, IReadOnlyCollection<string> here)
    {
        if (project.FindOriginal(source.Original) is not { } original)
        {
            return project.Sources.RemovedWith(source.Original) is not null ? SourceState.Removed : SourceState.Nowhere;
        }

        return source.Sha256 is { } made && original.Sha256 is { } now && made != now ? SourceState.FileChanged : Where(project, original, device, here);
    }

    private static SourceState Where(Project project, PieceOriginal original, HoldingDevice device, IReadOnlyCollection<string> here)
    {
        if (project.HasInFile(original) || original.IsHeldBy(device) || here.Contains(original.Id) || (original.Id == PieceOriginal.DocumentId && project.Document is not null))
        {
            return SourceState.Here;
        }

        return original.Held.Count > 0 ? SourceState.OnAnotherDevice : SourceState.Nowhere;
    }

    private static List<string> MadeFrom(List<SourceNotes> notes, string original) =>
        [.. notes.Where(each => each.Original == original).Select(static each => each.Id)];

    private static int PageMarks(Project project, string id) =>
        id == PieceOriginal.DocumentId ? project.DocumentMarks.Ink.Count + project.DocumentMarks.Bookmarks.Count : 0;

    private static List<SourceView> ViewsOf(List<SourceNotes> notes, List<SourceOriginal> originals)
    {
        List<SourceView> views = [];
        HashSet<string> placed = [];
        foreach (SourceOriginal original in originals.Where(static each => each.State == SourceState.Here))
        {
            ViewKind? kind = original.Original.Kind switch
            {
                OriginalKind.Pdf or OriginalKind.Images => ViewKind.Original,
                OriginalKind.Audio => ViewKind.Performance,
                _ => null,
            };
            if (kind is { } shown)
            {
                views.Add(new SourceView(original.Original.Id, shown, original.Original.Id, Working: false, original.Hidden, shown == ViewKind.Original ? "pdf" : "recording"));
            }

            IEnumerable<SourceNotes> made = notes.Where(each => each.Original == original.Original.Id && !placed.Contains(each.Id) && (IsWorking(each) || !IsAssembly(each)));
            foreach (SourceNotes each in made.OrderBy(static each => IsWorking(each) ? 0 : 1))
            {
                views.Add(new SourceView(each.Id, ViewKind.Notes, original.Original.Id, IsWorking(each), !IsWorking(each) && each.Hidden, each.Provenance.Op));
                placed.Add(each.Id);
            }
        }

        IEnumerable<SourceNotes> rest = notes.Where(each => !placed.Contains(each.Id));
        views.AddRange(rest.OrderBy(static each => IsWorking(each) ? 0 : IsAssembly(each) ? 2 : 1)
            .Select(static each => new SourceView(each.Id, ViewKind.Notes, null, IsWorking(each), !IsWorking(each) && each.Hidden, each.Provenance.Op)));
        return views;
    }

    private static bool IsAssembly(SourceNotes notes) => notes.Provenance.Op == NotesProvenance.Assembly;

    private static bool IsWorking(SourceNotes notes) => notes.Role == NotesRole.Working;
}