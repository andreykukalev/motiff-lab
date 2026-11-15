using System.Text.Json;

namespace MotiffLab.MusicCore.Assistant.Conversation;

/// <summary>What the assistant is asked to be, which decides the tools it may call.</summary>
public enum AssistantRole
{
    /// <summary>The read-only mentor (the AI companion study, section 2.3): it reads notes, facts, chords and Check flags, and answers with facts it cites; it makes no draft.</summary>
    Mentor,

    /// <summary>
    /// The proposer of «что если» variants (the AI companion study, section 2.4): it reads like the mentor and calls the
    /// tools that make drafts and check them; music-core makes every note and only the player keeps or takes a variant. Not
    /// served until a model provider exists (<see cref="AssistantRoles.Served"/>).
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

    /// <summary>The roles the relay answers today: the mentor; the proposer waits for a model provider and its instructions.</summary>
    public static IReadOnlyList<AssistantRole> Served { get; } = [AssistantRole.Mentor];

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