using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Projects;

/// <summary>What a device does next with one project against its copy saved online (version 0.2 sync).</summary>
internal enum SyncStep
{
    /// <summary>Both copies are as they were when the device last synced them, or the piece is not synced.</summary>
    Nothing,

    /// <summary>Only this device changed it, or it is not online yet: send it.</summary>
    Upload,

    /// <summary>Only the online copy changed, or it is not here yet: fetch it and keep it here.</summary>
    Download,

    /// <summary>Both changed it since they last met, or they never met: fetch the online copy and <see cref="ProjectSync.Resolve"/>.</summary>
    Resolve,

    /// <summary>The online copy was deleted on another device and nothing changed here since: stop syncing it, keep it here.</summary>
    Forget,

    /// <summary>The player removed the piece here and the online copy is as this device last saw it: delete it online too (the 1.6 plan's decision 14).</summary>
    DeleteOnline,
}

/// <summary>What a device knows about one project when it syncs: whether each copy exists and changed since it last synced it.</summary>
/// <param name="Here">The project is on this device.</param>
/// <param name="Online">The project is listed online.</param>
/// <param name="Known">This device synced it before, so it has a base: the online copy's ETag and save time it last saw.</param>
/// <param name="ChangedHere">Saved on this device since it last synced (or never synced).</param>
/// <param name="ChangedOnline">The online copy's save time is not the one this device saw last.</param>
/// <param name="DeletedOnline">This device saw the online copy go and keeps its own without sending it again.</param>
/// <param name="DeletedHere">The player removed the piece on this device, and its online copy is still to be deleted.</param>
internal sealed record SyncFacts(bool Here, bool Online, bool Known, bool ChangedHere, bool ChangedOnline, bool DeletedOnline = false, bool DeletedHere = false);

/// <summary>How the merge treats a field of the project file (0026 S8).</summary>
internal enum FieldClass
{
    /// <summary>The player's own data: on a conflict over the same field both values are kept.</summary>
    Player,

    /// <summary>Bookkeeping such as when the piece was last opened: the latest write wins (V21).</summary>
    Bookkeeping,

    /// <summary>Worked out again from the rest, such as the Check flags: never a conflict (V22).</summary>
    Derived,

    /// <summary>What an import brought, such as the scan's page reports: on a pull this device's stays when the online copy has none and the notes are the same import.</summary>
    Import,
}

/// <summary>What resolving two copies of one project keeps.</summary>
/// <param name="Kept">The project to keep on this device.</param>
/// <param name="Upload">True when <paramref name="Kept"/> goes online over the online copy (the device's copy won).</param>
/// <param name="PreviousCopy">The copy that lost, kept beside as a piece of its own with a new id; null when nothing was lost.</param>
internal sealed record SyncResolution(Project Kept, bool Upload, Project? PreviousCopy)
{
    /// <summary>What of the copy that lost the merge <paramref name="Kept"/> does not hold, and so <paramref name="PreviousCopy"/> keeps (<see cref="ProjectMerge"/>).</summary>
    public IReadOnlyList<MergeConflict> Conflicts { get; init; } = [];

    /// <summary>False when the two copies were not merged entity by entity (<see cref="ProjectMerge.Merged"/> failed its self-check, or one is a later build's): nothing is sent, and the other copy is kept beside.</summary>
    public bool Merged { get; init; } = true;
}

