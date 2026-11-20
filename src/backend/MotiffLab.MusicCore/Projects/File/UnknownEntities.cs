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
    internal static List<(int Index, JsonElement Element, T Value)> ReadEntries<T>(JsonElement array, string list, Func<JsonElement, T> read, Unknowns unknowns, string? owner = null)
    {
        List<(int, JsonElement, T)> entries = [];
        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            try
            {
                entries.Add((index, element, read(element)));
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
    internal static List<T> ReadList<T>(JsonElement array, string list, Func<JsonElement, T> read, Unknowns unknowns) =>
        [.. ReadEntries(array, list, read, unknowns).Select(static entry => entry.Value)];

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
        List<UnknownEntity> kept = [];
        replaced = [];
        foreach (UnknownEntity entity in unknown.Where(entity => entity.List == list).OrderBy(static entity => entity.Index))
        {
            if (entity.Id is { } id && taken.Contains(id))
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