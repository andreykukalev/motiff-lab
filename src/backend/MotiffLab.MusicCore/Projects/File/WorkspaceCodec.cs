using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Settings;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Projects.File.AssemblyCodec;
using static MotiffLab.MusicCore.Projects.File.JsonRead;
using static MotiffLab.MusicCore.Projects.File.Legacy.EarlierFile;
using static MotiffLab.MusicCore.Projects.File.MarksCodec;
using static MotiffLab.MusicCore.Projects.File.MetadataCodec;
using static MotiffLab.MusicCore.Projects.File.ProvenanceCodec;
using static MotiffLab.MusicCore.Projects.File.SpanCodec;
using static MotiffLab.MusicCore.Projects.File.UnknownEntities;
using static MotiffLab.MusicCore.Projects.File.VariationCodec;

namespace MotiffLab.MusicCore.Projects.File;

/// <summary>
/// The workspace of version 16 (decision record 0030): the working notes' id and everything the player made on them, in
/// their ticks. A file of an earlier version keeps the same fields at its top level, so one reader takes both. Beside
/// the codecs of its parts, it holds the ideas, the phrase boundaries, the piece's settings and hidden views, its place
/// in its collection ("partOf", version 0.4), whether the corrections are off ("editsOff") and the player's tempo
/// ("tempo", version 0.5). "partOf" and "editsOff" are optional within version 15: earlier builds open the file, with
/// the corrections on, and drop them when they write it again ("partOf" versions 0.2 and 0.3, "editsOff" 0.2 to 0.4).
/// </summary>
internal static class WorkspaceCodec
{
    internal static readonly HashSet<string> s_workspaceFields = new(StringComparer.Ordinal)
    {
        "working", "metadata", "partOf", "editsOff", "tempo", "motifs", "variations", "observations", "phrases", "marks", "bookmarks",
        "resumePoints", "assembly", "settings", "hiddenViews",
    };

    internal const string WorkspacePath = "workspace";

    internal static void WriteWorkspace(Utf8JsonWriter json, Project project)
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

    /// <summary>"editsOff": true while the corrections are off; left out otherwise.</summary>
    private static void WriteEditsOff(Utf8JsonWriter json, Project project)
    {
        if (project.EditsOff)
        {
            json.WriteBoolean("editsOff", true);
        }
    }

    /// <summary>"tempo": {"quarterNotesPerMinute": 92} once the player set the piece's tempo (version 0.5.4); left out otherwise.</summary>
    private static void WritePlayerTempo(Utf8JsonWriter json, Project project)
    {
        if (project.PlayerTempo is { } perMinute)
        {
            json.WriteStartObject("tempo");
            json.WriteNumber("quarterNotesPerMinute", perMinute);
            json.WriteEndObject();
        }
    }

