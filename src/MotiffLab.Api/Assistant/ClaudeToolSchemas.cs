using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MotiffLab.Api.Assistant;

/// <summary>
/// The catalogue's tool names as Claude allows them (letters, digits, "_" and "-"): "harmony.chords" is sent as
/// "harmony_chords" and read back as "harmony.chords", so the relay and the device only ever see the catalogue's names.
/// </summary>
internal sealed class ClaudeToolNames
{
    private readonly Dictionary<string, string> _wire = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _catalogue = new(StringComparer.Ordinal);

    /// <summary>The names of the tools in {"tools": [{name, ...}]}.</summary>
    public static ClaudeToolNames From(JsonElement catalogue)
    {
        var names = new ClaudeToolNames();
        foreach (JsonElement tool in Entries(catalogue))
        {
            string name = tool.GetProperty("name").GetString()!;
            string wire = Escape(name);
            names._wire[name] = wire;
            names._catalogue[wire] = name;
        }

        return names;
    }

    /// <summary>The tools of the catalogue that have a name and parameters.</summary>
    public static IEnumerable<JsonElement> Entries(JsonElement catalogue) =>
        catalogue.ValueKind == JsonValueKind.Object && catalogue.TryGetProperty("tools", out JsonElement tools) && tools.ValueKind == JsonValueKind.Array
            ? tools.EnumerateArray().Where(static tool => tool.ValueKind == JsonValueKind.Object
                && tool.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String
                && tool.TryGetProperty("parameters", out JsonElement parameters) && parameters.ValueKind == JsonValueKind.Object)
            : [];

    /// <summary>The name Claude is given for a catalogue name.</summary>
    public string Wire(string name) => _wire.TryGetValue(name, out string? wire) ? wire : Escape(name);

    /// <summary>The catalogue name of a name Claude used; a name it was not given stays as it is (the relay refuses it).</summary>
    public string Catalogue(string wire) => _catalogue.TryGetValue(wire, out string? name) ? name : wire;

    private static string Escape(string name) =>
        string.Concat(name.Select(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
}

/// <summary>
/// A tool's parameters as a strict Claude schema. The catalogue's schemas are strict already (every field required, no
/// other field), but strict tool use does not take numeric bounds, lengths or array limits beyond minItems 0 or 1, so those
/// keywords are moved into the field's description, where the model reads them; the device checks every call against the
/// full schema before a tool runs (TurnRunner), so nothing out of range runs.
/// </summary>
internal static class ClaudeToolSchemas
{
    private static readonly string[] s_moved = ["minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf", "minLength", "maxLength", "maxItems", "uniqueItems"];

    private static readonly string[] s_branches = ["anyOf", "allOf"];

    /// <summary>The schema's keywords for the tool's input_schema.</summary>
    public static Dictionary<string, JsonElement> Strict(JsonElement parameters)
    {
        var schema = (JsonObject)JsonNode.Parse(parameters.GetRawText())!;
        Loosen(schema);
        schema["additionalProperties"] = false;
        return schema.ToDictionary(static field => field.Key, static field => JsonSerializer.SerializeToElement(field.Value), StringComparer.Ordinal);
    }

    private static void Loosen(JsonObject schema)
    {
        MoveToDescription(schema);
        if (schema["properties"] is JsonObject properties)
        {
            foreach (JsonObject field in properties.Select(static property => property.Value).OfType<JsonObject>())
            {
                Loosen(field);
            }
        }

        if (schema["items"] is JsonObject items)
        {
            Loosen(items);
        }

        foreach (JsonObject branch in s_branches.Select(key => schema[key]).OfType<JsonArray>().SelectMany(static list => list).OfType<JsonObject>())
        {
            Loosen(branch);
        }
    }

    private static void MoveToDescription(JsonObject schema)
    {
        var limits = s_moved.Where(schema.ContainsKey).ToList();
        if (schema["minItems"] is JsonValue minItems && minItems.TryGetValue(out int least) && least > 1)
        {
            limits.Insert(0, "minItems");
        }

        if (limits.Count == 0)
        {
            return;
        }

        string note = string.Join(", ", limits.Select(key => string.Create(CultureInfo.InvariantCulture, $"{key} {schema[key]!.ToJsonString()}")));
        foreach (string key in limits)
        {
            schema.Remove(key);
        }

        string description = schema["description"]?.GetValue<string>() is { Length: > 0 } text ? $"{text} " : "";
        schema["description"] = $"{description}(Limits: {note}.)";
    }
}