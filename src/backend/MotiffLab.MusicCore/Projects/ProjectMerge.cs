using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Import.Scan;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// What of the copy that lost a merge the merged project does not hold (0026 C2): an entity or field both copies changed
/// since the base (or that differs, with no base), where the copy that won keeps its own.
/// </summary>
/// <param name="In">Where in the project file, as <see cref="ProjectSync.Fields"/> names it: "workspace.observations",
/// "notes.edits", "notes.scan.marks", "workspace.metadata", …</param>
/// <param name="Id">The entity there: its id, the note a correction is of, a mark's kind, bar and staff ("meter|3|"); null
/// for a whole field, and for every correction of the other copy when the two copies' notes differ (S9's source guard).</param>
internal sealed record MergeConflict(string In, string? Id);

/// <summary>Two copies of one project merged: the project and what of the copy that lost it does not hold.</summary>
internal sealed record MergedProject(Project Project, IReadOnlyList<MergeConflict> Conflicts);

/// <summary>
/// The three-way merge of two copies of one project against the copy both last agreed on (0026 S5, S8, S9, #322), per
/// entity rather than per file, by <see cref="MergeRules"/>: ideas, bookmarks and their categories, resume points, motifs
/// and variations, the marks on the score and on a PDF's pages, phrase boundaries and anchors, settings, the scan's marks
/// «что не так», and, through <see cref="SourcesMerge"/>, Notes, corrections, versions compared and drafts. An entity changed
/// on one side only takes that side; added on either side, it comes in; removed on one side and unchanged on the other, it
/// goes; changed on both differently, the copy that won keeps its own and the merge records a <see cref="MergeConflict"/>.
/// Removed on one side and changed on the other, the change stays, so nothing is lost. Without a base the merge is a union
/// with no deletions (S4), but for the marks on a PDF's pages, the winner's. Fields this build does not know (a later
/// build's, S6), in the workspace, a Notes or an original, merge as whole values, so they are kept. A field
/// that is not the player's (bookkeeping, derived, an import's) is the winner's. Pure and deterministic: the same three
/// copies give the same project and conflicts.
/// </summary>
internal static class ProjectMerge
{
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
    /// (null: none kept, a union). The result has the winner's id. Null when it fails the self-check (0026 C5): it is no
    /// project file, its reading lost what the merge kept, or it does not hold every change either copy made since the base
    /// but the conflicts recorded (<see cref="MergeCheck"/>); then nothing merged is to be kept or sent.
    /// </summary>
    public static MergedProject? Merged(Project winner, Project loser, Project? @base)
    {
        ArgumentNullException.ThrowIfNull(winner);
        ArgumentNullException.ThrowIfNull(loser);
        MergedProject sources = SourcesMerge.Merged(winner, loser, @base);
        List<MergeConflict> conflicts = [.. sources.Conflicts];
        Project withMarks = WithScanMarks(sources.Project, loser, @base, conflicts);

        JsonObject merged = Json(withMarks);
        JsonObject won = Json(winner);
        JsonObject theirs = Json(loser);
        JsonObject? before = @base is null ? null : Json(@base);
        var merge = new Merge(before is not null, conflicts);
        merged["workspace"] = merge.Node("workspace", merged["workspace"], theirs["workspace"], before?["workspace"]);
        foreach (string list in MergeRules.EntryLists)
        {
            merge.Entries(list, merged, theirs, before);
        }

        merge.Root(merged, theirs, before);
        Referenced(merged, theirs, won);
        if (!MergeCheck.Holds(won, merged, before, []) || !MergeCheck.Holds(theirs, merged, before, conflicts))
        {
            return null;
        }

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

        return MergeCheck.ReadBack(merged, Json(project)) ? new MergedProject(project, conflicts) : null;
    }

    private static JsonObject Json(Project project) => JsonNode.Parse(ProjectFile.Write(project))!.AsObject();

