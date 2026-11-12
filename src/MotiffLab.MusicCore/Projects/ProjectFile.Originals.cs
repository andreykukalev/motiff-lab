using System.Globalization;
using System.Text.Json;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The descriptions of the piece's originals (version 0.3, local-first: Andrei 2026-11-11 21:20 UTC), and the links
/// from the notes to the original they were made from, kept within version 15 as optional fields, like "metadata", so
/// version 0.2 of both apps still opens the file (it reads past fields it does not know).
/// </summary>
public static partial class ProjectFile
{
    private const string OriginalsList = "originals";

    /// <summary>
    /// "originals": [ … ], the descriptions the piece's own data does not show already (<see cref="PieceOriginals.Derived"/>):
    /// one with a fingerprint or a device's mark, or one whose bytes are not in this file. Left out while there is none,
    /// so a file of a piece with nothing more to say about its originals is written as before.
    /// </summary>
    private static void WriteOriginals(Utf8JsonWriter json, Project project)
    {
        Dictionary<string, PieceOriginal> derived = PieceOriginals.Derived(project).ToDictionary(static original => original.Id, StringComparer.Ordinal);
        List<PieceOriginal> kept = [.. project.KeptOriginals.Where(original => !derived.TryGetValue(original.Id, out PieceOriginal? same) || !same.Equals(original))];
        List<UnknownEntity> unknown = KeptUnknown(project.Unknown, OriginalsList, project.KeptOriginals.Select(static original => original.Id), null, out _);
        if (kept.Count == 0 && unknown.Count == 0)
        {
            return;
        }

        WriteList(json, OriginalsList, kept, unknown, original =>
        {
            json.WriteStartObject();
            json.WriteString("id", original.Id);
            json.WriteString("kind", original.Kind.ToString().ToLowerInvariant());
            if (original.Name is { } name)
            {
                json.WriteString("name", name);
            }

            json.WriteString("fileName", original.FileName);
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

            json.WriteEndObject();
        });
    }

    /// <summary>The project with the originals its file keeps; none when it has no "originals", as every file before version 0.3.</summary>
    private static Project WithOriginalsOf(this Project project, JsonElement root, Unknowns unknowns) =>
        project with { KeptOriginals = ReadOriginals(root, unknowns) };

    private static List<PieceOriginal> ReadOriginals(JsonElement root, Unknowns unknowns)
    {
        if (!root.TryGetProperty(OriginalsList, out JsonElement originals))
        {
            return [];
        }

        Expect(originals, JsonValueKind.Array, OriginalsList);
        List<PieceOriginal> read = ReadList(originals, OriginalsList, ReadOriginal, unknowns);
        return read.Select(static original => original.Id).Distinct(StringComparer.Ordinal).Count() == read.Count ? read : throw Damaged(OriginalsList);
    }

    private static PieceOriginal ReadOriginal(JsonElement original)
    {
        Expect(original, JsonValueKind.Object, OriginalsList);
        try
        {
            return new PieceOriginal(Text(original, "id"), LowerName<OriginalKind>(original, "kind"), Text(original, "fileName"), LowerName<OriginalKept>(original, "kept"), Time(original, "addedAt"))
            {
                Name = original.TryGetProperty("name", out _) ? Text(original, "name") : null,
                Sha256 = original.TryGetProperty("sha256", out _) ? Text(original, "sha256") : null,
                Bytes = original.TryGetProperty("bytes", out _) ? Long(original, "bytes") : null,
                Pages = original.TryGetProperty("pages", out _) ? Integer(original, "pages") : null,
                DurationMs = original.TryGetProperty("durationMs", out _) ? Long(original, "durationMs") : null,
                Held = original.TryGetProperty("held", out JsonElement held)
                    ? [.. Elements(held, "originals held").Select(mark =>
                    {
                        Expect(mark, JsonValueKind.Object, "originals held");
                        return new OriginalHeld(LowerName<HoldingDevice>(mark, "device"), Time(mark, "at"));
                    })]
                    : [],
            };
        }
        catch (ArgumentException)
        {
            throw Damaged(OriginalsList);
        }
    }

    /// <summary>"from" and "at" on a scan, a transcription or a performance (version 0.3): the original the notes were made from, and when.</summary>
    private static void WriteMadeFrom(Utf8JsonWriter json, string? from, DateTimeOffset? at)
    {
        if (from is not null)
        {
            json.WriteString("from", from);
        }

        if (at is { } time)
        {
            json.WriteString("at", time.ToString("O", CultureInfo.InvariantCulture));
        }
    }

    private static (string? From, DateTimeOffset? At) ReadMadeFrom(JsonElement element) => (
        element.TryGetProperty("from", out _) ? Text(element, "from") : null,
        element.TryGetProperty("at", out _) ? Time(element, "at") : null);

    private static JsonElement.ArrayEnumerator Elements(JsonElement array, string what)
    {
        Expect(array, JsonValueKind.Array, what);
        return array.EnumerateArray();
    }
}