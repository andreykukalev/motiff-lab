using System.Text.Json;

using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>The answers about performances (decision record 0022; contract 4).</summary>
internal static partial class Answer
{
    /// <summary>The performances of the piece, each with its counts.</summary>
    public static void Performances(Utf8JsonWriter json, Project project)
    {
        json.WriteStartArray("performances");
        foreach (SavedPerformance performance in project.Performances)
        {
            json.WriteStartObject();
            PerformanceHead(json, performance);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>
    /// One performance compared with the piece, bar by bar: where the bar is, whether it lines up, its kind, the marker
    /// text, the confidence in percent, whether the player dismissed it or took it, and the notes on each side.
    /// </summary>
    public static void Performance(Utf8JsonWriter json, SavedPerformance performance, PerformanceComparison comparison, int printed, Words words, MeterMap meter)
    {
        json.WriteStartObject("performance");
        PerformanceHead(json, performance);
        json.WriteNumber("matched", comparison.MatchedCount);
        json.WriteNumber("printed", printed);
        HashSet<int> dismissed = [.. performance.Dismissed];
        HashSet<int> taken = [.. performance.Taken.Select(bar => bar.Bar)];
        json.WriteStartArray("bars");
        foreach (BarComparison bar in comparison.Bars)
        {
            json.WriteStartObject();
            json.WriteNumber("bar", bar.Bar);
            Span(json, "span", meter.GetBar(bar.Bar).Span);
            json.WriteBoolean("aligned", bar.Aligned);
            json.WriteString("kind", Name(bar.Difference));
            json.WriteString("marker", bar.Marker(words));
            json.WriteNumber("confidence", bar.Confidence);
            json.WriteBoolean("dismissed", dismissed.Contains(bar.Bar));
            json.WriteBoolean("taken", taken.Contains(bar.Bar));
            json.WriteStartArray("matched");
            foreach (NoteMatch match in bar.Matched)
            {
                json.WriteStartObject();
                json.WriteString("printed", match.PrintedId);
                json.WriteString("played", match.PlayedId);
                json.WriteNumber("offset", match.Offset);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            Ids(json, "notHeard", bar.PrintedNotHeard);
            Ids(json, "probablyNotHeard", bar.ProbablyNotHeard);
            Placed(json, "played", bar.PlayedNotPrinted);
            Placed(json, "artefacts", bar.LikelyArtefacts);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void PerformanceHead(Utf8JsonWriter json, SavedPerformance performance)
    {
        json.WriteString("id", performance.Id);
        json.WriteString("name", performance.Name);
        json.WriteString("source", Name(performance.Source));
        if (performance.Engine is { } engine)
        {
            json.WriteString("engine", engine);
        }

        json.WriteNumber("recordingStart", performance.RecordingStart / 1000);
        json.WriteNumber("takenBars", performance.Taken.Count);
        json.WriteNumber("dismissedBars", performance.Dismissed.Count);
        // The merge last applied (testing items 32 and 34): when, so the version page can say so; null before any or once undone.
        if (performance.Merge is { AppliedAt: { } at })
        {
            json.WriteString("mergedAt", at.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            json.WriteNull("mergedAt");
        }

        json.WriteBoolean("mergeKept", performance.Merge is not null);
    }

    private static void Ids(Utf8JsonWriter json, string name, IReadOnlyList<string> ids)
    {
        json.WriteStartArray(name);
        foreach (string id in ids)
        {
            json.WriteStringValue(id);
        }

        json.WriteEndArray();
    }

    private static void Placed(Utf8JsonWriter json, string name, IReadOnlyList<PlacedNote> notes)
    {
        json.WriteStartArray(name);
        foreach (PlacedNote note in notes)
        {
            json.WriteStartObject();
            json.WriteString("id", note.Id);
            json.WriteNumber("pitch", note.Pitch.MidiNumber);
            json.WriteNumber("at", note.At.Value);
            json.WriteNumber("velocity", note.Velocity);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }
}