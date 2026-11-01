using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The JSON object a call came with, read field by field (0020). A missing field is null; a field
/// of the wrong kind is a bad request. Nothing here uses reflection, so the native build needs none.
/// </summary>
internal sealed class Request : IDisposable
{
    private readonly JsonDocument? _document;
    private readonly JsonElement? _root;

    private Request(JsonDocument? document, JsonElement? root)
    {
        _document = document;
        _root = root;
    }

    public static Request Empty { get; } = new(null, null);

    public static Request Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw FacadeException.BadRequest($"The request is not JSON: {exception.Message}");
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw FacadeException.BadRequest("The request must be a JSON object.");
        }

        return new Request(document, document.RootElement);
    }

    public static Request Of(JsonElement element) => new(null, element);

    public bool Has(string name) => _root is { } root && root.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null;

    public JsonElement? Element(string name) => _root is { } root && root.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null ? value : null;

    public string? Text(string name) => Element(name) switch
    {
        null => null,
        { ValueKind: JsonValueKind.String } value => value.GetString(),
        _ => throw Wrong(name, "text"),
    };

    public string Required(string name) => Text(name) ?? throw FacadeException.BadRequest($"The request needs \"{name}\".");

    public int? Int(string name) => Element(name) switch
    {
        null => null,
        { ValueKind: JsonValueKind.Number } value when value.TryGetInt32(out int number) => number,
        _ => throw Wrong(name, "a whole number"),
    };

    public long? Long(string name) => Element(name) switch
    {
        null => null,
        { ValueKind: JsonValueKind.Number } value when value.TryGetInt64(out long number) => number,
        _ => throw Wrong(name, "a whole number"),
    };

    public bool? Bool(string name) => Element(name) switch
    {
        null => null,
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        _ => throw Wrong(name, "true or false"),
    };

    public Request Object(string name) => Element(name) switch
    {
        null => Empty,
        { ValueKind: JsonValueKind.Object } value => Of(value),
        _ => throw Wrong(name, "an object"),
    };

    public IReadOnlyList<string> Texts(string name) => Element(name) switch
    {
        null => [],
        { ValueKind: JsonValueKind.Array } value => [.. value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : throw Wrong(name, "a list of text"))],
        _ => throw Wrong(name, "a list of text"),
    };

    /// <summary>An enum named in lower camel case, as answers write it, or in the C# spelling.</summary>
    public T? Enum<T>(string name)
        where T : struct, Enum
    {
        string? text = Text(name);
        if (text is null)
        {
            return null;
        }

        return System.Enum.TryParse(text, ignoreCase: true, out T value) && System.Enum.IsDefined(value)
            ? value
            : throw FacadeException.BadRequest($"\"{name}\" cannot be \"{text}\".");
    }

    public IReadOnlyList<T> Enums<T>(string name)
        where T : struct, Enum
    {
        var values = new List<T>();
        foreach (string text in Texts(name))
        {
            values.Add(System.Enum.TryParse(text, ignoreCase: true, out T value) && System.Enum.IsDefined(value)
                ? value
                : throw FacadeException.BadRequest($"\"{name}\" cannot hold \"{text}\"."));
        }

        return values;
    }

    /// <summary>A span {"onset", "end"} in ticks.</summary>
    public TickSpan? Span(string name)
    {
        Request span = Object(name);
        if (span._root is null)
        {
            return null;
        }

        long onset = span.Long("onset") ?? throw FacadeException.BadRequest($"\"{name}\" needs \"onset\".");
        long end = span.Long("end") ?? throw FacadeException.BadRequest($"\"{name}\" needs \"end\".");
        return onset >= 0 && end > onset ? TickSpan.FromBounds(new Tick(onset), new Tick(end)) : throw FacadeException.BadRequest($"\"{name}\" must end after it starts.");
    }

    /// <summary>A filter {"hand": "right"}, {"track": 0} or {"track": 0, "voice": 1}; nothing picks every note.</summary>
    public SelectionFilter Filter(string name)
    {
        Request filter = Object(name);
        if (filter._root is null)
        {
            return SelectionFilter.All;
        }

        try
        {
            if (filter.Enum<Hand>("hand") is { } hand)
            {
                return SelectionFilter.ForHand(hand);
            }

            return (filter.Int("track"), filter.Int("voice")) switch
            {
                (int track, int voice) => SelectionFilter.ForVoice(track, voice),
                (int track, null) => SelectionFilter.ForTrack(track),
                (null, int) => throw FacadeException.BadRequest("A voice filter needs its track."),
                _ => SelectionFilter.All,
            };
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }

    public JsonElement Root => _root ?? throw FacadeException.BadRequest("The request is empty.");

    public bool IsEmpty => _root is null;

    public void Dispose() => _document?.Dispose();

    private static FacadeException Wrong(string name, string expected) => FacadeException.BadRequest($"\"{name}\" must be {expected}.");
}