using MotiffLab.MusicCore.Settings;

namespace MotiffLab.MusicCore.Projects;

/// <summary>The piece's overrides of the settings (0018, section 10).</summary>
public sealed partial record Project
{
    /// <summary>
    /// The piece's overrides of the settings (0018, section 10): only settings of <see cref="SettingScope.Piece"/>
    /// scope, by id, each with a value the setting allows. They travel with the project.
    /// </summary>
    public IReadOnlyDictionary<string, string> SettingOverrides { get; init; } = s_noSettings;

    private static readonly IReadOnlyDictionary<string, string> s_noSettings = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The project with the setting overridden for this piece; null clears the override.</summary>
    /// <exception cref="KeyNotFoundException">No setting has the id.</exception>
    /// <exception cref="ArgumentException">The setting cannot be overridden for a piece, or does not allow the value.</exception>
    public Project OverrideSetting(string id, string? value)
    {
        Setting setting = SettingsCatalogue.Get(id);
        if (setting.Scope != SettingScope.Piece)
        {
            throw new ArgumentException($"\"{id}\" is the same for every piece.", nameof(id));
        }

        if (value is not null && !setting.Allows(value))
        {
            throw new ArgumentException($"\"{id}\" cannot be \"{value}\"; it is one of {string.Join(", ", setting.Choices)}.", nameof(value));
        }

        var overrides = new Dictionary<string, string>(SettingOverrides, StringComparer.Ordinal);
        if (value is null)
        {
            overrides.Remove(id);
        }
        else
        {
            overrides[id] = value;
        }

        return this with { SettingOverrides = overrides };
    }
}