using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// A piece of a sync group joined into one (version 0.6.1, #582 item 1): <paramref name="Keeper"/> is the index of the project
/// that stays, <paramref name="Joined"/> the indices of the ones that join it and go, and <paramref name="Project"/> the
/// keeper as it is saved afterwards.
/// </summary>
public sealed record JoinedPiece(int Keeper, IReadOnlyList<int> Joined, Project Project);

/// <summary>
/// One piece, one entry in the library, whatever device or source it came from (version 0.6.1, #582 item 1; Andrei,
/// 2026-11-15: "one unified logical library across Web and iPad"). A piece's identity across devices is the files it was
/// made from, by their fingerprints (<see cref="Keys"/>), not its project id: a project id is new on each device that adds the
/// file, so the same PDF added on the web and on the iPad became two pieces once they synced. Two projects of the same piece
/// become one when one of them carries nothing of the player's own that the other lacks (<see cref="AddsNothingTo"/>): it
/// joins the other (<see cref="Join"/>), which keeps its PDF, its originals and its details. Two copies that both carry
/// their own work stay apart, as a conflict copy does: no work is ever dropped. Nothing new is saved: the rule reads the
/// originals' fingerprints and the place in a collection the project files already keep.
/// </summary>
public static class SamePiece
{
    /// <summary>
    /// The player's own fields of a project file (<see cref="FieldClass.Player"/> in <see cref="ProjectSync.Fields"/>), each
    /// compared by <see cref="AddsNothingTo"/>, so a field added later with no entry here fails its test.
    /// </summary>
    public static IReadOnlySet<string> Compared { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "documentMarks", "tempo", "motifs", "variations", "observations", "edits", "phrases", "marks", "bookmarks", "resumePoints",
        "settings", "performances", "assembly", "metadata", "notes.name", "notes.fileName", "notes.edits", "notes.performance",
        "workspace", "workspace.working", "workspace.metadata", "workspace.tempo", "workspace.motifs", "workspace.variations",
        "workspace.observations", "workspace.phrases", "workspace.marks", "workspace.bookmarks", "workspace.resumePoints",
        "workspace.assembly", "workspace.settings", "workspace.hiddenViews",
    };

    /// <summary>Each of the player's fields, as it is copied from one project onto another to be compared written.</summary>
    private static readonly Func<Project, Project, Project>[] s_work =
    [
        static (to, from) => to with { DocumentMarks = from.DocumentMarks, Sources = to.Sources with { Marks = from.Sources.Marks } },
        static (to, from) => to with { PlayerTempo = from.PlayerTempo },
        static (to, from) => to with { Motifs = from.Motifs },
        static (to, from) => to with { Variations = from.Variations },
        static (to, from) => to with { Observations = from.Observations },
        static (to, from) => to with { Edits = from.Edits },
        static (to, from) => to with { Phrases = from.Phrases, ShortPhrases = from.ShortPhrases, Sentences = from.Sentences },
        static (to, from) => to with { Marks = from.Marks },
        static (to, from) => to with { Bookmarks = from.Bookmarks, Categories = from.Categories },
        static (to, from) => to with { ResumePoints = from.ResumePoints },
        static (to, from) => to with { SettingOverrides = from.SettingOverrides },
        static (to, from) => to with { Performances = from.Performances },
        static (to, from) => to with { Assembly = from.Assembly },
        static (to, from) => to with { HiddenViews = from.HiddenViews },
        static (to, from) => to with { Sources = to.Sources with { Notes = from.Sources.Notes } },
    ];

