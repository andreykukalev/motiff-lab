using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The piece's originals as the iPad shows them (version 0.3, local-first; within contract 12): each one's description
/// and whether its bytes are in this project file, with the devices that hold them, so the app says where each one is.
/// </summary>
internal static partial class Answer
{
    /// <summary>
    /// "originals": [{"id", "kind" ("pdf" / "images" / "audio" / "midi" / "musicxml"), "shown", "fileName", "folder"
    /// (version 0.5, where it was in the folder the player imported), "sha256", "bytes", "pages", "durationMs" (each null
    /// when not known), "kept" ("file" / "device" / "link", version 0.5: in the player's folder), "inFile", "held":
    /// [{"device" ("ipad" / "web"), "at"}]}], in the order <see cref="Project.Originals"/> lists them.
    /// </summary>
    public static void Originals(Utf8JsonWriter json, Project project)
    {
        json.WriteStartArray("originals");
        foreach (PieceOriginal original in project.Originals)
        {
            json.WriteStartObject();
            json.WriteString("id", original.Id);
            json.WriteString("kind", original.Kind.ToString().ToLowerInvariant());
            json.WriteString("shown", original.Shown);
            json.WriteString("fileName", original.FileName);
            OptionalText(json, "folder", original.Folder);
            OptionalText(json, "sha256", original.Sha256);
            OptionalNumber(json, "bytes", original.Bytes);
            OptionalNumber(json, "pages", original.Pages);
            OptionalNumber(json, "durationMs", original.DurationMs);
            json.WriteBoolean("inFile", project.HasInFile(original));
            json.WriteString("kept", original.Kept.ToString().ToLowerInvariant());
            json.WriteStartArray("held");
            foreach (OriginalHeld mark in original.Held)
            {
                Held(json, mark);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// One device's mark: {"device", "at"}.
    private static void Held(Utf8JsonWriter json, OriginalHeld mark)
    {
        json.WriteStartObject();
        json.WriteString("at", mark.At.ToString("O", CultureInfo.InvariantCulture));
        json.WriteString("device", mark.Device.ToString().ToLowerInvariant());
        json.WriteEndObject();
    }

    private static void OptionalText(Utf8JsonWriter json, string name, string? value)
    {
        if (value is null)
        {
            json.WriteNull(name);
        }
        else
        {
            json.WriteString(name, value);
        }
    }

    private static void OptionalNumber(Utf8JsonWriter json, string name, long? value)
    {
        if (value is { } number)
        {
            json.WriteNumber(name, number);
        }
        else
        {
            json.WriteNull(name);
        }
    }
}