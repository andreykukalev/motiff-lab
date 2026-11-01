using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Settings;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The project file: one JSON document that holds a project, used both for autosave on the device
/// and for export and re-import as a backup (S1-P1, AC-S1-20).
/// </summary>
/// <remarks>
/// Written by hand rather than by reflection, so the layout is fixed and the same project always
/// gives the same bytes. Enums are written by name. Reading checks every field and throws
/// <see cref="ProjectFileException"/> with a plain reason; it never half-reads a project.
/// </remarks>
public static class ProjectFile
{
    public const string FormatName = "motiff-lab-project";

    /// <summary>
    /// 12 keeps what the redesign keeps with the piece (0018): the marks (ink, margin notes, highlights, fingering), the
    /// bookmarks and their categories, the resume points that replace the sessions, the Check flags, the piece's settings
    /// overrides and the fact each idea came from;
    /// 11 adds the beats of a transcription's tempo, which follows the playing, and how many 16ths each is written as (0016);
    /// 10 adds the bars and tempo worked out for a transcription (0016);
    /// 9 marks a project heard in an audio or video recording by transcription (0014);
    /// 8 adds the player's work sessions and marks each observation with the session it was kept in (0012);
    /// 7 marks a project read from a photo or PDF, with the bars the player checked (0011);
    /// 6 adds the player's observations of phrases (#78); 5 the player's phrase boundaries;
    /// 4 saves the melody operations (V4, V9, V10), Accompaniment pattern (V2), Articulation (V13)
    /// and each step's scope; 3 adds
    /// the edit log of note corrections; 2 saves each variation as a chain of steps. Files of earlier
    /// versions still open: before 12 there are no marks, bookmarks or overrides, the sessions open as resume points named by
    /// their task, the ideas lose their session, and the Check flags are worked out when the project opens, resolved where
    /// the player confirmed a scanned bar; the bars of version 10 keep one tempo, versions before 10 keep a transcription's bars as the engine wrote them, versions before 9 were never transcribed, versions before 8 have no sessions, versions before 7 were never scanned, versions before 6 have no observations, steps of versions before 4
    /// change the whole selection, versions 1 and 2 have no corrections, and version 1 holds one
    /// step per variation.
    /// </summary>
    public const int Version = 12;

    /// <summary>The extension an exported project gets.</summary>
    public const string Extension = ".motiff.json";

