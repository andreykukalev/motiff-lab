using System.Globalization;
using System.Text.Json;

namespace MotiffLab.MusicCore.Assistant.Conversation;

/// <summary>Where a draft the assistant proposed stands: offered, accepted by the player, or dismissed.</summary>
public enum DraftState
{
    Proposed,
    Accepted,
    Dismissed,
}

/// <summary>
/// A draft a tool made during the conversation, kept as the call that made it (the tool, its arguments and which of its
/// drafts), so the device makes it again exactly when it is needed: tools are pure. Its notes are never stored here.
/// </summary>
public sealed record SessionDraft(string Id, string Tool, JsonElement Arguments, int Index, DraftState State)
{
    /// <summary>
    /// The draft made again by its call over <paramref name="piece"/> as it is now (tools are pure), or null when the call
    /// no longer makes it: the notes changed under it, so the player cannot accept a draft of notes that are gone.
    /// </summary>
    public AssistantDraft? Make(AssistantPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ToolResult result = AssistantTools.Call(piece, Tool, Arguments);
        return result.Ok && Index < result.Drafts.Count && !result.Drafts[Index].IsEmpty ? result.Drafts[Index] : null;
    }
}

/// <summary>
/// The conversation as the device keeps it (0028 section 4): the history the next turn sends, and the drafts proposed so
/// far with the player's decision on each. The server keeps nothing between turns, so this is the only record; the
/// device may keep it in memory or write it beside the piece (<see cref="Write"/>, <see cref="Read"/>). Immutable.
/// </summary>
public sealed record AssistantSession(AssistantRole Role, IReadOnlyList<TurnEntry> History, IReadOnlyList<SessionDraft> Drafts)
{
    public static AssistantSession Start(AssistantRole role) => new(role, [], []);

    /// <summary>Whether a question is still being answered: the server or the device has the next step.</summary>
    public bool Waiting => History.Count > 0 && History[^1].Kind != TurnEntryKind.Answer;

    /// <summary>Whether the model's tool calls wait for the device to run them.</summary>
    public bool AwaitsResults => History.Count > 0 && History[^1].Kind == TurnEntryKind.ToolCalls;

    /// <summary>The tool calls made since the last question.</summary>
    public int ToolCallsInQuestion => TurnRequest.CallsSinceQuestion(History);

    /// <summary>The tool results the device sent since the last question: with the context, where its answer's citations are found (<see cref="CitedFacts"/>).</summary>
    public IReadOnlyList<ToolOutcome> ResultsInQuestion => [.. History.Skip(TurnRequest.LastQuestion(History)).SelectMany(static entry => entry.Results)];

    /// <summary>The questions and answers, in order, for the conversation's view.</summary>
    public IEnumerable<TurnEntry> Exchanges => History.Where(static entry => entry.Kind is TurnEntryKind.Question or TurnEntryKind.Answer);

    /// <summary>The session with the player's question.</summary>
    /// <exception cref="InvalidOperationException">The last question is still being answered.</exception>
    public AssistantSession Ask(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        return Waiting ? throw new InvalidOperationException("The last question is still being answered.") : this with { History = [.. History, TurnEntry.Question(question.Trim())] };
    }

    /// <summary>The session with the model's tool calls, to run on the device.</summary>
    public AssistantSession Called(IReadOnlyList<ToolCall> calls) => this with { History = [.. History, TurnEntry.Called(calls)] };

