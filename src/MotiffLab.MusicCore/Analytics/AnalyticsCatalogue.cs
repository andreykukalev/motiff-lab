using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Analytics;

/// <summary>What one property of an event may hold.</summary>
public abstract record AnalyticsProperty(string Name)
{
    /// <summary>Whether <paramref name="value"/> may be sent under this property.</summary>
    public abstract bool Allows(object value);
}

/// <summary>A whole number from <paramref name="Min"/> to <paramref name="Max"/>.</summary>
public sealed record CountProperty(string Name, int Min, int Max) : AnalyticsProperty(Name)
{
    public override bool Allows(object value) => value is int count && count >= Min && count <= Max;
}

/// <summary>True or false.</summary>
public sealed record FlagProperty(string Name) : AnalyticsProperty(Name)
{
    public override bool Allows(object value) => value is bool;
}

/// <summary>One of a fixed list of words.</summary>
public sealed record ChoiceProperty(string Name, IReadOnlyList<string> Choices) : AnalyticsProperty(Name)
{
    public override bool Allows(object value) => value is string choice && Choices.Contains(choice, StringComparer.Ordinal);
}

/// <summary>
/// The Stage 1 analytics events (#64, docs/01-mvp-stage-1.md, study design): the few needed to see
/// how the week of home use went, and nothing about what was played. Events carry no note content,
/// no file names or titles, and no free text; a property outside the catalogue, or a value outside
/// its range or list, is refused. Analytics are off until the player turns them on.
/// </summary>
public static class AnalyticsCatalogue
{
    public const int MaxTracks = 64;

    public const int MaxMilliseconds = 600_000;

    /// <summary>Note counts are sent as the upper end of their band, so a file cannot be told apart by its exact count.</summary>
    public static IReadOnlyList<int> NoteBands { get; } = [100, 500, 1_000, 5_000, 20_000, int.MaxValue];

    private static readonly Dictionary<string, AnalyticsProperty[]> s_events = new(StringComparer.Ordinal)
    {
        ["app_opened"] = [],
        ["piece_imported"] =
        [
            new ChoiceProperty("format", ["midi", "musicxml", "mxl", "other"]),
            new FlagProperty("opened"),
            new ChoiceProperty("notes", [.. NoteBands.Select(BandName)]),
            new CountProperty("tracks", 0, MaxTracks),
            new FlagProperty("performance_timing"),
            new CountProperty("read_ms", 0, MaxMilliseconds),
        ],
        ["starter_opened"] = [new ChoiceProperty("piece", ["fur_elise"])],
        ["motif_selected"] = [new CountProperty("bars", 1, MotifSelection.MaxStructureBars)],
        ["playback_started"] =
        [
            new ChoiceProperty("hear", ["original", "variation", "both"]),
            new CountProperty("tempo_percent", 25, 150),
            new FlagProperty("loop"),
        ],
        ["variation_proposed"] =
        [
            new ChoiceProperty("op", [RhythmReshape.Instance.Id]),
            new ChoiceProperty("kind", [.. Enum.GetValues<RhythmReshapeKind>().Select(kind => Choice(kind))]),
            new CountProperty("amount", 0, RhythmReshapeParameters.MaxEighths),
            new CountProperty("kept", 0, Enum.GetValues<KeepProperty>().Length),
        ],
        ["variation_step"] =
        [
            new ChoiceProperty("action", ["accept", "reject", "undo", "redo"]),
            new CountProperty("steps", 0, VariationChain.MaxSteps),
        ],
        ["note_edited"] = [new ChoiceProperty("kind", [.. Enum.GetValues<HandEditKind>().Select(kind => Choice(kind))])],
        ["midi_exported"] = [new FlagProperty("variation"), new CountProperty("steps", 0, VariationChain.MaxSteps)],
        ["experiment_tried"] =
        [
            new ChoiceProperty("experiment", ExperimentCatalogue.Ids),
            new CountProperty("alternative", 0, ExperimentCatalogue.MaxAlternatives - 1),
            new ChoiceProperty("entry", ["offer", "question", "surprise", "another", "fact"]),
        ],
        ["experiment_kept"] =
        [
            new ChoiceProperty("experiment", ExperimentCatalogue.Ids),
            new CountProperty("alternative", 0, ExperimentCatalogue.MaxAlternatives - 1),
        ],
    };

    /// <summary>The events' names, in the order above.</summary>
    public static IEnumerable<string> EventNames => s_events.Keys;

    /// <summary>The properties an event may carry.</summary>
    /// <exception cref="ArgumentException">The catalogue has no event with this name.</exception>
    public static IReadOnlyList<AnalyticsProperty> PropertiesOf(string name) =>
        s_events.TryGetValue(name, out AnalyticsProperty[]? properties) ? properties : throw new ArgumentException($"The analytics catalogue has no event {name}.", nameof(name));

    /// <summary>An event with exactly the catalogue's properties, each checked against what it may hold.</summary>
    /// <exception cref="ArgumentException">The event, a property or a value is not in the catalogue.</exception>
    public static AnalyticsEvent Create(string name, IReadOnlyDictionary<string, object>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        IReadOnlyList<AnalyticsProperty> allowed = PropertiesOf(name);
        properties ??= new Dictionary<string, object>();
        foreach ((string key, object value) in properties)
        {
            AnalyticsProperty property = allowed.FirstOrDefault(property => property.Name == key)
                ?? throw new ArgumentException($"The event {name} has no property {key}.", nameof(properties));
            if (!property.Allows(value))
            {
                throw new ArgumentException($"The value of {name}.{key} is not one the catalogue allows.", nameof(properties));
            }
        }

        if (allowed.FirstOrDefault(property => !properties.ContainsKey(property.Name)) is { } missing)
        {
            throw new ArgumentException($"The event {name} needs its property {missing.Name}.", nameof(properties));
        }

        return new AnalyticsEvent(name, new Dictionary<string, object>(properties, StringComparer.Ordinal));
    }

    /// <summary>The band a note count falls in, named by its upper end: "100", "500", … "more".</summary>
    public static string Band(int notes) => BandName(NoteBands.First(band => notes <= band));

    /// <summary>An enum value as a choice: its name in snake case, for example "half_time".</summary>
    public static string Choice<T>(T value)
        where T : struct, Enum =>
        string.Concat(value.ToString().Select((c, i) => char.IsUpper(c) ? (i == 0 ? "" : "_") + char.ToLowerInvariant(c) : c.ToString()));

    private static string BandName(int band) => band == int.MaxValue ? "more" : band.ToString(System.Globalization.CultureInfo.InvariantCulture);
}