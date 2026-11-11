using System.Text.Json;

namespace MotiffLab.MusicCore.Projects;

/// <summary>The piece's title, composer and tags as the player set them in the library (version 0.2, within version 15).</summary>
public static partial class ProjectFile
{
    /// <summary>"metadata": {"title", "composer", "tags"}, each only when set; nothing at all while none is.</summary>
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

        if (metadata.Tags.Count > 0)
        {
            json.WriteStartArray("tags");
            foreach (string tag in metadata.Tags)
            {
                json.WriteStringValue(tag);
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    /// <summary>The metadata a file keeps; none when it has no "metadata", as every file written before it was kept.</summary>
    private static PieceMetadata ReadMetadata(JsonElement root)
    {
        if (!root.TryGetProperty("metadata", out JsonElement metadata))
        {
            return PieceMetadata.None;
        }

        Expect(metadata, JsonValueKind.Object, "metadata");
        IEnumerable<string?> tags = metadata.TryGetProperty("tags", out JsonElement list)
            ? list.ValueKind == JsonValueKind.Array
                ? [.. list.EnumerateArray().Select(tag => tag.ValueKind == JsonValueKind.String ? tag.GetString() : throw Damaged("metadata tags"))]
                : throw Damaged("metadata tags")
            : [];
        return new PieceMetadata(OptionalText(metadata, "title"), OptionalText(metadata, "composer"), tags);
    }

    private static string? OptionalText(JsonElement parent, string name) =>
        !parent.TryGetProperty(name, out JsonElement value) ? null
        : value.ValueKind == JsonValueKind.String ? value.GetString()
        : throw Damaged($"metadata {name}");
}