    /// <summary>"partOf": {"original", "pages": [first, last]} or {"original", "ms": [from, to]}; left out for a piece of its own.</summary>
    private static void WritePartOf(Utf8JsonWriter json, PartOf? partOf)
    {
        if (partOf is null)
        {
            return;
        }

        json.WriteStartObject("partOf");
        json.WriteString("original", partOf.Original);
        if (partOf.Pages is var (first, last))
        {
            json.WriteStartArray("pages");
            json.WriteNumberValue(first);
            json.WriteNumberValue(last);
            json.WriteEndArray();
        }

        if (partOf.Milliseconds is var (from, to))
        {
            json.WriteStartArray("ms");
            json.WriteNumberValue(from);
            json.WriteNumberValue(to);
            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    /// <summary>An idea; with a variation, the Notes it was built on (version 16): its own, else <paramref name="working"/>.</summary>
    private static void WriteObservation(Utf8JsonWriter json, Observation observation, string? working)
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
            WriteKey(json, variation.Key);

            json.WriteEndObject();
            if ((observation.Notes ?? working) is { } notes)
            {
                json.WriteString("notes", notes);
            }
        }

        if (observation.Provenance is { } provenance)
        {
            WriteProvenance(json, provenance);
        }

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

    /// <summary>
    /// The player's work as <paramref name="work"/> holds it: the top level of a file before version 16, the workspace of a
    /// version 16 file. The project has the identity given and no notes yet; the caller adds them.
    /// </summary>
    internal static Project ReadWork(JsonElement root, JsonElement work, int version, Unknowns unknowns)
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
        DateTimeOffset createdAt = JsonRead.Time(observation, "createdAt");
        // Before version 12 an observation named the session it was kept in; sessions are gone, so the name is dropped.
        string? factId = version >= 12 && observation.TryGetProperty("factId", out _) ? Text(observation, "factId") : null;
        try
        {
            return Observation.Create(id, new SavedPassage(phrase, phraseFilter), variation, name, note, createdAt, experiment) with
            {
                FactId = factId,
                Notes = version >= 16 && variation is not null && observation.TryGetProperty("notes", out _) ? Text(observation, "notes") : null,
                Provenance = version >= 16 && observation.TryGetProperty("provenance", out JsonElement provenance) ? ReadProvenance(provenance) : null,
            };
        }
        catch (ArgumentException)
        {
            throw Damaged("observation");
        }
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

    /// <summary>"hiddenViews": the ids of the views hidden from the switch, each a text that is not blank.</summary>
    private static List<string> ReadHiddenViews(JsonElement hidden)
    {
        Expect(hidden, JsonValueKind.Array, "hiddenViews");
        List<string> ids = [.. hidden.EnumerateArray().Select(static id => id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()) ? id.GetString()! : throw Damaged("hiddenViews"))];
        return ids.Distinct(StringComparer.Ordinal).Count() == ids.Count ? ids : throw Damaged("hiddenViews");
    }

    /// <summary>The project with the place in its collection its file keeps (<see cref="ReadPartOf"/>).</summary>
    private static Project WithPartOfIn(this Project project, JsonElement root) => project with { PartOf = ReadPartOf(root) };

    /// <summary>
    /// The piece's place in its collection; none when the file has no "partOf", and none for one with neither "pages" nor
    /// "ms" (a later build's other kind of place), so the piece opens as a piece of its own.
    /// </summary>
    private static PartOf? ReadPartOf(JsonElement root)
    {
        if (!root.TryGetProperty("partOf", out JsonElement partOf))
        {
            return null;
        }

        Expect(partOf, JsonValueKind.Object, "partOf");
        string original = Text(partOf, "original");
        bool pages = partOf.TryGetProperty("pages", out JsonElement pageRange);
        bool ms = partOf.TryGetProperty("ms", out JsonElement stretch);
        try
        {
            return (pages, ms) switch
            {
                (true, false) => PartOf.OnPages(original, checked((int)Pair(pageRange).First), checked((int)Pair(pageRange).Last)),
                (false, true) => PartOf.InRecording(original, Pair(stretch).First, Pair(stretch).Last),
                (false, false) => null,
                _ => throw Damaged("partOf"),
            };
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw Damaged("partOf");
        }
    }

    /// <summary>The project with the corrections off when its file says so; "editsOff" that is not true or false is damage.</summary>
    private static Project WithEditsOffIn(this Project project, JsonElement root) =>
        !root.TryGetProperty("editsOff", out JsonElement off) ? project : off.ValueKind switch
        {
            JsonValueKind.True => project with { EditsOff = true },
            JsonValueKind.False => project,
            _ => throw Damaged("editsOff"),
        };

    /// <summary>The project with the player's tempo when its file has one; a "tempo" without a whole number from 20 to 300 is damage.</summary>
    private static Project WithPlayerTempoIn(this Project project, JsonElement root) =>
        !root.TryGetProperty("tempo", out JsonElement tempo) ? project
            : tempo.ValueKind == JsonValueKind.Object && tempo.TryGetProperty("quarterNotesPerMinute", out JsonElement value)
                && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int perMinute) && Playback.PlayerTempo.IsValid(perMinute)
                ? project with { PlayerTempo = perMinute }
                : throw Damaged("tempo");
}