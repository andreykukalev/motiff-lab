using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Settings;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Projects.File.AssemblyCodec;
using static MotiffLab.MusicCore.Projects.File.DraftsCodec;
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
        "resumePoints", "assembly", "drafts", "settings", "hiddenViews",
    };

    internal const string WorkspacePath = "workspace";

    /// <summary>The fields of an idea this build reads; a file's others are kept (version 18).</summary>
    private static readonly HashSet<string> s_observationFields = new(StringComparer.Ordinal)
    {
        "id", "name", "factId", "note", "createdAt", "phrase", "anchor", "experiment", "variation", "notes", "provenance",
    };

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
        WriteList(json, "observations", project.Observations, KeptUnknown(project.Unknown, "observations", project.Observations.Select(static observation => observation.Id), null, out _), observation => WriteObservation(json, observation, project.HasNotes ? project.NotesId : null, project.UnknownFieldsAt(FieldsPath("observations", observation.Id))));
        WritePhrases(json, project);
        WriteMarks(json, project.Marks);
        WriteBookmarks(json, project);
        WriteResumePoints(json, project);
        if (!project.Assembly.IsEmpty)
        {
            WriteAssembly(json, project.Assembly);
        }

        WriteDrafts(json, project);

        json.WriteStartObject("settings");
        foreach ((string id, string value) in project.SettingOverrides.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            json.WriteString(id, value);
        }

        // A setting a later build has, or a value it allows, kept as the file held it (#314), unless the player set it here since.
        WriteUnknownFields(json, [.. project.UnknownFieldsAt(ProjectFile.SettingsPath).Where(field => !project.SettingOverrides.ContainsKey(field.Name))]);
        json.WriteEndObject();
        if (project.HiddenViews.Count > 0)
        {
            JsonWrite.Texts(json, "hiddenViews", project.HiddenViews);
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
            JsonWrite.Numbers(json, "pages", [first, last]);
        }

        if (partOf.Milliseconds is var (from, to))
        {
            JsonWrite.Numbers(json, "ms", [from, to]);
        }

        json.WriteEndObject();
    }

    /// <summary>
    /// An idea; with a variation, the Notes it was built on (version 16): its own, else <paramref name="working"/>; then the
    /// fields of it this build did not know (version 18).
    /// </summary>
    private static void WriteObservation(Utf8JsonWriter json, Observation observation, string? working, IReadOnlyList<UnknownField> unknown)
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
        if (observation.Anchor is { } anchor)
        {
            json.WriteString("anchor", anchor);
        }

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
            JsonWrite.Texts(json, "noteIds", variation.NoteIds);
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

        WriteUnknownFields(json, unknown);
        json.WriteEndObject();
    }

    private static void WritePhrases(Utf8JsonWriter json, Project project)
    {
        json.WriteStartObject("phrases");
        JsonWrite.Numbers(json, "added", project.Phrases.Added.Select(static tick => tick.Value));
        JsonWrite.Numbers(json, "removed", project.Phrases.Removed.Select(static tick => tick.Value));
        json.WriteStartObject("short");
        JsonWrite.Numbers(json, "added", project.ShortPhrases.Added.Select(static tick => tick.Value));
        JsonWrite.Numbers(json, "removed", project.ShortPhrases.Removed.Select(static tick => tick.Value));
        json.WriteEndObject();
        json.WriteStartObject("sentences");
        JsonWrite.Numbers(json, "added", project.Sentences.Added.Select(static tick => tick.Value));
        JsonWrite.Numbers(json, "removed", project.Sentences.Removed.Select(static tick => tick.Value));
        json.WriteEndObject();
        WriteAnchors(json, project.Anchors);
        json.WriteEndObject();
    }

    /// <summary>"anchors" (version 18, #318): each phrase anchor something is attached to, {id, onset, duration}; left out while there is none.</summary>
    private static void WriteAnchors(Utf8JsonWriter json, IReadOnlyList<PhraseAnchor> anchors)
    {
        if (anchors.Count == 0)
        {
            return;
        }

        json.WriteStartArray("anchors");
        foreach (PhraseAnchor anchor in anchors)
        {
            json.WriteStartObject();
            json.WriteString("id", anchor.Id);
            WriteSpan(json, anchor.Span);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>
    /// The project with the phrase anchors of a file of version 18, and an idea or a resume point naming one the file does
    /// not keep attached to none (<see cref="Project.WithKnownAnchors"/>).
    /// </summary>
    private static Project WithAnchorsIn(this Project project, JsonElement work, int version) =>
        (version >= 18 ? project with { MintedAnchors = ReadAnchors(Property(work, "phrases", JsonValueKind.Object)) } : project).WithKnownAnchors();

    /// <summary>The phrase anchors (version 18); none when "anchors" is absent. Two with one id, or a span that is no span, is damage.</summary>
    private static List<PhraseAnchor> ReadAnchors(JsonElement phrases)
    {
        if (!phrases.TryGetProperty("anchors", out JsonElement list))
        {
            return [];
        }

        List<PhraseAnchor> anchors = [.. Elements(list, "phrase anchors").Select(anchor =>
        {
            Expect(anchor, JsonValueKind.Object, "phrase anchors");
            string id = Text(anchor, "id");
            return !string.IsNullOrWhiteSpace(id) ? new PhraseAnchor(id, ReadSpan(anchor, "phrase anchor")) : throw Damaged("phrase anchors");
        })];
        return anchors.Select(static anchor => anchor.Id).Distinct(StringComparer.Ordinal).Count() == anchors.Count ? anchors : throw Damaged("phrase anchors");
    }

    /// <summary>
    /// The player's work as <paramref name="work"/> holds it: the top level of a file before version 16, the workspace of a
    /// version 16 file. The project has the identity given and no notes yet; the caller adds them.
    /// </summary>
    internal static Project ReadWork(JsonElement root, JsonElement work, int version, Unknowns unknowns)
    {
        List<SavedMotif> motifs = ReadMotifs(work, version, unknowns, out List<SavedVariation> variations);
        List<Observation> observations = version >= 6
            ? ReadList(Property(work, "observations", JsonValueKind.Array), "observations", observation => ReadObservation(observation, version), unknowns, s_observationFields)
            : [];
        if (observations.Select(static observation => observation.Id).Distinct(StringComparer.Ordinal).Count() != observations.Count)
        {
            throw Damaged("observations");
        }

        List<ResumePoint> resumePoints = version >= 12
            ? ReadList(Property(work, "resumePoints", JsonValueKind.Array), "resumePoints", ReadResumePoint, unknowns, s_resumePointFields)
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
            SettingOverrides = version >= 12 ? ReadSettings(Property(work, "settings", JsonValueKind.Object), version, unknowns) : new Dictionary<string, string>(StringComparer.Ordinal),
            Assembly = version >= 15 ? ReadAssembly(work) : PieceAssembly.Empty,
            Drafts = ReadDrafts(work, version, unknowns),
            Metadata = ReadMetadata(work),
            HiddenViews = version >= 16 && work.TryGetProperty("hiddenViews", out JsonElement hidden) ? DistinctTexts(hidden, "hiddenViews") : [],
        }.WithPartOfIn(work).WithEditsOffIn(work).WithPlayerTempoIn(work).WithAnchorsIn(work, version);
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
                Anchor = observation.TryGetProperty("anchor", out _) ? Text(observation, "anchor") : null,
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

    /// <summary>
    /// The piece's overrides. In a file of this layout a setting this build does not have, or a value of one it does not
    /// allow, is kept as the file held it and has no effect (0026 S6, #314); in an older file it is damage. The level a
    /// piece opened in, "phrases.granularity", is gone (version 18, the phrases spec): a file before 18 drops it.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadSettings(JsonElement settings, int version, Unknowns unknowns)
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        List<UnknownField> kept = [];
        foreach (JsonProperty property in settings.EnumerateObject())
        {
            if (version < 18 && property.Name == RemovedGranularity)
            {
                continue;
            }

            Setting? setting = SettingsCatalogue.Find(property.Name);
            string? value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            if (setting is { Scope: SettingScope.Piece } && value is not null && setting.Allows(value))
            {
                overrides[property.Name] = value;
            }
            else if (unknowns.Tolerant && (setting is null || (setting.Scope == SettingScope.Piece && value is not null)))
            {
                kept.Add(new UnknownField(property.Name, property.Value.GetRawText()));
            }
            else
            {
                throw Damaged("settings");
            }
        }

        if (kept.Count > 0)
        {
            unknowns.Fields[ProjectFile.SettingsPath] = kept;
        }

        return overrides;
    }

    /// <summary>The piece's phrase level of versions 12 to 17 (0018 section 3), gone since the score shows phrases only (version 1.2).</summary>
    private const string RemovedGranularity = "phrases.granularity";

    /// <summary>A list of ids, such as "hiddenViews" (the views hidden from the switch) or "formerIds": distinct texts that are not blank.</summary>
    internal static List<string> DistinctTexts(JsonElement list, string what)
    {
        Expect(list, JsonValueKind.Array, what);
        List<string> ids = [.. list.EnumerateArray().Select(id => id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()) ? id.GetString()! : throw Damaged(what))];
        return ids.Distinct(StringComparer.Ordinal).Count() == ids.Count ? ids : throw Damaged(what);
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