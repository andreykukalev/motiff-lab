using System.Reflection;

using MotiffLab.MusicCore.Assistant.Conversation;

namespace MotiffLab.Api.Assistant;

/// <summary>The roles' instructions, versioned files in Assistant/Prompts built into the API (0003 section 8).</summary>
internal static class AssistantPrompts
{
    /// <summary>The version of the instructions, logged with every turn.</summary>
    public const string Version = "mentor-v2";

    private static readonly Lazy<string> s_mentor = new(() => Load($"{Version}.md"));

    /// <summary>The role's instructions in the player's language.</summary>
    public static string For(AssistantRole role, string language) => role switch
    {
        AssistantRole.Mentor => s_mentor.Value.Replace("{language}", language == "ru" ? "Russian" : "English", StringComparison.Ordinal),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };

    private static string Load(string name)
    {
        Assembly assembly = typeof(AssistantPrompts).Assembly;
        string resource = assembly.GetManifestResourceNames().Single(found => found.EndsWith(name, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}