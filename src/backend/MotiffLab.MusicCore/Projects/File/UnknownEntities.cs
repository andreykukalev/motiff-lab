using System.Text.Json;

using MotiffLab.MusicCore.Pieces;

using static MotiffLab.MusicCore.Projects.File.JsonRead;

namespace MotiffLab.MusicCore.Projects.File;

/// <summary>
/// The tolerant reader (decision record 0026, S6): in a file of this build's layout (version 16 or 17), the entities of a list that hold a
/// value a later build added to an enum, kept whole as <see cref="UnknownEntity"/> and written back where they stood, and
/// the fields this build does not know, kept as <see cref="UnknownField"/> and written back after the fields it writes
/// (docs/project-file.md, "Unknown values").
/// </summary>
internal static class UnknownEntities
{
    /// <summary>
    /// The entities of one array of the file with their places. In a tolerant read an entity whose only fault is a value
    /// this build does not know is left out and kept as unknown; any other fault is damage, as before.
    /// </summary>
    /// <param name="owner">The field naming the entity it belongs with, kept with it when it is unknown.</param>
    /// <param name="fields">The fields this build reads in such an entity: the others are kept and written back (version 18).</param>
    internal static List<(int Index, JsonElement Element, T Value)> ReadEntries<T>(JsonElement array, string list, Func<JsonElement, T> read, Unknowns unknowns, string? owner = null, IReadOnlySet<string>? fields = null)
    {
        List<(int, JsonElement, T)> entries = [];
        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            try
            {
                entries.Add((index, element, read(element)));
                if (fields is not null)
                {
                    unknowns.KeepFields(list, element, fields);
                }
            }
            catch (ProjectFileException reason) when (unknowns.Keeps(reason, element))
            {
                unknowns.Keep(list, index, element, owner, (string)reason.Data[UnknownEnumKey]!);
            }

            index++;
        }