    public static byte[] Write(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("format", FormatName);
            json.WriteNumber("version", Version);
            json.WriteString("id", project.Id);
            json.WriteString("fileName", project.FileName);
            json.WriteString("createdAt", project.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteString("lastOpened", project.LastOpened.ToString("O", CultureInfo.InvariantCulture));
            json.WriteBase64String("source", project.Source);

            json.WriteStartArray("motifs");
            foreach (SavedMotif motif in project.Motifs)
            {
                json.WriteStartObject();
                json.WriteString("id", motif.Id);
                json.WriteString("name", motif.Name);
                json.WriteString("kind", motif.Kind.ToString());
                json.WriteString("mode", motif.Mode.ToString());
                WriteRange(json, motif.Span, motif.Filter);
                WriteNoteIds(json, motif.NoteIds);
                json.WriteEndObject();
            }

            json.WriteEndArray();

            json.WriteStartArray("variations");
            foreach (SavedVariation variation in project.Variations)
            {
                json.WriteStartObject();
                json.WriteString("id", variation.Id);
                json.WriteString("motifId", variation.MotifId);
                WriteChain(json, variation.Steps, variation.Pending, variation.Keep);
                json.WriteEndObject();
            }

            json.WriteEndArray();

            json.WriteStartArray("observations");
            foreach (Observation observation in project.Observations)
            {
                WriteObservation(json, observation);
            }

            json.WriteEndArray();

            json.WriteStartArray("edits");
            foreach (NoteEdit edit in project.Edits.Edits)
            {
                json.WriteStartObject();
                json.WriteString("noteId", edit.NoteId);
                WriteValues(json, "before", edit.Before);
                if (edit.After is { } after)
                {
                    WriteValues(json, "after", after);
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();

            json.WriteStartObject("phrases");
            WriteTicks(json, "added", project.Phrases.Added);
            WriteTicks(json, "removed", project.Phrases.Removed);
            json.WriteStartObject("short");
            WriteTicks(json, "added", project.ShortPhrases.Added);
            WriteTicks(json, "removed", project.ShortPhrases.Removed);
            json.WriteEndObject();
            json.WriteStartObject("sentences");
            WriteTicks(json, "added", project.Sentences.Added);
            WriteTicks(json, "removed", project.Sentences.Removed);
            json.WriteEndObject();
            json.WriteEndObject();

            json.WriteStartObject("marks");
            json.WriteStartArray("ink");
            foreach (InkStroke stroke in project.Marks.Ink)
            {
                WriteStroke(json, stroke);
            }

            json.WriteEndArray();
            json.WriteStartArray("notes");
            foreach (MarginNote note in project.Marks.Notes)
            {
                json.WriteStartObject();
                json.WriteString("id", note.Id);
                WriteSpan(json, note.Span);
                json.WriteString("text", note.Text);
                json.WriteString("createdAt", note.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("highlights");
            foreach (Highlight highlight in project.Marks.Highlights)
            {
                json.WriteStartObject();
                json.WriteString("id", highlight.Id);
                WriteSpan(json, highlight.Span);
                WriteNoteIds(json, highlight.NoteIds);
                json.WriteString("colour", highlight.Colour.ToString());
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("fingering");
            foreach (Fingering fingering in project.Marks.Fingering)
            {
                json.WriteStartObject();
                json.WriteString("noteId", fingering.NoteId);
                json.WriteNumber("finger", fingering.Finger);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();

            json.WriteStartObject("bookmarks");
            json.WriteStartArray("categories");
            foreach (BookmarkCategory category in project.Categories)
            {
                json.WriteStartObject();
                json.WriteString("id", category.Id);
                if (category.Name is { } categoryName)
                {
                    json.WriteString("name", categoryName);
                }

                json.WriteString("ink", category.Ink.ToString());
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("items");
            foreach (Bookmark bookmark in project.Bookmarks)
            {
                json.WriteStartObject();
                json.WriteString("id", bookmark.Id);
                json.WriteString("title", bookmark.Title);
                json.WriteString("category", bookmark.CategoryId);
                WriteSpan(json, bookmark.Span);
                json.WriteBoolean("fromPhrase", bookmark.FromPhrase);
                json.WriteString("createdAt", bookmark.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();

            json.WriteStartArray("resumePoints");
            foreach (ResumePoint point in project.ResumePoints)
            {
                json.WriteStartObject();
                json.WriteString("id", point.Id);
                if (point.Name is { } pointName)
                {
                    json.WriteString("name", pointName);
                }

                json.WriteNumber("at", point.At.Value);
                if (point.Passage is { } passage)
                {
                    json.WriteStartObject("passage");
                    WriteRange(json, passage.Span, passage.Filter);
                    json.WriteEndObject();
                }

                json.WriteBoolean("focused", point.Focused);
                json.WriteStartArray("overlays");
                foreach (Overlay overlay in point.Overlays)
                {
                    json.WriteStringValue(overlay.ToString());
                }

                json.WriteEndArray();
                json.WriteNumber("tempoPercent", point.TempoPercent);
                if (point.Hands is { } hands)
                {
                    json.WriteString("hands", hands.ToString());
                }

                json.WriteBoolean("loop", point.Loop);
                json.WriteString("createdAt", point.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
                json.WriteString("lastActive", point.LastActive.ToString("O", CultureInfo.InvariantCulture));
                json.WriteEndObject();
            }

            json.WriteEndArray();

            if (project.Check is { } check)
            {
                json.WriteStartArray("check");
                foreach (CheckFlag flag in check)
                {
                    json.WriteStartObject();
                    json.WriteNumber("bar", flag.Bar);
                    json.WriteString("source", flag.Source.ToString());
                    json.WriteString("reason", flag.Reason.ToString());
                    json.WriteBoolean("resolved", flag.Resolved);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }
            else
            {
                json.WriteNull("check");
            }

            json.WriteStartObject("settings");
            foreach ((string id, string value) in project.SettingOverrides.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                json.WriteString(id, value);
            }

            json.WriteEndObject();

            if (project.Scan is { } scan)
            {
                json.WriteStartObject("scan");
                json.WriteString("engine", scan.Engine);
                json.WriteStartArray("confirmedBars");
                foreach (int bar in scan.ConfirmedBars)
                {
                    json.WriteNumberValue(bar);
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("scan");
            }

            if (project.Transcription is { } transcription)
            {
                json.WriteStartObject("transcription");
                json.WriteString("engine", transcription.Engine);
                if (transcription.Bars is { } bars)
                {
                    json.WriteStartObject("bars");
                    json.WriteNumber("origin", bars.Origin.Value);
                    json.WriteNumber("sixteenthMicroseconds", bars.SixteenthMicroseconds);
                    json.WriteString("signature", bars.Signature.ToString());
                    json.WriteStartArray("barStarts");
                    foreach (int start in bars.BarStarts)
                    {
                        json.WriteNumberValue(start);
                    }

                    json.WriteEndArray();
                    json.WriteNumber("stepsPerBeat", bars.StepsPerBeat);
                    json.WriteStartArray("beats");
                    foreach (long beat in bars.Beats)
                    {
                        json.WriteNumberValue(beat);
                    }

                    json.WriteEndArray();
                    json.WriteEndObject();
                }
                else
                {
                    json.WriteNull("bars");
                }

                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("transcription");
            }

            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public static Project Read(ReadOnlySpan<byte> data)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(data.ToArray());
        }
        catch (JsonException)
        {
            throw new ProjectFileException("This is not a Motiff Lab project file: it is not valid JSON.");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out JsonElement format) || format.ValueKind != JsonValueKind.String || format.GetString() != FormatName)
            {
                throw new ProjectFileException("This is not a Motiff Lab project file.");
            }

            int version = Integer(root, "version");
            if (version > Version)
            {
                throw new ProjectFileException("This project was saved by a newer version of Motiff Lab. Update the app and try again.");
            }

            if (version < 1)
            {
                throw Damaged("version");
            }

            byte[] source;
            try
            {
                source = Property(root, "source", JsonValueKind.String).GetBytesFromBase64();
            }
            catch (FormatException)
            {
                throw Damaged("source");
            }

            var motifs = Property(root, "motifs", JsonValueKind.Array).EnumerateArray().Select(ReadMotif).ToList();
            var variations = Property(root, "variations", JsonValueKind.Array).EnumerateArray().Select(variation => ReadVariation(variation, version)).ToList();
            List<Observation> observations = version >= 6
                ? Property(root, "observations", JsonValueKind.Array).EnumerateArray().Select(observation => ReadObservation(observation, version)).ToList()
                : [];
            if (observations.Select(static observation => observation.Id).Distinct(StringComparer.Ordinal).Count() != observations.Count)
            {
                throw Damaged("observations");
            }

            NoteEditLog edits = version >= 3
                ? new NoteEditLog(Property(root, "edits", JsonValueKind.Array).EnumerateArray().Select(ReadEdit))
                : NoteEditLog.Empty;
            PhraseEdits phrases = version >= 5 ? ReadPhrases(Property(root, "phrases", JsonValueKind.Object)) : PhraseEdits.None;
            PhraseEdits shortPhrases = version >= 12 ? ReadPhraseLevel(Property(root, "phrases", JsonValueKind.Object), "short") : PhraseEdits.None;
            PhraseEdits sentences = version >= 12 ? ReadPhraseLevel(Property(root, "phrases", JsonValueKind.Object), "sentences") : PhraseEdits.None;
            ScanOrigin? scan = version >= 7 ? ReadScan(root) : null;
            TranscriptionOrigin? transcription = version >= 9 ? ReadTranscription(root, version) : null;
            if (scan is not null && transcription is not null)
            {
                throw Damaged("origin");
            }

            List<ResumePoint> resumePoints = version >= 12
                ? Property(root, "resumePoints", JsonValueKind.Array).EnumerateArray().Select(ReadResumePoint).ToList()
                : version >= 8 ? Property(root, "sessions", JsonValueKind.Array).EnumerateArray().Select(ReadSessionAsResumePoint).ToList() : [];
            if (resumePoints.Select(static point => point.Id).Distinct(StringComparer.Ordinal).Count() != resumePoints.Count)
            {
                throw Damaged("resume points");
            }

            Marks marks = version >= 12 ? ReadMarks(Property(root, "marks", JsonValueKind.Object)) : Marks.Empty;
            (IReadOnlyList<BookmarkCategory> categories, IReadOnlyList<Bookmark> bookmarks) = version >= 12
                ? ReadBookmarks(Property(root, "bookmarks", JsonValueKind.Object))
                : (BookmarkCategory.BuiltIn, []);
            IReadOnlyList<CheckFlag>? check = version >= 12 ? ReadCheck(root) : null;
            IReadOnlyDictionary<string, string> overrides = version >= 12 ? ReadSettings(Property(root, "settings", JsonValueKind.Object)) : new Dictionary<string, string>(StringComparer.Ordinal);

            return new Project(
                Text(root, "id"),
                Text(root, "fileName"),
                source,
                Time(root, "createdAt"),
                Time(root, "lastOpened"),
                motifs,
                variations)
            {
                Edits = edits,
                Phrases = phrases,
                ShortPhrases = shortPhrases,
                Sentences = sentences,
                Observations = observations,
                Scan = scan,
                Transcription = transcription,
                ResumePoints = resumePoints,
                Marks = marks,
                Categories = categories,
                Bookmarks = bookmarks,
                Check = check,
                SettingOverrides = overrides,
            };
        }
    }

    /// <summary>
    /// The ink strokes as the pencil layer saves and reads them: {"version": 2, "marks": [stroke, …]}, each stroke in the
    /// shape <see cref="InkStroke"/> describes, with the tool and the colour in lower case. The same shape the project
    /// file holds under "marks": "ink", so the layer and the file never translate.
    /// </summary>
    public static string WriteInk(IReadOnlyList<InkStroke> ink)
    {
        ArgumentNullException.ThrowIfNull(ink);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("version", 2);
            json.WriteStartArray("marks");
            foreach (InkStroke stroke in ink)
            {
                WriteStroke(json, stroke);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The strokes in a document <see cref="WriteInk"/> wrote; nothing, or an unreadable document, gives none.</summary>
    /// <exception cref="ProjectFileException">A stroke is not whole.</exception>
    public static IReadOnlyList<InkStroke> ReadInk(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw Damaged("ink");
        }

        using (document)
        {
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? Property(document.RootElement, "marks", JsonValueKind.Array).EnumerateArray().Select(ReadStroke).ToList()
                : throw Damaged("ink");
        }
    }

    private static void WriteStroke(Utf8JsonWriter json, InkStroke stroke)
    {
        json.WriteStartObject();
        json.WriteString("id", stroke.Id);
        json.WriteString("tool", Lower(stroke.Tool));
        json.WriteString("colour", Lower(stroke.Colour));
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

    /// <summary>One stroke in the pencil layer's shape; the facade (0020) reads a request's strokes with it.</summary>
    internal static InkStroke ReadStroke(JsonElement stroke)
    {
        Expect(stroke, JsonValueKind.Object, "ink");
        long bar = Long(stroke, "bar");
        double[] points = [.. Property(stroke, "points", JsonValueKind.Array)
            .EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.Number ? value.GetDouble() : throw Damaged("ink points"))];
        try
        {
            return new InkStroke(
                Text(stroke, "id"),
                LowerName<InkTool>(stroke, "tool"),
                LowerName<InkColour>(stroke, "colour"),
                Number(stroke, "size"),
                bar >= 0 ? new Tick(bar) : throw Damaged("ink bar"),
                Number(stroke, "u"),
                Number(stroke, "v"),
                points,
                Number(stroke, "pressure"));
        }
        catch (ArgumentException)
        {
            throw Damaged("ink");
        }
    }

    private static Marks ReadMarks(JsonElement marks)
    {
        List<InkStroke> ink = Property(marks, "ink", JsonValueKind.Array).EnumerateArray().Select(ReadStroke).ToList();
        List<MarginNote> notes = [];
        foreach (JsonElement note in Property(marks, "notes", JsonValueKind.Array).EnumerateArray())
        {
            Expect(note, JsonValueKind.Object, "notes");
            try
            {
                notes.Add(new MarginNote(Text(note, "id"), ReadSpan(note, "note"), Text(note, "text"), Time(note, "createdAt")));
            }
            catch (ArgumentException)
            {
                throw Damaged("notes");
            }
        }

        List<Highlight> highlights = [];
        foreach (JsonElement highlight in Property(marks, "highlights", JsonValueKind.Array).EnumerateArray())
        {
            Expect(highlight, JsonValueKind.Object, "highlights");
            try
            {
                highlights.Add(new Highlight(Text(highlight, "id"), ReadSpan(highlight, "highlight"), ReadNoteIds(highlight, "highlight notes"), Name<InkColour>(highlight, "colour")));
            }
            catch (ArgumentException)
            {
                throw Damaged("highlights");
            }
        }

        List<Fingering> fingering = [];
        foreach (JsonElement finger in Property(marks, "fingering", JsonValueKind.Array).EnumerateArray())
        {
            Expect(finger, JsonValueKind.Object, "fingering");
            int digit = Integer(finger, "finger");
            fingering.Add(digit is >= 1 and <= 5 ? new Fingering(Text(finger, "noteId"), digit) : throw Damaged("fingering"));
        }

        try
        {
            Marks read = Marks.Empty.WithInk(ink) with { Notes = notes, Highlights = highlights, Fingering = fingering };
            return notes.Select(static note => note.Id).Concat(highlights.Select(static highlight => highlight.Id)).Distinct(StringComparer.Ordinal).Count() == notes.Count + highlights.Count
                && fingering.Select(static finger => finger.NoteId).Distinct(StringComparer.Ordinal).Count() == fingering.Count
                ? read
                : throw Damaged("marks");
        }
        catch (ArgumentException)
        {
            throw Damaged("marks");
        }
    }

    private static (IReadOnlyList<BookmarkCategory> Categories, IReadOnlyList<Bookmark> Bookmarks) ReadBookmarks(JsonElement bookmarks)
    {
        List<BookmarkCategory> categories = [];
        foreach (JsonElement category in Property(bookmarks, "categories", JsonValueKind.Array).EnumerateArray())
        {
            Expect(category, JsonValueKind.Object, "bookmark categories");
            try
            {
                categories.Add(new BookmarkCategory(Text(category, "id"), category.TryGetProperty("name", out _) ? Text(category, "name") : null, Name<InkColour>(category, "ink")));
            }
            catch (ArgumentException)
            {
                throw Damaged("bookmark categories");
            }
        }

        // The four built-in categories are always there, under their own name when the file does not name them.
        for (int i = 0; i < BookmarkCategory.BuiltIn.Count; i++)
        {
            BookmarkCategory builtIn = BookmarkCategory.BuiltIn[i];
            if (categories.All(category => category.Id != builtIn.Id))
            {
                categories.Insert(Math.Min(i, categories.Count), builtIn);
            }
        }

        if (categories.Select(static category => category.Id).Distinct(StringComparer.Ordinal).Count() != categories.Count)
        {
            throw Damaged("bookmark categories");
        }

        List<Bookmark> items = [];
        foreach (JsonElement bookmark in Property(bookmarks, "items", JsonValueKind.Array).EnumerateArray())
        {
            Expect(bookmark, JsonValueKind.Object, "bookmarks");
            try
            {
                items.Add(new Bookmark(Text(bookmark, "id"), Text(bookmark, "title"), Text(bookmark, "category"), ReadSpan(bookmark, "bookmark"), Boolean(bookmark, "fromPhrase"), Time(bookmark, "createdAt")));
            }
            catch (ArgumentException)
            {
                throw Damaged("bookmarks");
            }
        }

        return items.Select(static bookmark => bookmark.Id).Distinct(StringComparer.Ordinal).Count() == items.Count
            && items.All(bookmark => categories.Any(category => category.Id == bookmark.CategoryId))
            ? (categories, items)
            : throw Damaged("bookmarks");
    }

    private static ResumePoint ReadResumePoint(JsonElement point)
    {
        Expect(point, JsonValueKind.Object, "resume points");
        SavedPassage? passage = null;
        if (point.TryGetProperty("passage", out _))
        {
            (TickSpan span, SelectionFilter filter) = ReadRange(Property(point, "passage", JsonValueKind.Object), "resume point passage");
            passage = new SavedPassage(span, filter);
        }

        long at = Long(point, "at");
        List<Overlay> overlays = Property(point, "overlays", JsonValueKind.Array)
            .EnumerateArray()
            .Select(overlay => overlay.ValueKind == JsonValueKind.String && TryName(overlay.GetString(), out Overlay value) ? value : throw Damaged("resume point overlays"))
            .ToList();
        try
        {
            return new ResumePoint(
                Text(point, "id"),
                point.TryGetProperty("name", out _) ? Text(point, "name") : null,
                at >= 0 ? new Tick(at) : throw Damaged("resume point"),
                passage,
                Boolean(point, "focused"),
                overlays,
                Integer(point, "tempoPercent"),
                point.TryGetProperty("hands", out _) ? Name<Hand>(point, "hands") : null,
                Boolean(point, "loop"),
                Time(point, "createdAt"),
                Time(point, "lastActive"));
        }
        catch (ArgumentException)
        {
            throw Damaged("resume point");
        }
    }

    /// <summary>A session of versions 8 to 11 as a resume point: its task is the name, its passage the place, "s_0001" becomes "r_0001".</summary>
    private static ResumePoint ReadSessionAsResumePoint(JsonElement session)
    {
        Expect(session, JsonValueKind.Object, "sessions");
        SavedPassage? passage = null;
        if (session.TryGetProperty("passage", out _))
        {
            (TickSpan span, SelectionFilter filter) = ReadRange(Property(session, "passage", JsonValueKind.Object), "session passage");
            passage = new SavedPassage(span, filter);
        }

        string id = Text(session, "id");
        string? task = session.TryGetProperty("task", out _) ? Text(session, "task") : null;
        try
        {
            return ResumePoint.Start(id.StartsWith("s_", StringComparison.Ordinal) ? "r_" + id[2..] : id, Time(session, "startedAt"), task)
                .Touch(passage, Time(session, "lastActive"));
        }
        catch (ArgumentException)
        {
            throw Damaged("session");
        }
    }

    private static IReadOnlyList<CheckFlag>? ReadCheck(JsonElement root)
    {
        if (!root.TryGetProperty("check", out JsonElement check) || check.ValueKind is not (JsonValueKind.Null or JsonValueKind.Array))
        {
            throw Damaged("check");
        }

        if (check.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        List<CheckFlag> flags = [];
        foreach (JsonElement flag in check.EnumerateArray())
        {
            Expect(flag, JsonValueKind.Object, "check");
            int bar = Integer(flag, "bar");
            flags.Add(bar >= 0 ? new CheckFlag(bar, Name<CheckSource>(flag, "source"), Name<CheckReason>(flag, "reason"), Boolean(flag, "resolved")) : throw Damaged("check"));
        }

        return flags;
    }

    private static IReadOnlyDictionary<string, string> ReadSettings(JsonElement settings)
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in settings.EnumerateObject())
        {
            Setting setting = SettingsCatalogue.Find(property.Name) ?? throw Damaged("settings");
            string value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : throw Damaged("settings");
            if (setting.Scope != SettingScope.Piece || !setting.Allows(value))
            {
                throw Damaged("settings");
            }

            overrides[property.Name] = value;
        }

        return overrides;
    }

    private static void WriteSpan(Utf8JsonWriter json, TickSpan span)
    {
        json.WriteNumber("onset", span.Onset.Value);
        json.WriteNumber("duration", span.Duration);
    }

    private static TickSpan ReadSpan(JsonElement parent, string what)
    {
        long onset = Long(parent, "onset");
        long duration = Long(parent, "duration");
        return onset >= 0 && duration > 0 ? new TickSpan(new Tick(onset), duration) : throw Damaged($"{what} range");
    }

    private static SavedMotif ReadMotif(JsonElement motif)
    {
        Expect(motif, JsonValueKind.Object, "motifs");
        (TickSpan span, SelectionFilter filter) = ReadRange(motif, "motif");
        IReadOnlyList<string> noteIds = ReadNoteIds(motif, "motif notes");
        string name = Text(motif, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw Damaged("motif name");
        }

        return new SavedMotif(
            Text(motif, "id"),
            name,
            Name<MotifSelectionKind>(motif, "kind"),
            Name<MotifMode>(motif, "mode"),
            span,
            filter,
            noteIds);
    }

    private static void WriteTicks(Utf8JsonWriter json, string name, IEnumerable<Tick> ticks)
    {
        json.WriteStartArray(name);
        foreach (Tick tick in ticks)
        {
            json.WriteNumberValue(tick.Value);
        }

        json.WriteEndArray();
    }

    private static void WriteRange(Utf8JsonWriter json, TickSpan span, SelectionFilter filter)
    {
        json.WriteNumber("onset", span.Onset.Value);
        json.WriteNumber("duration", span.Duration);
        json.WriteStartObject("filter");
        if (filter.Hand is Hand hand)
        {
            json.WriteString("hand", hand.ToString());
        }

        if (filter.Track is int track)
        {
            json.WriteNumber("track", track);
        }

        if (filter.Voice is int voice)
        {
            json.WriteNumber("voice", voice);
        }

        json.WriteEndObject();
    }

    /// <summary>A range and its filter, written by <see cref="WriteRange"/>; <paramref name="what"/> names it in a damaged file's reason.</summary>
    private static (TickSpan Span, SelectionFilter Filter) ReadRange(JsonElement parent, string what)
    {
        long onset = Long(parent, "onset");
        long duration = Long(parent, "duration");
        if (onset < 0 || duration <= 0)
        {
            throw Damaged($"{what} range");
        }

        JsonElement filter = Property(parent, "filter", JsonValueKind.Object);
        Hand? hand = filter.TryGetProperty("hand", out _) ? Name<Hand>(filter, "hand") : null;
        int? track = filter.TryGetProperty("track", out _) ? Integer(filter, "track") : null;
        int? voice = filter.TryGetProperty("voice", out _) ? Integer(filter, "voice") : null;
        try
        {
            return (new TickSpan(new Tick(onset), duration), (hand, track, voice) switch
            {
                (null, null, null) => SelectionFilter.All,
                ({ } h, null, null) => SelectionFilter.ForHand(h),
                (null, { } t, null) => SelectionFilter.ForTrack(t),
                (null, { } t, { } v) => SelectionFilter.ForVoice(t, v),
                _ => throw Damaged($"{what} filter"),
            });
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Damaged($"{what} filter");
        }
    }

    private static void WriteNoteIds(Utf8JsonWriter json, IReadOnlyList<string> noteIds)
    {
        json.WriteStartArray("noteIds");
        foreach (string id in noteIds)
        {
            json.WriteStringValue(id);
        }

        json.WriteEndArray();
    }

    private static PhraseEdits ReadPhrases(JsonElement phrases)
    {
        Tick[] Ticks(string name)
        {
            Tick[] ticks = [.. Property(phrases, name, JsonValueKind.Array)
                .EnumerateArray()
                .Select(tick => tick.ValueKind == JsonValueKind.Number && tick.TryGetInt64(out long value) && value > 0 ? new Tick(value) : throw Damaged("phrase boundaries"))];
            return ticks.Distinct().Order().SequenceEqual(ticks) ? ticks : throw Damaged("phrase boundaries");
        }

        return new PhraseEdits(Ticks("added"), Ticks("removed"));
    }

    /// <summary>The edits of the Short or the Sentence level inside "phrases" (0018; written since the engine package, read as none when absent).</summary>
    private static PhraseEdits ReadPhraseLevel(JsonElement phrases, string name) =>
        phrases.TryGetProperty(name, out JsonElement level) && level.ValueKind == JsonValueKind.Object ? ReadPhrases(level) : PhraseEdits.None;

    private static ScanOrigin? ReadScan(JsonElement root)
    {
        if (!root.TryGetProperty("scan", out JsonElement scan) || scan.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object))
        {
            throw Damaged("scan");
        }

        if (scan.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        int[] bars = [.. Property(scan, "confirmedBars", JsonValueKind.Array)
            .EnumerateArray()
            .Select(bar => bar.ValueKind == JsonValueKind.Number && bar.TryGetInt32(out int value) && value >= 0 ? value : throw Damaged("checked bars"))];
        return new ScanOrigin(Text(scan, "engine"), bars);
    }

    private static TranscriptionOrigin? ReadTranscription(JsonElement root, int version)
    {
        if (!root.TryGetProperty("transcription", out JsonElement transcription) || transcription.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object))
        {
            throw Damaged("transcription");
        }

        if (transcription.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        string engine = Text(transcription, "engine");
        if (string.IsNullOrWhiteSpace(engine))
        {
            throw Damaged("engine");
        }

        return new TranscriptionOrigin(engine, version >= 10 ? ReadBars(transcription, version) : null);
    }

    private static BarGrid? ReadBars(JsonElement transcription, int version)
    {
        if (!transcription.TryGetProperty("bars", out JsonElement bars) || bars.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object))
        {
            throw Damaged("bars");
        }

        if (bars.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        string[] signature = Text(bars, "signature").Split('/');
        TimeSignature? meter = signature.Length == 2 && int.TryParse(signature[0], NumberStyles.None, CultureInfo.InvariantCulture, out int numerator)
            && int.TryParse(signature[1], NumberStyles.None, CultureInfo.InvariantCulture, out int denominator)
            ? BarGrid.Signatures.FirstOrDefault(offered => offered.Numerator == numerator && offered.Denominator == denominator)
            : null;
        int[] starts = [.. Property(bars, "barStarts", JsonValueKind.Array)
            .EnumerateArray()
            .Select(start => start.ValueKind == JsonValueKind.Number && start.TryGetInt32(out int value) ? value : throw Damaged("bars"))];
        long origin = Long(bars, "origin");
        long[] beats = version >= 11
            ? [.. Property(bars, "beats", JsonValueKind.Array)
                .EnumerateArray()
                .Select(beat => beat.ValueKind == JsonValueKind.Number && beat.TryGetInt64(out long value) ? value : throw Damaged("bars"))]
            : [];
        int perBeat = version >= 11 ? Integer(bars, "stepsPerBeat") : 2;
        if (meter is not { } chosen || !BarGrid.Signatures.Contains(chosen) || origin < 0)
        {
            throw Damaged("bars");
        }

        try
        {
            return new BarGrid(new Tick(origin), Integer(bars, "sixteenthMicroseconds"), chosen, starts, beats, perBeat);
        }
        catch (ArgumentException)
        {
            throw Damaged("bars");
        }
    }

    private static List<string> ReadNoteIds(JsonElement parent, string what) =>
        Property(parent, "noteIds", JsonValueKind.Array)
            .EnumerateArray()
            .Select(id => id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()) ? id.GetString()! : throw Damaged(what))
            .ToList();

    /// <summary>A chain's steps, the pending one and the Keep choices, as a variation and a kept variation save them.</summary>
    private static void WriteChain(Utf8JsonWriter json, IReadOnlyList<SavedStep> steps, SavedStep? pending, IReadOnlyList<KeepProperty> keep)
    {
        json.WriteStartArray("steps");
        foreach (SavedStep step in steps)
        {
            json.WriteStartObject();
            WriteStep(json, step);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        if (pending is not null)
        {
            json.WriteStartObject("pending");
            WriteStep(json, pending);
            json.WriteEndObject();
        }

        json.WriteStartArray("keep");
        foreach (KeepProperty property in keep)
        {
            json.WriteStringValue(property.ToString());
        }

        json.WriteEndArray();
    }

    private static List<KeepProperty> ReadKeep(JsonElement parent) =>
        Property(parent, "keep", JsonValueKind.Array)
            .EnumerateArray()
            .Select(property => property.ValueKind == JsonValueKind.String && TryName(property.GetString(), out KeepProperty value) ? value : throw Damaged("Keep choices"))
            .Distinct()
            .Order()
            .ToList();

    private static void WriteObservation(Utf8JsonWriter json, Observation observation)
    {
        json.WriteStartObject();
        json.WriteString("id", observation.Id);
        json.WriteString("name", observation.Name);
        if (observation.FactId is { } factId)
        {
            json.WriteString("factId", factId);
        }

        if (observation.Note is { } note)
        {
            json.WriteString("note", note);
        }

        json.WriteString("createdAt", observation.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        json.WriteStartObject("phrase");
        WriteRange(json, observation.Phrase.Span, observation.Phrase.Filter);
        json.WriteEndObject();
        if (observation is { ExperimentId: { } experiment, Alternative: { } alternative })
        {
            json.WriteStartObject("experiment");
            json.WriteString("id", experiment);
            json.WriteNumber("alternative", alternative);
            json.WriteEndObject();
        }

        if (observation.Variation is { } variation)
        {
            json.WriteStartObject("variation");
            WriteRange(json, variation.Passage.Span, variation.Passage.Filter);
            WriteNoteIds(json, variation.NoteIds);
            WriteChain(json, variation.Steps, variation.Pending, variation.Keep);
            if (variation.Key is { } key)
            {
                json.WriteStartObject("key");
                json.WriteNumber("fifths", key.Fifths);
                json.WriteString("mode", key.Mode.ToString());
                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        json.WriteEndObject();
    }

    private static Observation ReadObservation(JsonElement observation, int version)
    {
        Expect(observation, JsonValueKind.Object, "observations");
        (TickSpan phrase, SelectionFilter phraseFilter) = ReadRange(Property(observation, "phrase", JsonValueKind.Object), "observation phrase");
        (string Id, int Alternative)? experiment = null;
        if (observation.TryGetProperty("experiment", out _))
        {
            JsonElement tried = Property(observation, "experiment", JsonValueKind.Object);
            int alternative = Integer(tried, "alternative");
            experiment = alternative >= 0 ? (Text(tried, "id"), alternative) : throw Damaged("observation experiment");
        }

        KeptVariation? variation = observation.TryGetProperty("variation", out _) ? ReadKeptVariation(Property(observation, "variation", JsonValueKind.Object)) : null;
        string? note = observation.TryGetProperty("note", out _) ? Text(observation, "note") : null;
        string id = Text(observation, "id");
        string name = Text(observation, "name");
        DateTimeOffset createdAt = Time(observation, "createdAt");
        // Before version 12 an observation named the session it was kept in; sessions are gone, so the name is dropped.
        string? factId = version >= 12 && observation.TryGetProperty("factId", out _) ? Text(observation, "factId") : null;
        try
        {
            return Observation.Create(id, new SavedPassage(phrase, phraseFilter), variation, name, note, createdAt, experiment) with { FactId = factId };
        }
        catch (ArgumentException)
        {
            throw Damaged("observation");
        }
    }

    private static KeptVariation ReadKeptVariation(JsonElement variation)
    {
        (TickSpan span, SelectionFilter filter) = ReadRange(variation, "kept variation");
        List<string> noteIds = ReadNoteIds(variation, "kept variation notes");
        if (noteIds.Count == 0)
        {
            throw Damaged("kept variation notes");
        }

        List<SavedStep> steps = Property(variation, "steps", JsonValueKind.Array).EnumerateArray().Select(ReadStep).ToList();
        SavedStep? pending = variation.TryGetProperty("pending", out JsonElement step) ? ReadStep(step) : null;
        if (steps.Count == 0 && pending is null)
        {
            throw Damaged("kept variation steps");
        }

        MusicalKey? key = null;
        if (variation.TryGetProperty("key", out _))
        {
            JsonElement reading = Property(variation, "key", JsonValueKind.Object);
            int fifths = Integer(reading, "fifths");
            key = fifths is >= -7 and <= 7 ? new MusicalKey(fifths, Name<KeyMode>(reading, "mode")) : throw Damaged("kept variation key");
        }

        return new KeptVariation(new SavedPassage(span, filter), noteIds, steps, pending, ReadKeep(variation), key);
    }

    private static void WriteValues(Utf8JsonWriter json, string name, NoteValues values)
    {
        json.WriteStartObject(name);
        json.WriteNumber("pitch", values.Pitch.MidiNumber);
        json.WriteNumber("onset", values.Span.Onset.Value);
        json.WriteNumber("duration", values.Span.Duration);
        json.WriteNumber("velocity", values.Velocity);
        json.WriteEndObject();
    }

    private static NoteEdit ReadEdit(JsonElement edit)
    {
        Expect(edit, JsonValueKind.Object, "note corrections");
        return new NoteEdit(
            Text(edit, "noteId"),
            ReadValues(Property(edit, "before", JsonValueKind.Object)),
            edit.TryGetProperty("after", out JsonElement after) ? ReadValues(after) : null);
    }

    private static NoteValues ReadValues(JsonElement values, string what = "note corrections")
    {
        Expect(values, JsonValueKind.Object, what);
        int pitch = Integer(values, "pitch");
        long onset = Long(values, "onset");
        long duration = Long(values, "duration");
        int velocity = Integer(values, "velocity");
        if (pitch is < 0 or > Pitch.MaxMidiNumber || onset < 0 || duration <= 0 || velocity is < Note.MinVelocity or > Note.MaxVelocity)
        {
            throw Damaged(what);
        }

        return new NoteValues(new Pitch(pitch), new TickSpan(new Tick(onset), duration), velocity);
    }

    /// <summary>One variation step as the file writes it; the facade (0020) uses the same shape.</summary>
    internal static void WriteStep(Utf8JsonWriter json, SavedStep step)
    {
        json.WriteString("operation", step.Operation);
        json.WriteStartObject("parameters");
        switch (step.Parameters)
        {
            case RhythmReshapeParameters reshape:
                json.WriteString("kind", reshape.Kind.ToString());
                json.WriteNumber("eighths", reshape.Eighths);
                break;
            case ArticulationParameters articulation:
                json.WriteString("kind", articulation.Kind.ToString());
                break;
            case AccompanimentPatternParameters pattern:
                json.WriteString("kind", pattern.Kind.ToString());
                json.WriteString("unit", pattern.Unit.ToString());
                break;
            case MelodicSequenceParameters sequence:
                json.WriteNumber("steps", sequence.Steps);
                json.WriteNumber("repeats", sequence.Repeats);
                json.WriteBoolean("diatonic", sequence.Diatonic);
                break;
            case IntervalReshapeParameters reshape:
                json.WriteNumber("steps", reshape.Steps);
                WriteIntervals(json, reshape.Intervals);
                break;
            case ChangeDirectionParameters direction:
                json.WriteBoolean("diatonic", direction.Diatonic);
                WriteIntervals(json, direction.Intervals);
                break;
            case HandEditParameters edit:
                json.WriteString("kind", edit.Kind.ToString());
                json.WriteString("noteId", edit.NoteId);
                if (edit.Values is { } values)
                {
                    WriteValues(json, "values", values);
                }

                break;
            default:
                throw new ArgumentException($"Operation {step.Operation} has parameters the project file cannot write.", nameof(step));
        }

        if (step.Parameters.Scope != OperationScope.All)
        {
            json.WriteString("scope", step.Parameters.Scope.ToString());
        }

        json.WriteEndObject();
    }

    /// <summary>The picked intervals, numbered from 0; nothing when every interval is picked.</summary>
    private static void WriteIntervals(Utf8JsonWriter json, IntervalSet? intervals)
    {
        if (intervals is null)
        {
            return;
        }

        json.WriteStartArray("intervals");
        foreach (int index in intervals.Indices)
        {
            json.WriteNumberValue(index);
        }

        json.WriteEndArray();
    }

    private static SavedVariation ReadVariation(JsonElement variation, int version)
    {
        Expect(variation, JsonValueKind.Object, "variations");
        List<KeepProperty> keep = ReadKeep(variation);

        // Version 1 saved one step on the variation itself.
        List<SavedStep> steps = version == 1
            ? [ReadStep(variation)]
            : Property(variation, "steps", JsonValueKind.Array).EnumerateArray().Select(ReadStep).ToList();
        SavedStep? pending = version > 1 && variation.TryGetProperty("pending", out JsonElement step) ? ReadStep(step) : null;
        return new SavedVariation(
            Text(variation, "id"),
            Text(variation, "motifId"),
            steps,
            pending,
            keep);
    }

    /// <summary>One variation step as the file reads it: an object with "operation" and "parameters".</summary>
    internal static SavedStep ReadStep(JsonElement step)
    {
        Expect(step, JsonValueKind.Object, "variation steps");
        string operation = Text(step, "operation");
        JsonElement parameters = Property(step, "parameters", JsonValueKind.Object);
        OperationParameters read = operation switch
        {
            HandEdit.OperationId => ReadHandEdit(parameters),
            RhythmReshape.OperationId => ReadRhythmReshape(parameters),
            Articulation.OperationId => new ArticulationParameters(Name<ArticulationKind>(parameters, "kind")),
            AccompanimentPattern.OperationId => new AccompanimentPatternParameters(Name<AccompanimentPatternKind>(parameters, "kind"), Name<PatternUnit>(parameters, "unit")),
            MelodicSequence.OperationId => ReadMelodicSequence(parameters),
            IntervalReshape.OperationId => ReadIntervalReshape(parameters),
            ChangeDirection.OperationId => new ChangeDirectionParameters(Boolean(parameters, "diatonic"), ReadIntervals(parameters)),
            _ => throw new ProjectFileException($"A variation uses an operation ({operation}) this version of Motiff Lab does not have."),
        };
        OperationScope scope = parameters.TryGetProperty("scope", out _) ? Name<OperationScope>(parameters, "scope") : OperationScope.All;
        return new SavedStep(operation, read with { Scope = scope });
    }

    private static HandEditParameters ReadHandEdit(JsonElement parameters)
    {
        HandEditKind kind = Name<HandEditKind>(parameters, "kind");
        NoteValues? values = kind == HandEditKind.Remove ? null : ReadValues(Property(parameters, "values", JsonValueKind.Object), "variation steps");
        return new HandEditParameters(kind, Text(parameters, "noteId"), values);
    }

    private static RhythmReshapeParameters ReadRhythmReshape(JsonElement parameters)
    {
        int eighths = Integer(parameters, "eighths");
        return eighths is < RhythmReshapeParameters.MinEighths or > RhythmReshapeParameters.MaxEighths
            ? throw Damaged("variation parameters")
            : new RhythmReshapeParameters(Name<RhythmReshapeKind>(parameters, "kind"), eighths);
    }

    private static MelodicSequenceParameters ReadMelodicSequence(JsonElement parameters)
    {
        int steps = Integer(parameters, "steps");
        int repeats = Integer(parameters, "repeats");
        return steps == 0 || Math.Abs(steps) > MelodicSequenceParameters.MaxSteps || repeats is < MelodicSequenceParameters.MinRepeats or > MelodicSequenceParameters.MaxRepeats
            ? throw Damaged("variation parameters")
            : new MelodicSequenceParameters(steps, repeats, Boolean(parameters, "diatonic"));
    }

    private static IntervalReshapeParameters ReadIntervalReshape(JsonElement parameters)
    {
        int steps = Integer(parameters, "steps");
        return steps == 0 || Math.Abs(steps) > IntervalReshapeParameters.MaxSteps
            ? throw Damaged("variation parameters")
            : new IntervalReshapeParameters(steps, ReadIntervals(parameters));
    }

    /// <summary>The picked intervals, or null when the file names none, which picks every interval.</summary>
    private static IntervalSet? ReadIntervals(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("intervals", out _))
        {
            return null;
        }

        int[] indices = [.. Property(parameters, "intervals", JsonValueKind.Array)
            .EnumerateArray()
            .Select(index => index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out int value) && value >= 0 ? value : throw Damaged("variation parameters"))];
        return indices.Length > 0 ? new IntervalSet(indices) : throw Damaged("variation parameters");
    }

    private static JsonElement Property(JsonElement parent, string name, JsonValueKind kind)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != kind)
        {
            throw Damaged(name);
        }

        return value;
    }

    private static void Expect(JsonElement element, JsonValueKind kind, string what)
    {
        if (element.ValueKind != kind)
        {
            throw Damaged(what);
        }
    }

    private static string Text(JsonElement parent, string name)
    {
        string value = Property(parent, name, JsonValueKind.String).GetString()!;
        return value.Length > 0 ? value : throw Damaged(name);
    }

    private static bool Boolean(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw Damaged(name);

    private static int Integer(JsonElement parent, string name) =>
        Property(parent, name, JsonValueKind.Number).TryGetInt32(out int value) ? value : throw Damaged(name);

    private static double Number(JsonElement parent, string name) =>
        Property(parent, name, JsonValueKind.Number).TryGetDouble(out double value) && double.IsFinite(value) ? value : throw Damaged(name);

    /// <summary>An enum in lower camel case, as the pencil layer writes a stroke's tool and colour.</summary>
    private static string Lower<T>(T value)
        where T : struct, Enum
    {
        string name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static T LowerName<T>(JsonElement parent, string name)
        where T : struct, Enum
    {
        string text = Text(parent, name);
        return Enum.GetNames<T>().FirstOrDefault(known => string.Equals(known, text, StringComparison.OrdinalIgnoreCase)) is { } match ? Enum.Parse<T>(match) : throw Damaged(name);
    }

    private static long Long(JsonElement parent, string name) =>
        Property(parent, name, JsonValueKind.Number).TryGetInt64(out long value) ? value : throw Damaged(name);

    private static DateTimeOffset Time(JsonElement parent, string name) =>
        DateTimeOffset.TryParseExact(Text(parent, name), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset value)
            ? value
            : throw Damaged(name);

    private static T Name<T>(JsonElement parent, string name)
        where T : struct, Enum =>
        TryName(Text(parent, name), out T value) ? value : throw Damaged(name);

    /// <summary>An enum by its exact name; numbers are not names.</summary>
    private static bool TryName<T>(string? text, out T value)
        where T : struct, Enum
    {
        value = default;
        return text is not null && Enum.GetNames<T>().Contains(text, StringComparer.Ordinal) && Enum.TryParse(text, out value);
    }

    private static ProjectFileException Damaged(string what) =>
        new($"This project file is damaged: its {what} cannot be read.");
}