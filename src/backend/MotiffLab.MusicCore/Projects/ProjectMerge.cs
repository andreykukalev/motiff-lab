using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Projects.File;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// What of the copy that lost a merge the merged project does not hold (0026 C2): an entity or field both copies changed
/// since the base (or that differs, with no base), where the copy that won keeps its own.
/// </summary>
/// <param name="In">Where in the project file, as <see cref="ProjectSync.Fields"/> names it: "workspace.observations",
/// "notes.edits", "notes.scan.marks", "workspace.metadata", …</param>
/// <param name="Id">The entity there: its id, the note a correction is of, a mark's kind, bar and staff ("meter|3|"); null
/// for a field.</param>
internal sealed record MergeConflict(string In, string? Id);

/// <summary>Two copies of one project merged: the project and what of the copy that lost it does not hold.</summary>
internal sealed record MergedProject(Project Project, IReadOnlyList<MergeConflict> Conflicts);

/// <summary>
/// The three-way merge of two copies of one project against the copy both last agreed on (0026 S5, S8, S9, #322), per
/// entity rather than per file: ideas, bookmarks and their categories, resume points, motifs and variations, the marks on
/// the score, phrase boundaries and anchors, settings, the scan's marks «что не так», and, through
/// <see cref="SourcesMerge"/>, Notes, corrections, versions compared and drafts. An entity changed on one side only takes
/// that side; added on either side, it comes in; removed on one side and unchanged on the other, it goes; changed on both
/// differently, the copy that won keeps its own and the merge records a <see cref="MergeConflict"/>. Removed on one side
/// and changed on the other, the change stays, so nothing is lost. Without a base the merge is a union with no deletions
/// (S4). Fields this build does not know (a later build's, S6) merge as whole values, so they are kept. Pure and
/// deterministic: the same three copies give the same project and conflicts.
/// </summary>
internal static class ProjectMerge
{
    /// <summary>The workspace's lists merged entity by entity, each by the field that names an entry.</summary>
    private static readonly Dictionary<string, string> s_keyed = new(StringComparer.Ordinal)
    {
        ["workspace.motifs"] = "id",
        ["workspace.variations"] = "id",
        ["workspace.observations"] = "id",
        ["workspace.resumePoints"] = "id",
        ["workspace.bookmarks.items"] = "id",
        ["workspace.bookmarks.categories"] = "id",
        ["workspace.phrases.anchors"] = "id",
        ["workspace.marks.ink"] = "id",
        ["workspace.marks.notes"] = "id",
        ["workspace.marks.highlights"] = "id",
        ["workspace.marks.fingering"] = "noteId",
    };

    /// <summary>The lists of values merged value by value: the phrase boundaries set and taken away, the views hidden.</summary>
    private static readonly HashSet<string> s_sets = new(StringComparer.Ordinal)
    {
        "workspace.phrases.added", "workspace.phrases.removed", "workspace.phrases.short.added", "workspace.phrases.short.removed",
        "workspace.phrases.sentences.added", "workspace.phrases.sentences.removed", "workspace.hiddenViews",
    };

    /// <summary>The objects merged field by field; every other field merges as a whole value.</summary>
    private static readonly HashSet<string> s_objects = new(StringComparer.Ordinal)
    {
        "workspace", "workspace.phrases", "workspace.phrases.short", "workspace.phrases.sentences", "workspace.marks", "workspace.bookmarks", "workspace.settings",
    };

    /// <summary>
    /// The fields the copy that won says: the working notes and whether the corrections are off (bookkeeping, V21), the place
    /// in a collection (an import's), and the drafts, which <see cref="SourcesMerge"/> merged.
    /// </summary>
    private static readonly HashSet<string> s_winners = new(StringComparer.Ordinal)
    {
        "workspace.working", "workspace.editsOff", "workspace.partOf", "workspace.drafts",
    };

    /// <summary>The top-level fields this build writes; any other is a later build's and merges as a whole value.</summary>
    private static readonly HashSet<string> s_root = new(StringComparer.Ordinal)
    {
        "format", "version", "minReader", "id", "formerIds", "fileName", "createdAt", "lastOpened", "originals", "notes", "workspace",
    };

