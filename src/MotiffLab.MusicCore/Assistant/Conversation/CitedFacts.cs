using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assistant.Conversation;

/// <summary>A fact an answer cites, as the conversation shows it: its id, the engine's own line for it and its places in the score.</summary>
/// <param name="Places">The notes or bars it names; empty when it names none, such as a fact about the passage as a whole.</param>
public sealed record CitedFact(string Id, string Text, IReadOnlyList<FactPlace> Places);

/// <summary>
/// The facts an answer cites, found where the turn gave them (the context's digest or a tool result): each with the
/// engine's line and its places, read back from the note addresses or the bars the item names, so a citation links to its
/// place in the score. Pure; it reads what the engine wrote and adds nothing.
/// </summary>
public static class CitedFacts
{
    /// <summary>The cited fact <paramref name="id"/>, or null when neither the context nor the results list it.</summary>
    public static CitedFact? Of(AssistantPiece piece, string id, JsonElement context, IEnumerable<ToolOutcome> results)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(results);
        JsonElement? item = Find(context, id) ?? results.Select(result => Find(result.Content, id)).FirstOrDefault(found => found is not null);
        if (item is not { } found)
        {
            return null;
        }

        string text = found.GetProperty("text").GetString() ?? id;
        return new CitedFact(id, text, [.. NotePlaces(piece, found), .. BarPlace(piece, found, text)]);
    }

    /// <summary>The cited facts of an answer, each once, in the order cited; those the turn did not give are left out.</summary>
    public static IReadOnlyList<CitedFact> In(AssistantPiece piece, IEnumerable<AnswerPart> parts, JsonElement context, IReadOnlyList<ToolOutcome> results)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return [.. parts.Select(static part => part.Cites).OfType<string>().Distinct(StringComparer.Ordinal).Select(id => Of(piece, id, context, results)).OfType<CitedFact>()];
    }

    private static IEnumerable<FactPlace> NotePlaces(AssistantPiece piece, JsonElement item)
    {
        if (!item.TryGetProperty("places", out JsonElement places) || places.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement place in places.EnumerateArray())
        {
            Note[] notes = place.TryGetProperty("notes", out JsonElement addresses) && addresses.ValueKind == JsonValueKind.Array
                ? [.. addresses.EnumerateArray().Select(address => NoteAddress.TryParse(address.GetString(), out NoteAddress at) ? piece.Addresses.NoteAt(at) : null).OfType<Note>()]
                : [];
            if (notes.Length > 0)
            {
                yield return FactPlace.Of(place.TryGetProperty("label", out JsonElement label) ? label.GetString() ?? "" : "", notes);
            }
        }
    }

    /// <summary>The bars the item names, as a stretch of time, when it names no notes.</summary>
    private static IEnumerable<FactPlace> BarPlace(AssistantPiece piece, JsonElement item, string text)
    {
        if (item.TryGetProperty("places", out _) || !item.TryGetProperty("bars", out JsonElement bars)
            || !bars.TryGetProperty("from", out JsonElement from) || !bars.TryGetProperty("to", out JsonElement to)
            || !from.TryGetInt32(out int first) || !to.TryGetInt32(out int last)
            || first > last || first < piece.FirstBar || last > piece.LastBar)
        {
            yield break;
        }

        yield return new FactPlace(text, TickSpan.FromBounds(piece.Meter.GetBar(first).Span.Onset, piece.Meter.GetBar(last).Span.End), []);
    }

    /// <summary>The object with this "id" and a "text", anywhere in <paramref name="json"/>.</summary>
    private static JsonElement? Find(JsonElement json, string id)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            if (json.TryGetProperty("id", out JsonElement own) && own.ValueKind == JsonValueKind.String && own.GetString() == id
                && json.TryGetProperty("text", out JsonElement text) && text.ValueKind == JsonValueKind.String)
            {
                return json;
            }

            return json.EnumerateObject().Select(property => Find(property.Value, id)).FirstOrDefault(found => found is not null);
        }

        return json.ValueKind == JsonValueKind.Array ? json.EnumerateArray().Select(item => Find(item, id)).FirstOrDefault(found => found is not null) : null;
    }
}