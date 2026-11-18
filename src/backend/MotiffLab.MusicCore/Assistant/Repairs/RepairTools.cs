using System.Text.Json;

using MotiffLab.MusicCore.Import;

namespace MotiffLab.MusicCore.Assistant.Repairs;

/// <summary>The repair tool of the registry: the engine's candidates for one Check flag (0028 section 6).</summary>
internal static class RepairTools
{
    internal static IEnumerable<AssistantTool> All()
    {
        yield return new AssistantTool(
            CheckRepairs.Tool,
            ToolKind.ProposeDraft,
            "The engine's repair candidates for one Check flag, best first, each a draft of note corrections; or why no repair is safe. Pick and order them from the player's words; never invent another. The flag stays until the player resolves it.",
            ToolSchema.Object(
                ("bar", ToolSchema.Integer(0, AssistantTools.MaxBarNumber), "The flagged bar."),
                ("reason", ToolSchema.Choice<CheckReason>(), "The flag's reason, as check.flags names it.")),
            static (piece, parameters) => Run(piece, parameters));
    }

    private static ToolResult Run(AssistantPiece piece, JsonElement parameters)
    {
        int bar = parameters.GetProperty("bar").GetInt32();
        CheckReason reason = ToolSchema.Read<CheckReason>(parameters.GetProperty("reason"));
        if (piece.Check.FirstOrDefault(flag => flag.Bar == bar && flag.Reason == reason) is not { } flag)
        {
            return ToolResult.Refused(piece.Words.T(
                $"Bar {bar} has no Check flag \"{ToolSchema.Camel(reason.ToString())}\".",
                $"У такта {bar} нет флажка «{CheckFlag.NameOf(reason, piece.Words)}»."));
        }

        RepairOptions options = CheckRepairs.For(piece, flag);
        return ToolResult.Proposed([.. options.Candidates.Select(static candidate => candidate.Draft)]) with { NoDraftReason = options.NoneReason };
    }
}