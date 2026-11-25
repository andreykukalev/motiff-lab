using System.Numerics;
using System.Text.Json;

namespace MotiffLab.MusicCore;

/// <summary>
/// The JSON primitives the facade's answers and the project file are both written with (audit D16), beside
/// <see cref="Utf8JsonWriter"/>'s own: an enum's name, a list of texts and a list of numbers. They know no music type, so
/// every namespace may use them; the shapes of music (a stroke, a range) are the project file's, which the facade reaches
/// through <c>ProjectFile</c>.
/// </summary>
internal static class JsonWrite
{
    /// <summary>An enum's name in lower camel case: "pitchOrder", "userEdited".</summary>
    internal static string Name<T>(T value)
        where T : struct, Enum
    {
        string name = value.ToString();
        return name.Length > 0 ? char.ToLowerInvariant(name[0]) + name[1..] : name;
    }

    /// <summary>A list of texts: the value of <paramref name="name"/>, or with no name an element of the array being written.</summary>
    internal static void Texts(Utf8JsonWriter json, string? name, IEnumerable<string> texts)
    {
        if (name is null)
        {
            json.WriteStartArray();
        }
        else
        {
            json.WriteStartArray(name);
        }

        foreach (string text in texts)
        {
            json.WriteStringValue(text);
        }

        json.WriteEndArray();
    }

    /// <summary>A list of whole numbers: counts, bars, ticks (a tick's <c>Value</c>), pitches.</summary>
    internal static void Numbers<T>(Utf8JsonWriter json, string name, IEnumerable<T> numbers)
        where T : IBinaryInteger<T>
    {
        json.WriteStartArray(name);
        foreach (T number in numbers)
        {
            json.WriteNumberValue(long.CreateChecked(number));
        }

        json.WriteEndArray();
    }
}