using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Projects.File.JsonRead;
using static MotiffLab.MusicCore.Projects.File.SpanCodec;
using static MotiffLab.MusicCore.Projects.File.UnknownEntities;

namespace MotiffLab.MusicCore.Projects.File;

/// <summary>
/// The player's marks in the project file (version 12, decision record 0018): ink strokes, in the shape the pencil
/// layer saves too; the marks on a PDF's pages (0023); the marks on the score (ink, margin notes, highlights,
/// fingering); the bookmarks and their categories; the resume points; and the Check flags.
/// </summary>
internal static class MarksCodec
{
    /// <summary>
    /// One stroke in the pencil layer's shape. The facade (0020) answers strokes in it too, with the place the stroke starts
    /// written by <paramref name="place"/>: "page" on a PDF's pages, "bar" with its "barNumber" on the score; the file
    /// writes "bar", the tick of its bar.
    /// </summary>
    internal static void WriteStroke(Utf8JsonWriter json, InkStroke stroke, Action<Utf8JsonWriter, InkStroke>? place = null)
    {
        json.WriteStartObject();
        json.WriteString("id", stroke.Id);
        json.WriteString("tool", JsonWrite.Name(stroke.Tool));
        json.WriteString("colour", JsonWrite.Name(stroke.Colour));
        json.WriteNumber("size", stroke.Size);
        if (place is null)
        {
            json.WriteNumber("bar", stroke.Bar.Value);
        }
        else
        {
            place(json, stroke);
        }

        json.WriteNumber("u", stroke.U);
        json.WriteNumber("v", stroke.V);
        json.WriteStartArray("points");
        foreach (double value in stroke.Points)
        {
            json.WriteNumberValue(value);
        }

        json.WriteEndArray();
        json.WriteNumber("pressure", stroke.Pressure);
        WriteMargin(json, stroke.Margin);
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
                Number(stroke, "pressure"))
            {
                Margin = ReadMargin(stroke),
            };
        }
        catch (ArgumentException)
        {
            throw Damaged("ink");
        }
    }

    /// <summary>
    /// "margin" on a stroke: "left" or "right", only for a stroke begun in a line's margin (version 0.4; versions 0.2 and
    /// 0.3 drop it when they write the file again).
    /// </summary>
    private static void WriteMargin(Utf8JsonWriter json, StrokeMargin? margin)
    {
        if (margin is { } side)
        {
            json.WriteString("margin", JsonWrite.Name(side));
        }
    }

    private static StrokeMargin? ReadMargin(JsonElement stroke) =>
        stroke.TryGetProperty("margin", out _) ? LowerName<StrokeMargin>(stroke, "margin") : null;

    /// <summary>The marks on a PDF's pages (0023): {"ink": [stroke, …], "bookmarks": [{"id", "page", "name"?, "createdAt"}]}.</summary>
    internal static void WriteDocumentMarks(Utf8JsonWriter json, string field, DocumentMarks marks)
    {
        json.WriteStartObject(field);
        WriteList(json, "ink", marks.Ink, KeptUnknown(marks.Unknown, "documentMarks.ink", marks.Ink.Select(static stroke => stroke.Id), null, out _), stroke => WriteStroke(json, stroke));
        json.WriteStartArray("bookmarks");
        foreach (PageBookmark bookmark in marks.Bookmarks)
        {
            json.WriteStartObject();
            json.WriteString("id", bookmark.Id);
            json.WriteNumber("page", bookmark.Page);
            if (bookmark.Name is { } name)
            {
                json.WriteString("name", name);
            }

            json.WriteString("createdAt", bookmark.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    internal static DocumentMarks ReadDocumentMarks(JsonElement marks, Unknowns unknowns)
    {
        Expect(marks, JsonValueKind.Object, "documentMarks");
        List<InkStroke> ink = ReadList(Property(marks, "ink", JsonValueKind.Array), "documentMarks.ink", ReadStroke, unknowns);
        List<PageBookmark> bookmarks = [];
        foreach (JsonElement bookmark in Property(marks, "bookmarks", JsonValueKind.Array).EnumerateArray())
        {
            Expect(bookmark, JsonValueKind.Object, "documentMarks");
            string? name = bookmark.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() : throw Damaged("documentMarks")
                : null;
            try
            {
                bookmarks.Add(new PageBookmark(Text(bookmark, "id"), Integer(bookmark, "page"), name, JsonRead.Time(bookmark, "createdAt")));
            }
            catch (ArgumentException)
            {
                throw Damaged("documentMarks");
            }
        }

        try
        {
            return bookmarks.Select(static bookmark => bookmark.Page).Distinct().Count() == bookmarks.Count
                && bookmarks.Select(static bookmark => bookmark.Id).Distinct(StringComparer.Ordinal).Count() == bookmarks.Count
                ? DocumentMarks.Empty.WithInk(ink) with { Bookmarks = [.. bookmarks.OrderBy(static bookmark => bookmark.Page)] }
                : throw Damaged("documentMarks");
        }
        catch (ArgumentException)
        {
            throw Damaged("documentMarks");
        }
    }

    internal static void WriteMarks(Utf8JsonWriter json, Marks marks)
    {
        json.WriteStartObject("marks");
        WriteList(json, "ink", marks.Ink, KeptUnknown(marks.Unknown, "marks.ink", marks.Ink.Select(static stroke => stroke.Id), null, out _), stroke => WriteStroke(json, stroke));
        json.WriteStartArray("notes");
        foreach (MarginNote note in marks.Notes)
        {
            json.WriteStartObject();
            json.WriteString("id", note.Id);
            WriteSpan(json, note.Span);
            json.WriteString("text", note.Text);
            json.WriteString("createdAt", note.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        WriteList(json, "highlights", marks.Highlights, KeptUnknown(marks.Unknown, "marks.highlights", marks.Highlights.Select(static highlight => highlight.Id), null, out _), highlight =>
        {
            json.WriteStartObject();
            json.WriteString("id", highlight.Id);
            WriteSpan(json, highlight.Span);
            JsonWrite.Texts(json, "noteIds", highlight.NoteIds);
            json.WriteString("colour", highlight.Colour.ToString());
            json.WriteEndObject();
        });
        WriteFingering(json, marks.Fingering);
        json.WriteEndObject();
    }

    internal static Marks ReadMarks(JsonElement marks, Unknowns unknowns)
    {
        List<InkStroke> ink = ReadList(Property(marks, "ink", JsonValueKind.Array), "marks.ink", ReadStroke, unknowns);
        List<MarginNote> notes = [];
        foreach (JsonElement note in Property(marks, "notes", JsonValueKind.Array).EnumerateArray())
        {
            Expect(note, JsonValueKind.Object, "notes");
            try
            {
                notes.Add(new MarginNote(Text(note, "id"), ReadSpan(note, "note"), Text(note, "text"), JsonRead.Time(note, "createdAt")));
            }
            catch (ArgumentException)
            {
                throw Damaged("notes");
            }
        }

        List<Highlight> highlights = ReadList(Property(marks, "highlights", JsonValueKind.Array), "marks.highlights", highlight =>
        {
            Expect(highlight, JsonValueKind.Object, "highlights");
            try
            {
                return new Highlight(Text(highlight, "id"), ReadSpan(highlight, "highlight"), ReadNoteIds(highlight, "highlight notes"), Name<InkColour>(highlight, "colour"));
            }
            catch (ArgumentException)
            {
                throw Damaged("highlights");
            }
        }, unknowns);

        List<Fingering> fingering = [];
        foreach (JsonElement finger in Property(marks, "fingering", JsonValueKind.Array).EnumerateArray())
        {
            Expect(finger, JsonValueKind.Object, "fingering");
            int digit = Integer(finger, "finger");
            fingering.Add(digit is >= 1 and <= 5 ? new Fingering(Text(finger, "noteId"), digit) : throw Damaged("fingering"));
        }

        try
        {
            Marks read = Marks.Empty.WithInkAsWritten(ink) with { Notes = notes, Highlights = highlights, Fingering = fingering };
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

    /// <summary>"fingering": each note's finger, as the file and the facade's marks answer both write it.</summary>
    internal static void WriteFingering(Utf8JsonWriter json, IEnumerable<Fingering> fingering)
    {
        ArgumentNullException.ThrowIfNull(json);
        json.WriteStartArray("fingering");
        foreach (Fingering finger in fingering)
        {
            json.WriteStartObject();
            json.WriteString("noteId", finger.NoteId);
            json.WriteNumber("finger", finger.Finger);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>The fields of a bookmark category, a bookmark and a resume point this build reads; a file's others are kept (version 18).</summary>
    private static readonly HashSet<string> s_categoryFields = new(StringComparer.Ordinal) { "id", "name", "ink" };

    private static readonly HashSet<string> s_bookmarkFields = new(StringComparer.Ordinal) { "id", "title", "category", "onset", "duration", "fromPhrase", "createdAt" };

    internal static readonly HashSet<string> s_resumePointFields = new(StringComparer.Ordinal)
    {
        "id", "name", "at", "passage", "focused", "overlays", "tempoPercent", "hands", "loop", "createdAt", "lastActive",
    };

    internal static void WriteBookmarks(Utf8JsonWriter json, Project project)
    {
        json.WriteStartObject("bookmarks");
        // A built-in category read with an ink this build does not know is written back as the file held it while its
        // stand-in is as read (#364); renamed, the stand-in takes its place.
        List<BookmarkCategory> categories = [.. project.Categories.Where(category => !project.Unknown.Any(entity => entity.List == "bookmarks.categories" && entity.Id == category.Id && category.IsBuiltIn && StandIn(entity) == category))];
        List<UnknownEntity> unknownCategories = KeptUnknown(project.Unknown, "bookmarks.categories", categories.Select(static category => category.Id), null, out HashSet<string> replacedCategories);
        WriteList(json, "categories", categories, unknownCategories, category =>
        {
            json.WriteStartObject();
            json.WriteString("id", category.Id);
            if (category.Name is { } categoryName)
            {
                json.WriteString("name", categoryName);
            }

            json.WriteString("ink", category.Ink.ToString());
            WriteUnknownFields(json, project.UnknownFieldsAt(FieldsPath("bookmarks.categories", category.Id)));
            json.WriteEndObject();
        });

        WriteList(json, "items", project.Bookmarks, KeptUnknown(project.Unknown, "bookmarks.items", project.Bookmarks.Select(static bookmark => (bookmark.Id, bookmark.CategoryId)), replacedCategories), bookmark =>
        {
            json.WriteStartObject();
            json.WriteString("id", bookmark.Id);
            json.WriteString("title", bookmark.Title);
            json.WriteString("category", bookmark.CategoryId);
            WriteSpan(json, bookmark.Span);
            json.WriteBoolean("fromPhrase", bookmark.FromPhrase);
            json.WriteString("createdAt", bookmark.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            WriteUnknownFields(json, project.UnknownFieldsAt(FieldsPath("bookmarks.items", bookmark.Id)));
            json.WriteEndObject();
        });

        json.WriteEndObject();
    }

    internal static (IReadOnlyList<BookmarkCategory> Categories, IReadOnlyList<Bookmark> Bookmarks) ReadBookmarks(JsonElement bookmarks, Unknowns unknowns)
    {
        List<BookmarkCategory> categories = ReadList(Property(bookmarks, "categories", JsonValueKind.Array), "bookmarks.categories", static category => ReadCategory(category), unknowns, s_categoryFields);

        // The four built-in categories are always there, so bookmarks can be added to them (#364): under their own name when
        // the file does not name them; one the file holds with an ink this build does not know is kept unknown, written back
        // as it was, and stands in as StandIn shows it, its bookmarks read as known.
        for (int i = 0; i < BookmarkCategory.BuiltIn.Count; i++)
        {
            BookmarkCategory builtIn = BookmarkCategory.BuiltIn[i];
            if (categories.All(category => category.Id != builtIn.Id))
            {
                UnknownEntity? kept = unknowns.Kept.FirstOrDefault(entity => entity.List == "bookmarks.categories" && entity.Id == builtIn.Id);
                categories.Insert(Math.Min(i, categories.Count), kept is null ? builtIn : StandIn(kept));
            }
        }

        HashSet<string> unknownCategories = [.. Project.UnknownIds(unknowns.Kept, "bookmarks.categories").Where(static id => !BookmarkCategory.IsBuiltInId(id))];

        if (categories.Select(static category => category.Id).Distinct(StringComparer.Ordinal).Count() != categories.Count)
        {
            throw Damaged("bookmark categories");
        }

        // A bookmark in an unknown category is left unknown with it: an app could not show it in its category.
        List<Bookmark> items = Known(
            ReadEntries(Property(bookmarks, "items", JsonValueKind.Array), "bookmarks.items", ReadBookmark, unknowns, "category", s_bookmarkFields),
            "bookmarks.items",
            static bookmark => bookmark.CategoryId,
            unknownCategories,
            "category",
            unknowns);

        return items.Select(static bookmark => bookmark.Id).Distinct(StringComparer.Ordinal).Count() == items.Count
            && items.All(bookmark => categories.Any(category => category.Id == bookmark.CategoryId))
            ? (categories, items)
            : throw Damaged("bookmarks");
    }

    /// <param name="ink">The ink it is read in, in place of its own (<see cref="StandIn"/>).</param>
    private static BookmarkCategory ReadCategory(JsonElement category, InkColour? ink = null)
    {
        Expect(category, JsonValueKind.Object, "bookmark categories");
        try
        {
            return new BookmarkCategory(Text(category, "id"), category.TryGetProperty("name", out _) ? Text(category, "name") : null, ink ?? Name<InkColour>(category, "ink"));
        }
        catch (ArgumentException)
        {
            throw Damaged("bookmark categories");
        }
    }

    /// <summary>
    /// A built-in category kept unknown for its ink, as this build shows it (#364): under the name the file gives it, in the
    /// built-in's own ink, never one this build does not have.
    /// </summary>
    private static BookmarkCategory StandIn(UnknownEntity category)
    {
        using JsonDocument kept = JsonDocument.Parse(category.Json);
        return ReadCategory(kept.RootElement, BookmarkCategory.BuiltIn.Single(builtIn => builtIn.Id == category.Id).Ink);
    }

    private static Bookmark ReadBookmark(JsonElement bookmark)
    {
        Expect(bookmark, JsonValueKind.Object, "bookmarks");
        try
        {
            return new Bookmark(Text(bookmark, "id"), Text(bookmark, "title"), Text(bookmark, "category"), ReadSpan(bookmark, "bookmark"), Boolean(bookmark, "fromPhrase"), JsonRead.Time(bookmark, "createdAt"));
        }
        catch (ArgumentException)
        {
            throw Damaged("bookmarks");
        }
    }

    internal static void WriteResumePoints(Utf8JsonWriter json, Project project) =>
        WriteList(json, "resumePoints", project.ResumePoints, KeptUnknown(project.Unknown, "resumePoints", project.ResumePoints.Select(static point => point.Id), null, out _), point =>
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
            JsonWrite.Texts(json, "overlays", point.Overlays.Select(static overlay => overlay.ToString()));
            json.WriteNumber("tempoPercent", point.TempoPercent);
            if (point.Hands is { } hands)
            {
                json.WriteString("hands", hands.ToString());
            }

            json.WriteBoolean("loop", point.Loop);
            json.WriteString("createdAt", point.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteString("lastActive", point.LastActive.ToString("O", CultureInfo.InvariantCulture));
            WriteUnknownFields(json, project.UnknownFieldsAt(FieldsPath("resumePoints", point.Id)));
            json.WriteEndObject();
        });

    internal static ResumePoint ReadResumePoint(JsonElement point)
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
                JsonRead.Time(point, "createdAt"),
                JsonRead.Time(point, "lastActive"));
        }
        catch (ArgumentException)
        {
            throw Damaged("resume point");
        }
    }

    internal static void WriteCheck(Utf8JsonWriter json, IReadOnlyList<CheckFlag>? check, List<UnknownEntity> unknownFlags)
    {
        if (check is null && unknownFlags.Count == 0)
        {
            json.WriteNull("check");
            return;
        }

        WriteList(json, "check", check ?? [], unknownFlags, flag =>
        {
            json.WriteStartObject();
            json.WriteNumber("bar", flag.Bar);
            json.WriteString("source", flag.Source.ToString());
            json.WriteString("reason", flag.Reason.ToString());
            json.WriteBoolean("resolved", flag.Resolved);
            json.WriteEndObject();
        });
    }

    internal static IReadOnlyList<CheckFlag>? ReadCheck(JsonElement root, Unknowns unknowns)
    {
        if (!root.TryGetProperty("check", out JsonElement check) || check.ValueKind is not (JsonValueKind.Null or JsonValueKind.Array))
        {
            throw Damaged("check");
        }

        if (check.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadList(check, "check", flag =>
        {
            Expect(flag, JsonValueKind.Object, "check");
            int bar = Integer(flag, "bar");
            return bar >= 0 ? new CheckFlag(bar, Name<CheckSource>(flag, "source"), Name<CheckReason>(flag, "reason"), Boolean(flag, "resolved")) : throw Damaged("check");
        }, unknowns);
    }
}