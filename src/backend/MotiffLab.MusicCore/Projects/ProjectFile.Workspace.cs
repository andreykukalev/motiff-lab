using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects.File;

using static MotiffLab.MusicCore.Projects.File.JsonRead;
using static MotiffLab.MusicCore.Projects.File.MarksCodec;
using static MotiffLab.MusicCore.Projects.File.SpanCodec;
using static MotiffLab.MusicCore.Projects.File.UnknownEntities;
using static MotiffLab.MusicCore.Projects.File.VariationCodec;

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
        return new Project(Text(root, "id"), Text(root, "fileName"), [], JsonRead.Time(root, "createdAt"), JsonRead.Time(root, "lastOpened"), motifs, variations)
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