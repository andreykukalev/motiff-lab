using System.Text.Json;

namespace MotiffLab.MusicCore.Assistant.Conversation;

/// <summary>
/// One turn of a conversation as the device sends it to the Motiff Lab API (0028 section 4): the role, the language, the
/// context of the question (the selected passage's facts digest and notes, or the piece's digest) and the whole history
/// so far. The server keeps nothing between turns: the device holds the conversation (<see cref="AssistantSession"/>),
/// runs the model's tool calls itself (<see cref="TurnRunner"/>) and sends their results in the next turn.
/// </summary>
/// <param name="Context">{"digest": {...}, "notes": {...}?}: what <see cref="AssistantContext"/> writes.</param>
public sealed record TurnRequest(AssistantRole Role, string Language, JsonElement Context, IReadOnlyList<TurnEntry> History)
{
    /// <summary>The version of this format; another is refused.</summary>
    public const int Version = 1;

    /// <summary>The largest request, in bytes of JSON: a passage of 32 bars with its notes and a conversation fit.</summary>
    public const int MaxBytes = 128 * 1024;

    /// <summary>The most entries of a conversation sent at once.</summary>
    public const int MaxEntries = 40;

    /// <summary>The longest question, in characters.</summary>
    public const int MaxQuestion = 1000;

    /// <summary>The tool calls made since the last question.</summary>
    public int ToolCallsInQuestion => CallsSinceQuestion(History);

    /// <summary>The player's last question, which the engine's template answers; null before any.</summary>
    public string? Question => History.LastOrDefault(static entry => entry.Kind == TurnEntryKind.Question)?.Text;

    /// <summary>Whether a tool made a draft since the last question: then the answer is about the draft, whose explanation is the engine's, and need cite no fact.</summary>
    public bool Drafted => AnswerCheck.Drafted(History.Skip(LastQuestion(History)).SelectMany(static entry => entry.Results));

    /// <summary>Every fact id the model may cite in this turn: the context's and those of the tool results since the last question.</summary>
    public IReadOnlySet<string> KnownIds => AnswerCheck.Known(Context, History.Skip(LastQuestion(History)).SelectMany(static entry => entry.Results));

    public void Write(Utf8JsonWriter json)
    {
        ArgumentNullException.ThrowIfNull(json);
        json.WriteStartObject();
        json.WriteNumber("version", Version);
        json.WriteString("role", AssistantRoles.Id(Role));
        json.WriteString("language", Language);
        json.WritePropertyName("context");
        Context.WriteTo(json);
        json.WriteStartArray("history");
        foreach (TurnEntry entry in History)
        {
            entry.Write(json);
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    /// <summary>The request as UTF-8 JSON.</summary>
    public byte[] ToJson()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            Write(json);
        }

        return buffer.ToArray();
    }

    /// <summary>The request in <paramref name="json"/>, or null with the reason it is refused, in English (the server's log and error code read it).</summary>
    public static TurnRequest? Read(JsonElement json, out string? refusal)
    {
        refusal = Shape(json);
        if (refusal is not null)
        {
            return null;
        }

        TurnEntry?[] entries = [.. json.GetProperty("history").EnumerateArray().Select(TurnEntry.Read)];
        if (entries.Any(static entry => entry is null))
        {
            refusal = "An entry of the history is not a question, tool calls, tool results or an answer.";
            return null;
        }

        var request = new TurnRequest(AssistantRoles.Parse(json.GetProperty("role").GetString())!.Value, json.GetProperty("language").GetString()!, json.GetProperty("context").Clone(), [.. entries.OfType<TurnEntry>()]);
        refusal = Order(request.History) ?? Calls(request);
        return refusal is null ? request : null;
    }

    private static string? Shape(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("version", out JsonElement version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != Version)
        {
            return $"The request is not version {Version} of the turn format.";
        }

        if (!json.TryGetProperty("role", out JsonElement role) || AssistantRoles.Parse(role.ValueKind == JsonValueKind.String ? role.GetString() : null) is null)
        {
            return "The role is not one the assistant has.";
        }

        if (!json.TryGetProperty("language", out JsonElement language) || language.GetString() is not ("en" or "ru"))
        {
            return "The language is en or ru.";
        }

        return Contents(json);
    }

    private static string? Contents(JsonElement json)
    {
        if (!json.TryGetProperty("context", out JsonElement context) || context.ValueKind != JsonValueKind.Object
            || !context.TryGetProperty("digest", out JsonElement digest) || digest.ValueKind != JsonValueKind.Object
            || !digest.TryGetProperty("ids", out JsonElement ids) || ids.ValueKind != JsonValueKind.Array)
        {
            return "The context has no facts digest with its ids.";
        }

        return !json.TryGetProperty("history", out JsonElement history) || history.ValueKind != JsonValueKind.Array || history.GetArrayLength() is 0 or > MaxEntries
            ? $"The history holds 1 to {MaxEntries} entries."
            : null;
    }

    /// <summary>Questions and answers take turns; each run of tool calls is answered by their results, call for call; the turn ends on a question or on results.</summary>
    private static string? Order(IReadOnlyList<TurnEntry> history)
    {
        TurnEntryKind? previous = null;
        foreach (TurnEntry entry in history)
        {
            bool fits = (previous, entry.Kind) switch
            {
                (null or TurnEntryKind.Answer, TurnEntryKind.Question) => true,
                (TurnEntryKind.Question or TurnEntryKind.ToolResults, TurnEntryKind.ToolCalls or TurnEntryKind.Answer) => true,
                (TurnEntryKind.ToolCalls, TurnEntryKind.ToolResults) => true,
                _ => false,
            };
            if (!fits)
            {
                return "The history is out of order: a question, then tool calls and their results in turn, then the answer.";
            }

            if (entry.Kind == TurnEntryKind.Question && entry.Text!.Trim().Length is 0 or > MaxQuestion)
            {
                return $"A question holds 1 to {MaxQuestion} characters.";
            }

            previous = entry.Kind;
        }

        for (int index = 1; index < history.Count; index++)
        {
            if (history[index].Kind == TurnEntryKind.ToolResults
                && !history[index].Results.Select(static result => result.Id).Order(StringComparer.Ordinal).SequenceEqual(history[index - 1].Calls.Select(static call => call.Id).Order(StringComparer.Ordinal)))
            {
                return "Each tool call is answered by one result with its id.";
            }
        }

        return previous is TurnEntryKind.Question or TurnEntryKind.ToolResults ? null : "The turn ends on a question or on tool results.";
    }

    private static string? Calls(TurnRequest request)
    {
        if (request.History.SelectMany(static entry => entry.Calls).FirstOrDefault(call => !AssistantRoles.Allows(request.Role, call.Name)) is { } call)
        {
            return $"The {AssistantRoles.Id(request.Role)} has no tool \"{call.Name}\".";
        }

        return request.ToolCallsInQuestion > AssistantRoles.MostToolCalls ? $"A question takes at most {AssistantRoles.MostToolCalls} tool calls." : null;
    }

    internal static int LastQuestion(IReadOnlyList<TurnEntry> history)
    {
        for (int index = history.Count - 1; index >= 0; index--)
        {
            if (history[index].Kind == TurnEntryKind.Question)
            {
                return index;
            }
        }

        return 0;
    }

    internal static int CallsSinceQuestion(IReadOnlyList<TurnEntry> history) =>
        history.Skip(LastQuestion(history)).Sum(static entry => entry.Calls.Count);
}