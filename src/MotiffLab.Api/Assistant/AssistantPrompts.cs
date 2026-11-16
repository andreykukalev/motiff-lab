using System.Reflection;

using MotiffLab.MusicCore.Assistant.Conversation;

namespace MotiffLab.Api.Assistant;

/// <summary>The roles' instructions, versioned files in Assistant/Prompts built into the API (0003 section 8).</summary>
internal static class AssistantPrompts
{
    /// <summary>The version of the mentor's instructions, logged with its turns.</summary>
    public const string Version = "mentor-v2";

    /// <summary>The version of the proposer's instructions.</summary>
    public const string ProposerVersion = "proposer-v1";

    private static readonly Lazy<string> s_mentor = new(() => Load($"{Version}.md"));

    private static readonly Lazy<string> s_proposer = new(() => Load($"{ProposerVersion}.md"));

    /// <summary>The version of the role's instructions, for the logs.</summary>
    public static string VersionOf(AssistantRole role) => role == AssistantRole.Proposer ? ProposerVersion : Version;

    /// <summary>The role's instructions in the player's language.</summary>
    public static string For(AssistantRole role, string language)
    {
        string text = role switch
        {
            AssistantRole.Mentor => s_mentor.Value,
            AssistantRole.Proposer => s_proposer.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
        };
        return text.Replace("{language}", language == "ru" ? "Russian" : "English", StringComparison.Ordinal);
    }

    private static string Load(string name)
    {
        Assembly assembly = typeof(AssistantPrompts).Assembly;
        string resource = assembly.GetManifestResourceNames().Single(found => found.EndsWith(name, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}