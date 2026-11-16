using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Settings;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The workspace of version 16 (decision record 0030): the working notes' id and everything the player made on them, in
/// their ticks. A file of an earlier version keeps the same fields at its top level, so one reader takes both.
/// </summary>
public static partial class ProjectFile
{
    private static readonly HashSet<string> s_workspaceFields = new(StringComparer.Ordinal)
    {
        "working", "metadata", "partOf", "editsOff", "tempo", "motifs", "variations", "observations", "phrases", "marks", "bookmarks",
        "resumePoints", "assembly", "settings", "hiddenViews",
    };

    private const string WorkspacePath = "workspace";

    private static void WriteWorkspace(Utf8JsonWriter json, Project project)
    {
        json.WriteStartObject(WorkspacePath);
        if (project.HasNotes)
        {
            json.WriteString("working", project.NotesId);
        }

        WriteMetadata(json, project.Metadata);
        WritePartOf(json, project.PartOf);
        WriteEditsOff(json, project);
        WritePlayerTempo(json, project);
        WriteMotifsAndVariations(json, project);
        WriteList(json, "observations", project.Observations, KeptUnknown(project.Unknown, "observations", project.Observations.Select(static observation => observation.Id), null, out _), observation => WriteObservation(json, observation, project.HasNotes ? project.NotesId : null));
        WritePhrases(json, project);
        WriteMarks(json, project.Marks);
        WriteBookmarks(json, project);
        WriteResumePoints(json, project);
        if (!project.Assembly.IsEmpty)
        {
            WriteAssembly(json, project.Assembly);
        }

        json.WriteStartObject("settings");
        foreach ((string id, string value) in project.SettingOverrides.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            json.WriteString(id, value);
        }

        json.WriteEndObject();
        if (project.HiddenViews.Count > 0)
        {
            json.WriteStartArray("hiddenViews");
            foreach (string id in project.HiddenViews)
            {
                json.WriteStringValue(id);
            }

            json.WriteEndArray();
        }

        WriteUnknownFields(json, project.UnknownFieldsAt(WorkspacePath));
        json.WriteEndObject();
    }

    private static void WriteMotifsAndVariations(Utf8JsonWriter json, Project project)
    {
        List<UnknownEntity> unknownMotifs = KeptUnknown(project.Unknown, "motifs", project.Motifs.Select(static motif => motif.Id), null, out HashSet<string> replacedMotifs);
        WriteList(json, "motifs", project.Motifs, unknownMotifs, motif =>
        {
            json.WriteStartObject();
            json.WriteString("id", motif.Id);
            json.WriteString("name", motif.Name);
            json.WriteString("kind", motif.Kind.ToString());
            json.WriteString("mode", motif.Mode.ToString());
            WriteRange(json, motif.Span, motif.Filter);
            WriteNoteIds(json, motif.NoteIds);
            if (motif.Notes is { } notes)
            {
                json.WriteString("notes", notes);
            }

            json.WriteEndObject();
        });

        WriteList(json, "variations", project.Variations, KeptUnknown(project.Unknown, "variations", project.Variations.Select(static variation => variation.Id), replacedMotifs, out _), variation =>
        {
            json.WriteStartObject();
            json.WriteString("id", variation.Id);
            json.WriteString("motifId", variation.MotifId);
            WriteChain(json, variation.Steps, variation.Pending, variation.Keep);
            WriteKey(json, variation.Key);
            json.WriteEndObject();
        });
    }

    private static void WriteEdits(Utf8JsonWriter json, NoteEditLog edits)
    {
        json.WriteStartArray("edits");
        foreach (NoteEdit edit in edits.Edits)
        {
            json.WriteStartObject();
            json.WriteString("noteId", edit.NoteId);
            if (edit.Before is { } before)
            {
                WriteValues(json, "before", before);
            }
            else
            {
                // A note the player added (version 14, testing item 15): its track and hand, then its values.
                json.WriteStartObject("added");
                json.WriteNumber("track", edit.Track);
                if (edit.Hand is { } hand)
                {
                    json.WriteString("hand", hand.ToString());
                }

                json.WriteEndObject();
            }

            if (edit.After is { } after)
            {
                WriteValues(json, "after", after);
            }

            // Version 16: corrections accepted together, and who proposed one when it was not the player.
            if (edit.Group is { } group)
            {
                json.WriteString("group", group);
            }

            if (edit.Origin is { } origin)
            {
                json.WriteString("origin", origin);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WritePhrases(Utf8JsonWriter json, Project project)
    {
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
    }

    private static void WriteMarks(Utf8JsonWriter json, Marks marks)
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
            WriteNoteIds(json, highlight.NoteIds);
            json.WriteString("colour", highlight.Colour.ToString());
            json.WriteEndObject();
        });
        WriteFingering(json, marks.Fingering);
        json.WriteEndObject();
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

    private static void WriteBookmarks(Utf8JsonWriter json, Project project)
    {
        json.WriteStartObject("bookmarks");
        List<UnknownEntity> unknownCategories = KeptUnknown(project.Unknown, "bookmarks.categories", project.Categories.Select(static category => category.Id), null, out HashSet<string> replacedCategories);
        WriteList(json, "categories", project.Categories, unknownCategories, category =>
        {
            json.WriteStartObject();
            json.WriteString("id", category.Id);
            if (category.Name is { } categoryName)
            {
                json.WriteString("name", categoryName);
            }

            json.WriteString("ink", category.Ink.ToString());
            json.WriteEndObject();
        });

        WriteList(json, "items", project.Bookmarks, KeptUnknown(project.Unknown, "bookmarks.items", project.Bookmarks.Select(static bookmark => bookmark.Id), replacedCategories, out _), bookmark =>
        {
            json.WriteStartObject();
            json.WriteString("id", bookmark.Id);
            json.WriteString("title", bookmark.Title);
            json.WriteString("category", bookmark.CategoryId);
            WriteSpan(json, bookmark.Span);
            json.WriteBoolean("fromPhrase", bookmark.FromPhrase);
            json.WriteString("createdAt", bookmark.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteEndObject();
        });

        json.WriteEndObject();
    }

    private static void WriteResumePoints(Utf8JsonWriter json, Project project) =>
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
        });

