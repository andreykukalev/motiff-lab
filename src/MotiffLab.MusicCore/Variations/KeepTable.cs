namespace MotiffLab.MusicCore.Variations;

/// <summary>
/// A footnote on one cell of the Keep table: the operation keeps the property only in the narrower
/// sense the footnote states.
/// </summary>
/// <param name="Number">The footnote's number in the spec's table, 1–4.</param>
/// <param name="Text">The footnote as the player reads it on the operation card.</param>
/// <param name="ConflictsWith">
/// A property that cannot be kept together with this cell's property for this operation; the
/// operation is off while both are on (footnote 1). Null when there is none.
/// </param>
public sealed record KeepFootnote(int Number, string Text, KeepProperty? ConflictsWith = null);

/// <summary>One operation's row of the Keep table (S1-E2).</summary>
/// <param name="OperationId">The catalogue id, for example "V1".</param>
/// <param name="Name">The operation's name as the player reads it.</param>
/// <param name="WhatItChanges">"changes the size of the melody's intervals", completing "&lt;Name&gt; …, so it is off while Keep x is on".</param>
/// <param name="Compatible">The properties the operation can run with kept: the row's ✓ cells.</param>
/// <param name="Footnotes">The row's footnoted cells, each a compatible property.</param>
public sealed record KeepRow(
    string OperationId,
    string Name,
    string WhatItChanges,
    IReadOnlySet<KeepProperty> Compatible,
    IReadOnlyDictionary<KeepProperty, KeepFootnote> Footnotes);

/// <summary>A one-tap starting point (S1-E2): what to keep, and the operations it suggests.</summary>
/// <param name="Id">
/// A stable id. Experiment records and track-page invitations store it next to the operation and
/// its parameters, so it never changes once shipped.
/// </param>
/// <param name="Label">The preset as the player reads it, for example "Keep the pitch order, change the rhythm".</param>
/// <param name="Keep">The Keep choices the preset turns on; every other one is turned off.</param>
/// <param name="Operations">The catalogue ids it suggests, in the spec's order. Each runs with <paramref name="Keep"/>.</param>
public sealed record KeepPreset(string Id, string Label, IReadOnlySet<KeepProperty> Keep, IReadOnlyList<string> Operations);

/// <summary>
/// The Keep table and its presets from the Stage 1 spec, as data: which Keep choices each catalogue
/// operation can run with, its footnotes, and the five presets. Every operation reads its row here,
/// so the enabled state, the disabled reasons and the cards come from one table (S1-E2, AC-S1-10).
/// The table holds every Stage 1 operation, including those this version does not offer yet.
/// </summary>
/// <remarks>
/// The footnotes are enforced where they apply. ¹ V1 is off while Keep melody and Keep rhythm are
/// both on, and its Keep melody check reads the melody's pitches, not their rhythm. ² With a key,
/// the intervals check also accepts sizes kept in scale steps. ³ After a V3 step, the melody check
/// accepts the melody's pitch classes with their octaves changed. ⁴ <see cref="AllowsInversions"/>
/// is false while Keep bass is on. The musicality check in milestone 3 may adjust the table.
/// </remarks>
public static class KeepTable
{
    public static KeepFootnote MelodyPitchesOnly { get; } = new(1, "Melody pitches stay; its rhythm changes, so Keep melody and Keep rhythm cannot both be on.", KeepProperty.Rhythm);

    public static KeepFootnote ScaleStepIntervals { get; } = new(2, "Diatonic sequences keep interval sizes in scale steps, not semitones.");

    public static KeepFootnote MelodyPitchClasses { get; } = new(3, "Melody pitch classes stay; its octave may change.");

    public static KeepFootnote UpperVoicesOnly { get; } = new(4, "Only spacing, register of upper voices and hand distribution change; inversions are off while Keep bass is on.");

    /// <summary>
    /// The table's rows in the spec's order: rhythm, melody, accompaniment, voicing, difficulty. V13
    /// Articulation, added for the Explore experiments, sits with the rhythm operations. Keep rhythm
    /// covers onsets and lengths, so it rules V13 out even though V13 keeps every onset.
    /// </summary>
    public static IReadOnlyList<KeepRow> Rows { get; } =
    [
        Row("V1", "Rhythm reshape", "changes when notes start and how long they last",
            [KeepProperty.Pitches, KeepProperty.PitchOrder, KeepProperty.Contour, KeepProperty.Intervals, KeepProperty.Melody, KeepProperty.Bass, KeepProperty.Harmony, KeepProperty.Register],
            (KeepProperty.Melody, MelodyPitchesOnly)),
        Row("V13", "Articulation", "changes how long notes sound",
            [KeepProperty.Pitches, KeepProperty.PitchOrder, KeepProperty.Contour, KeepProperty.Intervals, KeepProperty.Melody, KeepProperty.Bass, KeepProperty.Harmony, KeepProperty.Register]),
        Row("V7", "Density and rests", "changes how many notes there are and where the rests fall",
            [KeepProperty.PitchOrder, KeepProperty.Contour, KeepProperty.Bass, KeepProperty.Harmony, KeepProperty.Register]),
        Row("V8", "Accent shift", "changes which notes are accented",
            [.. Enum.GetValues<KeepProperty>()]),
        Row("V4", "Melodic sequence", "repeats the motif at another pitch level",
            [KeepProperty.Contour, KeepProperty.Intervals, KeepProperty.Rhythm],
            (KeepProperty.Intervals, ScaleStepIntervals)),
        Row("V9", "Interval reshape", "changes the size of the melody's intervals",
            [KeepProperty.Contour, KeepProperty.Rhythm, KeepProperty.Bass]),
        Row("V10", "Change direction", "turns the melody's movement upside down",
            [KeepProperty.Rhythm, KeepProperty.Bass]),
        Row("V11", "Passing and neighbour notes", "adds notes between the main notes",
            [KeepProperty.Bass, KeepProperty.Harmony, KeepProperty.Register]),
        Row("V2", "Accompaniment pattern", "changes how the chords are played",
            [KeepProperty.Melody, KeepProperty.Harmony]),
        Row("V12", "Bass pattern", "changes the bass line",
            [KeepProperty.Melody, KeepProperty.Harmony]),
        Row("V3", "Voicing and register", "changes how the chords are voiced and where they sit",
            [KeepProperty.Rhythm, KeepProperty.Melody, KeepProperty.Bass, KeepProperty.Harmony],
            (KeepProperty.Melody, MelodyPitchClasses), (KeepProperty.Bass, UpperVoicesOnly)),
        Row("V6", "Simplify", "plays fewer, easier notes",
            [KeepProperty.Contour, KeepProperty.Harmony]),
    ];

