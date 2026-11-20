using System.Collections.Concurrent;
using System.Reflection;

using MotiffLab.MusicCore.Assistant.Conversation;

namespace MotiffLab.Api.Assistant;

/// <summary>The roles' instructions, versioned files in Assistant/Prompts built into the API (0003 section 8).</summary>
internal static class AssistantPrompts
{
    private static readonly ConcurrentDictionary<AssistantRole, string> s_texts = new();

    /// <summary>The version of the role's instructions, the name of its file, logged with its turns.</summary>
    public static string VersionOf(AssistantRole role) => role switch
    {
        AssistantRole.Mentor => "mentor-v2",
        AssistantRole.Proposer => "proposer-v2",
        AssistantRole.PlaceAssembler => "place-assembler-v1",
        AssistantRole.Explainer => "explainer-v1",
        AssistantRole.SeamChooser => "seam-chooser-v1",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };

    /// <summary>The role's instructions in the player's language.</summary>
    public static string For(AssistantRole role, string language) =>
        s_texts.GetOrAdd(role, static role => Load($"{VersionOf(role)}.md")).Replace("{language}", language == "ru" ? "Russian" : "English", StringComparison.Ordinal);

    private static string Load(string name)
    {
        Assembly assembly = typeof(AssistantPrompts).Assembly;
        string resource = assembly.GetManifestResourceNames().Single(found => found.EndsWith(name, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}