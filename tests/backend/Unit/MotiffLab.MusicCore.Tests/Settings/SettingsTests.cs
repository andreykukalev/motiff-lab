using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Settings;

namespace MotiffLab.MusicCore.Tests.Settings;

/// <summary>The settings model (0018, section 10; preparation item 5): presets, Custom, the per-piece overrides and the device document.</summary>
public class SettingsTests
{
    [Fact]
    public void The_default_is_the_practising_preset_and_one_change_makes_it_custom_until_changed_back()
    {
        SettingsState state = SettingsState.Default;
        Assert.Equal(Preset.Practising, state.Preset);
        Assert.True(state.IsOn("guidance.brackets"));
        Assert.False(state.IsOn("guidance.hints"));
        Assert.Equal("phrases", state["phrases.granularity"]);

        // A switch no preset governs starts at its own default: on where the catalogue says so, else off.
        Assert.True(state.IsOn("playback.follow"));
        Assert.False(state.IsOn("score.noteNames"));

        SettingsState changed = state.With("guidance.hints", Setting.On);
        Assert.Equal(Preset.Custom, changed.Preset);
        Assert.Equal(Preset.Practising, changed.With("guidance.hints", Setting.Off).Preset);

        // A device setting never changes the preset label.
        Assert.Equal(Preset.Practising, state.With("appearance.theme", "night").Preset);
    }

    [Fact]
    public void Presets_set_every_governed_setting_and_leave_the_rest()
    {
        SettingsState night = SettingsState.Default.With("appearance.theme", "night");

        SettingsState learning = night.WithPreset(Preset.Learning);
        Assert.Equal(Preset.Learning, learning.Preset);
        Assert.Equal("short", learning["phrases.granularity"]);
        Assert.True(learning.IsOn("guidance.hints"));
        Assert.Equal("night", learning["appearance.theme"]);

        SettingsState clean = night.WithPreset(Preset.Clean);
        Assert.Equal(Preset.Clean, clean.Preset);
        Assert.False(clean.IsOn("guidance.brackets"));
        Assert.True(clean.IsOn("layers.ink"));
        Assert.True(clean.IsOn("layers.bookmarks"));
        Assert.False(clean.IsOn("layers.notes"));
        Assert.Equal("playing", clean["guidance.transportPill"]);
        Assert.Equal("1", clean["guidance.barFade"]);
        Assert.Throws<ArgumentOutOfRangeException>(() => night.WithPreset(Preset.Custom));
    }

    [Fact]
    public void Values_are_checked_against_the_catalogue()
    {
        Assert.Throws<KeyNotFoundException>(() => SettingsState.Default.With("guidance.magic", Setting.On));
        Assert.Throws<ArgumentException>(() => SettingsState.Default.With("guidance.hints", "maybe"));
        Assert.Throws<KeyNotFoundException>(() => SettingsState.Default["nothing.here"]);
        Assert.All(SettingsCatalogue.All, setting => Assert.True(setting.Allows(setting.Default), setting.Id));
        Assert.All(SettingsCatalogue.Governed, setting => Assert.Equal(setting.Presets!.Practising, setting.Default));
        Assert.All(SettingsCatalogue.All.Where(setting => setting.Scope == SettingScope.Device), setting => Assert.False(setting.IsGoverned, setting.Id));
        Assert.Equal(SettingsCatalogue.All.Count, SettingsCatalogue.All.Select(setting => setting.Id).Distinct().Count());
    }

    [Fact]
    public void The_experiment_switches_are_gone_from_the_catalogue()
    {
        // The iPad has had no experiment switch since v0.6.5 (W1.11); the earlier ones went with their versions.
        Assert.All([.. s_experimentSwitches, "experimental.autoBookmarks", "experimental.pieceParts", "experimental.playheadsFade"], id => Assert.Null(SettingsCatalogue.Find(id)));
    }

    [Fact]
    public void A_piece_overrides_only_what_it_may_and_the_project_file_keeps_it()
    {
        Project project = Project.Create("p_test", "a.mid", [1, 2, 3], DateTimeOffset.UnixEpoch)
            .OverrideSetting("phrases.granularity", "sentences")
            .OverrideSetting("layers.check", Setting.Off);

        SettingsState inForce = SettingsState.Default.Resolve(project.SettingOverrides);
        Assert.Equal("sentences", inForce["phrases.granularity"]);
        Assert.False(inForce.IsOn("layers.check"));
        Assert.Equal(Preset.Custom, inForce.Preset);
        Assert.Equal(Preset.Practising, SettingsState.Default.Resolve(project.OverrideSetting("phrases.granularity", null).OverrideSetting("layers.check", null).SettingOverrides).Preset);

        Assert.Throws<ArgumentException>(() => project.OverrideSetting("appearance.theme", "night"));
        Assert.Throws<ArgumentException>(() => project.OverrideSetting("guidance.hints", Setting.On));
        Assert.Throws<ArgumentException>(() => project.OverrideSetting("layers.check", "dim"));
        Assert.Throws<KeyNotFoundException>(() => project.OverrideSetting("layers.magic", Setting.On));
    }

    /// <summary>The 23 experiment switches of versions 0.4 to 0.6.4, removed in 0.7 (W1.11): a device's store may still name them.</summary>
    private static readonly string[] s_experimentSwitches =
    [
        "experimental.pieces", "experimental.editsLayer", "experimental.liveTempo", "experimental.readingMarks", "experimental.reference",
        "experimental.harmony", "experimental.photoPages", "experimental.resume", "experimental.hands", "experimental.beats",
        "experimental.playheads", "experimental.meters", "experimental.libraryImport", "experimental.scanEngine", "experimental.assistantRepairs",
        "experimental.assistant", "experimental.assistantActions", "experimental.assistantContext", "experimental.assistantVariants",
        "experimental.sources", "experimental.musicalIdeas", "experimental.placeSelection", "experimental.dictation",
    ];

