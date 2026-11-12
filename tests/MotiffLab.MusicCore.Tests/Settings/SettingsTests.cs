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
    public void The_version_0_5_experiments_are_off_on_the_device_and_one_switch_replaces_the_two_for_pieces()
    {
        foreach (string id in new[] { "experimental.pieces", "experimental.editsLayer", "experimental.liveTempo", "experimental.harmony", "experimental.readingMarks" })
        {
            Setting setting = SettingsCatalogue.Get(id);
            Assert.Equal(SettingSection.Experimental, setting.Section);
            Assert.Equal(SettingScope.Device, setting.Scope);
            Assert.False(SettingsState.Default.IsOn(id), id);
        }

        Assert.Null(SettingsCatalogue.Find("experimental.autoBookmarks"));
        Assert.Null(SettingsCatalogue.Find("experimental.pieceParts"));
    }

    [Fact]
    public void The_playheads_experiment_is_off_on_the_device_and_its_fade_is_on_inside_it()
    {
        foreach (string id in new[] { "experimental.playheads", "experimental.playheadsFade" })
        {
            Setting setting = SettingsCatalogue.Get(id);
            Assert.Equal(SettingSection.Experimental, setting.Section);
            Assert.Equal(SettingScope.Device, setting.Scope);
        }

        Assert.False(SettingsState.Default.IsOn("experimental.playheads"));
        Assert.True(SettingsState.Default.IsOn("experimental.playheadsFade"));
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
}