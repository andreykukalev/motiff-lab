namespace MotiffLab.MusicCore.Settings;

/// <summary>The three presets of the Settings sheet (0018, section 10), and Custom once a switch departs from one.</summary>
public enum Preset
{
    Learning,
    Practising,
    Clean,
    Custom,
}

/// <summary>The sections of the Settings sheet, in its order.</summary>
public enum SettingSection
{
    Level,
    Guidance,
    Score,
    MarksAndLayers,
    Playback,
    PianoRoll,
    Assistant,
    Appearance,
    Account,
}

/// <summary>What a setting's value is.</summary>
public enum SettingKind
{
    /// <summary>"on" or "off".</summary>
    Switch,

    /// <summary>One of the setting's choices.</summary>
    Choice,
}

/// <summary>Where a setting's value lives (0018, section 10; Andrei, 2026-10-31).</summary>
public enum SettingScope
{
    /// <summary>A device feature: stays on the device and never travels.</summary>
    Device,

    /// <summary>How the player works: on the device, and for Pro in the account, so every device agrees.</summary>
    Account,

    /// <summary>Like <see cref="Account"/>, and a piece can override it; the override travels in the project file.</summary>
    Piece,
}

/// <summary>What the three presets set a governed setting to; a setting with no preset values is not governed.</summary>
public sealed record PresetValues(string Learning, string Practising, string Clean)
{
    public string For(Preset preset) => preset switch
    {
        Preset.Learning => Learning,
        Preset.Practising => Practising,
        Preset.Clean => Clean,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Custom sets nothing."),
    };
}

/// <summary>
/// One adjustable element of the app (0018, section 10): its id, section, kind and choices, the value it
/// has before anyone touches it, what the presets set it to when they govern it, and where its value lives.
/// Values are text: "on" and "off" for a switch, a choice's name otherwise, so every client stores and sends
/// them the same way.
/// </summary>
/// <param name="Id">"section.name" in lower camel case, for example "phrases.granularity".</param>
/// <param name="Choices">The choices of a <see cref="SettingKind.Choice"/>; "on" and "off" for a switch.</param>
/// <param name="Default">The value before any preset: the Practising preset's where one governs it.</param>
/// <param name="Presets">What the presets set it to, or null when they leave it alone.</param>
public sealed record Setting(string Id, SettingSection Section, SettingKind Kind, IReadOnlyList<string> Choices, string Default, PresetValues? Presets, SettingScope Scope)
{
    public const string On = "on";
    public const string Off = "off";

    public bool IsGoverned => Presets is not null;

    public bool Allows(string value) => Choices.Contains(value, StringComparer.Ordinal);

    public override string ToString() => Id;
}