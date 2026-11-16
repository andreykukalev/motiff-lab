using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

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

    /// <summary>
    /// The context of a question about <paramref name="selection"/> (null: the whole piece) in <paramref name="scope"/>: the
    /// bars it touches, read whole, or the piece's digest; null when the bars are more than a range may hold.
    /// </summary>
    public static JsonElement? OfQuestion(AssistantPiece piece, TickSpan? selection, AskScope scope, Hand? hand = null)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsWholePiece(selection))
        {
            return OfPiece(piece);
        }

        BarRange bars = BarRange.Of(piece.Meter, selection!.Value);
        return piece.RangeRefusal(bars.FirstBar, bars.LastBar) is null ? OfRange(piece, bars.FirstBar, bars.LastBar, hand) : null;
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