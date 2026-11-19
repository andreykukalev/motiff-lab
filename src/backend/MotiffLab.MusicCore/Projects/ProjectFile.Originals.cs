using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects.File;

using static MotiffLab.MusicCore.Projects.File.JsonRead;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The descriptions of the piece's originals (version 0.3, local-first: Andrei 2026-11-11 21:20 UTC), and the links
/// from the notes to the original they were made from, kept within version 15 as optional fields, like "metadata", so
/// version 0.2 of both apps still opens the file (it reads past fields it does not know).
/// </summary>
public static partial class ProjectFile
{
    internal const string OriginalsList = "originals";

    /// <summary>The fields of an original's description (version 0.3), as every original of version 16 starts.</summary>
    private static void WriteOriginalFields(Utf8JsonWriter json, PieceOriginal original)
    {
        json.WriteString("id", original.Id);
        json.WriteString("kind", original.Kind.ToString().ToLowerInvariant());
        if (original.Name is { } name)
        {
            json.WriteString("name", name);
        }

        json.WriteString("fileName", original.FileName);
        if (original.Folder is { } folder)
        {
            json.WriteString("folder", folder);
        }

        if (original.Sha256 is { } sha256)
        {
            json.WriteString("sha256", sha256);
        }

        if (original.Bytes is { } bytes)
        {
            json.WriteNumber("bytes", bytes);
        }

        if (original.Pages is { } pages)
        {
            json.WriteNumber("pages", pages);
        }

        if (original.DurationMs is { } duration)
        {
            json.WriteNumber("durationMs", duration);
        }

        json.WriteString("addedAt", original.AddedAt.ToString("O", CultureInfo.InvariantCulture));
        json.WriteString("kept", original.Kept.ToString().ToLowerInvariant());
        if (original.Held.Count > 0)
        {
            json.WriteStartArray("held");
            foreach (OriginalHeld mark in original.Held)
            {
                json.WriteStartObject();
                json.WriteString("device", mark.Device.ToString().ToLowerInvariant());
                json.WriteString("at", mark.At.ToString("O", CultureInfo.InvariantCulture));
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }
    }

    internal static PieceOriginal ReadOriginal(JsonElement original)
    {
        Expect(original, JsonValueKind.Object, OriginalsList);
        try
        {
            return new PieceOriginal(Text(original, "id"), LowerName<OriginalKind>(original, "kind"), Text(original, "fileName"), LowerName<OriginalKept>(original, "kept"), JsonRead.Time(original, "addedAt"))
            {
                Name = original.TryGetProperty("name", out _) ? Text(original, "name") : null,
                Folder = original.TryGetProperty("folder", out JsonElement folder) ? FolderOf(folder) : null,
                Sha256 = original.TryGetProperty("sha256", out _) ? Text(original, "sha256") : null,
                Bytes = original.TryGetProperty("bytes", out _) ? Long(original, "bytes") : null,
                Pages = original.TryGetProperty("pages", out _) ? Integer(original, "pages") : null,
                DurationMs = original.TryGetProperty("durationMs", out _) ? Long(original, "durationMs") : null,
                Held = original.TryGetProperty("held", out JsonElement held)
                    ? [.. Elements(held, "originals held").Select(mark =>
                    {
                        Expect(mark, JsonValueKind.Object, "originals held");
                        return new OriginalHeld(LowerName<HoldingDevice>(mark, "device"), JsonRead.Time(mark, "at"));
                    })]
                    : [],
            };
        }
        catch (ArgumentException)
        {
            throw Damaged(OriginalsList);
        }
    }

    /// <summary>"folder" (version 0.5): where the file was inside the folder the player imported; empty text for the folder itself.</summary>
    private static string FolderOf(JsonElement folder) =>
        folder.ValueKind == JsonValueKind.String && PieceOriginal.IsFolder(folder.GetString()!) ? folder.GetString()! : throw Damaged(OriginalsList);

    /// <summary>
    /// The project read, when it is a piece: with notes, with its PDF, or (version 0.5) describing its PDF that is in the
    /// player's folder or on another device (<see cref="Project.DescribesDocument"/>); a file with none of them is damaged.
    /// </summary>
    private static Project Described(this Project project) =>
        project.HasNotes || project.Document is not null || project.DescribesDocument ? project : throw Damaged("source");
}