    /// <summary>
    /// What the piece is known by on every device: the fingerprint of its PDF and of the file its notes were imported from,
    /// and of the collection it was cut from with its place there. A fingerprint the file does not keep yet (a PDF saved
    /// before 0.5) is worked out from the bytes only when <paramref name="fromBytes"/> is true, as a sync pass does for the
    /// few projects it compares; a library row takes only what the file keeps.
    /// </summary>
    public static IReadOnlyList<string> Keys(Project project, bool fromBytes = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        List<string> keys = [];
        foreach (PieceOriginal original in project.Originals)
        {
            bool part = project.PartOf is { } place && place.Original == original.Id;
            if (original.Id is not (PieceOriginal.DocumentId or PieceOriginal.SourceFileId) && !part)
            {
                continue;
            }

            string? sha = original.Sha256 ?? original.Id switch
            {
                PieceOriginal.DocumentId when fromBytes && project.Document is { } pdf => PieceOriginal.Fingerprint(pdf),
                // The imported file is the notes as they were picked: small, so its fingerprint is always worked out.
                PieceOriginal.SourceFileId when project.HasNotes => PieceOriginal.Fingerprint(project.Source),
                _ => null,
            };
            if (sha is not null)
            {
                keys.Add(part ? Key(sha, project.PartOf) : sha);
            }
        }

        return [.. keys.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>A fingerprint as a key, with the place in its collection for a piece cut from one ("sha#p3-5", "sha#ms0-90000").</summary>
    private static string Key(string sha, PartOf? place) => place switch
    {
        { Pages: { } pages } => $"{sha}#p{pages.First}-{pages.Last}",
        { Milliseconds: { } stretch } => $"{sha}#ms{stretch.From}-{stretch.To}",
        _ => sha,
    };

    /// <summary>
    /// The groups of projects that may be one piece, by the keys their library rows give (<see cref="ProjectSummary.PieceKeys"/>):
    /// projects that share a key, directly or through another, as their ids in the order given; only groups of two or more.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Candidates(IEnumerable<(string Id, IReadOnlyList<string> Keys)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        List<(string Id, IReadOnlyList<string> Keys)> all = [.. rows];
        int[] group = Groups(all.Select(static row => row.Keys).ToList());
        return [.. Enumerable.Range(0, all.Count).GroupBy(index => group[index]).Where(static each => each.Count() > 1)
            .Select(each => (IReadOnlyList<string>)[.. each.Select(index => all[index].Id)])];
    }

    /// <summary>
    /// Whether <paramref name="copy"/> carries nothing of its own that <paramref name="other"/> lacks: the same notes or none,
    /// its originals all among the other's, no Notes, removals or fields of a later build of its own, and each of the
    /// player's fields either empty or written the same as the other's. Its details (title, composer, tags, playlists) join
    /// the other's when <paramref name="joining"/>; else (a conflict between two copies of one project) they must be among
    /// the other's too. Which file the notes or PDF bytes are in, the id and the times are not the player's work.
    /// </summary>
    public static bool AddsNothingTo(Project copy, Project other, bool joining = true)
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(other);
        if (!SourcesAmong(copy, other) || (!joining && !DetailsAmong(copy.Metadata, other.Metadata)))
        {
            return false;
        }

        Project probe = Probe(other);
        foreach (Func<Project, Project, Project> field in s_work)
        {
            byte[] mine = ProjectFile.Write(field(probe, copy));
            if (!mine.AsSpan().SequenceEqual(ProjectFile.Write(field(probe, s_blank))) && !mine.AsSpan().SequenceEqual(ProjectFile.Write(probe)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <paramref name="keeper"/> with what <paramref name="copy"/> of the same piece brings: its details where the keeper has
    /// none (tags and playlists added), its originals' descriptions (<see cref="PieceOriginals.Merge"/>, this
    /// <paramref name="device"/>'s holding marks from the copy, whose bytes are here), and its PDF when the keeper's is on
    /// another device: linked again by its fingerprint, its pages spread over the notes. Null when the PDF cannot be linked
    /// to the keeper, so the copy is not joined and its bytes stay where they are.
    /// </summary>
    public static Project? Join(Project keeper, Project copy, HoldingDevice? device)
    {
        ArgumentNullException.ThrowIfNull(keeper);
        ArgumentNullException.ThrowIfNull(copy);
        PieceMetadata mine = keeper.Metadata;
        PieceMetadata details = new(mine.Title ?? copy.Metadata.Title, mine.Composer ?? copy.Metadata.Composer, [.. mine.Tags, .. copy.Metadata.Tags], [.. mine.Playlists, .. copy.Metadata.Playlists]);
        Project joined = details.Equals(mine) ? keeper : keeper with { Metadata = details };
        IReadOnlyList<PieceOriginal> originals = PieceOriginals.Merge(keeper.Originals, copy.Originals, device);
        if (!originals.SequenceEqual(joined.Originals))
        {
            joined = joined with { KeptOriginals = originals };
        }

        if (keeper.Document is not null || copy.Document is not { } pdf)
        {
            return joined;
        }

        if (joined.FindOriginal(PieceOriginal.DocumentId) is not { Kind: OriginalKind.Pdf })
        {
            return null;
        }

        return Pages(joined, copy, pdf) is { } pages ? joined.RelinkDocument(pdf, pages) : null;
    }

    /// <summary>
    /// The projects of one group joined as far as they can be: in the order they were made (then by id), so every device
    /// that has the same projects joins them alike, each joins a piece already kept when it adds nothing to it, or the kept
    /// piece and what joined it join this one when they add nothing to it. A previous copy kept beside its project in a
    /// conflict is never joined to it. A project in <paramref name="stay"/> (its bytes are
    /// linked on this device) is never joined away, though others may join it. Projects that do not share a key
    /// (<see cref="Keys"/>) are never one piece. Only the pieces some project joined are answered.
    /// </summary>
    public static IReadOnlyList<JoinedPiece> Plan(IReadOnlyList<Project> projects, IReadOnlySet<int>? stay = null, HoldingDevice? device = null)
    {
        ArgumentNullException.ThrowIfNull(projects);
        int[] group = Groups([.. projects.Select(static project => Keys(project, fromBytes: true))]);
        List<JoinedPiece> answer = [];
        foreach (IGrouping<int, int> each in Enumerable.Range(0, projects.Count).GroupBy(index => group[index]).Where(static each => each.Count() > 1))
        {
            List<Kept> kept = [];
            foreach (int index in each.OrderBy(index => projects[index].CreatedAt).ThenBy(index => projects[index].Id, StringComparer.Ordinal))
            {
                if (!Into(kept, index, projects, stay, device))
                {
                    kept.Add(new Kept(index, projects[index], []));
                }
            }

            answer.AddRange(kept.Where(static piece => piece.Joined.Count > 0).Select(static piece => new JoinedPiece(piece.Index, piece.Joined, piece.Project)));
        }

        return [.. answer.OrderBy(static piece => piece.Keeper)];
    }

    /// <summary>A piece kept so far in a plan: its project's index, the project with what joined it, and the indices that joined.</summary>
    private sealed record Kept(int Index, Project Project, List<int> Joined);

    /// <summary>Joins the project at <paramref name="index"/> to a piece kept, or a piece kept to it; false when neither adds nothing to the other.</summary>
    private static bool Into(List<Kept> kept, int index, IReadOnlyList<Project> projects, IReadOnlySet<int>? stay, HoldingDevice? device)
    {
        Project project = projects[index];
        bool goes = stay?.Contains(index) != true;
        for (int at = 0; at < kept.Count; at++)
        {
            Kept piece = kept[at];
            if (PreviousCopy(project, projects[piece.Index]))
            {
                continue;
            }

            if (goes && AddsNothingTo(project, projects[piece.Index]) && Join(piece.Project, project, device) is { } joined)
            {
                kept[at] = piece with { Project = joined, Joined = [.. piece.Joined, index] };
                return true;
            }

            if (Takes(project, piece, projects, stay, device) is { } taken)
            {
                kept[at] = new Kept(index, taken, [.. piece.Joined, piece.Index]);
                return true;
            }
        }

        return false;
    }

    /// <summary>The project with a kept piece and everything that joined it, when none of them stays or adds anything; else null.</summary>
    private static Project? Takes(Project project, Kept piece, IReadOnlyList<Project> projects, IReadOnlySet<int>? stay, HoldingDevice? device)
    {
        Project? taken = project;
        foreach (int other in (int[])[piece.Index, .. piece.Joined])
        {
            if (taken is null || stay?.Contains(other) == true || PreviousCopy(projects[other], project) || !AddsNothingTo(projects[other], project))
            {
                return null;
            }

            taken = Join(taken, projects[other], device);
        }

        return taken;
    }

    /// <summary>Where the PDF's pages start in the keeper's notes: the copy's own map when it has the same notes, else spread evenly.</summary>
    private static PageMap? Pages(Project keeper, Project copy, byte[] pdf)
    {
        if (!keeper.HasNotes)
        {
            return new PageMap([Time.Tick.Zero]);
        }

        if (copy.HasNotes && copy.DocumentPages is { } own)
        {
            return own;
        }

        try
        {
            ImportedPiece piece = OpenedProject.Open(keeper, EditsLayer.Off).Piece;
            return PageMap.Even(piece.Meter, piece.End, Math.Max(1, PdfDocument.PageCount(pdf)));
        }
        catch (Exception exception) when (exception is ProjectFileException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the two are one project and the previous copy a conflict kept beside it (<see cref="ProjectSync.PreviousCopy"/>,
    /// Andrei, 2026-11-11: "the previous copy is kept beside as a piece of its own"): it keeps the moment the project was made,
    /// which two pieces added apart never share. Those are never joined.
    /// </summary>
    private static bool PreviousCopy(Project one, Project other) => one.CreatedAt == other.CreatedAt;

    /// <summary>
    /// Whether the copy's place in a collection, notes, originals, removals and bytes are all the other's too, and it holds
    /// nothing a later build wrote that this one cannot compare.
    /// </summary>
    private static bool SourcesAmong(Project copy, Project other) =>
        copy.PartOf == other.PartOf && SameNotesOrNone(copy, other) && copy.Unknown.Count == 0 && copy.UnknownFields.Count == 0
        && (copy.ReadFrom is not { } read || read.ProjectId == other.ReadFrom?.ProjectId)
        && copy.Originals.All(original => other.Originals.Any(each => SameOriginal(original, each)))
        && copy.Sources.Removed.All(removed => other.Sources.RemovedWith(removed.Original.Id) is not null)
        && copy.Sources.Data.All(pair => other.Sources.Data.TryGetValue(pair.Key, out byte[]? bytes) && bytes.AsSpan().SequenceEqual(pair.Value));

    private static bool SameNotesOrNone(Project copy, Project other) =>
        !copy.HasNotes || (other.HasNotes && copy.NotesId == other.NotesId && copy.Source.AsSpan().SequenceEqual(other.Source));

    private static bool SameOriginal(PieceOriginal original, PieceOriginal other) =>
        original.Sha256 is { } sha && other.Sha256 is not null ? sha == other.Sha256 : original.Id == other.Id && original.Kind == other.Kind;

    private static bool DetailsAmong(PieceMetadata copy, PieceMetadata other) =>
        (copy.Title is null || copy.Title == other.Title) && (copy.Composer is null || copy.Composer == other.Composer)
        && copy.Tags.All(tag => other.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        && copy.Playlists.All(playlist => other.Playlists.Contains(playlist, StringComparer.OrdinalIgnoreCase));

    /// <summary>A project to write the player's fields on for comparing: the other's notes and originals, without its PDF bytes.</summary>
    private static Project Probe(Project project) =>
        project with { Document = null, KeptOriginals = [.. project.Originals], Sources = project.Sources with { Data = SourcesLayer.None.Data } };

    /// <summary>A project with none of the player's fields, whose empty values a copy's are compared with.</summary>
    private static readonly Project s_blank = Project.Create("p_blank", "blank.musicxml", [], DateTimeOffset.UnixEpoch);

    /// <summary>For each list of keys, the index of its group: lists that share a key, directly or through another, share one.</summary>
    private static int[] Groups(IReadOnlyList<IReadOnlyList<string>> keys)
    {
        int[] parent = [.. Enumerable.Range(0, keys.Count)];
        int Root(int index) => parent[index] == index ? index : parent[index] = Root(parent[index]);
        Dictionary<string, int> first = new(StringComparer.Ordinal);
        for (int index = 0; index < keys.Count; index++)
        {
            foreach (string key in keys[index])
            {
                if (first.TryGetValue(key, out int earlier))
                {
                    parent[Root(index)] = Root(earlier);
                }
                else
                {
                    first[key] = index;
                }
            }
        }

        // A project with no key is a group of its own.
        return [.. Enumerable.Range(0, keys.Count).Select(index => keys[index].Count == 0 ? -1 - index : Root(index))];
    }
}