using System.Text.Json;

using MotiffLab.MusicCore.Projects.Drafts;

namespace MotiffLab.MusicCore.Assistant.Drafts;

/// <summary>
/// What the assistant proposes in a draft (decision record 0034, section 6): the draft as music-core changes it by the
/// player's own operations, never applied. Steps it adds carry the origin <c>assistant</c> and, when more than one, one group,
/// so one undo takes them back; a bridge chosen at a seam changes the draft's structure instead. Only the player accepts it
/// («Принять»): the app sends the same call again and music-core makes it again (tools are pure).
/// </summary>
/// <param name="Tool">The tool that made it.</param>
/// <param name="Place">The place it changes; for a bridge, the bridge place.</param>
/// <param name="Before">The draft as it is.</param>
/// <param name="After">The draft as the proposal leaves it.</param>
/// <param name="Title">Its button, such as «Взять приёмы 1, 3».</param>
/// <param name="Explanation">What it changes, written only from the steps or the structure it makes.</param>
public sealed record DraftProposal(string Tool, string Place, Draft Before, Draft After, string Title, string Explanation)
{
    /// <summary>The origin of a step the assistant proposed, as the file names it.</summary>
    public const string Origin = "assistant";

    /// <summary>Whether it changes the draft's structure (a bridge place and its base), which is not a step.</summary>
    public bool Structure { get; init; }

    /// <summary>The steps it adds, in number order.</summary>
    public IReadOnlyList<DraftStep> Steps => [.. After.Steps.Where(step => step.Number >= Before.Next).OrderBy(static step => step.Number)];

    /// <summary>
    /// Writes it as an entry of a tool's "drafts": "tool", "applied" (false), "title", "explanation", "draft", "place",
    /// "structure", "steps" [{"kind" (take), "technique" {"kind", "bars", "label"}}] and, for a bridge, "base" (the chosen
    /// candidate's variant).
    /// </summary>
    public void Write(Utf8JsonWriter json, Words words)
    {
        ArgumentNullException.ThrowIfNull(json);
        json.WriteStartObject();
        json.WriteString("tool", Tool);
        json.WriteBoolean("applied", false);
        json.WriteString("title", Title);
        json.WriteString("explanation", Explanation);
        json.WriteString("draft", After.Id);
        json.WriteString("place", Place);
        json.WriteBoolean("structure", Structure);
        json.WriteStartArray("steps");
        foreach (DraftStep step in Steps)
        {
            json.WriteStartObject();
            json.WriteString("kind", step is DraftTake ? "take" : "leave");
            if (step is DraftTake { Technique: { } technique })
            {
                json.WriteStartObject("technique");
                json.WriteString("kind", ToolSchema.Camel(technique.Kind.ToString()));
                json.WriteStartArray("bars");
                foreach (int bar in technique.Bars)
                {
                    json.WriteNumberValue(bar);
                }

                json.WriteEndArray();
                json.WriteString("label", DraftLabels.Technique(technique.Kind, words));
                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        if (Structure)
        {
            json.WriteString("base", After.FindPlace(Place)!.Base);
        }

        json.WriteEndObject();
    }
}