using System.Text.Json.Nodes;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// How each part of a project file merges, for <see cref="ProjectMerge"/> and its self-check <see cref="MergeCheck"/> alike:
/// the lists merged entry by entry and the field that names an entry, the lists merged value by value, the objects merged
/// field by field, and the entries whose fields have rules of their own. Which fields are the player's comes from
/// <see cref="ProjectSync.Fields"/> (0026 S8): a field of another class is the copy saved last's and never a conflict; a
/// field this build does not know (a later build's) is the player's.
/// </summary>
internal static class MergeRules
{
    /// <summary>The lists merged entry by entry, each with the key of an entry.</summary>
    private static readonly Dictionary<string, Func<JsonNode?, string>> s_keyed = new(StringComparer.Ordinal)
    {
        ["workspace.motifs"] = ById,
        ["workspace.variations"] = ById,
        ["workspace.observations"] = ById,
        ["workspace.resumePoints"] = ById,
        ["workspace.bookmarks.items"] = ById,
        ["workspace.bookmarks.categories"] = ById,
        ["workspace.phrases.anchors"] = ById,
        ["workspace.marks.ink"] = ById,
        ["workspace.marks.notes"] = ById,
        ["workspace.marks.highlights"] = ById,
        ["workspace.marks.fingering"] = static entry => Key(entry, "noteId"),
        ["workspace.drafts"] = ById,
        ["notes"] = ById,
        // The 1.6 plan's decision 16: a mark «что не так» by its kind, bar and staff.
        ["notes.scan.marks"] = static entry => $"{Field(entry, "kind")}|{Field(entry, "bar")}|{Field(entry, "staff")}",
        // An original by its fingerprint, else its id, as PieceOriginals.Merge unites them.
        ["originals"] = static entry => Field(entry, "sha256") ?? Key(entry, "id"),
        // The marks on a PDF's pages (version 0.4) by kind, as two iPads that hold the PDF both write them, with a base or
        // without (S4's union, as every other entity): a stroke by its id, a bookmark by its page, as a page has one.
        ["originals.marks.ink"] = ById,
        ["originals.marks.bookmarks"] = static entry => Key(entry, "page"),
    };

    /// <summary>The lists merged value by value: the phrase boundaries set and taken away at each level, the views hidden.</summary>
    private static readonly HashSet<string> s_sets = new(StringComparer.Ordinal)
    {
        "workspace.phrases.added", "workspace.phrases.removed", "workspace.phrases.short.added", "workspace.phrases.short.removed",
        "workspace.phrases.sentences.added", "workspace.phrases.sentences.removed", "workspace.hiddenViews",
    };

    /// <summary>The objects merged field by field; a field of one merges as a whole value unless a rule above names it.</summary>
    private static readonly HashSet<string> s_objects = new(StringComparer.Ordinal)
    {
        "workspace", "workspace.phrases", "workspace.phrases.short", "workspace.phrases.sentences", "workspace.marks", "workspace.bookmarks",
        "workspace.settings", "notes.scan", "originals.marks",
    };

    /// <summary>
    /// The player's fields <see cref="SourcesMerge"/> and <see cref="ProjectMerge"/>'s scan marks merge on the project rather
    /// than its file (Notes by id with their names and marks, corrections note by note, versions compared, drafts); the file merge
    /// takes them as those left them.
    /// </summary>
    private static readonly HashSet<string> s_onProject = new(StringComparer.Ordinal)
    {
        "workspace.drafts", "notes.name", "notes.fileName", "notes.edits", "notes.performance", "notes.scan.marks", "notes.marks",
    };

    /// <summary>Lists whose entries are objects whose fields have rules of their own (a Notes: its name, corrections, marks; an original: its page marks).</summary>
    private static readonly HashSet<string> s_entryFields = new(StringComparer.Ordinal) { "notes", "originals" };

    /// <summary>The lists whose entries' fields have rules of their own (<see cref="HasEntryFields"/>).</summary>
    public static IEnumerable<string> EntryLists => s_entryFields;