/// <summary>
/// The rules both apps sync projects by (version 0.2, decided by Andrei 2026-11-11: new pieces and their changes go between
/// the devices by themselves; when both changed one, the last save wins and the previous copy is kept). The device stays the
/// working copy: it keeps for each project the online copy's ETag and save time it last saw (outside the project file), sends
/// what changed here with that ETag (<c>If-Match</c>), a piece new here only as a new one online, and fetches what changed
/// online. A project the player removes on this device is deleted online too (version 1.6), unless it changed online since;
/// one deleted online stays here. Two copies both changed merge entity by entity (<see cref="ProjectMerge"/>, version 1.6).
/// A copy a later build saved (<see cref="Project.IsReadOnly"/>) is kept here read-only and never sent over by this build.
/// </summary>
internal static class ProjectSync
{
    /// <summary>
    /// The class of every top-level field of the project file, and of the fields within one that a version added on its own
    /// (by path, such as "transcription.bars.meters"), so that a field with none fails its test (0026 S8).
    /// </summary>
    public static IReadOnlyDictionary<string, FieldClass> Fields { get; } = new Dictionary<string, FieldClass>(StringComparer.Ordinal)
    {
        ["format"] = FieldClass.Bookkeeping,
        ["version"] = FieldClass.Bookkeeping,
        ["id"] = FieldClass.Bookkeeping,
        // Version 18 (#317): the ids a re-keyed piece was known by before, for the sync to follow it.
        ["formerIds"] = FieldClass.Bookkeeping,
        ["fileName"] = FieldClass.Import,
        ["createdAt"] = FieldClass.Bookkeeping,
        ["lastOpened"] = FieldClass.Bookkeeping,
        // Version 18: the oldest build that reads the file, this build's (0026 S6).
        ["minReader"] = FieldClass.Bookkeeping,
        ["source"] = FieldClass.Import,
        ["document"] = FieldClass.Import,
        ["documentPages"] = FieldClass.Import,
        ["documentMarks"] = FieldClass.Player,
        ["readFrom"] = FieldClass.Import,
        ["originals"] = FieldClass.Import,
        ["partOf"] = FieldClass.Import,
        ["editsOff"] = FieldClass.Bookkeeping,
        ["tempo"] = FieldClass.Player,
        ["motifs"] = FieldClass.Player,
        ["variations"] = FieldClass.Player,
        ["observations"] = FieldClass.Player,
        ["edits"] = FieldClass.Player,
        ["phrases"] = FieldClass.Player,
        ["marks"] = FieldClass.Player,
        ["bookmarks"] = FieldClass.Player,
        ["resumePoints"] = FieldClass.Player,
        ["sessions"] = FieldClass.Bookkeeping,
        ["scan"] = FieldClass.Import,
        ["scan.pages"] = FieldClass.Import,
        ["transcription"] = FieldClass.Import,
        ["transcription.bars.meters"] = FieldClass.Import,
        ["transcription.sentFromMs"] = FieldClass.Import,
        ["check"] = FieldClass.Derived,
        ["settings"] = FieldClass.Player,
        ["performances"] = FieldClass.Player,
        ["assembly"] = FieldClass.Player,
        ["metadata"] = FieldClass.Player,

        // Version 16 (0030): the Notes are what an import brought, with the player's corrections and the derived Check flags,
        // and the workspace is the player's, except whether the corrections are off.
        ["notes"] = FieldClass.Import,
        ["notes.id"] = FieldClass.Bookkeeping,
        ["notes.name"] = FieldClass.Player,

        ["notes.fileName"] = FieldClass.Player,
        ["notes.provenance"] = FieldClass.Import,
        ["notes.music"] = FieldClass.Import,
        ["notes.edits"] = FieldClass.Player,
        ["notes.check"] = FieldClass.Derived,
        ["notes.scan"] = FieldClass.Import,
        ["notes.scan.pages"] = FieldClass.Import,
        ["notes.scan.confirmedBars"] = FieldClass.Import,
        ["notes.scan.unread"] = FieldClass.Import,
        ["notes.scan.read"] = FieldClass.Import,
        // Version 18: the player's marks «что не так», merged by kind, bar and staff (the 1.6 plan's decision 16).
        ["notes.scan.marks"] = FieldClass.Player,
        ["notes.transcription"] = FieldClass.Import,
        ["notes.transcription.bars.meters"] = FieldClass.Import,
        ["notes.transcription.sentFromMs"] = FieldClass.Import,
        ["notes.pageMap"] = FieldClass.Import,
        ["notes.performance"] = FieldClass.Player,
        // The marks on the score drawn while other Notes were the working ones, as workspace.marks.
        ["notes.marks"] = FieldClass.Player,
        // Version 16 (0030): an original's description is what an import brought; the marks on a PDF's pages are the
        // player's (version 0.4), merged mark by mark.
        ["originals.id"] = FieldClass.Bookkeeping,
        ["originals.kind"] = FieldClass.Import,
        ["originals.name"] = FieldClass.Import,
        ["originals.fileName"] = FieldClass.Import,
        ["originals.folder"] = FieldClass.Import,
        ["originals.sha256"] = FieldClass.Import,
        ["originals.bytes"] = FieldClass.Import,
        ["originals.pages"] = FieldClass.Import,
        ["originals.durationMs"] = FieldClass.Import,
        ["originals.addedAt"] = FieldClass.Import,
        ["originals.kept"] = FieldClass.Import,
        ["originals.held"] = FieldClass.Import,
        ["originals.removed"] = FieldClass.Import,
        ["originals.data"] = FieldClass.Import,
        ["originals.marks"] = FieldClass.Player,
        ["workspace"] = FieldClass.Player,
        ["workspace.working"] = FieldClass.Player,
        ["workspace.metadata"] = FieldClass.Player,
        ["workspace.partOf"] = FieldClass.Import,
        ["workspace.editsOff"] = FieldClass.Bookkeeping,
        ["workspace.tempo"] = FieldClass.Player,
        ["workspace.motifs"] = FieldClass.Player,
        ["workspace.variations"] = FieldClass.Player,
        ["workspace.observations"] = FieldClass.Player,
        ["workspace.phrases"] = FieldClass.Player,
        ["workspace.marks"] = FieldClass.Player,
        ["workspace.bookmarks"] = FieldClass.Player,
        ["workspace.resumePoints"] = FieldClass.Player,
        ["workspace.assembly"] = FieldClass.Player,
        ["workspace.drafts"] = FieldClass.Player,
        ["workspace.settings"] = FieldClass.Player,
        ["workspace.hiddenViews"] = FieldClass.Player,
    };