    /// <summary>The session with the device's results; every draft a result offers is kept as proposed.</summary>
    public AssistantSession Resulted(IReadOnlyList<ToolOutcome> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        IReadOnlyList<ToolCall> calls = AwaitsResults ? History[^1].Calls : [];
        var drafts = Drafts.ToList();
        foreach (ToolOutcome result in results)
        {
            if (calls.FirstOrDefault(call => call.Id == result.Id) is not { } call || !result.Content.TryGetProperty("drafts", out JsonElement made) || made.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            for (int index = 0; index < made.GetArrayLength(); index++)
            {
                drafts.Add(new SessionDraft(string.Create(CultureInfo.InvariantCulture, $"d{drafts.Count + 1}"), call.Name, call.Arguments, index, DraftState.Proposed));
            }
        }

        return this with { History = [.. History, TurnEntry.Resulted(results)], Drafts = drafts };
    }

    /// <summary>The session with the checked answer.</summary>
    public AssistantSession Answered(string text) => this with { History = [.. History, TurnEntry.Answered(text)] };

    /// <summary>The session without the question still being answered and what followed it, when that turn could not be completed.</summary>
    public AssistantSession Withdrawn() => Waiting ? this with { History = [.. History.Take(TurnRequest.LastQuestion(History))] } : this;

    /// <summary>The session with the player's decision on a draft.</summary>
    public AssistantSession Decided(string draftId, DraftState state) =>
        this with { Drafts = [.. Drafts.Select(draft => draft.Id == draftId ? draft with { State = state } : draft)] };

    /// <summary>
    /// The next turn: the role, the language, the context and the history, the oldest finished exchanges left out when there are
    /// more than <see cref="TurnRequest.MaxEntries"/> entries.
    /// </summary>
    public TurnRequest Request(string language, JsonElement context)
    {
        IReadOnlyList<TurnEntry> history = History;
        while (history.Count > TurnRequest.MaxEntries && NextQuestion(history) is int next)
        {
            history = [.. history.Skip(next)];
        }

        return new TurnRequest(Role, language, context, history);
    }

    public void Write(Utf8JsonWriter json)
    {
        ArgumentNullException.ThrowIfNull(json);
        json.WriteStartObject();
        json.WriteString("role", AssistantRoles.Id(Role));
        json.WriteStartArray("history");
        foreach (TurnEntry entry in History)
        {
            entry.Write(json);
        }

        json.WriteEndArray();
        json.WriteStartArray("drafts");
        foreach (SessionDraft draft in Drafts)
        {
            json.WriteStartObject();
            json.WriteString("id", draft.Id);
            json.WriteString("tool", draft.Tool);
            json.WritePropertyName("arguments");
            draft.Arguments.WriteTo(json);
            json.WriteNumber("index", draft.Index);
            json.WriteString("state", ToolSchema.Camel(draft.State.ToString()));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    /// <summary>The session written in <paramref name="json"/>, or null when it is not one.</summary>
    public static AssistantSession? Read(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("role", out JsonElement role) || AssistantRoles.Parse(role.GetString()) is not { } read
            || !json.TryGetProperty("history", out JsonElement history) || history.ValueKind != JsonValueKind.Array
            || !json.TryGetProperty("drafts", out JsonElement drafts) || drafts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        TurnEntry?[] entries = [.. history.EnumerateArray().Select(TurnEntry.Read)];
        SessionDraft?[] kept = [.. drafts.EnumerateArray().Select(ReadDraft)];
        return entries.Contains(null) || kept.Contains(null) ? null : new AssistantSession(read, [.. entries.OfType<TurnEntry>()], [.. kept.OfType<SessionDraft>()]);
    }

    private static SessionDraft? ReadDraft(JsonElement json)
    {
        try
        {
            DraftState state = Enum.GetValues<DraftState>().First(value => ToolSchema.Camel(value.ToString()) == json.GetProperty("state").GetString());
            return new SessionDraft(json.GetProperty("id").GetString()!, json.GetProperty("tool").GetString()!, json.GetProperty("arguments").Clone(), json.GetProperty("index").GetInt32(), state);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static int? NextQuestion(IReadOnlyList<TurnEntry> history)
    {
        for (int index = 1; index < history.Count; index++)
        {
            if (history[index].Kind == TurnEntryKind.Question)
            {
                return index;
            }
        }

        return null;
    }
}