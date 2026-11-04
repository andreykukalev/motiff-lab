using MotiffLab.MusicCore.Settings;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Settings;

/// <summary>
/// The player's settings on this device (0018 section 10): music-core's <see cref="SettingsState"/>, read once from the
/// device's preferences and kept there one setting to a key, the key being the setting's id. The Settings sheet, the
/// presets and the controls on each element change it here, and every page that shows a setting listens to
/// <see cref="Changed"/>, so a change made in the sheet over the score shows on the score at once. The language and
/// the theme keep their own stores (<c>UiText</c>, <c>theme.ts</c>); saving online and analytics have their own pages.
/// </summary>
public sealed class DeviceSettings(Preferences preferences)
{
    /// <summary>The settings this store keeps; the others are kept where they always were.</summary>
    private static readonly string[] s_kept = [.. SettingsCatalogue.All
        .Select(setting => setting.Id)
        .Where(id => id is not ("appearance.language" or "appearance.theme" or "score.layout" or "account.saveOnline" or "account.analytics"))];

    /// <summary>Set once the player chose a preset on the first run, or in Settings: the first run then never asks again.</summary>
    public const string PresetChosenKey = "settings.presetChosen";

    private Task? _loading;

    public SettingsState State { get; private set; } = SettingsState.Default;

    /// <summary>Whether a preset was ever chosen on this device.</summary>
    public bool PresetChosen { get; private set; }

    /// <summary>Raised after every change, from the sheet, a preset or an element's own control.</summary>
    public event Action? Changed;

    /// <summary>Reads the stored settings, once; later calls wait for the same reading.</summary>
    public Task LoadAsync() => _loading ??= ReadAsync();

    public bool IsOn(string id) => State.IsOn(id);

    public string this[string id] => State[id];

    /// <summary>One setting changed and kept on the device.</summary>
    public async Task SetAsync(string id, string value)
    {
        await LoadAsync();
        if (State[id] == value)
        {
            return;
        }

        State = State.With(id, value);
        if (s_kept.Contains(id))
        {
            await preferences.SetAsync(id, value);
        }

        Changed?.Invoke();
    }

    public Task SetAsync(string id, bool on) => SetAsync(id, on ? Setting.On : Setting.Off);

    /// <summary>Every setting the preset governs set as it says, and the choice remembered so the first run does not ask again.</summary>
    public async Task ApplyPresetAsync(Preset preset)
    {
        await LoadAsync();
        State = State.WithPreset(preset);
        foreach (Setting setting in SettingsCatalogue.Governed.Where(setting => s_kept.Contains(setting.Id)))
        {
            await preferences.SetAsync(setting.Id, State[setting.Id]);
        }

        PresetChosen = true;
        await preferences.SetAsync(PresetChosenKey, Setting.On);
        Changed?.Invoke();
    }

    private async Task ReadAsync()
    {
        var stored = new List<KeyValuePair<string, string>>();
        foreach (string id in s_kept)
        {
            if (await preferences.GetAsync(id) is { } value)
            {
                stored.Add(new(id, value));
            }
        }

        State = SettingsState.From(stored);
        PresetChosen = await preferences.GetAsync(PresetChosenKey) == Setting.On;
    }
}