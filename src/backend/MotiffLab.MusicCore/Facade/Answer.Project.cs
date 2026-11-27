using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Settings;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The answers about the project (0018; contract 2): its summary on a library card and in the open answer, and what it keeps
/// with the piece: marks, bookmarks, resume points, ideas, settings and Check.
/// </summary>
internal static partial class Answer
{
    /// <summary>The ids of a list's entities kept unknown for a variation operation this build does not have; left out when none.</summary>
    private static void NewerIds(Utf8JsonWriter json, string name, Project project, string list)
    {
        string[] ids = [.. project.Unknown.Where(entity => entity.List == list && entity.Enum == UnknownEntity.NewerOperation && entity.Id is not null).Select(static entity => entity.Id!)];
        if (ids.Length > 0)
        {
            JsonWrite.Texts(json, name, ids);
        }
    }

    public static void Project(Utf8JsonWriter json, string name, Project project)
    {
        json.WriteStartObject(name);
        json.WriteString("id", project.Id);
        // Version 18 (#317): the ids the piece was known by before it was re-keyed; left out when none.
        JsonWrite.TextsIfAny(json, "formerIds", project.FormerIds);
        json.WriteString("fileName", project.FileName);
        json.WriteString("createdAt", project.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        json.WriteString("lastOpened", project.LastOpened.ToString("O", CultureInfo.InvariantCulture));
        json.WriteString("origin", project.Scan is not null ? "scanned" : project.Transcription is not null ? "transcribed" : "imported");
        json.WriteNumber("motifs", project.Motifs.Count);
        json.WriteNumber("variations", project.Variations.Count);
        json.WriteNumber("corrections", project.Edits.Edits.Count);
        json.WriteNumber("resumePoints", project.ResumePoints.Count);
        json.WriteNumber("ideas", project.Observations.Count);
        json.WriteNumber("marks", project.Marks.Count);
        json.WriteNumber("bookmarks", project.Bookmarks.Count);
        json.WriteNumber("openChecks", project.OpenChecks.Count);
        Details(json, project.Details);
        // Within contract 4, for the library's cards (0018 section 9): the bars still to check, whether the piece has notes
        // and a PDF (0023), and the resume point worked from last.
        json.WriteNumber("checkBars", project.Summary.CheckBars);
        json.WriteBoolean("hasNotes", project.HasNotes);
        json.WriteBoolean("document", project.Document is not null);
        // Within contract 12 (version 0.3): the originals and where their bytes are, for the iPad's badges.
        Originals(json, project);
        // Within contract 12 (version 0.4): where a piece made with «Пьесы в файле» is in its collection.
        PartOf(json, project.PartOf);
        PieceKeys(json, project);
        // Version 18 (0026 S6): a project a later version wrote opens read-only and is never saved here; a variation or an
        // idea of an operation this build does not have is kept as it was and named here, made by a later build (#314).
        if (project.IsReadOnly)
        {
            json.WriteBoolean("readOnly", true);
        }

        NewerIds(json, "newerVariations", project, "variations");
        NewerIds(json, "newerIdeas", project, "observations");
        // Within contract 4 (2026-11-05): notes read from a PDF piece name it, and where each of its pages starts in the
        // notes, in ticks, for PDF | Notes.
        if (project.ReadFrom is { } readFrom)
        {
            json.WriteString("readFrom", readFrom.ProjectId);
            JsonWrite.Numbers(json, "readFromPages", readFrom.Pages.Starts.Select(static start => start.Value));
        }

        // Contract 6 (testing item 16): a piece with notes and a PDF kept inside it, where each of the PDF's pages starts
        // in the notes, in ticks, for PDF | Notes within the one piece.
        if (project.HasNotes && project.DocumentPages is { } pages)
        {
            JsonWrite.Numbers(json, "documentPages", pages.Starts.Select(static start => start.Value));
        }

        if (project.RecentResumePoints.FirstOrDefault() is { } latest)
        {
            json.WriteStartObject("latest");
            json.WriteString("id", latest.Id);
            if (latest.Name is { } pointName)
            {
                json.WriteString("name", pointName);
            }

            json.WriteNumber("at", latest.At.Value);
            json.WriteString("lastActive", latest.LastActive.ToString("O", CultureInfo.InvariantCulture));
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("latest");
        }

        json.WriteEndObject();
    }

    public static void Marks(Utf8JsonWriter json, Marks marks, MeterMap meter)
    {
        json.WriteStartArray("ink");
        foreach (InkStroke stroke in marks.Ink)
        {
            ProjectFile.WriteStroke(json, stroke, (writer, ink) =>
            {
                writer.WriteNumber("bar", ink.Bar.Value);
                writer.WriteNumber("barNumber", meter.BarAt(ink.Bar).Number);
            });
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
            JsonWrite.Texts(json, "notes", highlight.NoteIds);
            json.WriteString("colour", JsonWrite.Name(highlight.Colour));
            json.WriteEndObject();
        }

        json.WriteEndArray();

        ProjectFile.WriteFingering(json, marks.Fingering);
    }

    public static void Bookmarks(Utf8JsonWriter json, Project project, MeterMap meter, Words words)
    {
        json.WriteStartArray("categories");
        foreach (BookmarkCategory category in project.Categories)
        {
            json.WriteStartObject();
            json.WriteString("id", category.Id);
            json.WriteString("name", category.NameIn(words));
            json.WriteString("ink", JsonWrite.Name(category.Ink));
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
        JsonWrite.Texts(json, "overlays", point.Overlays.Select(JsonWrite.Name));
        json.WriteNumber("tempoPercent", point.TempoPercent);
        json.WriteString("hands", point.Hands is { } hands ? JsonWrite.Name(hands) : "both");
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
                JsonWrite.Texts(json, "steps", variation.Steps.Select(static step => step.Operation));
            }

            json.WriteString("createdAt", idea.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>
    /// The Check flags (0018 section 9) in bar order, each with its reason's short name and explanation in the piece's
    /// language and the span of its bar (null for a bar the piece does not reach), and where the doubts come from: the
    /// scan's or the transcription's engine. A recording's notes whose length is uncertain come apart as "lengths" (#352,
    /// contract 11): one entry per bar with its notes, its reasons' names and explanations; empty for any other piece. A scan
    /// with the server's page reports (version 0.4) tells its "pages" (<see cref="ScanPages"/>); empty for any other piece.
    /// </summary>
    public static void Check(Utf8JsonWriter json, Project project, Words words, MeterMap meter, Tick end, IReadOnlyList<UncertainLengths> lengths, IReadOnlyList<ScanPage> pages)
    {
        json.WriteStartArray("flags");
        foreach (CheckFlag flag in (project.Check ?? []).OrderBy(flag => flag.Bar))
        {
            json.WriteStartObject();
            json.WriteNumber("bar", flag.Bar);
            json.WriteString("source", JsonWrite.Name(flag.Source));
            json.WriteString("reason", JsonWrite.Name(flag.Reason));
            json.WriteString("name", CheckFlag.NameOf(flag.Reason, words));
            json.WriteString("text", flag.Describe(words));
            json.WriteBoolean("resolved", flag.Resolved);
            BarSpan(json, flag.Bar, meter, end);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteNumber("open", project.OpenChecks.Count);
        Lengths(json, lengths, words, meter, end);
        ScanPages(json, pages, project.Scan?.ConfirmedBars ?? []);
        json.WriteString("origin", project.Scan is not null ? "scanned" : project.Transcription is not null ? "transcribed" : "imported");
        if ((project.Scan?.Engine ?? project.Transcription?.Engine) is { } engine)
        {
            json.WriteString("engine", engine);
        }
        else
        {
            json.WriteNull("engine");
        }
    }

    /// <summary>"lengths": a recording's bars with notes of uncertain length (#352), each with its notes, reasons, their names and explanations.</summary>
    private static void Lengths(Utf8JsonWriter json, IReadOnlyList<UncertainLengths> lengths, Words words, MeterMap meter, Tick end)
    {
        json.WriteStartArray("lengths");
        foreach (UncertainLengths bar in lengths)
        {
            json.WriteStartObject();
            json.WriteNumber("bar", bar.Bar);
            JsonWrite.Texts(json, "noteIds", bar.NoteIds);
            JsonWrite.Texts(json, "reasons", bar.Reasons.Select(JsonWrite.Name));
            JsonWrite.Texts(json, "names", bar.Reasons.Select(reason => HeardLengths.NameOf(reason, words)));
            JsonWrite.Texts(json, "texts", bar.Reasons.Select(reason => HeardLengths.Explain(reason, words)));
            BarSpan(json, bar.Bar, meter, end);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>"span": the bar's span, or null for a bar the piece does not reach.</summary>
    private static void BarSpan(Utf8JsonWriter json, int bar, MeterMap meter, Tick end)
    {
        if (bar >= meter.FirstBarNumber && meter.GetBar(bar).Span is { } span && span.Onset < end)
        {
            Span(json, "span", span);
        }
        else
        {
            json.WriteNull("span");
        }
    }

    /// <summary>The settings catalogue (0018, section 10): every setting with its choices, default, preset values and scope.</summary>
    public static void SettingsCatalogue(Utf8JsonWriter json)
    {
        json.WriteStartArray("settings");
        foreach (Setting setting in Settings.SettingsCatalogue.All)
        {
            json.WriteStartObject();
            json.WriteString("id", setting.Id);
            json.WriteString("section", JsonWrite.Name(setting.Section));
            json.WriteString("kind", JsonWrite.Name(setting.Kind));
            JsonWrite.Texts(json, "choices", setting.Choices);
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

            json.WriteString("scope", JsonWrite.Name(setting.Scope));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        JsonWrite.Texts(json, "presets", [JsonWrite.Name(Preset.Learning), JsonWrite.Name(Preset.Practising), JsonWrite.Name(Preset.Clean)]);
    }

    public static void SettingsState(Utf8JsonWriter json, SettingsState state)
    {
        json.WriteString("preset", JsonWrite.Name(state.Preset));
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

    /// <summary>
    /// The title, composer, tags and playlists a library card shows (contract 12): "title" and "titleFrom" ("player",
    /// "file" or "fileName"), "composer" and "composerFrom" ("player" or "file"; both null when neither names one), "tags"
    /// and "playlists".
    /// </summary>
    public static void Details(Utf8JsonWriter json, PieceDetails details)
    {
        json.WriteString("title", details.Title.Value);
        json.WriteString("titleFrom", From(details.Title.Attribution));
        if (details.Composer is { } composer)
        {
            json.WriteString("composer", composer.Value);
            json.WriteString("composerFrom", From(composer.Attribution));
        }
        else
        {
            json.WriteNull("composer");
            json.WriteNull("composerFrom");
        }

        JsonWrite.Texts(json, "tags", details.Tags);
        JsonWrite.Texts(json, "playlists", details.Playlists);
    }

    private static string From(Events.Attribution attribution) => attribution.Provenance switch
    {
        Events.Provenance.UserEdited => "player",
        Events.Provenance.Imported => "file",
        _ => "fileName",
    };

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