    /// <summary>The corrections of a Notes, compared note by note: all the corrections of one note together.</summary>
    internal const string EditsPath = "notes.edits";

    /// <summary>
    /// The class of the field at <paramref name="path"/>: its own in <see cref="ProjectSync.Fields"/>; the player's for a field
    /// this build does not know (a later build's) where the file's fields are named one by one (the top level, an entry
    /// whose fields have rules, an object merged field by field); else, for a value within a field (a setting), its field's.
    /// </summary>
    public static FieldClass Class(string path)
    {
        if (ProjectSync.Fields.TryGetValue(path, out FieldClass found))
        {
            return found;
        }

        int dot = path.LastIndexOf('.');
        return dot < 0 || s_objects.Contains(path[..dot]) || s_entryFields.Contains(path[..dot]) ? FieldClass.Player : Class(path[..dot]);
    }

    /// <summary>The key of an entry of the list at <paramref name="path"/>, or null when it is no list merged entry by entry.</summary>
    public static Func<JsonNode?, string>? Keyed(string path) => s_keyed.GetValueOrDefault(path);

    public static bool IsSet(string path) => s_sets.Contains(path);

    /// <summary>Whether the object at <paramref name="path"/> is merged field by field.</summary>
    public static bool ByField(string path) => s_objects.Contains(path);

    public static bool OnProject(string path) => s_onProject.Contains(path);

    public static bool HasEntryFields(string path) => s_entryFields.Contains(path);

    /// <summary>
    /// How the player's field at <paramref name="path"/> (version 16's layout) is merged, so a field with none fails its test:
    /// "entities", "values", "fields", "project" (<see cref="OnProject"/>), "whole" (a field of an object merged field by
    /// field, or of a Notes); null when nothing merges it.
    /// </summary>
    public static string? MergedBy(string path) =>
        s_onProject.Contains(path) ? "project"
        : s_keyed.ContainsKey(path) ? "entities"
        : s_sets.Contains(path) ? "values"
        : s_objects.Contains(path) ? "fields"
        : path.Contains('.', StringComparison.Ordinal) && s_objects.Contains(path[..path.LastIndexOf('.')]) ? "whole"
        : null;

    private static string ById(JsonNode? entry) => Key(entry, "id");

    /// <summary>An entry's key: the text of its <paramref name="field"/>, or, for an entry without one, the entry itself.</summary>
    public static string Key(JsonNode? entry, string field) => Field(entry, field) ?? entry?.ToJsonString() ?? "null";

    /// <summary>The text of a field of an entry (a text as it is, any other value as its JSON), or null when it has none.</summary>
    private static string? Field(JsonNode? entry, string name) =>
        entry is JsonObject each && each[name] is JsonValue value ? (value.TryGetValue(out string? text) ? text : value.ToJsonString()) : null;

    /// <summary>The node at a dotted path, or null.</summary>
    public static JsonNode? At(JsonObject root, string path) =>
        path.Split('.').Aggregate<string, JsonNode?>(root, (node, name) => node is JsonObject each ? each[name] : null);

    /// <summary>The entries of a list by their key, the first of each key.</summary>
    public static Dictionary<string, JsonNode?> Index(JsonArray? list, Func<JsonNode?, string> key)
    {
        Dictionary<string, JsonNode?> index = new(StringComparer.Ordinal);
        foreach (JsonNode? entry in list ?? [])
        {
            index.TryAdd(key(entry), entry);
        }

        return index;
    }

    /// <summary>The values of a list, each as its JSON text.</summary>
    public static List<string> Values(JsonArray? list) => [.. (list ?? []).Select(static value => value?.ToJsonString() ?? "null")];

    /// <summary>The names of both objects' fields, the first's first.</summary>
    public static IEnumerable<string> Names(JsonObject mine, JsonObject? theirs) =>
        mine.Select(static pair => pair.Key).Concat((theirs ?? []).Select(static pair => pair.Key).Where(name => !mine.ContainsKey(name))).ToList();
}