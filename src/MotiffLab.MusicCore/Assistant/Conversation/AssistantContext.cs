using System.Text.Json;

using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Assistant.Conversation;

/// <summary>
/// What a question is asked about, as the device sends it (0028 section 5; the consent lists exactly this): for a passage,
/// its facts digest and its notes by address (pitch, place in the bar, length, velocity); for the whole piece, the piece's
/// digest only. Never the file, its name or the rest of the piece's notes.
/// </summary>
public static class AssistantContext
{
    /// <summary>{"digest": the passage's facts digest, "notes": its notes}, as <c>facts.get</c> and <c>notes.list</c> write them.</summary>
    public static JsonElement OfRange(AssistantPiece piece, int from, int to, Hand? hand = null)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (piece.RangeRefusal(from, to) is { } refusal)
        {
            throw new ArgumentException(refusal, nameof(to));
        }

        return Written(json =>
        {
            json.WritePropertyName("digest");
            FactsDigest.OfRange(piece, from, to, hand).Write(json);
            json.WritePropertyName("notes");
            ReadingTools.WriteNotes(json, piece, piece.Read(from, to, hand));
        });
    }

    /// <summary>{"digest": the whole piece's digest}.</summary>
    public static JsonElement OfPiece(AssistantPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        return Written(json =>
        {
            json.WritePropertyName("digest");
            FactsDigest.OfPiece(piece).Write(json);
        });
    }

    private static JsonElement Written(Action<Utf8JsonWriter> fields)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            fields(json);
            json.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }
}