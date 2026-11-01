using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Settings;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>The answers about what the project keeps with the piece (0018; contract 2): marks, bookmarks, resume points, ideas, settings and Check.</summary>
internal static partial class Answer
{
    public static void Marks(Utf8JsonWriter json, Marks marks, MeterMap meter)
    {
        json.WriteStartArray("ink");
        foreach (InkStroke stroke in marks.Ink)
        {
            json.WriteStartObject();
            json.WriteString("id", stroke.Id);
            json.WriteString("tool", Name(stroke.Tool));
            json.WriteString("colour", Name(stroke.Colour));
            json.WriteNumber("size", stroke.Size);
            json.WriteNumber("bar", stroke.Bar.Value);
            json.WriteNumber("u", stroke.U);
            json.WriteNumber("v", stroke.V);
            json.WriteStartArray("points");
            foreach (double value in stroke.Points)
            {
                json.WriteNumberValue(value);
            }

            json.WriteEndArray();
            json.WriteNumber("pressure", stroke.Pressure);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteStartArray("notes");
        foreach (MarginNote note in marks.Notes)
        {
            json.WriteStartObject();
            json.WriteString("id", note.Id);
            Place(json, note.Span, meter);
            json.WriteString("text", note.Text);
            json.WriteString("createdAt", note.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteStartArray("highlights");
        foreach (Highlight highlight in marks.Highlights)
        {
            json.WriteStartObject();
            json.WriteString("id", highlight.Id);
            Place(json, highlight.Span, meter);
            Texts(json, "notes", highlight.NoteIds);
            json.WriteString("colour", Name(highlight.Colour));
            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteStartArray("fingering");
        foreach (Fingering fingering in marks.Fingering)
        {
            json.WriteStartObject();
            json.WriteString("noteId", fingering.NoteId);
            json.WriteNumber("finger", fingering.Finger);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    public static void Bookmarks(Utf8JsonWriter json, Project project, MeterMap meter, Words words)
    {
        json.WriteStartArray("categories");
        foreach (BookmarkCategory category in project.Categories)
        {
            json.WriteStartObject();
            json.WriteString("id", category.Id);
            json.WriteString("name", category.NameIn(words));
            json.WriteString("ink", Name(category.Ink));
            json.WriteBoolean("builtIn", category.IsBuiltIn);
            json.WriteNumber("bookmarks", project.Bookmarks.Count(bookmark => bookmark.CategoryId == category.Id));
            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteStartArray("bookmarks");
        foreach (Bookmark bookmark in project.Bookmarks)
        {
            json.WriteStartObject();
            json.WriteString("id", bookmark.Id);
            json.WriteString("title", bookmark.Title);
            json.WriteString("category", bookmark.CategoryId);
            Place(json, bookmark.Span, meter);
            json.WriteBoolean("fromPhrase", bookmark.FromPhrase);
            json.WriteNumber("ideas", project.Observations.Count(idea => idea.IsAbout(bookmark.Span)));
            json.WriteString("createdAt", bookmark.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    public static void ResumePoints(Utf8JsonWriter json, Project project, MeterMap meter)
    {
        json.WriteStartArray("resumePoints");
        foreach (ResumePoint point in project.RecentResumePoints)
        {
            ResumePoint(json, point, meter);
        }

        json.WriteEndArray();
    }

    public static void ResumePoint(Utf8JsonWriter json, ResumePoint point, MeterMap meter)
    {
        json.WriteStartObject();
        json.WriteString("id", point.Id);
        json.WriteString("name", point.Name);
        json.WriteNumber("at", point.At.Value);
        json.WriteNumber("bar", meter.BarAt(point.At).Number);
        if (point.Passage is { } passage)
        {
            json.WriteStartObject("passage");
            Place(json, passage.Span, meter);
            Filter(json, "filter", passage.Filter);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("passage");
        }

        json.WriteBoolean("focused", point.Focused);
        json.WriteStartArray("overlays");
        foreach (Overlay overlay in point.Overlays)
        {
            json.WriteStringValue(Name(overlay));
        }

        json.WriteEndArray();
        json.WriteNumber("tempoPercent", point.TempoPercent);
        json.WriteString("hands", point.Hands is { } hands ? Name(hands) : "both");
        json.WriteBoolean("loop", point.Loop);
        json.WriteString("createdAt", point.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        json.WriteString("lastActive", point.LastActive.ToString("O", CultureInfo.InvariantCulture));
        json.WriteEndObject();
    }

    public static void Ideas(Utf8JsonWriter json, Project project, MeterMap meter)
    {
        json.WriteStartArray("ideas");
        foreach (Observation idea in project.Observations)
        {
            json.WriteStartObject();
            json.WriteString("id", idea.Id);
            json.WriteString("name", idea.Name);
            json.WriteString("note", idea.Note);
            json.WriteStartObject("phrase");
            Place(json, idea.Phrase.Span, meter);
            Filter(json, "filter", idea.Phrase.Filter);
            json.WriteEndObject();
            if (idea is { ExperimentId: { } experiment, Alternative: { } alternative })
            {
                json.WriteStartObject("experiment");
                json.WriteString("id", experiment);
                json.WriteNumber("alternative", alternative);
                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("experiment");
            }

            json.WriteString("factId", idea.FactId);
            json.WriteBoolean("hasVariation", idea.Variation is not null);
            if (idea.Variation is { } variation)
            {
                json.WriteStartArray("steps");
                foreach (SavedStep step in variation.Steps)
                {
                    json.WriteStringValue(step.Operation);
                }

                json.WriteEndArray();
            }

            json.WriteString("createdAt", idea.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    public static void Check(Utf8JsonWriter json, Project project, Words words)
    {
        json.WriteStartArray("flags");
        foreach (CheckFlag flag in (project.Check ?? []).OrderBy(flag => flag.Bar))
        {
            json.WriteStartObject();
            json.WriteNumber("bar", flag.Bar);
            json.WriteString("source", Name(flag.Source));
            json.WriteString("reason", Name(flag.Reason));
            json.WriteString("text", flag.Describe(words));
            json.WriteBoolean("resolved", flag.Resolved);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteNumber("open", project.OpenChecks.Count);
    }

    /// <summary>The settings catalogue (0018, section 10): every setting with its choices, default, preset values and scope.</summary>
    public static void SettingsCatalogue(Utf8JsonWriter json)
    {
        json.WriteStartArray("settings");
        foreach (Setting setting in Settings.SettingsCatalogue.All)
        {
            json.WriteStartObject();
            json.WriteString("id", setting.Id);
            json.WriteString("section", Name(setting.Section));
            json.WriteString("kind", Name(setting.Kind));
            Texts(json, "choices", setting.Choices);
            json.WriteString("default", setting.Default);
            if (setting.Presets is { } presets)
            {
                json.WriteStartObject("presets");
                json.WriteString("learning", presets.Learning);
                json.WriteString("practising", presets.Practising);
                json.WriteString("clean", presets.Clean);
                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("presets");
            }

            json.WriteString("scope", Name(setting.Scope));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        Texts(json, "presets", [Name(Preset.Learning), Name(Preset.Practising), Name(Preset.Clean)]);
    }

    public static void SettingsState(Utf8JsonWriter json, SettingsState state)
    {
        json.WriteString("preset", Name(state.Preset));
        json.WriteStartObject("values");
        foreach ((string id, string value) in state.Values)
        {
            json.WriteString(id, value);
        }

        json.WriteEndObject();
    }

    public static void Overrides(Utf8JsonWriter json, Project project)
    {
        json.WriteStartObject("overrides");
        foreach ((string id, string value) in project.SettingOverrides.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            json.WriteString(id, value);
        }

        json.WriteEndObject();
    }

    /// <summary>A span with the bars it covers, as the drawers list a mark: "span": {onset, end}, "bars": {first, last}.</summary>
    private static void Place(Utf8JsonWriter json, TickSpan span, MeterMap meter)
    {
        Span(json, "span", span);
        json.WriteStartObject("bars");
        json.WriteNumber("first", meter.BarAt(span.Onset).Number);
        json.WriteNumber("last", meter.BarAt(new Tick(span.End.Value - 1)).Number);
        json.WriteEndObject();
    }
}