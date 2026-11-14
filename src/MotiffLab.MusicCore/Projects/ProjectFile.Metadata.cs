using System.Text.Json;

namespace MotiffLab.MusicCore.Projects;

/// <summary>The piece's title, composer, tags and playlists as the player set them in the library (version 0.2, within version 15).</summary>
public static partial class ProjectFile
{
    /// <summary>"metadata": {"title", "composer", "tags", "playlists"}, each only when set; nothing at all while none is.</summary>
    private static void WriteMetadata(Utf8JsonWriter json, PieceMetadata metadata)
    {
        if (metadata.IsEmpty)
        {
            return;
        }

        json.WriteStartObject("metadata");
        if (metadata.Title is { } title)
        {
            json.WriteString("title", title);
        }

        if (metadata.Composer is { } composer)
        {
            json.WriteString("composer", composer);
        }

        WriteNames(json, "tags", metadata.Tags);
        WriteNames(json, "playlists", metadata.Playlists);
        json.WriteEndObject();
    }

    private static void WriteNames(Utf8JsonWriter json, string name, IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return;
        }

        json.WriteStartArray(name);
        foreach (string each in names)
        {
            json.WriteStringValue(each);
        }

        json.WriteEndArray();
    }

    /// <summary>The metadata a file keeps; none when it has no "metadata", as every file written before it was kept.</summary>
    private static PieceMetadata ReadMetadata(JsonElement root)
    {
        if (!root.TryGetProperty("metadata", out JsonElement metadata))
        {
            return PieceMetadata.None;
        }

        Expect(metadata, JsonValueKind.Object, "metadata");
        return new PieceMetadata(OptionalText(metadata, "title"), OptionalText(metadata, "composer"), Names(metadata, "tags"), Names(metadata, "playlists"));
    }

    /// <summary>A list of names, or none when the field is left out.</summary>
    private static string?[] Names(JsonElement parent, string name) =>
        !parent.TryGetProperty(name, out JsonElement list) ? []
        : list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Select(each => each.ValueKind == JsonValueKind.String ? each.GetString() : throw Damaged($"metadata {name}"))]
            : throw Damaged($"metadata {name}");

    private static string? OptionalText(JsonElement parent, string name) =>
        !parent.TryGetProperty(name, out JsonElement value) ? null
        : value.ValueKind == JsonValueKind.String ? value.GetString()
        : throw Damaged($"metadata {name}");
}