    /// <summary>The next step for a project from what the device knows about it.</summary>
    public static SyncStep Next(SyncFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return (facts.Here, facts.Online) switch
        {
            (true, true) when !facts.Known => SyncStep.Resolve,
            (true, true) => (facts.ChangedHere, facts.ChangedOnline) switch
            {
                (false, false) => SyncStep.Nothing,
                (true, false) => SyncStep.Upload,
                (false, true) => SyncStep.Download,
                (true, true) => SyncStep.Resolve,
            },
            // Not online: new here, sent; or deleted online elsewhere, kept here unless it changed here since.
            (true, false) when !facts.Known => SyncStep.Upload,
            (true, false) when facts.DeletedOnline => facts.ChangedHere ? SyncStep.Upload : SyncStep.Nothing,
            (true, false) => facts.ChangedHere ? SyncStep.Upload : SyncStep.Forget,
            // Not here: new online, fetched; removed here by the player, deleted online unless it changed online since.
            (false, true) when !facts.Known => SyncStep.Download,
            (false, true) when facts.DeletedHere => facts.ChangedOnline ? SyncStep.Download : SyncStep.DeleteOnline,
            (false, true) => facts.ChangedOnline ? SyncStep.Download : SyncStep.Nothing,
            (false, false) => SyncStep.Forget,
        };
    }

    /// <summary>
    /// The online copy as it is kept here: a PDF stays on the device (0026 C3), so when the online copy has none, the
    /// device's own PDF and its pages stay with the piece. The marks on the PDF's pages (version 0.4) are the online copy's,
    /// as every other field is: a pull is of a copy changed online only, and two iPads that hold the PDF both write them, so
    /// keeping this one's would drop what the other wrote since (v1.6 r-3). Unless the online copy has none and this device
    /// has some, as a copy a build before 0.4 sent (it leaves them out), so they are never wiped by it. So are the fields
    /// version 0.4 adds (<see cref="KeptAdditions"/>).
    /// </summary>
    /// <remarks>
    /// The originals' descriptions (version 0.3) are a union of both copies by fingerprint, else by id, so an original
    /// one device knows is never lost; <paramref name="device"/>'s own holding marks come from <paramref name="here"/>
    /// and every other device's from the online copy (<see cref="PieceOriginals.Merge"/>).
    /// </remarks>
    public static Project Pulled(Project online, Project? here, HoldingDevice? device = null)
    {
        ArgumentNullException.ThrowIfNull(online);
        if (here is null || !SameProject(here, online))
        {
            return online;
        }

        bool keepsPdf = here.Document is not null && online.Document is null;
        Project kept = keepsPdf ? online with { Document = here.Document, DocumentPages = here.DocumentPages } : online;
        IReadOnlyList<PieceOriginal> originals = PieceOriginals.Merge(online.Originals, here.Originals, device);
        if (!originals.SequenceEqual(kept.Originals))
        {
            kept = kept with { KeptOriginals = originals };
        }

        kept = KeptAdditions(!HasPageMarks(online) && HasPageMarks(here) ? kept with { DocumentMarks = here.DocumentMarks } : kept, here);
        // Version 16: the bytes of the other originals stay on the device as the PDF does, and a removal travels as its tombstone.
        kept = here.Sources.Data.Count == 0 ? kept : kept with { Sources = kept.Sources with { Data = here.Sources.Data } };
        // The marks on another PDF's pages go with the file; a copy with none for an original never clears this device's.
        if (here.Sources.Marks.Keys.Except(kept.Sources.Marks.Keys).ToList() is { Count: > 0 } mine)
        {
            Dictionary<string, DocumentMarks> marks = new(kept.Sources.Marks, StringComparer.Ordinal);
            mine.ForEach(id => marks[id] = here.Sources.Marks[id]);
            kept = kept with { Sources = kept.Sources with { Marks = marks } };
        }

        return SourcesMerge.WithTombstones(kept, here.Sources.Removed);
    }