        return entries;
    }

    /// <summary>The entities of one array of the file this build reads, as <see cref="ReadEntries"/> reads them.</summary>
    internal static List<T> ReadList<T>(JsonElement array, string list, Func<JsonElement, T> read, Unknowns unknowns, IReadOnlySet<string>? fields = null) =>
        [.. ReadEntries(array, list, read, unknowns, fields: fields).Select(static entry => entry.Value)];

    /// <summary>
    /// Where the unknown fields of an entity of a workspace list stand in <see cref="Project.UnknownFields"/> (version 18):
    /// "workspace/", the list and the entity's id, such as "workspace/motifs/m_0001".
    /// </summary>
    internal static string FieldsPath(string list, string id) => $"{WorkspaceCodec.WorkspacePath}/{list}/{id}";

    /// <summary>The entities read, but those that belong with an unknown entity, which are kept as unknown with it.</summary>
    /// <param name="key">The id of the entity an entity belongs with (a motif's own id, a variation's motif).</param>
    /// <param name="owner">The field that names it, kept with an unknown entity; null when it is the entity's own id.</param>
    internal static List<T> Known<T>(List<(int Index, JsonElement Element, T Value)> entries, string list, Func<T, string> key, IReadOnlySet<string> unknownKeys, string? owner, Unknowns unknowns)
    {
        List<T> known = [];
        foreach ((int index, JsonElement element, T value) in entries)
        {
            if (unknownKeys.Contains(key(value)))
            {
                unknowns.Keep(list, index, element, owner, null);
            }
            else
            {
                known.Add(value);
            }
        }

        return known;
    }

    /// <summary>
    /// The unknown entities of a list to write back: not one whose id a known entity of the list now has (the app made a
    /// new one in its place), nor one that belongs with an unknown entity dropped so; <paramref name="replaced"/> names the
    /// ones dropped.
    /// </summary>
    internal static List<UnknownEntity> KeptUnknown(IEnumerable<UnknownEntity> unknown, string list, IEnumerable<string> knownIds, IReadOnlySet<string>? replacedOwners, out HashSet<string> replaced)
    {
        HashSet<string> taken = [.. knownIds];
        return Kept(unknown, list, entity => taken.Contains(entity.Id!), replacedOwners, out replaced);
    }

    /// <summary>
    /// The unknown entities of a list whose entities belong with another (a variation's motif, a bookmark's category) to
    /// write back, as <see cref="KeptUnknown(IEnumerable{UnknownEntity}, string, IEnumerable{string}, IReadOnlySet{string}?, out HashSet{string})"/>
    /// keeps them, but a known entity takes an unknown one's place only when both have the same id and owner: one of another
    /// owner with its id never drops it (#364).
    /// </summary>
    internal static List<UnknownEntity> KeptUnknown(IEnumerable<UnknownEntity> unknown, string list, IEnumerable<(string Id, string Owner)> known, IReadOnlySet<string> replacedOwners)
    {
        HashSet<(string, string)> taken = [.. known];
        return Kept(unknown, list, entity => entity.Owner is { } owner && taken.Contains((entity.Id!, owner)), replacedOwners, out _);
    }

    private static List<UnknownEntity> Kept(IEnumerable<UnknownEntity> unknown, string list, Func<UnknownEntity, bool> taken, IReadOnlySet<string>? replacedOwners, out HashSet<string> replaced)
    {
        List<UnknownEntity> kept = [];
        replaced = [];
        foreach (UnknownEntity entity in unknown.Where(entity => entity.List == list).OrderBy(static entity => entity.Index))
        {
            if (entity.Id is { } id && taken(entity))
            {
                replaced.Add(id);
            }
            else if (entity.Owner is not { } owner || replacedOwners is null || !replacedOwners.Contains(owner))
            {
                kept.Add(entity);
            }
        }

        return kept;
    }

    /// <summary>
    /// An array of the file: the known entities in their order, each unknown one written back unchanged at the place it
    /// had, so a file read and written with no change gives the same entities in the same order.
    /// </summary>
    internal static void WriteList<T>(Utf8JsonWriter json, string name, IEnumerable<T> known, IReadOnlyList<UnknownEntity> unknown, Action<T> write)
    {
        json.WriteStartArray(name);
        int written = 0;
        int next = 0;
        foreach (T entity in known)
        {
            for (; next < unknown.Count && unknown[next].Index <= written; next++, written++)
            {
                WriteUnknown(json, unknown[next]);
            }

            write(entity);
            written++;
        }

        for (; next < unknown.Count; next++)
        {
            WriteUnknown(json, unknown[next]);
        }

        json.WriteEndArray();
    }

    private static void WriteUnknown(Utf8JsonWriter json, UnknownEntity entity)
    {
        using JsonDocument kept = JsonDocument.Parse(entity.Json);
        kept.RootElement.WriteTo(json);
    }

    private static string? TextOf(JsonElement entity, string name) =>
        entity.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// What a read keeps as unknown (S6). Only a file in this build's layout (version 16 or 17) is read so: the builds
    /// that wrote an older file knew no value this one does not, so an unknown value there is damage.
    /// </summary>
    internal sealed class Unknowns(bool tolerant)
    {
        public List<UnknownEntity> Kept { get; } = [];

        /// <summary>The unknown fields of the entities read whole (<see cref="FieldsPath"/>) and of the settings (<see cref="ProjectFile.SettingsPath"/>).</summary>
        public Dictionary<string, IReadOnlyList<UnknownField>> Fields { get; } = new(StringComparer.Ordinal);

        /// <summary>Whether the read keeps what this build does not know: a file in this build's layout.</summary>
        public bool Tolerant => tolerant;

        /// <summary>The fields of a list's entity this build does not read, kept by its id (a tolerant read only).</summary>
        public void KeepFields(string list, JsonElement entity, IReadOnlySet<string> known)
        {
            if (tolerant && TextOf(entity, "id") is { } id)
            {
                UnknownEntities.KeepFields(Fields, FieldsPath(list, id), entity, known);
            }
        }

        /// <summary>Whether the only fault of a list's entity is a value this build does not know.</summary>
        public bool Keeps(ProjectFileException reason, JsonElement entity) =>
            tolerant && entity.ValueKind == JsonValueKind.Object && reason.Data[UnknownEnumKey] is string;

        public void Keep(string list, int index, JsonElement entity, string? owner, string? enumName) =>
            Kept.Add(new UnknownEntity(list, index, TextOf(entity, "id"), owner is null ? null : TextOf(entity, owner), entity.GetRawText()) { Enum = enumName });

        /// <summary>The project read, with the unknown entities where they are kept: the marks' and the PDF's marks' own, and the rest on the project.</summary>
        public Project KeptIn(Project project) => project with
        {
            Unknown = InOrder(Kept.Where(static entity => !entity.List.StartsWith("marks.", StringComparison.Ordinal) && !entity.List.StartsWith("documentMarks.", StringComparison.Ordinal))),
            Marks = project.Marks with { Unknown = InOrder(Kept.Where(static entity => entity.List.StartsWith("marks.", StringComparison.Ordinal))) },
            DocumentMarks = project.DocumentMarks with { Unknown = InOrder(Kept.Where(static entity => entity.List.StartsWith("documentMarks.", StringComparison.Ordinal))) },
        };

        private static List<UnknownEntity> InOrder(IEnumerable<UnknownEntity> entities) =>
            [.. entities.OrderBy(static entity => entity.List, StringComparer.Ordinal).ThenBy(static entity => entity.Index)];
    }

    /// <summary>The fields kept unknown, each written back as the file held it, after the fields this build writes.</summary>
    internal static void WriteUnknownFields(Utf8JsonWriter json, IReadOnlyList<UnknownField> fields)
    {
        foreach (UnknownField field in fields)
        {
            json.WritePropertyName(field.Name);
            using JsonDocument kept = JsonDocument.Parse(field.Json);
            kept.RootElement.WriteTo(json);
        }
    }

    internal static List<UnknownField> UnknownFieldsOf(JsonElement element, IReadOnlySet<string> known) =>
        [.. element.EnumerateObject().Where(field => !known.Contains(field.Name)).Select(static field => new UnknownField(field.Name, field.Value.GetRawText()))];

    internal static void KeepFields(Dictionary<string, IReadOnlyList<UnknownField>> fields, string path, JsonElement element, IReadOnlySet<string> known)
    {
        List<UnknownField> unknown = UnknownFieldsOf(element, known);
        if (unknown.Count > 0)
        {
            fields[path] = unknown;
        }
    }
}