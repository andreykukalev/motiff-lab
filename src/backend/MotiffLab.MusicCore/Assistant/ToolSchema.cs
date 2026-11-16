using System.Globalization;
using System.Text.Json;

namespace MotiffLab.MusicCore.Assistant;

/// <summary>
/// One JSON schema of a tool's parameters (the AI companion study, section 4.2), written once and used twice: as the
/// schema the model is given, and as the check every call goes through before a tool runs. Strict: every field is
/// required and no other field is allowed, so a value the schema allows is a value the engine accepts. A value outside
/// it is refused with the reason, in the player's language, and nothing runs.
/// </summary>
public abstract class ToolSchema
{
    /// <summary>Writes the schema as JSON Schema (draft 2020-12 subset: type, enum, minimum, maximum, items, pattern).</summary>
    public void Write(Utf8JsonWriter json)
    {
        ArgumentNullException.ThrowIfNull(json);
        json.WriteStartObject();
        WriteFields(json);
        json.WriteEndObject();
    }

    /// <summary>The schema's keywords, inside the object <see cref="Write"/> opens.</summary>
    protected abstract void WriteFields(Utf8JsonWriter json);

    /// <summary>Why <paramref name="value"/> does not fit, naming <paramref name="path"/>; null when it fits.</summary>
    public abstract string? Refusal(JsonElement value, string path, Words words);

    /// <summary>An object with these fields, all required, no others.</summary>
    public static ToolSchema Object(params (string Name, ToolSchema Schema, string Description)[] fields) => new ObjectSchema(fields);

    /// <summary>A whole number from <paramref name="min"/> to <paramref name="max"/>.</summary>
    public static ToolSchema Integer(int min, int max) => new IntegerSchema(min, max, null);

    /// <summary>A whole number that is one of <paramref name="values"/>.</summary>
    public static ToolSchema IntegerOf(params int[] values) => new IntegerSchema(values.Min(), values.Max(), values);

    /// <summary>One of the names of <typeparamref name="T"/>, written in lower camel case ("doubleTime").</summary>
    public static ToolSchema Choice<T>()
        where T : struct, Enum => new ChoiceSchema([.. Enum.GetNames<T>().Select(Camel)]);

    /// <summary>One of <paramref name="values"/>.</summary>
    public static ToolSchema Choice(params string[] values) => new ChoiceSchema(values);

    public static ToolSchema Boolean { get; } = new BooleanSchema();

    /// <summary>Text matching <paramref name="pattern"/>.</summary>
    public static ToolSchema Text(string pattern) => new TextSchema(pattern);

    /// <summary>A list of <paramref name="items"/>, <paramref name="min"/> to <paramref name="max"/> long, each value once when <paramref name="unique"/>.</summary>
    public static ToolSchema List(ToolSchema items, int min, int max, bool unique = false) => new ListSchema(items, min, max, unique);

    /// <summary><paramref name="schema"/> or null: an optional value the strict schema still requires to be sent.</summary>
    public static ToolSchema OrNull(ToolSchema schema) => new NullableSchema(schema);

    /// <summary>"DoubleTime" as "doubleTime".</summary>
    public static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>The enum value a checked choice names, matched without regard to case.</summary>
    public static T Read<T>(JsonElement value)
        where T : struct, Enum => Enum.Parse<T>(value.GetString()!, ignoreCase: true);

    private static string Wrong(string path, string expected, JsonElement value, Words w) => w.T(
        $"\"{path}\" must be {expected}; it is {Shown(value)}.",
        $"«{path}» должно быть {expected}; прислано {Shown(value)}.");

    private static string Shown(JsonElement value)
    {
        string text = value.GetRawText();
        return text.Length <= 40 ? text : $"{text[..40]}…";
    }

    private sealed class ObjectSchema((string Name, ToolSchema Schema, string Description)[] fields) : ToolSchema
    {
        protected override void WriteFields(Utf8JsonWriter json)
        {
            json.WriteString("type", "object");
            json.WriteStartObject("properties");
            foreach ((string name, ToolSchema schema, string description) in fields)
            {
                json.WriteStartObject(name);
                json.WriteString("description", description);
                schema.WriteFields(json);
                json.WriteEndObject();
            }

            json.WriteEndObject();
            json.WriteStartArray("required");
            foreach ((string name, _, _) in fields)
            {
                json.WriteStringValue(name);
            }

            json.WriteEndArray();
            json.WriteBoolean("additionalProperties", false);
        }

        public override string? Refusal(JsonElement value, string path, Words w)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                return Wrong(path, w.T("an object", "объектом"), value, w);
            }

            string prefix = path.Length == 0 ? "" : $"{path}.";
            string? extra = value.EnumerateObject().Select(static property => property.Name).FirstOrDefault(name => fields.All(field => field.Name != name));
            if (extra is not null)
            {
                return w.T(
                    $"\"{prefix}{extra}\" is not a parameter; the parameters are {string.Join(", ", fields.Select(field => field.Name))}.",
                    $"«{prefix}{extra}» не параметр; параметры: {string.Join(", ", fields.Select(field => field.Name))}.");
            }

