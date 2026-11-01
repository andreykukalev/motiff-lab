using System.Text.Json;

namespace MotiffLab.MusicCore.Settings;

/// <summary>
/// The player's settings as a whole (0018, section 10): a value for every setting of the catalogue. The
/// preset label is read from the values, so changing one governed switch shows Custom and changing it
/// back shows the preset again. Per-piece overrides from the project file lie over it with
/// <see cref="Resolve"/>. Pure: every change gives a new state.
/// </summary>
public sealed record SettingsState
{
    private readonly IReadOnlyDictionary<string, string> _values;

    private SettingsState(IReadOnlyDictionary<string, string> values) => _values = values;

    /// <summary>The state before anyone chose: the Practising preset.</summary>
    public static SettingsState Default { get; } = Of(Preset.Practising);

    /// <summary>Every setting as the preset sets it, the rest at their defaults.</summary>
    public static SettingsState Of(Preset preset) => new(SettingsCatalogue.ValuesOf(preset));

    /// <summary>
    /// A state from stored values: ids the catalogue does not know and values a setting does not allow
    /// are dropped, and settings not stored take their default, so an older or damaged store still opens.
    /// </summary>
    public static SettingsState From(IEnumerable<KeyValuePair<string, string>> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var values = new Dictionary<string, string>(SettingsCatalogue.ValuesOf(Preset.Practising), StringComparer.Ordinal);
        foreach ((string id, string value) in stored)
        {
            if (SettingsCatalogue.Find(id) is { } setting && setting.Allows(value))
            {
                values[id] = value;
            }
        }

        return new SettingsState(values);
    }

    /// <summary>Every setting's value, in catalogue order.</summary>
    public IEnumerable<KeyValuePair<string, string>> Values => SettingsCatalogue.All.Select(setting => new KeyValuePair<string, string>(setting.Id, _values[setting.Id]));

    /// <summary>The preset the governed settings match, or Custom.</summary>
    public Preset Preset
    {
        get
        {
            foreach (Preset preset in new[] { Preset.Practising, Preset.Learning, Preset.Clean })
            {
                if (SettingsCatalogue.Governed.All(setting => _values[setting.Id] == setting.Presets!.For(preset)))
                {
                    return preset;
                }
            }

            return Preset.Custom;
        }
    }

    public string this[string id] => _values.TryGetValue(id, out string? value) ? value : SettingsCatalogue.Get(id).Default;

    /// <summary>Whether a switch is on.</summary>
    public bool IsOn(string id) => this[id] == Setting.On;

    /// <summary>The state with every governed setting set as the preset says; the rest stays.</summary>
    public SettingsState WithPreset(Preset preset)
    {
        if (preset == Preset.Custom)
        {
            throw new ArgumentOutOfRangeException(nameof(preset), preset, "Custom is not a preset to apply.");
        }

        var values = new Dictionary<string, string>(_values, StringComparer.Ordinal);
        foreach (Setting setting in SettingsCatalogue.Governed)
        {
            values[setting.Id] = setting.Presets!.For(preset);
        }

        return new SettingsState(values);
    }

    /// <summary>The state with one setting changed.</summary>
    /// <exception cref="KeyNotFoundException">No setting has the id.</exception>
    /// <exception cref="ArgumentException">The setting does not allow the value.</exception>
    public SettingsState With(string id, string value)
    {
        Setting setting = SettingsCatalogue.Get(id);
        if (!setting.Allows(value))
        {
            throw new ArgumentException($"\"{id}\" cannot be \"{value}\"; it is one of {string.Join(", ", setting.Choices)}.", nameof(value));
        }

        return new SettingsState(new Dictionary<string, string>(_values, StringComparer.Ordinal) { [id] = value });
    }

    /// <summary>
    /// The settings in force for a piece: its overrides over these values. Only settings a piece may
    /// override count; anything else in <paramref name="overrides"/> is ignored.
    /// </summary>
    public SettingsState Resolve(IReadOnlyDictionary<string, string> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var values = new Dictionary<string, string>(_values, StringComparer.Ordinal);
        foreach (Setting setting in SettingsCatalogue.PerPiece)
        {
            if (overrides.TryGetValue(setting.Id, out string? value) && setting.Allows(value))
            {
                values[setting.Id] = value;
            }
        }

        return new SettingsState(values);
    }

    /// <summary>Only the device settings, for the store that never leaves the device.</summary>
    public IEnumerable<KeyValuePair<string, string>> DeviceValues => Values.Where(pair => SettingsCatalogue.Get(pair.Key).Scope == SettingScope.Device);

    /// <summary>The settings that travel with the account for Pro: everything but the device ones.</summary>
    public IEnumerable<KeyValuePair<string, string>> AccountValues => Values.Where(pair => SettingsCatalogue.Get(pair.Key).Scope != SettingScope.Device);

    /// <summary>The state as one JSON document, {"version": 1, "values": {id: value, …}}, the shape both apps store on the device.</summary>
    public string Write()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("version", 1);
            json.WriteStartObject("values");
            foreach ((string id, string value) in Values)
            {
                json.WriteString(id, value);
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>A state read from <see cref="Write"/>'s document; anything unreadable gives the default.</summary>
    public static SettingsState Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Default;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("values", out JsonElement values) || values.ValueKind != JsonValueKind.Object)
            {
                return Default;
            }

            return From(values.EnumerateObject().Where(property => property.Value.ValueKind == JsonValueKind.String).Select(property => new KeyValuePair<string, string>(property.Name, property.Value.GetString()!)));
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    public bool Equals(SettingsState? other) => other is not null && Values.SequenceEqual(other.Values);

    public override int GetHashCode() => _values.Count;
}