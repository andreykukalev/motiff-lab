using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MotiffLab.Api.Assistant;

/// <summary>
/// The catalogue's tool names as Claude and Azure OpenAI allow them (letters, digits, "_" and "-"): "harmony.chords" is sent
/// as "harmony_chords" and read back as "harmony.chords", so the relay and the device only ever see the catalogue's names.
/// </summary>
internal sealed class ToolNames
{
    private readonly Dictionary<string, string> _wire = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _catalogue = new(StringComparer.Ordinal);

    /// <summary>The names of the tools in {"tools": [{name, ...}]}.</summary>
    public static ToolNames From(JsonElement catalogue)
    {
        var names = new ToolNames();
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

    /// <summary>The tool's description in the catalogue, or empty.</summary>
    public static string Description(JsonElement tool) =>
        tool.TryGetProperty("description", out JsonElement description) ? description.GetString() ?? "" : "";

    /// <summary>The name the model is given for a catalogue name.</summary>
    public string Wire(string name) => _wire.TryGetValue(name, out string? wire) ? wire : Escape(name);

    /// <summary>The catalogue name of a name the model used; a name it was not given stays as it is (the relay refuses it).</summary>
    public string Catalogue(string wire) => _catalogue.TryGetValue(wire, out string? name) ? name : wire;

    private static string Escape(string name) =>
        string.Concat(name.Select(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
}

/// <summary>
/// A tool's parameters as a strict schema for a provider. The catalogue's schemas are strict already (every field required,
/// no other field), but strict tool use takes only part of JSON Schema, less on Azure OpenAI than on Claude, so the keywords
/// a provider does not take are moved into the field's description, where the model reads them; the device checks every
/// call against the full schema before a tool runs (TurnRunner), so nothing out of range runs.
/// </summary>
internal static class StrictToolSchemas
{
    /// <summary>Claude: no numeric bounds, lengths or array limits beyond minItems 0 or 1.</summary>
    private static readonly Dialect s_claude = new(
        ["minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf", "minLength", "maxLength", "maxItems", "uniqueItems"],
        MinItemsAboveOne: true,
        CloseEveryObject: false);

    /// <summary>Azure OpenAI (structured outputs): no bounds, lengths, patterns, formats or array limits; every object closed.</summary>
    private static readonly Dialect s_openAI = new(
        ["minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf", "minLength", "maxLength", "pattern", "format",
         "minItems", "maxItems", "uniqueItems", "minProperties", "maxProperties", "patternProperties", "propertyNames"],
        MinItemsAboveOne: false,
        CloseEveryObject: true);

    private static readonly string[] s_branches = ["anyOf", "allOf"];

    /// <summary>The schema's keywords for a Claude tool's input_schema.</summary>
    public static Dictionary<string, JsonElement> ForClaude(JsonElement parameters) =>
        Strict(parameters, s_claude).ToDictionary(static field => field.Key, static field => JsonSerializer.SerializeToElement(field.Value), StringComparer.Ordinal);

    /// <summary>The schema as JSON for an Azure OpenAI function tool's parameters.</summary>
    public static string ForOpenAI(JsonElement parameters) => Strict(parameters, s_openAI).ToJsonString();

    private static JsonObject Strict(JsonElement parameters, Dialect dialect)
    {
        var schema = (JsonObject)JsonNode.Parse(parameters.GetRawText())!;
        Loosen(schema, dialect);
        schema["additionalProperties"] = false;
        return schema;
    }

    private static void Loosen(JsonObject schema, Dialect dialect)
    {
        MoveToDescription(schema, dialect);
        if (schema["properties"] is JsonObject properties)
        {
            if (dialect.CloseEveryObject)
            {
                schema["additionalProperties"] = false;
            }

            foreach (JsonObject field in properties.Select(static property => property.Value).OfType<JsonObject>())
            {
                Loosen(field, dialect);
            }
        }

        if (schema["items"] is JsonObject items)
        {
            Loosen(items, dialect);
        }

        foreach (JsonObject branch in s_branches.Select(key => schema[key]).OfType<JsonArray>().SelectMany(static list => list).OfType<JsonObject>())
        {
            Loosen(branch, dialect);
        }
    }

    private static void MoveToDescription(JsonObject schema, Dialect dialect)
    {
        var limits = dialect.Moved.Where(schema.ContainsKey).ToList();
        if (dialect.MinItemsAboveOne && schema["minItems"] is JsonValue minItems && minItems.TryGetValue(out int least) && least > 1)
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

    /// <summary>What a provider's strict mode does not take: the keywords moved, minItems only above 1, every object closed.</summary>
    private sealed record Dialect(string[] Moved, bool MinItemsAboveOne, bool CloseEveryObject);
}