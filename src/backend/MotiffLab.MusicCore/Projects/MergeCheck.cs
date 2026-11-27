using System.Text.Json.Nodes;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The merge's self-check (0026 C5, #322 criterion 6): whether a merged file holds every change one copy made since the
/// base to the player's fields (with no base, all of them), by the rules the merge follows (<see cref="MergeRules"/>): each
/// entity as it is, each value of a list, each field of an object, each Notes' corrections note by note, and every
/// original by its fingerprint with each mark on its pages. A field of another class (bookkeeping, derived, an import's)
/// is the copy saved last's.
/// </summary>
internal static class MergeCheck
{
    /// <summary>
    /// Whether <paramref name="kept"/> holds every change <paramref name="side"/> made since <paramref name="before"/> (all of
    /// <paramref name="side"/> when null), except where <paramref name="excused"/> says the merge kept the other copy's.
    /// </summary>
    public static bool Holds(JsonObject side, JsonObject kept, JsonObject? before, IReadOnlyList<MergeConflict> excused)
    {
        var check = new Check(before is not null, excused);
        return side.All(field => check.Node(field.Key, field.Value, kept[field.Key], before?[field.Key]));
    }

    /// <summary>
    /// Whether the merged file read back as a project (<paramref name="read"/>, written again) still holds all of
    /// <paramref name="merged"/>. Two anchors on one span become one as a file is read and what named the other names the
    /// one kept, so an anchor's id is not compared: an idea or a resume point that had one still has one.
    /// </summary>
    public static bool ReadBack(JsonObject merged, JsonObject read)
    {
        HashSet<string> anchored = Unanchored(merged, out JsonObject was);
        HashSet<string> still = Unanchored(read, out JsonObject now);
        return anchored.IsSubsetOf(still) && Holds(was, now, null, []);
    }

    /// <summary>A copy of the file without the anchors and the ids of the ideas and resume points that named one.</summary>
    private static HashSet<string> Unanchored(JsonObject file, out JsonObject copy)
    {
        copy = file.DeepClone().AsObject();
        HashSet<string> anchored = new(StringComparer.Ordinal);
        if (copy["workspace"]?["phrases"] is JsonObject phrases)
        {
            phrases.Remove("anchors");
        }

        foreach (string list in new[] { "observations", "resumePoints" })
        {
            foreach (JsonObject entry in (copy["workspace"]?[list] as JsonArray ?? []).OfType<JsonObject>().Where(static entry => entry.Remove("anchor")))
            {
                anchored.Add($"{list}:{MergeRules.Key(entry, "id")}");
            }
        }

        return anchored;
    }

    private sealed class Check(bool hasBase, IReadOnlyList<MergeConflict> excused)
    {
        /// <summary>Whether <paramref name="kept"/> holds the side's field at <paramref name="path"/>, of the entry <paramref name="entity"/> names (a Notes) when it is one's.</summary>
        public bool Node(string path, JsonNode? side, JsonNode? kept, JsonNode? before, string? entity = null)
        {
            if (side is null || (hasBase && JsonNode.DeepEquals(side, before)) || Excused(path, entity))
            {
                return true;
            }

            if (MergeRules.Keyed(path) is { } key)
            {
                return Entries(path, key, side as JsonArray, kept as JsonArray, before as JsonArray);
            }

            if (path == MergeRules.EditsPath)
            {
                return Edits(side as JsonArray, kept as JsonArray, before as JsonArray);
            }

            if (MergeRules.ByField(path, hasBase) && side is JsonObject fields)
            {
                return fields.All(field => Node($"{path}.{field.Key}", field.Value, kept?[field.Key], before?[field.Key], entity));
            }

            if (MergeRules.Class(path) != FieldClass.Player)
            {
                return true;
            }

            return MergeRules.IsSet(path)
                ? MergeRules.Values(side as JsonArray).Except(hasBase ? MergeRules.Values(before as JsonArray) : []).All(MergeRules.Values(kept as JsonArray).Contains)
                : JsonNode.DeepEquals(side, kept);
        }

        private bool Entries(string path, Func<JsonNode?, string> key, JsonArray? side, JsonArray? kept, JsonArray? before)
        {
            Dictionary<string, JsonNode?> held = MergeRules.Index(kept, key);
            Dictionary<string, JsonNode?> was = MergeRules.Index(before, key);
            foreach ((string id, JsonNode? entry) in MergeRules.Index(side, key))
            {
                JsonNode? old = was.GetValueOrDefault(id);
                if ((hasBase && JsonNode.DeepEquals(entry, old)) || Excused(path, id))
                {
                    continue;
                }

                if (!held.TryGetValue(id, out JsonNode? mine) || !Entry(path, entry, mine, old))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>An entry held: a Notes or an original field by field (an original's description is the copy saved last's, its page marks each held), any other as it is.</summary>
        private bool Entry(string path, JsonNode? entry, JsonNode? kept, JsonNode? before) =>
            MergeRules.HasEntryFields(path) && entry is JsonObject fields
                ? fields.All(field => Node($"{path}.{field.Key}", field.Value, kept?[field.Key], before?[field.Key], MergeRules.Key(entry, "id")))
                : JsonNode.DeepEquals(entry, kept);

        /// <summary>Each note's corrections held as the side left them, or carried further along the same ones.</summary>
        private bool Edits(JsonArray? side, JsonArray? kept, JsonArray? before)
        {
            static ILookup<string, string> Notes(JsonArray? log) =>
                (log ?? []).ToLookup(static edit => MergeRules.Key(edit, "noteId"), static edit => edit?.ToJsonString() ?? "null", StringComparer.Ordinal);
            ILookup<string, string> held = Notes(kept);
            ILookup<string, string> was = Notes(before);
            return Notes(side).All(note => (hasBase && note.SequenceEqual(was[note.Key])) || Excused(MergeRules.EditsPath, note.Key)
                || held[note.Key].Take(note.Count()).SequenceEqual(note));
        }

        private bool Excused(string path, string? id) => excused.Any(conflict => conflict.In == path && (conflict.Id is null || conflict.Id == id));
    }
}