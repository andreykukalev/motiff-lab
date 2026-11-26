using System.Text.Json;

namespace MotiffLab.MusicCore.Assistant;

/// <summary>What a tool does (the AI companion study, section 4.2). None of them changes the piece.</summary>
public enum ToolKind
{
    /// <summary>Reads what music-core found: notes, facts, chords, Check flags.</summary>
    Read,

    /// <summary>Makes a draft of notes (or several to choose from) for the player to hear and accept.</summary>
    ProposeDraft,

    /// <summary>Checks a proposal against the rules and the Keep choices, without making a draft.</summary>
    Check,

    /// <summary>Offers the player an action (a loop, a tempo, a hand), shown as a button; never taken by the tool.</summary>
    SuggestAction,
}

/// <summary>
/// One tool of the assistant's registry: its name, what kind it is, its description for the model, the strict schema of
/// its parameters and a pure executor. The executor runs only on parameters the schema accepts.
/// </summary>
public sealed record AssistantTool(string Name, ToolKind Kind, string Description, ToolSchema Parameters, Func<AssistantPiece, JsonElement, ToolResult> Execute);

/// <summary>A tool's answer: a refusal with its reason, or a result (a draft, several drafts, or what was read).</summary>
public sealed record ToolResult
{
    private ToolResult()
    {
    }

    /// <summary>Why the tool did nothing, in the player's language; null when it answered.</summary>
    public string? Refusal { get; private init; }

    /// <summary>The drafts of notes the tool made, in the order it offers them; empty for a tool that reads or works in a draft.</summary>
    public IReadOnlyList<AssistantDraft> Drafts { get; private init; } = [];

    /// <summary>What a draft's tool proposes (decision record 0034, section 6), written as the one entry of "drafts"; null otherwise.</summary>
    public Drafts.DraftProposal? Proposal { get; private init; }

    /// <summary>Why a proposing tool made no draft although it ran, such as a Check reason with no safe repair; null otherwise.</summary>
    public string? NoDraftReason { get; init; }

    /// <summary>Writes what the tool read or checked, beside the drafts.</summary>
    internal Action<Utf8JsonWriter>? Body { get; private init; }

    public bool Ok => Refusal is null;

    public static ToolResult Refused(string reason) => new() { Refusal = reason };

    internal static ToolResult Read(Action<Utf8JsonWriter> body) => new() { Body = body };

    public static ToolResult Proposed(IReadOnlyList<AssistantDraft> drafts, Action<Utf8JsonWriter>? body = null) => new() { Drafts = drafts, Body = body };

    internal static ToolResult Proposing(Drafts.DraftProposal proposal) => new() { Proposal = proposal };
}

/// <summary>
/// The assistant's tool registry (0028 section 2; the AI companion study, section 4.2): one catalogue of the music-core
/// operations an assistant may call, the same list for every request. Each call is checked against the tool's schema,
/// then run by music-core on the device, which answers with what it read or with drafts; nothing is applied to the piece,
/// and accepting a draft, taking a variant or closing a Check flag is the player's alone (no tool does it). Pure: the same
/// piece, tool and parameters give the same answer.
/// </summary>
internal static class AssistantTools
{
    /// <summary>The highest bar number a schema allows; the piece's own bars are checked when the tool runs.</summary>
    public const int MaxBarNumber = 9999;

    /// <summary>The highest seed a call may pass; operations that choose among equal options read only it.</summary>
    public const int MaxSeed = 999_999;

    /// <summary>The highest interval or onset index a call may pick.</summary>
    public const int MaxIndex = 63;

    /// <summary>The most note edits one call may make.</summary>
    public const int MaxEdits = 16;

    /// <summary>A 32nd in ticks: the unit of a note edit's "start" and "length".</summary>
    public const long ThirtySecond = Time.CanonicalTime.TicksPerQuarter / 8;

    /// <summary>The velocity of an added note when the call gives none.</summary>
    public const int AddedVelocity = 80;

