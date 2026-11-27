using System.Globalization;
using System.Text.Json;

namespace MotiffLab.MusicCore.Projects.File;

/// <summary>
/// The JSON primitives the project file is read with: a field of the kind expected, or the file is damaged and refused
/// with a plain reason (<see cref="Damaged"/>); an enum by its name, marked when the value is one this build does not
/// know, so the tolerant reader can keep the entity that holds it (decision record 0026, S6).
/// </summary>
internal static class JsonRead
{
    internal static JsonDocument Parse(ReadOnlySpan<byte> data)
    {
        try
        {
            return JsonDocument.Parse(data.ToArray());
        }
        catch (JsonException)
        {
            throw new ProjectFileException("This is not a Motiff Lab project file: it is not valid JSON.");
        }
    }

    internal static JsonElement Property(JsonElement parent, string name, JsonValueKind kind)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != kind)
        {
            throw Damaged(name);
        }

        return value;
    }

    internal static void Expect(JsonElement element, JsonValueKind kind, string what)
    {
        if (element.ValueKind != kind)
        {
            throw Damaged(what);
        }
    }

    internal static JsonElement Expected(JsonElement element, string what)
    {
        Expect(element, JsonValueKind.Object, what);
        return element;
    }

    internal static JsonElement.ArrayEnumerator Elements(JsonElement array, string what)
    {
        Expect(array, JsonValueKind.Array, what);
        return array.EnumerateArray();
    }

    internal static string Text(JsonElement parent, string name)
    {
        string value = Property(parent, name, JsonValueKind.String).GetString()!;
        return value.Length > 0 ? value : throw Damaged(name);
    }

    internal static bool Boolean(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw Damaged(name);

    internal static int Integer(JsonElement parent, string name) =>
        Property(parent, name, JsonValueKind.Number).TryGetInt32(out int value) ? value : throw Damaged(name);

    internal static double Number(JsonElement parent, string name) =>
        Property(parent, name, JsonValueKind.Number).TryGetDouble(out double value) && double.IsFinite(value) ? value : throw Damaged(name);

    internal static long Long(JsonElement parent, string name) =>
        Property(parent, name, JsonValueKind.Number).TryGetInt64(out long value) ? value : throw Damaged(name);

    internal static DateTimeOffset Time(JsonElement parent, string name) =>
        DateTimeOffset.TryParseExact(Text(parent, name), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset value)
            ? value
            : throw Damaged(name);

    internal static T Name<T>(JsonElement parent, string name)
        where T : struct, Enum
    {
        string text = Text(parent, name);
        return TryName(text, out T value) ? value : throw Unknown<T>(name, text);
    }

    internal static T LowerName<T>(JsonElement parent, string name)
        where T : struct, Enum
    {
        string text = Text(parent, name);
        return Enum.GetNames<T>().FirstOrDefault(known => string.Equals(known, text, StringComparison.OrdinalIgnoreCase)) is { } match ? Enum.Parse<T>(match) : throw Unknown<T>(name, text);
    }

    /// <summary>
    /// The reason for a value that is none of the enum's names: "damaged", as for any field, marked with the enum's type
    /// name when the value is itself a name (ASCII letters and digits, from a letter), so a tolerant read can keep the
    /// entity that holds it (S6). Where no list keeps it, the file is refused as before.
    /// </summary>
    internal static ProjectFileException Unknown<T>(string name, string text)
        where T : struct, Enum
        => Marked(Damaged(name), typeof(T).Name, text);

    /// <summary>
    /// <paramref name="reason"/> marked as a value of <paramref name="type"/> this build does not know when the value is a
    /// name (ASCII letters and digits, from a letter), so a tolerant read can keep the entity that holds it (S6).
    /// </summary>
    internal static ProjectFileException Marked(ProjectFileException reason, string type, string text)
    {
        if (text.Length > 0 && char.IsAsciiLetter(text[0]) && text.All(char.IsAsciiLetterOrDigit))
        {
            reason.Data[UnknownEnumKey] = type;
        }

        return reason;
    }

    /// <summary>An enum by its exact name; numbers are not names.</summary>
    internal static bool TryName<T>(string? text, out T value)
        where T : struct, Enum
    {
        value = default;
        return text is not null && Enum.GetNames<T>().Contains(text, StringComparer.Ordinal) && Enum.TryParse(text, out value);
    }

    /// <summary>The key under which a "damaged" reason names the enum whose value this build does not know.</summary>
    internal const string UnknownEnumKey = "MotiffLab.UnknownEnum";

    /// <summary>Two whole numbers, as "pages" and "ms" hold them.</summary>
    internal static (long First, long Last) Pair(JsonElement pair) =>
        pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() == 2
        && pair[0].ValueKind == JsonValueKind.Number && pair[0].TryGetInt64(out long first)
        && pair[1].ValueKind == JsonValueKind.Number && pair[1].TryGetInt64(out long last)
            ? (first, last)
            : throw Damaged("partOf");

    internal static ProjectFileException Damaged(string what) =>
        new($"This project file is damaged: its {what} cannot be read.");
}