    /// <summary>What an entry names in another list, so an entry kept is never left naming one that went (the list, the field, the list it names).</summary>
    private static readonly (string List, string Field, string Named)[] s_references =
    [
        ("workspace.variations", "motifId", "workspace.motifs"),
        ("workspace.observations", "anchor", "workspace.phrases.anchors"),
        ("workspace.resumePoints", "anchor", "workspace.phrases.anchors"),
        ("workspace.bookmarks.items", "category", "workspace.bookmarks.categories"),
    ];

    /// <summary>
    /// <paramref name="winner"/> and <paramref name="loser"/>, two copies of one project, merged against <paramref name="base"/>
    /// (null: none kept, a union). The result has the winner's id, and is read again as a project file; null when it is not
    /// one, or the reading lost an entry the merge kept (the self-check of 0026 C5): then nothing merged is to be kept.
    /// </summary>
    public static MergedProject? Merged(Project winner, Project loser, Project? @base)
    {
        ArgumentNullException.ThrowIfNull(winner);
        ArgumentNullException.ThrowIfNull(loser);
        List<MergeConflict> conflicts = [];
        Project sources = SourcesMerge.Merged(winner, loser, @base);
        SourceConflicts(sources, loser, @base, conflicts);
        sources = WithScanMarks(sources, loser, @base, conflicts);

        JsonObject merged = Json(sources);
        JsonObject theirs = Json(loser);
        JsonObject? before = @base is null ? null : Json(@base);
        var merge = new Merge(before is not null, conflicts);
        merged["workspace"] = merge.Node("workspace", merged["workspace"], theirs["workspace"], before?["workspace"]);
        merge.Root(merged, theirs, before);
        JsonObject won = Json(winner);
        Referenced(merged, theirs, won);
        if (JsonNode.DeepEquals(merged, won))
        {
            // Nothing of the other copy's came in: the winner as it is.
            return new MergedProject(winner, conflicts);
        }

        Project project;
        try
        {
            project = ProjectFile.Read(Encoding.UTF8.GetBytes(merged.ToJsonString()));
        }
        catch (ProjectFileException)
        {
            return null;
        }

        return Holds(Json(project), merged) ? new MergedProject(project, conflicts) : null;
    }

    private static JsonObject Json(Project project) => JsonNode.Parse(ProjectFile.Write(project))!.AsObject();

    /// <summary>The corrections, versions compared and drafts of <paramref name="loser"/> that <see cref="SourcesMerge"/> left out, both copies having changed them.</summary>
    private static void SourceConflicts(Project merged, Project loser, Project? @base, List<MergeConflict> conflicts)
    {
        Edits(merged.Edits, loser.Edits, SameNotes(@base, merged.NotesId, merged.Source) ? @base!.Edits : null, SameNotes(loser, merged.NotesId, merged.Source), conflicts);
        foreach (OtherNotes theirs in loser.Sources.Notes)
        {
            if (merged.Sources.Notes.FirstOrDefault(each => each.Id == theirs.Id) is { } mine)
            {
                OtherNotes? before = @base?.Sources.Notes.FirstOrDefault(each => each.Id == theirs.Id);
                bool sameMusic = mine.Music.AsSpan().SequenceEqual(theirs.Music);
                Edits(mine.Edits, theirs.Edits, before is not null && before.Music.AsSpan().SequenceEqual(mine.Music) ? before.Edits : null, sameMusic, conflicts);
            }
        }

        Changed("notes.performance", loser.Performances, static each => each.Id, merged.FindPerformance, id => @base?.FindPerformance(id), static (one, other) => one.Equals(other), conflicts);
        Changed("workspace.drafts", loser.Drafts, static each => each.Id, merged.FindDraft, id => @base?.FindDraft(id), DraftsCodec.Same, conflicts);
    }

    /// <summary>The entries of the loser's that the merged project keeps otherwise, though the base did not have them so.</summary>
    private static void Changed<T>(string where, IEnumerable<T> theirs, Func<T, string> id, Func<string, T?> merged, Func<string, T?> before, Func<T, T, bool> same, List<MergeConflict> conflicts)
        where T : class
    {
        foreach (T entry in theirs)
        {
            if (merged(id(entry)) is { } mine && !same(mine, entry) && !(before(id(entry)) is { } was && same(was, entry)))
            {
                conflicts.Add(new MergeConflict(where, id(entry)));
            }
        }
    }

    private static bool SameNotes(Project? project, string notesId, byte[] source) =>
        project is not null && project.NotesId == notesId && project.Source.AsSpan().SequenceEqual(source);