    /// <summary>Every tool of a piece, in the order of the catalogue: reading, proposing, checking, suggesting.</summary>
    public static IReadOnlyList<AssistantTool> All => s_all ??= [.. ReadingTools.All(), .. VariationTools.All(), .. NoteTools.All(), .. TieTools.All(), .. Repairs.RepairTools.All()];

    /// <summary>The tools of a draft's place (decision record 0034, section 6), which run only on a piece in a draft (<see cref="AssistantPiece.Draft"/>).</summary>
    public static IReadOnlyList<AssistantTool> InDraft => s_inDraft ??= [.. Drafts.DraftTools.All()];

    private static IReadOnlyList<AssistantTool>? s_all;

    private static IReadOnlyList<AssistantTool>? s_inDraft;

    /// <summary>The tool with this name, of a piece or of a draft, or null.</summary>
    public static AssistantTool? Find(string name) => All.Concat(InDraft).FirstOrDefault(tool => tool.Name == name);

    /// <summary>
    /// Runs a tool: refused when there is no such tool, when the parameters do not fit its schema, or when music-core
    /// cannot apply them to these notes (with the engine's own reason); otherwise its answer.
    /// </summary>
    public static ToolResult Call(AssistantPiece piece, string name, JsonElement parameters)
    {
        ArgumentNullException.ThrowIfNull(piece);
        Words w = piece.Words;
        if (Find(name) is not { } tool)
        {
            return ToolResult.Refused(w.T($"There is no tool \"{name}\".", $"Инструмента «{name}» нет."));
        }

        if (tool.Parameters.Refusal(parameters, "", w) is { } refusal)
        {
            return ToolResult.Refused(refusal);
        }

        try
        {
            return tool.Execute(piece, parameters);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return ToolResult.Refused(exception.Message);
        }
    }

    /// <summary>Writes the catalogue: {"tools": [{name, kind, description, parameters}]}, the same for every piece and request.</summary>
    public static void WriteCatalogue(Utf8JsonWriter json) => WriteTools(json, All);

    /// <summary>Writes these tools as the catalogue does: {"tools": [{name, kind, description, parameters}]}.</summary>
    internal static void WriteTools(Utf8JsonWriter json, IEnumerable<AssistantTool> tools)
    {
        ArgumentNullException.ThrowIfNull(json);
        json.WriteStartArray("tools");
        foreach (AssistantTool tool in tools)
        {
            json.WriteStartObject();
            json.WriteString("name", tool.Name);
            json.WriteString("kind", ToolSchema.Camel(tool.Kind.ToString()));
            json.WriteString("description", tool.Description);
            json.WritePropertyName("parameters");
            tool.Parameters.Write(json);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>Writes a tool's answer: "tool", "kind", "ok", then "refusal", or "result" and "drafts".</summary>
    public static void WriteResult(Utf8JsonWriter json, AssistantPiece piece, string name, ToolResult result)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(result);
        json.WriteString("tool", name);
        if (Find(name) is { } tool)
        {
            json.WriteString("kind", ToolSchema.Camel(tool.Kind.ToString()));
        }

        json.WriteBoolean("ok", result.Ok);
        if (result.Refusal is { } refusal)
        {
            json.WriteString("refusal", refusal);
            return;
        }

        if (result.Body is { } body)
        {
            json.WritePropertyName("result");
            body(json);
        }

        if (result.Drafts.Count > 0 || result.Proposal is not null || result.NoDraftReason is not null || Find(name)?.Kind == ToolKind.ProposeDraft)
        {
            json.WriteStartArray("drafts");
            foreach (AssistantDraft draft in result.Drafts)
            {
                draft.Write(json, piece);
            }

            result.Proposal?.Write(json, piece.Words);

            json.WriteEndArray();
        }

        if (result.NoDraftReason is { } none)
        {
            json.WriteString("noDraft", none);
        }
    }
}