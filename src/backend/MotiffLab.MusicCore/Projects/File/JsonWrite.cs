using System.Text.Json;

namespace MotiffLab.MusicCore.Projects.File;

/// <summary>The JSON primitives the project file is written with, beside <see cref="Utf8JsonWriter"/>'s own.</summary>
internal static class JsonWrite
{
    /// <summary>An enum in lower camel case, as the pencil layer writes a stroke's tool and colour.</summary>
    internal static string Lower<T>(T value)
        where T : struct, Enum
    {
        string name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    internal static void WriteNumbers(Utf8JsonWriter json, string name, IEnumerable<long> numbers)
    {
        json.WriteStartArray(name);
        foreach (long number in numbers)
        {
            json.WriteNumberValue(number);
        }

        json.WriteEndArray();
    }
}