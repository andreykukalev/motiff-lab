using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// A version 16 file laid out as a file of an earlier version (docs/project-file.md, "From version 15 (and earlier) to
/// 16", read backwards), so the tests of the older readers build their files from today's writer: the notes, the PDF and
/// the work at the top level, in the order the version 15 writer wrote them.
/// </summary>
internal static class LegacyFile
{
    private static readonly JsonSerializerOptions s_indented = new() { WriteIndented = true };

    /// <summary>The file as version <paramref name="version"/> laid it out, its text indented as the writer before version 16 wrote it.</summary>
    public static string AsVersion(byte[] written, int version) => Flat(written, version).ToJsonString(s_indented).ReplaceLineEndings("\n");

    /// <summary>The same as bytes.</summary>
    public static byte[] Bytes(byte[] written, int version) => Encoding.UTF8.GetBytes(AsVersion(written, version));

    private static JsonObject Flat(byte[] written, int version)
    {
        JsonObject root = JsonNode.Parse(written)!.AsObject();
        JsonObject work = root["workspace"]!.AsObject();
        string? working = work["working"]?.GetValue<string>();
        JsonObject[] notes = [.. root["notes"]!.AsArray().Select(static entry => entry!.AsObject())];
        JsonObject? base_ = notes.FirstOrDefault(entry => entry["id"]!.GetValue<string>() == working);
        JsonObject[] originals = [.. root["originals"]!.AsArray().Select(static entry => entry!.AsObject())];
        JsonObject? pdf = originals.FirstOrDefault(static original => original["id"]!.GetValue<string>() == PieceOriginal.DocumentId);

        JsonObject flat = new() { ["format"] = ProjectFile.FormatName, ["version"] = version };
        foreach (string name in new[] { "id", "fileName", "createdAt", "lastOpened" })
        {
            flat[name] = root[name]!.DeepClone();
        }

        Copy(work, flat, "metadata");
        flat["source"] = base_?["music"]?.DeepClone() ?? "";
        Copy(pdf, flat, "data", "document");
        Copy(pdf, flat, "marks", "documentMarks");
        PageMap(base_, flat);
        flat["originals"] = new JsonArray([.. originals.Select(Description)]);
        foreach (string name in new[] { "partOf", "editsOff", "tempo", "motifs", "variations", "observations" })
        {
            Copy(work, flat, name);
        }

        flat["edits"] = base_?["edits"]?.DeepClone() ?? new JsonArray();
        foreach (string name in new[] { "phrases", "marks", "bookmarks", "resumePoints" })
        {
            Copy(work, flat, name);
        }

        flat["check"] = base_?["check"]?.DeepClone();
        flat["performances"] = new JsonArray([.. notes.Where(entry => entry != base_).Select(Performance)]);
        Copy(work, flat, "assembly");
        Copy(work, flat, "settings");
        flat["scan"] = Origin(base_, "scan");
        flat["transcription"] = Origin(base_, "transcription");
        return flat;
    }

    private static void Copy(JsonObject? from, JsonObject to, string name, string? as_ = null)
    {
        if (from?[name] is { } value)
        {
            to[as_ ?? name] = value.DeepClone();
        }
    }

    private static void PageMap(JsonObject? notes, JsonObject flat)
    {
        if (notes?["pageMap"] is not JsonObject map)
        {
            return;
        }

        if (map["project"] is { } project)
        {
            flat["readFrom"] = new JsonObject { ["project"] = project.DeepClone(), ["pages"] = map["starts"]!.DeepClone() };
        }
        else
        {
            flat["documentPages"] = map["starts"]!.DeepClone();
        }
    }

    private static JsonObject Description(JsonObject original)
    {
        JsonObject described = original.DeepClone().AsObject();
        described.Remove("data");
        described.Remove("marks");
        return described;
    }

    /// <summary>A scan's or a transcription's block with the engine, original and time its provenance names; null without one.</summary>
    private static JsonNode? Origin(JsonObject? notes, string name)
    {
        if (notes?[name] is not JsonObject block)
        {
            return null;
        }

        JsonObject provenance = notes["provenance"]!.AsObject();
        JsonObject origin = new() { ["engine"] = provenance["engine"]!.DeepClone() };
        foreach ((string key, JsonNode? value) in block)
        {
            origin[key] = value?.DeepClone();
        }

        MadeFrom(provenance, origin);
        return origin;
    }

    private static JsonObject Performance(JsonObject notes)
    {
        JsonObject provenance = notes["provenance"]!.AsObject();
        JsonObject comparison = notes["performance"]!.AsObject();
        JsonObject performance = new() { ["id"] = notes["id"]!.DeepClone(), ["name"] = notes["name"]!.DeepClone(), ["source"] = comparison["source"]!.DeepClone() };
        Copy(provenance, performance, "engine");
        performance["midi"] = notes["music"]!.DeepClone();
        foreach (string name in new[] { "recordingStart", "anchors", "dismissed", "taken", "merge" })
        {
            Copy(comparison, performance, name);
        }

        MadeFrom(provenance, performance);
        return performance;
    }

    private static void MadeFrom(JsonObject provenance, JsonObject into)
    {
        if (provenance["from"]!.AsArray().FirstOrDefault(static source => source!["project"] is null) is { } from)
        {
            into["from"] = from["original"]!.DeepClone();
        }

        Copy(provenance, into, "at");
    }
}