    /// <summary>
    /// The notes <paramref name="loser"/> corrected that <paramref name="merged"/> holds otherwise, though they were not left
    /// as the base had them; with another source, every correction of the loser's since the base is one conflict (S9's guard).
    /// </summary>
    private static void Edits(NoteEditLog merged, NoteEditLog loser, NoteEditLog? @base, bool sameMusic, List<MergeConflict> conflicts)
    {
        if (!sameMusic)
        {
            if (loser.Edits.Count > 0 && !loser.Equals(@base))
            {
                conflicts.Add(new MergeConflict("notes.edits", null));
            }

            return;
        }

        foreach (string id in loser.EditedIds)
        {
            List<NoteEdit> theirs = SourcesMerge.Of(loser, id);
            if (!theirs.SequenceEqual(SourcesMerge.Of(merged, id)) && (@base is null || !theirs.SequenceEqual(SourcesMerge.Of(@base, id))))
            {
                conflicts.Add(new MergeConflict("notes.edits", id));
            }
        }
    }

    /// <summary>
    /// The working notes' marks «что не так» (version 18) merged by kind, bar and staff (the 1.6 plan's decision 16), while
    /// both copies read the same notes: a mark only <paramref name="loser"/> put or changed comes in as the player puts one
    /// (<see cref="ScanOrigin.WithMark"/>), one it took away since the base goes, and one both changed differently stays the
    /// winner's.
    /// </summary>
    private static Project WithScanMarks(Project merged, Project loser, Project? @base, List<MergeConflict> conflicts)
    {
        if (merged.Scan is not { } scan || loser.Scan is not { } theirs || !SameNotes(loser, merged.NotesId, merged.Source))
        {
            return merged;
        }

        IReadOnlyList<ReadingMark>? before = SameNotes(@base, merged.NotesId, merged.Source) ? @base!.Scan?.Marks ?? [] : null;
        ScanOrigin kept = WithTheirMarks(scan, theirs.Marks, before, conflicts);
        // Taken away on the other copy since the base, and not changed here.
        foreach (ReadingMark gone in before?.Where(was => At(theirs.Marks, was) is null && was.Equals(At(scan.Marks, was))) ?? [])
        {
            kept = kept.WithoutMark(gone.Kind, gone.Bar, gone.Staff);
        }

        return kept.Equals(scan) ? merged : merged with { Scan = kept };
    }

    private static ReadingMark? At(IReadOnlyList<ReadingMark>? marks, ReadingMark place) => marks?.FirstOrDefault(mark => mark.SamePlace(place));

    /// <summary>The scan with the marks only the other copy put or changed since <paramref name="before"/>; one both changed stays.</summary>
    private static ScanOrigin WithTheirMarks(ScanOrigin scan, IReadOnlyList<ReadingMark> theirs, IReadOnlyList<ReadingMark>? before, List<MergeConflict> conflicts)
    {
        ScanOrigin kept = scan;
        foreach (ReadingMark mark in theirs)
        {
            ReadingMark? mine = At(scan.Marks, mark);
            ReadingMark? was = At(before, mark);
            if (mark.Equals(mine) || (before is not null && mark.Equals(was)))
            {
                continue;
            }

            if (mine is null || (before is not null && mine.Equals(was)))
            {
                kept = kept.WithMark(mark);
            }
            else
            {
                conflicts.Add(new MergeConflict("notes.scan.marks", $"{mark.Kind}|{mark.Bar}|{mark.Staff}"));
            }
        }

        return kept;
    }

    /// <summary>
    /// An entry the merge kept that names one that went (a variation its motif, an idea or a resume point its anchor, a
    /// bookmark its category) takes it back from the copy that has it, so the file names only what it keeps.
    /// </summary>
    private static void Referenced(JsonObject merged, JsonObject loser, JsonObject winner)
    {
        foreach ((string list, string field, string named) in s_references)
        {
            if (At(merged, list) is not JsonArray entries || entries.Count == 0)
            {
                continue;
            }

            JsonArray kept = At(merged, named) as JsonArray ?? Put(merged, named);
            HashSet<string> ids = [.. kept.Select(entry => Key(entry, "id"))];
            foreach (string id in entries.Select(entry => entry?[field]?.GetValue<string>()).OfType<string>().Distinct(StringComparer.Ordinal).Where(id => !ids.Contains(id)))
            {
                if (new[] { winner, loser }.Select(copy => (At(copy, named) as JsonArray)?.FirstOrDefault(entry => Key(entry, "id") == id)).FirstOrDefault(entry => entry is not null) is { } entry)
                {
                    kept.Add(entry.DeepClone());
                }
            }
        }
    }