    /// <summary>The five presets, in the spec's order.</summary>
    public static IReadOnlyList<KeepPreset> Presets { get; } =
    [
        new("keep-pitch-order", "Keep the pitch order, change the rhythm", new HashSet<KeepProperty> { KeepProperty.PitchOrder }, ["V1", "V7", "V8"]),
        new("keep-rhythm", "Keep rhythm, change intervals", new HashSet<KeepProperty> { KeepProperty.Rhythm }, ["V9", "V10", "V4"]),
        new("keep-melody", "Keep the melody, change the accompaniment", new HashSet<KeepProperty> { KeepProperty.Melody }, ["V2", "V12", "V3"]),
        new("keep-bass", "Keep the bass, change the upper voices", new HashSet<KeepProperty> { KeepProperty.Bass }, ["V3"]),
        new("keep-contour", "Keep the contour, change interval sizes", new HashSet<KeepProperty> { KeepProperty.Contour }, ["V9"]),
    ];

    /// <summary>The row of this catalogue id, or null when the table has none.</summary>
    public static KeepRow? Find(string operationId) => Rows.FirstOrDefault(row => row.OperationId == operationId);

    /// <summary>The preset with this id, or null.</summary>
    public static KeepPreset? FindPreset(string id) => Presets.FirstOrDefault(preset => preset.Id == id);

    /// <summary>
    /// Why the Keep choices rule the operation out, or null when it can run: the kept properties
    /// without a ✓ in its row, or two kept properties a footnote says cannot both be on.
    /// </summary>
    /// <exception cref="ArgumentException">The table has no row for <paramref name="operationId"/>.</exception>
    public static string? DisabledReason(string operationId, IReadOnlySet<KeepProperty> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        KeepRow row = Find(operationId) ?? throw new ArgumentException($"The Keep table has no row for {operationId}.", nameof(operationId));
        foreach ((KeepProperty property, KeepFootnote footnote) in row.Footnotes.OrderBy(static cell => cell.Key))
        {
            if (footnote.ConflictsWith is { } other && keep.Contains(property) && keep.Contains(other))
            {
                return $"{row.Name} {row.WhatItChanges}, so Keep {Name(property)} and Keep {Name(other)} cannot both be on.";
            }
        }

        KeepProperty[] blocking = [.. Enum.GetValues<KeepProperty>().Where(property => keep.Contains(property) && !row.Compatible.Contains(property))];
        return blocking.Length == 0
            ? null
            : $"{row.Name} {row.WhatItChanges}, so it is off while Keep {string.Join(" and Keep ", blocking.Select(Name))} {(blocking.Length == 1 ? "is" : "are")} on.";
    }

    /// <summary>The footnotes that qualify what the operation keeps under these Keep choices, by number, for its card.</summary>
    public static IReadOnlyList<KeepFootnote> Footnotes(string operationId, IReadOnlySet<KeepProperty> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        return Find(operationId) is { } row
            ? [.. row.Footnotes.Where(cell => keep.Contains(cell.Key)).Select(static cell => cell.Value).OrderBy(static footnote => footnote.Number)]
            : [];
    }

    /// <summary>Whether V3 may change a chord's inversion: not while Keep bass is on (footnote 4).</summary>
    public static bool AllowsInversions(IReadOnlySet<KeepProperty> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        return !keep.Contains(KeepProperty.Bass);
    }

    /// <summary>
    /// Whether one of the steps, by catalogue id, keeps <paramref name="property"/> only in the sense
    /// of <paramref name="footnote"/>, so the check of the whole chain accepts that sense.
    /// </summary>
    internal static bool Qualifies(IEnumerable<string> operationIds, KeepProperty property, KeepFootnote footnote) =>
        operationIds.Any(id => Find(id) is { } row && row.Footnotes.TryGetValue(property, out KeepFootnote? cell) && cell == footnote);

    private static KeepRow Row(string id, string name, string whatItChanges, KeepProperty[] compatible, params (KeepProperty Property, KeepFootnote Footnote)[] footnotes) =>
        new(id, name, whatItChanges, compatible.ToHashSet(), footnotes.ToDictionary(static cell => cell.Property, static cell => cell.Footnote));

    private static string Name(KeepProperty property) => property switch
    {
        KeepProperty.PitchOrder => "pitch order",
        _ => property.ToString().ToLowerInvariant(),
    };
}