    /// <summary>
    /// What an iPad keeps (DeviceSettings, "motiff.settings"): the values music-core answered, with the experiment switches,
    /// each turned from its default, and one gone since version 0.5.2.
    /// </summary>
    private static Dictionary<string, string> KeptOnAnOlderDevice()
    {
        var kept = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["guidance.hints"] = Setting.On,
            ["layers.notes"] = Setting.Off,
            ["appearance.theme"] = "night",
            ["experimental.playheadsFade"] = Setting.On,
        };
        foreach (string id in s_experimentSwitches)
        {
            kept[id] = id == "experimental.dictation" ? Setting.Off : Setting.On;
        }

        return kept;
    }

    private static bool IsExperimentSwitch(string id) => id.StartsWith("experimental.", StringComparison.Ordinal);

    private static KeyValuePair<string, string>[] Live(IEnumerable<KeyValuePair<string, string>> values) => [.. values.Where(pair => !IsExperimentSwitch(pair.Key))];

    private static KeyValuePair<string, string>[] Live(JsonElement values) =>
        Live(values.EnumerateObject().Select(property => new KeyValuePair<string, string>(property.Name, property.Value.GetString()!)));

    private static JsonElement SettingsStateAnswer(string request)
    {
        JsonElement answer = JsonDocument.Parse(new MusicCoreFacade().Call(0, "settings.state", request)).RootElement;
        Assert.False(answer.TryGetProperty("error", out JsonElement error), $"{error} {answer}");
        return answer;
    }

    [Fact]
    public void A_store_that_names_the_experiment_switches_opens_with_every_other_value_as_kept()
    {
        Dictionary<string, string> kept = KeptOnAnOlderDevice();

        SettingsState stored = SettingsState.From(kept);
        SettingsState withoutThem = SettingsState.From(Live(kept));

        Assert.Equal(Live(withoutThem.Values), Live(stored.Values));
        Assert.Equal((Preset.Custom, Preset.Custom), (stored.Preset, withoutThem.Preset));
        Assert.Equal("night", stored["appearance.theme"]);
        Assert.True(stored.IsOn("guidance.hints"));
    }

    [Fact]
    public void The_settings_state_the_ipad_asks_for_is_the_same_with_the_experiment_switches_in_its_store()
    {
        Dictionary<string, string> kept = KeptOnAnOlderDevice();
        string values = JsonSerializer.Serialize(kept);
        string liveValues = JsonSerializer.Serialize(Live(kept).ToDictionary(StringComparer.Ordinal));

        foreach (string change in new[] { "", """, "preset": "clean" """, """, "set": {"id": "guidance.hints", "value": "off"}""" })
        {
            JsonElement answer = SettingsStateAnswer($$"""{"values": {{values}}{{change}}}""");
            JsonElement withoutThem = SettingsStateAnswer($$"""{"values": {{liveValues}}{{change}}}""");
            Assert.Equal(withoutThem.GetProperty("preset").GetString(), answer.GetProperty("preset").GetString());
            Assert.Equal(Live(withoutThem.GetProperty("values")), Live(answer.GetProperty("values")));

            // The iPad keeps the answer and sends it on the next change: it answers the same again.
            JsonElement again = SettingsStateAnswer($$"""{"values": {{answer.GetProperty("values").GetRawText()}}}""");
            Assert.Equal(answer.GetProperty("values").GetRawText(), again.GetProperty("values").GetRawText());
        }

        // A piece never overrides an experiment switch: one among the overrides is passed over.
        JsonElement overridden = SettingsStateAnswer($$$"""{"values": {{{values}}}, "overrides": {"experimental.resume": "off", "experimental.dictation": "on", "layers.notes": "on"}}""");
        JsonElement overriddenLive = SettingsStateAnswer($$$"""{"values": {{{values}}}, "overrides": {"layers.notes": "on"}}""");
        Assert.Equal(overriddenLive.GetRawText(), overridden.GetRawText());
    }

    [Fact]
    public void A_piece_cannot_override_an_experiment_switch_and_a_file_that_does_is_refused()
    {
        var facade = new MusicCoreFacade();
        string furElise = Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", "fur-elise-original.musicxml");
        Assert.False(JsonDocument.Parse(facade.Open("fur-elise-original.musicxml", File.ReadAllBytes(furElise))).RootElement.TryGetProperty("error", out _));
        JsonElement refused = JsonDocument.Parse(facade.Call(1, "settings.override", """{"id": "experimental.resume", "value": "on"}""")).RootElement;
        Assert.Equal("badRequest", refused.GetProperty("error").GetString());
        Assert.Empty(JsonDocument.Parse(facade.Call(1, "settings")).RootElement.GetProperty("overrides").EnumerateObject());

        // No build wrote one (a device setting never goes into the file), so a file that names one is damaged, as it was.
        JsonObject file = JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", "v16-worked.motiff.json")))!.AsObject();
        file["workspace"]!["settings"]!["experimental.resume"] = Setting.On;
        byte[] bytes = Encoding.UTF8.GetBytes(file.ToJsonString());

        Assert.Equal("This project file is damaged: its settings cannot be read.", Assert.Throws<ProjectFileException>(() => ProjectFile.Read(bytes)).Message);
        Assert.Equal("p_0b5e7c1d2a3f4e5d6c7b8a9f0e1d2c3b", ProjectFile.ReadEnvelope(bytes).Id);
    }
}