    /// <summary>Whether the project read back keeps every entry of the merged file's lists by its key (0026 C5); anchors on one span become one as a file is read, so they are not counted.</summary>
    private static bool Holds(JsonObject read, JsonObject merged) =>
        s_keyed.Where(list => list.Key != "workspace.phrases.anchors").All(list =>
            (At(merged, list.Key) as JsonArray ?? []).Select(entry => Key(entry, list.Value)).ToHashSet(StringComparer.Ordinal)
                .IsSubsetOf((At(read, list.Key) as JsonArray ?? []).Select(entry => Key(entry, list.Value))));

    private static JsonNode? At(JsonObject root, string path) =>
        path.Split('.').Aggregate<string, JsonNode?>(root, (node, name) => node is JsonObject each ? each[name] : null);

    private static JsonArray Put(JsonObject root, string path)
    {
        string[] names = path.Split('.');
        JsonObject parent = names[..^1].Aggregate(root, (node, name) => node[name] as JsonObject ?? (JsonObject)(node[name] = new JsonObject()));
        var list = new JsonArray();
        parent[names[^1]] = list;
        return list;
    }

    /// <summary>An entry's key: the text of its <paramref name="field"/>, or, for an entry without one, the entry itself.</summary>
    private static string Key(JsonNode? entry, string field) =>
        entry is JsonObject each && each[field] is JsonValue value ? value.ToJsonString() : entry?.ToJsonString() ?? "null";

    /// <summary>One merge's state: whether a base is kept, and the conflicts met.</summary>
    private sealed class Merge(bool hasBase, List<MergeConflict> conflicts)
    {
        /// <summary>The field at <paramref name="path"/> merged by its rule; null when it goes.</summary>
        public JsonNode? Node(string path, JsonNode? mine, JsonNode? theirs, JsonNode? before)
        {
            if (s_winners.Contains(path))
            {
                if (!JsonNode.DeepEquals(mine, theirs) && path != "workspace.drafts" && !(hasBase && JsonNode.DeepEquals(theirs, before)))
                {
                    conflicts.Add(new MergeConflict(path, null));
                }

                return mine?.DeepClone();
            }

            if (s_keyed.TryGetValue(path, out string? key))
            {
                return Keyed(path, key, mine as JsonArray, theirs as JsonArray, before as JsonArray);
            }

            if (s_sets.Contains(path))
            {
                return Set(mine as JsonArray, theirs as JsonArray, before as JsonArray);
            }

            if (s_objects.Contains(path) && mine is JsonObject ours && theirs is JsonObject other)
            {
                return Object(path, ours, other, before as JsonObject);
            }

            return Whole(path, null, mine, theirs, before);
        }

        /// <summary>The top level: the later time last opened, the later version and reader, the ids known before, and a later build's fields.</summary>
        public void Root(JsonObject merged, JsonObject theirs, JsonObject? before)
        {
            merged["lastOpened"] = Later(merged["lastOpened"], theirs["lastOpened"]);
            merged["version"] = Math.Max(merged["version"]!.GetValue<int>(), theirs["version"]!.GetValue<int>());
            merged["minReader"] = Math.Max(merged["minReader"]!.GetValue<int>(), theirs["minReader"]!.GetValue<int>());
            string id = merged["id"]!.GetValue<string>();
            string[] former = [.. new[] { merged, theirs }.SelectMany(copy => (copy["formerIds"] as JsonArray ?? []).Select(each => each!.GetValue<string>()))
                .Append(theirs["id"]!.GetValue<string>()).Where(each => each != id).Distinct(StringComparer.Ordinal)];
            merged.Remove("formerIds");
            if (former.Length > 0)
            {
                merged["formerIds"] = new JsonArray([.. former.Select(each => (JsonNode)each)]);
            }

            foreach (string name in Names(merged, theirs).Where(name => !s_root.Contains(name)))
            {
                Set(merged, name, Whole(name, null, merged[name], theirs[name], before?[name]));
            }
        }