    /// <summary>
    /// What versions 0.4 and 0.5 add within version 15 and a build before them drops on writing, kept from this device when
    /// the online copy has none: the piece's place in its collection (<see cref="Project.PartOf"/>), the scan server's page
    /// reports (<see cref="ScanOrigin.Pages"/>), a recording's changes of signature (<see cref="BarGrid.Meters"/>) and where
    /// in its file the stretch heard started (<see cref="Import.Transcription.TranscriptionOrigin.SentFromMilliseconds"/>) while the notes are the
    /// same import. A margin on a stroke goes with the file, as every stroke does; whether the corrections
    /// are off (<see cref="Project.EditsOff"/>) is bookkeeping, and the copy saved last says it.
    /// </summary>
    private static Project KeptAdditions(Project kept, Project here)
    {
        if (kept.PartOf is null && here.PartOf is { } partOf)
        {
            kept = kept with { PartOf = partOf };
        }

        if (kept.Scan is { Pages.Count: 0 } scan && here.Scan is { Pages.Count: > 0 } mine && kept.Source.AsSpan().SequenceEqual(here.Source))
        {
            kept = kept with { Scan = scan with { Pages = mine.Pages } };
        }

        // Version 0.5: the changes of signature of a recording's bars, when the online copy has the same grid without them.
        if (kept.Transcription is { Bars: { Meters.Count: 0 } theirs } heard && here.Transcription?.Bars is { Meters.Count: > 0 } bars
            && theirs.Equals(bars with { Meters = [] }) && kept.Source.AsSpan().SequenceEqual(here.Source))
        {
            kept = kept with { Transcription = heard with { Bars = theirs with { Meters = bars.Meters } } };
        }

        // Version 0.5: where in the recording file the stretch heard started, when the online copy does not say.
        if (kept.Transcription is { SentFromMilliseconds: null } online && here.Transcription?.SentFromMilliseconds is { } sentFrom
            && kept.Source.AsSpan().SequenceEqual(here.Source))
        {
            kept = kept with { Transcription = online with { SentFromMilliseconds = sentFrom } };
        }

        return kept;
    }

    private static bool HasPageMarks(Project project) => !project.DocumentMarks.IsEmpty || project.DocumentMarks.Unknown.Count > 0;

