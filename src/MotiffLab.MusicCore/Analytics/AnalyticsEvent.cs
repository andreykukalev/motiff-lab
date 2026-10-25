using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Analytics;

/// <summary>
/// One anonymous usage event (#64): a name from <see cref="AnalyticsCatalogue"/> and properties
/// that are only counts, flags and choices from fixed lists. The catalogue checks every property,
/// so a file name, a title or anything about the notes cannot be put in one.
/// </summary>
/// <param name="Properties">Each value is an <see cref="int"/>, a <see cref="bool"/> or one of the property's choices.</param>
public sealed record AnalyticsEvent(string Name, IReadOnlyDictionary<string, object> Properties)
{
    /// <summary>The app was opened: once per load of the app, for days of use in the study.</summary>
    public static AnalyticsEvent AppOpened() => AnalyticsCatalogue.Create("app_opened");

    /// <summary>A file was imported, or turned down; the note count is rounded up to a band.</summary>
    public static string FormatOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".mid" or ".midi" => "midi",
        ".musicxml" or ".xml" => "musicxml",
        ".mxl" => "mxl",
        _ => "other",
    };

    /// <summary>A file was imported, or turned down; the note count is rounded up to a band.</summary>
    /// <param name="format">"midi", "musicxml", "mxl", or "other" for a file of another kind.</param>
    /// <param name="opened">False when the file was turned down.</param>
    public static AnalyticsEvent PieceImported(string format, bool opened, int notes, int tracks, bool performanceTiming, long readMilliseconds) =>
        AnalyticsCatalogue.Create("piece_imported", new Dictionary<string, object>
        {
            ["format"] = format,
            ["opened"] = opened,
            ["notes"] = AnalyticsCatalogue.Band(notes),
            ["tracks"] = Math.Clamp(tracks, 0, AnalyticsCatalogue.MaxTracks),
            ["performance_timing"] = performanceTiming,
            ["read_ms"] = (int)Math.Clamp(readMilliseconds, 0, AnalyticsCatalogue.MaxMilliseconds),
        });

    /// <summary>A piece built into the app was opened (S1-C1).</summary>
    /// <param name="piece">"fur_elise".</param>
    public static AnalyticsEvent StarterOpened(string piece) =>
        AnalyticsCatalogue.Create("starter_opened", new Dictionary<string, object> { ["piece"] = piece });

    /// <summary>A motif was selected (S1-M1), by its length in bars.</summary>
    public static AnalyticsEvent MotifSelected(int bars) =>
        AnalyticsCatalogue.Create("motif_selected", new Dictionary<string, object> { ["bars"] = bars });

    /// <summary>Playback started (S1-W4, S1-W5).</summary>
    /// <param name="hear">"original", "variation" or "both".</param>
    public static AnalyticsEvent PlaybackStarted(string hear, int tempoPercent, bool loop) =>
        AnalyticsCatalogue.Create("playback_started", new Dictionary<string, object>
        {
            ["hear"] = hear,
            ["tempo_percent"] = tempoPercent,
            ["loop"] = loop,
        });

    /// <summary>A rhythm reshape (V1) was proposed, with how many properties were kept.</summary>
    public static AnalyticsEvent VariationProposed(RhythmReshapeParameters parameters, int kept)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return AnalyticsCatalogue.Create("variation_proposed", new Dictionary<string, object>
        {
            ["op"] = RhythmReshape.Instance.Id,
            ["kind"] = AnalyticsCatalogue.Choice(parameters.Kind),
            ["amount"] = parameters.Kind == RhythmReshapeKind.Displace ? parameters.Eighths : 0,
            ["kept"] = kept,
        });
    }

    /// <summary>A step of a variation was accepted, rejected, undone or redone (S1-E4), with the steps the chain then holds.</summary>
    /// <param name="action">"accept", "reject", "undo" or "redo".</param>
    public static AnalyticsEvent VariationStep(string action, int steps) =>
        AnalyticsCatalogue.Create("variation_step", new Dictionary<string, object> { ["action"] = action, ["steps"] = steps });

    /// <summary>A note of a variation was edited by hand (S1-E7).</summary>
    public static AnalyticsEvent NoteEdited(HandEditKind kind) =>
        AnalyticsCatalogue.Create("note_edited", new Dictionary<string, object> { ["kind"] = AnalyticsCatalogue.Choice(kind) });

    /// <summary>The original or the variation was exported as MIDI (S1-X1).</summary>
    public static AnalyticsEvent MidiExported(bool variation, int steps) =>
        AnalyticsCatalogue.Create("midi_exported", new Dictionary<string, object> { ["variation"] = variation, ["steps"] = steps });

    /// <summary>An experiment of the Explore card was tried (decision record 0010), by its id and which alternative.</summary>
    /// <param name="entry">How the player got to it: "offer" (picked from the card), "question", "surprise", "another" (Try another) or "fact" (from a fact on the Understand tab).</param>
    public static AnalyticsEvent ExperimentTried(string experiment, int alternative, string entry) =>
        AnalyticsCatalogue.Create("experiment_tried", new Dictionary<string, object> { ["experiment"] = experiment, ["alternative"] = alternative, ["entry"] = entry });

    /// <summary>The player kept the variation an experiment made.</summary>
    public static AnalyticsEvent ExperimentKept(string experiment, int alternative) =>
        AnalyticsCatalogue.Create("experiment_kept", new Dictionary<string, object> { ["experiment"] = experiment, ["alternative"] = alternative });
}