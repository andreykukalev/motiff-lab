using System.Text.Json;

namespace MotiffLab.MusicCore.Assistant.Conversation;

/// <summary>What the assistant is asked to be, which decides the tools it may call.</summary>
public enum AssistantRole
{
    /// <summary>The read-only mentor (the AI companion study, section 2.3): it reads notes, facts, chords and Check flags, and answers with facts it cites; it makes no draft.</summary>
    Mentor,

    /// <summary>
    /// The assistant that acts (#582 item 17.3; 0028 sections 1-3; the AI companion study, section 2.4): it reads like the
    /// mentor and, when the player asks for a change, chooses one catalogue operation with in-range parameters (a repair by
    /// Check reason, a variation, a note edit or a joined tie); music-core makes every note as a draft, and only the player
    /// accepts it. Served once with the experiment «Помощник правит ноты».
    /// </summary>
    Proposer,
}

/// <summary>
/// The tools of each role and the limits of a conversation (0028 section 4): the server and the device read the same list,
/// so the model is offered exactly the tools the device will run.
/// </summary>
public static class AssistantRoles
{
    /// <summary>The most tool calls the model may make while answering one question (0028 section 4, proposed 8).</summary>
    public const int MostToolCalls = 8;

    /// <summary>The role's tools, in the catalogue's order.</summary>
    public static IReadOnlyList<AssistantTool> ToolsOf(AssistantRole role) => role switch
    {
        AssistantRole.Mentor => [.. AssistantTools.All.Where(static tool => tool.Kind == ToolKind.Read)],
        AssistantRole.Proposer => [.. AssistantTools.All.Where(static tool => tool.Kind is ToolKind.Read or ToolKind.ProposeDraft or ToolKind.Check)],
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };

    /// <summary>The roles the relay answers: both, each with its own instructions.</summary>
    public static IReadOnlyList<AssistantRole> Served { get; } = [AssistantRole.Mentor, AssistantRole.Proposer];

    /// <summary>
    /// Why a call cannot run as the model made it, before any notes are read: the role lacks the tool, or its arguments do
    /// not fit the tool's schema (a missing field, a value out of range); null when it may run. The relay and the device
    /// check the same way; the device then checks the piece itself (its bars, its notes) when it runs the call.
    /// </summary>
    public static string? CallRefusal(AssistantRole role, ToolCall call, Words words)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (!Allows(role, call.Name) || AssistantTools.Find(call.Name) is not { } tool)
        {
            return words.T($"The {Id(role)} has no tool \"{call.Name}\".", $"У роли «{Id(role)}» нет инструмента «{call.Name}».");
        }

        return tool.Parameters.Refusal(call.Arguments, "", words);
    }

    /// <summary>Whether the role may call the tool.</summary>
    public static bool Allows(AssistantRole role, string tool) => ToolsOf(role).Any(allowed => allowed.Name == tool);

    /// <summary>"mentor".</summary>
    public static string Id(AssistantRole role) => ToolSchema.Camel(role.ToString());

    /// <summary>The role named <paramref name="id"/>, or null.</summary>
    public static AssistantRole? Parse(string? id) =>
        Enum.GetValues<AssistantRole>().Cast<AssistantRole?>().FirstOrDefault(role => Id(role!.Value) == id);

    /// <summary>Writes the role's catalogue: {"tools": [{name, kind, description, parameters}]}, the same each time.</summary>
    public static void WriteCatalogue(Utf8JsonWriter json, AssistantRole role) => AssistantTools.WriteTools(json, ToolsOf(role));
}