            foreach ((string name, ToolSchema schema, _) in fields)
            {
                if (!value.TryGetProperty(name, out JsonElement field))
                {
                    return w.T($"\"{prefix}{name}\" is missing.", $"Нет «{prefix}{name}».");
                }

                if (schema.Refusal(field, $"{prefix}{name}", w) is { } refusal)
                {
                    return refusal;
                }
            }

            return null;
        }
    }

    private sealed class IntegerSchema(int min, int max, int[]? values) : ToolSchema
    {
        protected override void WriteFields(Utf8JsonWriter json)
        {
            json.WriteString("type", "integer");
            if (values is not null)
            {
                json.WriteStartArray("enum");
                foreach (int item in values)
                {
                    json.WriteNumberValue(item);
                }

                json.WriteEndArray();
            }
            else
            {
                json.WriteNumber("minimum", min);
                json.WriteNumber("maximum", max);
            }
        }

        public override string? Refusal(JsonElement value, string path, Words w)
        {
            string expected = values is not null
                ? w.T($"one of {string.Join(", ", values)}", $"одним из чисел {string.Join(", ", values)}")
                : w.T($"a whole number from {min} to {max}", $"целым числом от {min} до {max}");
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && number >= min && number <= max && (values is null || values.Contains(number))
                ? null
                : Wrong(path, expected, value, w);
        }
    }

    private sealed class ChoiceSchema(string[] values) : ToolSchema
    {
        protected override void WriteFields(Utf8JsonWriter json)
        {
            json.WriteString("type", "string");
            json.WriteStartArray("enum");
            foreach (string item in values)
            {
                json.WriteStringValue(item);
            }

            json.WriteEndArray();
        }

        public override string? Refusal(JsonElement value, string path, Words w) =>
            value.ValueKind == JsonValueKind.String && values.Any(item => string.Equals(item, value.GetString(), StringComparison.OrdinalIgnoreCase))
                ? null
                : Wrong(path, w.T($"one of {string.Join(", ", values)}", $"одним из: {string.Join(", ", values)}"), value, w);
    }

    private sealed class BooleanSchema : ToolSchema
    {
        protected override void WriteFields(Utf8JsonWriter json)
        {
            json.WriteString("type", "boolean");
        }

        public override string? Refusal(JsonElement value, string path, Words w) =>
            value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : Wrong(path, w.T("true or false", "true или false"), value, w);
    }

    private sealed class TextSchema(string pattern) : ToolSchema
    {
        protected override void WriteFields(Utf8JsonWriter json)
        {
            json.WriteString("type", "string");
            json.WriteString("pattern", pattern);
        }

        public override string? Refusal(JsonElement value, string path, Words w) =>
            value.ValueKind == JsonValueKind.String && System.Text.RegularExpressions.Regex.IsMatch(value.GetString()!, pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                ? null
                : Wrong(path, w.T($"text matching {pattern}", $"текстом вида {pattern}"), value, w);
    }

    private sealed class ListSchema(ToolSchema items, int min, int max, bool unique) : ToolSchema
    {
        protected override void WriteFields(Utf8JsonWriter json)
        {
            json.WriteString("type", "array");
            json.WritePropertyName("items");
            items.Write(json);
            json.WriteNumber("minItems", min);
            json.WriteNumber("maxItems", max);
            if (unique)
            {
                json.WriteBoolean("uniqueItems", true);
            }
        }

        public override string? Refusal(JsonElement value, string path, Words w)
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < min || value.GetArrayLength() > max)
            {
                return Wrong(path, w.T($"a list of {min} to {max} items", $"списком из {min}–{max} элементов"), value, w);
            }

            JsonElement[] all = [.. value.EnumerateArray()];
            for (int i = 0; i < all.Length; i++)
            {
                if (items.Refusal(all[i], string.Create(CultureInfo.InvariantCulture, $"{path}[{i}]"), w) is { } refusal)
                {
                    return refusal;
                }
            }

            bool repeated = unique && all.Select(item => item.GetRawText().ToUpperInvariant()).Distinct(StringComparer.Ordinal).Count() < all.Length;
            return repeated ? w.T($"\"{path}\" names an item twice.", $"В «{path}» элемент назван дважды.") : null;
        }
    }

    private sealed class NullableSchema(ToolSchema inner) : ToolSchema
    {
        protected override void WriteFields(Utf8JsonWriter json)
        {
            json.WriteStartArray("anyOf");
            inner.Write(json);
            json.WriteStartObject();
            json.WriteString("type", "null");
            json.WriteEndObject();
            json.WriteEndArray();
        }

        public override string? Refusal(JsonElement value, string path, Words w) =>
            value.ValueKind == JsonValueKind.Null ? null : inner.Refusal(value, path, w);
    }
}