        private JsonObject Object(string path, JsonObject mine, JsonObject theirs, JsonObject? before)
        {
            var merged = new JsonObject();
            foreach (string name in Names(mine, theirs))
            {
                Set(merged, name, Node($"{path}.{name}", mine[name], theirs[name], before?[name]));
            }

            return merged;
        }

        private JsonArray Keyed(string path, string key, JsonArray? mine, JsonArray? theirs, JsonArray? before)
        {
            Dictionary<string, JsonNode?> ours = Index(mine, key);
            Dictionary<string, JsonNode?> other = Index(theirs, key);
            Dictionary<string, JsonNode?> was = Index(before, key);
            var merged = new JsonArray();
            foreach (string id in ours.Keys.Concat(other.Keys.Where(id => !ours.ContainsKey(id))))
            {
                if (Whole(path, id, ours.GetValueOrDefault(id), other.GetValueOrDefault(id), was.GetValueOrDefault(id)) is { } entry)
                {
                    merged.Add(entry);
                }
            }

            return merged;
        }

        private static Dictionary<string, JsonNode?> Index(JsonArray? list, string key)
        {
            Dictionary<string, JsonNode?> index = new(StringComparer.Ordinal);
            foreach (JsonNode? entry in list ?? [])
            {
                index.TryAdd(Key(entry, key), entry);
            }

            return index;
        }

        /// <summary>Each value kept or taken away by the side that changed it; ticks in order.</summary>
        private JsonArray Set(JsonArray? mine, JsonArray? theirs, JsonArray? before)
        {
            static List<string> Values(JsonArray? list) => [.. (list ?? []).Select(value => value?.ToJsonString() ?? "null")];
            List<string> ours = Values(mine);
            List<string> other = Values(theirs);
            List<string> was = Values(before);
            List<JsonNode?> kept = [];
            foreach ((string value, JsonNode? node) in ours.Zip(mine ?? []).Concat(other.Zip(theirs ?? []).Where(pair => !ours.Contains(pair.First))))
            {
                bool inMine = ours.Contains(value), inTheirs = other.Contains(value), inBase = was.Contains(value);
                bool keep = inMine == inTheirs ? inMine : hasBase ? (inMine == inBase ? inTheirs : inMine) : true;
                if (keep && !kept.Any(each => each!.ToJsonString() == value))
                {
                    kept.Add(node?.DeepClone());
                }
            }

            bool ticks = kept.All(static each => each is JsonValue value && value.TryGetValue(out int _));
            IEnumerable<JsonNode?> ordered = ticks ? kept.OrderBy(static each => each!.GetValue<int>()) : kept;
            return new JsonArray([.. ordered]);
        }

        /// <summary>
        /// A value as a whole: the side that changed it since the base; with no base, the one there is; changed on both, the
        /// side that changed it rather than removed it, else the winner's, with a conflict.
        /// </summary>
        private JsonNode? Whole(string path, string? id, JsonNode? mine, JsonNode? theirs, JsonNode? before)
        {
            JsonNode? kept = mine;
            if (JsonNode.DeepEquals(mine, theirs) || (hasBase && JsonNode.DeepEquals(theirs, before)))
            {
                kept = mine;
            }
            else if ((hasBase && JsonNode.DeepEquals(mine, before)) || mine is null)
            {
                kept = theirs;
            }
            else if (theirs is not null)
            {
                conflicts.Add(new MergeConflict(path, id is null ? null : Unquoted(id)));
            }

            return kept?.DeepClone();
        }

        private static string Unquoted(string key) => key.Length > 1 && key[0] == '"' && key[^1] == '"' ? key[1..^1] : key;

        private static JsonNode? Later(JsonNode? mine, JsonNode? theirs) =>
            DateTimeOffset.Parse(theirs!.GetValue<string>(), CultureInfo.InvariantCulture) > DateTimeOffset.Parse(mine!.GetValue<string>(), CultureInfo.InvariantCulture)
                ? theirs.DeepClone() : mine.DeepClone();

        private static IEnumerable<string> Names(JsonObject mine, JsonObject theirs) =>
            mine.Select(static pair => pair.Key).Concat(theirs.Select(static pair => pair.Key).Where(name => !mine.ContainsKey(name))).ToList();

        private static void Set(JsonObject target, string name, JsonNode? value)
        {
            target.Remove(name);
            if (value is not null)
            {
                target[name] = value;
            }
        }
    }
}