    private static bool SameNotes(Project? project, string notesId, byte[] source) =>
        project is not null && project.NotesId == notesId && project.Source.AsSpan().SequenceEqual(source);

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
                conflicts.Add(new MergeConflict("notes.scan.marks", MarkKey(mark)));
            }
        }

        return kept;
    }

    /// <summary>A mark's key as the file's merge rules take it (<see cref="MergeRules.Keyed"/>), from the mark as the file writes it.</summary>
    private static string MarkKey(ReadingMark mark)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            ProjectFile.WriteReadingMark(json, mark);
            json.WriteEndObject();
        }

        return MergeRules.Keyed("notes.scan.marks")!(JsonNode.Parse(buffer.ToArray()));
    }

    /// <summary>
    /// An entry the merge kept that names one that went (a variation its motif, an idea or a resume point its anchor, a
    /// bookmark its category) takes it back from the copy that has it, so the file names only what it keeps.
    /// </summary>
    private static void Referenced(JsonObject merged, JsonObject loser, JsonObject winner)
    {
        foreach ((string list, string field, string named) in s_references)
        {
            if (MergeRules.At(merged, list) is not JsonArray entries || entries.Count == 0)
            {
                continue;
            }

            JsonArray kept = MergeRules.At(merged, named) as JsonArray ?? Put(merged, named);
            HashSet<string> ids = [.. kept.Select(static entry => MergeRules.Key(entry, "id"))];
            foreach (string id in entries.Select(entry => entry?[field]?.GetValue<string>()).OfType<string>().Distinct(StringComparer.Ordinal).Where(id => !ids.Contains(id)))
            {
                if (new[] { winner, loser }.Select(copy => (MergeRules.At(copy, named) as JsonArray)?.FirstOrDefault(entry => MergeRules.Key(entry, "id") == id)).FirstOrDefault(static entry => entry is not null) is { } entry)
                {
                    kept.Add(entry.DeepClone());
                }
            }
        }
    }

    private static JsonArray Put(JsonObject root, string path)
    {
        string[] names = path.Split('.');
        JsonObject parent = names[..^1].Aggregate(root, static (node, name) => node[name] as JsonObject ?? (JsonObject)(node[name] = new JsonObject()));
        var list = new JsonArray();
        parent[names[^1]] = list;
        return list;
    }

    /// <summary>One merge's state: whether a base is kept, and the conflicts met.</summary>
    private sealed class Merge(bool hasBase, List<MergeConflict> conflicts)
    {
        /// <summary>The field at <paramref name="path"/> merged by its rule, of the entry <paramref name="entity"/> names when it is one's; null when it goes.</summary>
        public JsonNode? Node(string path, JsonNode? mine, JsonNode? theirs, JsonNode? before, string? entity = null)
        {
            // Merged on the project already; or not the player's: the copy saved last says it (V21, V22).
            if (MergeRules.OnProject(path) || MergeRules.Class(path) != FieldClass.Player)
            {
                return mine?.DeepClone();
            }

            if (MergeRules.Keyed(path) is { } key)
            {
                return Keyed(path, key, mine as JsonArray, theirs as JsonArray, before as JsonArray);
            }

            if (MergeRules.IsSet(path))
            {
                return Set(mine as JsonArray, theirs as JsonArray, before as JsonArray);
            }

            if (MergeRules.ByField(path, hasBase) && mine is JsonObject ours && theirs is JsonObject other)
            {
                var merged = new JsonObject();
                foreach (string name in MergeRules.Names(ours, other))
                {
                    Set(merged, name, Node($"{path}.{name}", ours[name], other[name], before?[name], entity));
                }

                return merged;
            }

            return Whole(path, entity, mine, theirs, before);
        }

        /// <summary>
        /// The fields of each entry of <paramref name="list"/> (a Notes, an original) that both copies have, merged by their
        /// rules: an original's page marks mark by mark, a later build's field as a whole; the rest stay the copy saved last's.
        /// </summary>
        public void Entries(string list, JsonObject merged, JsonObject theirs, JsonObject? before)
        {
            Func<JsonNode?, string> key = MergeRules.Keyed(list)!;
            Dictionary<string, JsonNode?> other = MergeRules.Index(theirs[list] as JsonArray, key);
            Dictionary<string, JsonNode?> was = MergeRules.Index(before?[list] as JsonArray, key);
            foreach (JsonObject entry in (merged[list] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (other.GetValueOrDefault(key(entry)) is not JsonObject their)
                {
                    continue;
                }

                JsonNode? old = was.GetValueOrDefault(key(entry));
                string entity = MergeRules.Key(entry, "id");
                foreach (string name in MergeRules.Names(entry, their))
                {
                    // A field written as null (a Notes' Check) stays: only one the merge changed is set.
                    if (Node($"{list}.{name}", entry[name], their[name], old?[name], entity) is var value && !JsonNode.DeepEquals(value, entry[name]))
                    {
                        Set(entry, name, value);
                    }
                }
            }
        }

        /// <summary>The top level: the later time last opened, the later version and reader, the ids known before, and a later build's fields.</summary>
        public void Root(JsonObject merged, JsonObject theirs, JsonObject? before)
        {
            merged["lastOpened"] = Later(merged["lastOpened"], theirs["lastOpened"]);
            merged["version"] = Math.Max(merged["version"]!.GetValue<int>(), theirs["version"]!.GetValue<int>());
            merged["minReader"] = Math.Max(merged["minReader"]!.GetValue<int>(), theirs["minReader"]!.GetValue<int>());
            string id = merged["id"]!.GetValue<string>();
            string[] former = [.. new[] { merged, theirs }.SelectMany(static copy => (copy["formerIds"] as JsonArray ?? []).Select(static each => each!.GetValue<string>()))
                .Append(theirs["id"]!.GetValue<string>()).Where(each => each != id).Distinct(StringComparer.Ordinal)];
            merged.Remove("formerIds");
            if (former.Length > 0)
            {
                merged["formerIds"] = new JsonArray([.. former.Select(static each => (JsonNode)each)]);
            }

            foreach (string name in MergeRules.Names(merged, theirs).Where(static name => !ProjectFile.RootFields.Contains(name)))
            {
                Set(merged, name, Whole(name, null, merged[name], theirs[name], before?[name]));
            }
        }

        private JsonArray Keyed(string path, Func<JsonNode?, string> key, JsonArray? mine, JsonArray? theirs, JsonArray? before)
        {
            Dictionary<string, JsonNode?> ours = MergeRules.Index(mine, key);
            Dictionary<string, JsonNode?> other = MergeRules.Index(theirs, key);
            Dictionary<string, JsonNode?> was = MergeRules.Index(before, key);
            var merged = new JsonArray();
            List<string> ids = [.. ours.Keys.Concat(other.Keys.Where(id => !ours.ContainsKey(id)))];
            // Entries keyed by a number, a PDF's bookmarks by their page, stay in its order, as the file keeps them.
            if (ids.All(static id => int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            {
                ids.Sort(static (one, two) => int.Parse(one, CultureInfo.InvariantCulture).CompareTo(int.Parse(two, CultureInfo.InvariantCulture)));
            }

            foreach (string id in ids)
            {
                if (Whole(path, id, ours.GetValueOrDefault(id), other.GetValueOrDefault(id), was.GetValueOrDefault(id)) is { } entry)
                {
                    merged.Add(entry);
                }
            }

            return merged;
        }

        /// <summary>Each value kept or taken away by the side that changed it; ticks in order.</summary>
        private JsonArray Set(JsonArray? mine, JsonArray? theirs, JsonArray? before)
        {
            List<string> ours = MergeRules.Values(mine);
            List<string> other = MergeRules.Values(theirs);
            List<string> was = MergeRules.Values(before);
            List<JsonNode?> kept = [];
            foreach ((string value, JsonNode? node) in ours.Zip(mine ?? []).Concat(other.Zip(theirs ?? []).Where(pair => !ours.Contains(pair.First))))
            {
                bool inMine = ours.Contains(value), inTheirs = other.Contains(value), inBase = was.Contains(value);
                bool keep = inMine == inTheirs ? inMine : !hasBase || (inMine == inBase ? inTheirs : inMine);
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
                conflicts.Add(new MergeConflict(path, id));
            }

            return kept?.DeepClone();
        }

        private static JsonNode? Later(JsonNode? mine, JsonNode? theirs) =>
            DateTimeOffset.Parse(theirs!.GetValue<string>(), CultureInfo.InvariantCulture) > DateTimeOffset.Parse(mine!.GetValue<string>(), CultureInfo.InvariantCulture)
                ? theirs.DeepClone() : mine.DeepClone();

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