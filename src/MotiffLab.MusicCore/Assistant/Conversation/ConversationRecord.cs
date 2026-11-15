using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assistant.Conversation;

/// <summary>
/// One question of a kept conversation as the player saw it: the question, the answer as checked (citations as [[id]]),
/// whether it was the engine's template, the note that said why nothing was sent, and the facts it cited with their places.
/// </summary>
/// <param name="Answer">The checked answer; null when the question went unanswered (<paramref name="Note"/> says why).</param>
/// <param name="Note">A UI text key for why nothing was sent, such as "mentor.notConfigured"; null when answered.</param>
public sealed record SavedExchange(string Question, string? Answer, bool Fallback, string? Note, IReadOnlyList<CitedFact> Cited);

/// <summary>
/// A conversation of the full assistant about one piece, as the device keeps it (#582 item 17.1, the lead's decision 4): its
/// id, a title from its first question, when it started and was last added to, the session the next turn goes on from
/// (<see cref="AssistantSession"/>), and the exchanges the panel shows. It is kept on the device beside the piece, never
/// in the project file and never sent anywhere but in the turns themselves; the player returns to it from the list of
/// the piece's conversations. Immutable; the same record reads back whole.
/// </summary>
public sealed record ConversationRecord(string Id, string Title, DateTimeOffset Started, DateTimeOffset Updated, AssistantSession Session, IReadOnlyList<SavedExchange> Exchanges)
{
    /// <summary>The version of this format; another is not read.</summary>
    public const int Version = 1;

    /// <summary>The longest title, in characters: the first question, cut at a word.</summary>
    public const int MaxTitle = 60;

    /// <summary>A new conversation, empty, started at <paramref name="at"/>.</summary>
    public static ConversationRecord Start(string id, AssistantRole role, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new ConversationRecord(id, "", at, at, AssistantSession.Start(role), []);
    }

    /// <summary>The conversation with one more exchange and the session after it; the first question names it.</summary>
    public ConversationRecord With(SavedExchange exchange, AssistantSession session, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        ArgumentNullException.ThrowIfNull(session);
        return this with { Title = Title.Length > 0 ? Title : TitleOf(exchange.Question), Updated = at, Session = session, Exchanges = [.. Exchanges, exchange] };
    }

    /// <summary>The first question as a title: at most <see cref="MaxTitle"/> characters, cut at a word, with an ellipsis.</summary>
    public static string TitleOf(string question)
    {
        string text = string.Join(' ', (question ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length <= MaxTitle)
        {
            return text;
        }

        int cut = text.LastIndexOf(' ', MaxTitle - 1);
        return string.Concat(text.AsSpan(0, cut > MaxTitle / 2 ? cut : MaxTitle - 1), "…");
    }

    /// <summary>The record as UTF-8 JSON.</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("version", Version);
            json.WriteString("id", Id);
            json.WriteString("title", Title);
            json.WriteString("started", Started.ToString("O", CultureInfo.InvariantCulture));
            json.WriteString("updated", Updated.ToString("O", CultureInfo.InvariantCulture));
            json.WritePropertyName("session");
            Session.Write(json);
            json.WriteStartArray("exchanges");
            foreach (SavedExchange exchange in Exchanges)
            {
                WriteExchange(json, exchange);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The record in <paramref name="text"/>, or null when it is not one of this version.</summary>
    public static ConversationRecord? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement json = document.RootElement;
            if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("version", out JsonElement version) || version.GetInt32() != Version
                || AssistantSession.Read(json.GetProperty("session")) is not { } session)
            {
                return null;
            }

            return new ConversationRecord(
                json.GetProperty("id").GetString()!,
                json.GetProperty("title").GetString() ?? "",
                DateTimeOffset.Parse(json.GetProperty("started").GetString()!, CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(json.GetProperty("updated").GetString()!, CultureInfo.InvariantCulture),
                session,
                [.. json.GetProperty("exchanges").EnumerateArray().Select(ReadExchange)]);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static void WriteExchange(Utf8JsonWriter json, SavedExchange exchange)
    {
        json.WriteStartObject();
        json.WriteString("question", exchange.Question);
        json.WriteString("answer", exchange.Answer);
        json.WriteBoolean("fallback", exchange.Fallback);
        json.WriteString("note", exchange.Note);
        json.WriteStartArray("cited");
        foreach (CitedFact fact in exchange.Cited)
        {
            json.WriteStartObject();
            json.WriteString("id", fact.Id);
            json.WriteString("text", fact.Text);
            json.WriteStartArray("places");
            foreach (FactPlace place in fact.Places)
            {
                json.WriteStartObject();
                json.WriteString("label", place.Label);
                json.WriteNumber("onset", place.Span.Onset.Value);
                json.WriteNumber("end", place.Span.End.Value);
                json.WriteStartArray("notes");
                foreach (string id in place.NoteIds)
                {
                    json.WriteStringValue(id);
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static SavedExchange ReadExchange(JsonElement json) => new(
        json.GetProperty("question").GetString()!,
        json.GetProperty("answer").GetString(),
        json.GetProperty("fallback").GetBoolean(),
        json.GetProperty("note").GetString(),
        [.. json.GetProperty("cited").EnumerateArray().Select(static fact => new CitedFact(
            fact.GetProperty("id").GetString()!,
            fact.GetProperty("text").GetString()!,
            [.. fact.GetProperty("places").EnumerateArray().Select(static place => new FactPlace(
                place.GetProperty("label").GetString()!,
                TickSpan.FromBounds(new Tick(place.GetProperty("onset").GetInt64()), new Tick(place.GetProperty("end").GetInt64())),
                [.. place.GetProperty("notes").EnumerateArray().Select(static id => id.GetString()!)]))]))]);
}