    /// <summary>
    /// Two copies of one project, both changed, merged entity by entity against <paramref name="base"/> (version 1.6,
    /// <see cref="ProjectMerge"/>): what one copy changed comes into the other, and where both changed one thing the copy
    /// saved last keeps its own and the merge records the conflict. Nothing is ever lost: unless the merge met no conflict
    /// and holds every change the other copy made since the base (its self-check, <see cref="MergeCheck"/>), the other copy
    /// is kept beside as a piece of its own named by <paramref name="copyTitle"/> ("{0}" stands for its title). In the merge
    /// the online copy brings its own marks on the PDF's pages and this device's copy its own when it writes them
    /// (<see cref="Project.WritesDocumentMarks"/>), so another iPad's come in mark by mark. A merge that fails its self-check (0026 C5) is not sent: the copy saved last is
    /// kept here with the other's Notes and corrections, as before version 1.6, and the other beside unless it adds nothing
    /// (<see cref="SamePiece.AddsNothingTo"/>). Two copies that are the same keep this device's. A copy re-keyed from the
    /// other's id (<see cref="Project.FormerIds"/>) with the same creation time is the same project, and keeps the online id.
    /// A different piece that only carries the same id (an old numbered id, V8) is no previous copy: the online one is kept
    /// beside under a new id as it is, and this device's goes online; when either is a later build's, the online one is kept
    /// here as it came and this device's beside instead. A later build's copy (<see cref="Project.IsReadOnly"/>) is never
    /// merged nor sent (the 1.6 plan's decision 14): the online copy is kept as a pull keeps it and this device's beside
    /// (<see cref="Apart"/>).
    /// </summary>
    /// <param name="here">The project on this device.</param>
    /// <param name="savedHere">When it was saved here last.</param>
    /// <param name="online">The online copy, as fetched.</param>
    /// <param name="savedOnline">When the online copy was saved, as the API lists it.</param>
    /// <param name="copyTitle">The previous copy's title, with "{0}" for the piece's title.</param>
    /// <param name="newId">The previous copy's id (<c>MusicCoreFacade.NewProjectId</c>).</param>
    /// <param name="device">This device, whose marks of holding an original are its own (version 0.3); null keeps the online marks.</param>
    /// <param name="base">
    /// The copy both last agreed on, when the device keeps one (0026 S5): with it, what one side removed or undid since goes
    /// through the merge; without it, the merge is a union with no deletions (S4), the marks on a PDF's pages too.
    /// </param>
    /// <param name="sendsKept">
    /// Whether the app sends <see cref="SyncResolution.Kept"/> when <see cref="SyncResolution.Upload"/> is true. One that
    /// sends its own copy instead (the iPad before version 1.6's c-17b) is asked to send only when its copy was saved last,
    /// and then keeps the online copy beside unless it adds nothing, as before version 1.6, so nothing only online is lost.
    /// When it does not send, the file it keeps is never sent and the next pull writes over it, so its own copy is kept
    /// beside unless it adds nothing to the online one.
    /// </param>
    public static SyncResolution Resolve(Project here, DateTimeOffset savedHere, Project online, DateTimeOffset savedOnline, string copyTitle, string newId, HoldingDevice? device = null, Project? @base = null, bool sendsKept = true)
    {
        ArgumentNullException.ThrowIfNull(here);
        ArgumentNullException.ThrowIfNull(online);
        ArgumentNullException.ThrowIfNull(copyTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(newId);
        // The online copy as a pull keeps it, with its own page marks, so the merge sees another iPad's.
        Project fetched = Pulled(online, here, device);
        if (Apart(here, online, fetched, copyTitle, newId) is { } apart)
        {
            return apart;
        }

        // This device's copy wins with the page marks it does not write as the pull brings them, so it never sends old ones,
        // and with the originals of both (PieceOriginals.Merge, as the pull unites them), so an original only online stays.
        Project mine = fetched.WritesDocumentMarks(device) || here.DocumentMarks.Equals(fetched.DocumentMarks) ? here : here with { DocumentMarks = fetched.DocumentMarks };
        mine = mine.Originals.SequenceEqual(fetched.Originals) ? mine : mine with { KeptOriginals = fetched.Originals };
        bool hereWins = savedHere >= savedOnline;
        (Project winner, Project loser, Project lost) = hereWins ? (mine, fetched, online) : (fetched, here, here);
        if (ProjectMerge.Merged(winner, loser, @base) is not { } merged)
        {
            // The app that sends "file" sends it on the next pass; one that does not never sends what it keeps (as below).
            Project unmerged = OnlineId(SourcesMerge.Merged(winner, loser, @base).Project, here, online);
            Project? beside = sendsKept ? Lost(lost, unmerged, copyTitle, newId) : Lost(here, fetched, copyTitle, newId);
            return new SyncResolution(unmerged, Upload: false, beside) { Merged = false };
        }

        Project kept = OnlineId(merged.Project, here, online);
        bool upload = hereWins || (sendsKept && !ProjectFile.Write(kept).AsSpan().SequenceEqual(ProjectFile.Write(fetched)));
        // An app that sends its own copy rather than the one kept (before c-17b) would drop what only the online copy has
        // when it sends, and what only its own has when it does not, as it never sends the file it keeps then.
        Project? previous = merged.Conflicts.Count > 0 ? PreviousCopy(lost, copyTitle, newId)
            : sendsKept ? null
            : upload ? Lost(online, here, copyTitle, newId) : Lost(here, fetched, copyTitle, newId);
        return new SyncResolution(kept, upload, previous) { Conflicts = merged.Conflicts };
    }

    /// <summary>
    /// Two copies that are not merged, or null: of different pieces (see <see cref="Resolve"/>); the same, this device's kept;
    /// or one a later build's (<see cref="Project.IsReadOnly"/>), which this build neither merges nor sends, as a merged file it
    /// could not send would be written over by the next pull. Then the online copy is kept here as a pull keeps it, read-only
    /// when it is a later build's (the 1.6 plan's decisions 14 and 27), and this device's beside as a piece of its own, so the
    /// next pass sends it as a new one: always when it is a later build's (what that build added is not compared), else
    /// unless it adds nothing.
    /// </summary>
    private static SyncResolution? Apart(Project here, Project online, Project fetched, string copyTitle, string newId)
    {
        bool later = online.IsReadOnly || here.IsReadOnly;
        if (!SameProject(here, online))
        {
            return later
                ? new SyncResolution(online, Upload: false, PreviousCopy: here with { Id = newId })
                : new SyncResolution(here, Upload: true, PreviousCopy: online with { Id = newId });
        }

        if (ProjectFile.Write(fetched).AsSpan().SequenceEqual(ProjectFile.Write(here)))
        {
            return new SyncResolution(here, Upload: false, PreviousCopy: null);
        }

        if (!later)
        {
            return null;
        }

        Project? beside = here.IsReadOnly ? PreviousCopy(here, copyTitle, newId) : Lost(here, fetched, copyTitle, newId);
        return new SyncResolution(fetched, Upload: false, beside) { Merged = false };
    }

    /// <summary>
    /// Whether the two copies are of one project: the same id, or one re-keyed from the other's (<see cref="Project.FormerIds"/>,
    /// #317), and made at the same time, so a different piece that carried the same old numbered id (V8) is not.
    /// </summary>
    private static bool SameProject(Project here, Project online) =>
        here.CreatedAt == online.CreatedAt
        && (here.Id == online.Id || here.FormerIds.Contains(online.Id, StringComparer.Ordinal) || online.FormerIds.Contains(here.Id, StringComparer.Ordinal));

    /// <summary>The merged project under the online copy's id, which a project saved online keeps (the 1.6 plan's decision 9), this device's id among those it was known by.</summary>
    private static Project OnlineId(Project kept, Project here, Project online) =>
        kept.Id == online.Id ? kept : kept with
        {
            Id = online.Id,
            FormerIds = [.. kept.FormerIds.Concat(online.FormerIds).Append(here.Id).Where(id => id != online.Id).Distinct(StringComparer.Ordinal)],
        };

    /// <summary>
    /// The copy that lost as a previous copy, or null when it carries nothing of the player's that the copy kept lacks
    /// (version 0.6.1, #582 item 1): the same piece only opened on both devices differs in when it was last opened, and a
    /// copy of it beside would be the same piece twice in the library.
    /// </summary>
    private static Project? Lost(Project loser, Project kept, string copyTitle, string newId) =>
        SamePiece.AddsNothingTo(loser, kept, joining: false) ? null : PreviousCopy(loser, copyTitle, newId);

    /// <summary>The copy that lost, as a piece of its own: a new id and the player's title from <paramref name="copyTitle"/>.</summary>
    public static Project PreviousCopy(Project project, string copyTitle, string newId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(copyTitle);
        PieceMetadata metadata = project.Metadata;
        string title = copyTitle.Replace("{0}", project.Details.Title.Value, StringComparison.Ordinal);
        return project with { Id = newId, Metadata = new PieceMetadata(title, metadata.Composer, metadata.Tags, metadata.Playlists) };
    }
}