    /// <summary>"hiddenViews": the ids of the views hidden from the switch, each a text that is not blank.</summary>
    private static List<string> ReadHiddenViews(JsonElement hidden)
    {
        Expect(hidden, JsonValueKind.Array, "hiddenViews");
        List<string> ids = [.. hidden.EnumerateArray().Select(static id => id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()) ? id.GetString()! : throw Damaged("hiddenViews"))];
        return ids.Distinct(StringComparer.Ordinal).Count() == ids.Count ? ids : throw Damaged("hiddenViews");
    }

    /// <summary>
    /// The player's work as <paramref name="work"/> holds it: the top level of a file before version 16, the workspace of a
    /// version 16 file. The project has the identity given and no notes yet; the caller adds them.
    /// </summary>
    private static Project ReadWork(JsonElement root, JsonElement work, int version, Unknowns unknowns)
    {
        List<SavedMotif> motifs = ReadMotifs(work, version, unknowns, out List<SavedVariation> variations);
        List<Observation> observations = version >= 6
            ? ReadList(Property(work, "observations", JsonValueKind.Array), "observations", observation => ReadObservation(observation, version), unknowns)
            : [];
        if (observations.Select(static observation => observation.Id).Distinct(StringComparer.Ordinal).Count() != observations.Count)
        {
            throw Damaged("observations");
        }

        List<ResumePoint> resumePoints = version >= 12
            ? ReadList(Property(work, "resumePoints", JsonValueKind.Array), "resumePoints", ReadResumePoint, unknowns)
            : version >= 8 ? Property(work, "sessions", JsonValueKind.Array).EnumerateArray().Select(ReadSessionAsResumePoint).ToList() : [];
        if (resumePoints.Select(static point => point.Id).Distinct(StringComparer.Ordinal).Count() != resumePoints.Count)
        {
            throw Damaged("resume points");
        }

        (IReadOnlyList<BookmarkCategory> categories, IReadOnlyList<Bookmark> bookmarks) = version >= 12
            ? ReadBookmarks(Property(work, "bookmarks", JsonValueKind.Object), unknowns)
            : (BookmarkCategory.BuiltIn, []);
        return new Project(Text(root, "id"), Text(root, "fileName"), [], Time(root, "createdAt"), Time(root, "lastOpened"), motifs, variations)
        {
            Phrases = version >= 5 ? ReadPhrases(Property(work, "phrases", JsonValueKind.Object)) : PhraseEdits.None,
            ShortPhrases = version >= 12 ? ReadPhraseLevel(Property(work, "phrases", JsonValueKind.Object), "short") : PhraseEdits.None,
            Sentences = version >= 12 ? ReadPhraseLevel(Property(work, "phrases", JsonValueKind.Object), "sentences") : PhraseEdits.None,
            Observations = observations,
            ResumePoints = resumePoints,
            Marks = version >= 12 ? ReadMarks(Property(work, "marks", JsonValueKind.Object), unknowns) : Marks.Empty,
            Categories = categories,
            Bookmarks = bookmarks,
            SettingOverrides = version >= 12 ? ReadSettings(Property(work, "settings", JsonValueKind.Object)) : new Dictionary<string, string>(StringComparer.Ordinal),
            Assembly = version >= 15 ? ReadAssembly(work) : PieceAssembly.Empty,
            Metadata = ReadMetadata(work),
            HiddenViews = version >= 16 && work.TryGetProperty("hiddenViews", out JsonElement hidden) ? ReadHiddenViews(hidden) : [],
        }.WithPartOfIn(work).WithEditsOffIn(work).WithPlayerTempoIn(work);
    }
}