using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects.File;
using MotiffLab.MusicCore.Settings;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Projects.File.EditsCodec;
using static MotiffLab.MusicCore.Projects.File.JsonRead;
using static MotiffLab.MusicCore.Projects.File.MarksCodec;
using static MotiffLab.MusicCore.Projects.File.PerformanceCodec;
using static MotiffLab.MusicCore.Projects.File.ProvenanceCodec;
using static MotiffLab.MusicCore.Projects.File.SpanCodec;
using static MotiffLab.MusicCore.Projects.File.UnknownEntities;
using static MotiffLab.MusicCore.Projects.File.VariationCodec;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The project file: one JSON document that holds a project, used both for autosave on the device
/// and for export and re-import as a backup (S1-P1, AC-S1-20).
/// </summary>
/// <remarks>
/// Written by hand rather than by reflection, so the layout is fixed and the same project always
/// gives the same bytes. Enums are written by name. Reading checks every field and throws
/// <see cref="ProjectFileException"/> with a plain reason; it never half-reads a project. The one exception is the
/// tolerant reader (decision record 0026, S6): in a file of this version, an entity of a list that holds a value a later
/// build added to an enum is kept whole as an <see cref="UnknownEntity"/> and written back unchanged where it stood
/// (docs/project-file.md, "Unknown enum values").
/// </remarks>
public static partial class ProjectFile
{
    public const string FormatName = "motiff-lab-project";

    /// <summary>
    /// 16 lays the file out in the three layers of decision record 0030: the originals (with the PDF's bytes and page marks,
    /// and the tombstones of removed ones), the Notes (each with its provenance, music, corrections and Check flags, and the
    /// compared ones with their comparison) and the workspace (the working notes and the player's work), keeping the fields
    /// it does not know; every earlier file is converted on reading (docs/project-file.md, "Opening an older file");
    /// within 15, "metadata" keeps the piece's title, composer and tags as the player set them in the library (version
    /// 0.2); a file without it has none, and a build before it drops the field when it writes the file again;
    /// 15 adds the assembly of the player's own version from the piece's sources: its journal, the notes applied and
    /// the notes before the assembly (0024);
    /// 14 lets a correction add a note, with its track and hand (testing item 15);
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
    /// versions still open: before 13 there are no performances, before 12 there are no marks, bookmarks or overrides, the sessions open as resume points named by
    /// their task, the ideas lose their session, and the Check flags are worked out when the project opens, resolved where
    /// the player confirmed a scanned bar; the bars of version 10 keep one tempo, versions before 10 keep a transcription's bars as the engine wrote them, versions before 9 were never transcribed, versions before 8 have no sessions, versions before 7 were never scanned, versions before 6 have no observations, steps of versions before 4
    /// change the whole selection, versions 1 and 2 have no corrections, and version 1 holds one
    /// step per variation.
    /// </summary>
    public const int Version = 16;

    /// <summary>The extension an exported project gets.</summary>
    public const string Extension = ".motiff.json";

    /// <summary>
    /// The project as a version 16 file (decision record 0030): its originals, its Notes and its workspace, each with the
    /// fields this build did not know when it read them (0026 S6). The same project always gives the same bytes.
    /// </summary>
    public static byte[] Write(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project = project.AsFiled();

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
            WriteOriginalsLayer(json, project);
            WriteNotesLayer(json, project);
            WriteWorkspace(json, project);
            WriteUnknownFields(json, project.UnknownFieldsAt(""));
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// The project a file holds, of any version from 1 to <see cref="Version"/>, converted to version 16 in memory: the
    /// same file always gives the same project. A file of a later version is refused before any field is read.
    /// </summary>
    public static Project Read(ReadOnlySpan<byte> data)
    {
        using (JsonDocument document = Parse(data))
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

            var unknowns = new Unknowns(version == Version);
            Project project = version == Version ? ReadLayers(root, unknowns) : ReadEarlier(root, version, unknowns);
            return unknowns.KeptIn(project).WithProvenanceKept().Described();
        }
    }

    /// <summary>A file of a version before 16: its notes, its PDF and its work at the top level.</summary>
    private static Project ReadEarlier(JsonElement root, int version, Unknowns unknowns)
    {
        byte[] source;
        byte[]? pdf = null;
        try
        {
            source = Property(root, "source", JsonValueKind.String).GetBytesFromBase64();
        }
        catch (FormatException)
        {
            throw Damaged("source");
        }

        if (root.TryGetProperty("document", out JsonElement documentElement))
        {
            pdf = ReadPdf(documentElement, "document");
        }

        ScanOrigin? scan = version >= 7 ? ReadScan(root) : null;
        Import.Transcription.TranscriptionOrigin? transcription = version >= 9 ? ReadTranscription(root, version) : null;
        if (scan is not null && transcription is not null)
        {
            throw Damaged("origin");
        }

        List<SavedPerformance> performances = version >= 13
            ? [.. Property(root, "performances", JsonValueKind.Array).EnumerateArray().Select(ReadPerformance)]
            : [];
        return ReadWork(root, root, version, unknowns) with
        {
            Source = source,
            Edits = version >= 3 ? new NoteEditLog(Property(root, "edits", JsonValueKind.Array).EnumerateArray().Select(ReadEdit)) : NoteEditLog.Empty,
            Scan = scan,
            Transcription = transcription,
            Check = version >= 12 ? ReadCheck(root, unknowns) : null,
            Performances = DistinctPerformances(performances),
            Document = pdf,
            DocumentMarks = root.TryGetProperty("documentMarks", out JsonElement documentMarksElement) ? ReadDocumentMarks(documentMarksElement, unknowns) : DocumentMarks.Empty,
            DocumentPages = root.TryGetProperty("documentPages", out JsonElement pagesElement) ? ReadDocumentPages(pagesElement, pdf, source) : null,
            ReadFrom = root.TryGetProperty("readFrom", out JsonElement readFromElement) ? ReadReadFrom(readFromElement, source) : null,
            KeptOriginals = ReadOriginals(root, unknowns),
        };
    }

    /// <summary>A PDF's bytes in base64, as "document" and an original's "data" hold them; anything else is damage.</summary>
    private static byte[] ReadPdf(JsonElement element, string what)
    {
        byte[] pdf;
        try
        {
            pdf = element.ValueKind == JsonValueKind.String ? element.GetBytesFromBase64() : throw Damaged(what);
        }
        catch (FormatException)
        {
            throw Damaged(what);
        }

        return PdfDocument.IsPdf(pdf) ? pdf : throw Damaged(what);
    }

    private static List<SavedPerformance> DistinctPerformances(List<SavedPerformance> performances) =>
        performances.Select(static performance => performance.Id).Distinct(StringComparer.Ordinal).Count() == performances.Count ? performances : throw Damaged("performances");

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

    /// <summary>One stroke in the pencil layer's shape (<see cref="MarksCodec.ReadStroke"/>): the facade (0020) reads a request's strokes with it.</summary>
    internal static InkStroke ReadStroke(JsonElement stroke) => MarksCodec.ReadStroke(stroke);

    /// <summary>"fingering" as the file writes it (<see cref="MarksCodec.WriteFingering"/>): the facade's marks answer writes it too.</summary>
    internal static void WriteFingering(Utf8JsonWriter json, IEnumerable<Fingering> fingering) => MarksCodec.WriteFingering(json, fingering);

    /// <summary>One variation step as the file writes it (<see cref="VariationCodec.WriteStep"/>): the facade (0020) uses the same shape.</summary>
    internal static void WriteStep(Utf8JsonWriter json, SavedStep step) => VariationCodec.WriteStep(json, step);

    /// <summary>Where each page of a recognised PDF starts in its notes (0023): ticks from page 1, which starts at 0.</summary>
    private static PageMap ReadDocumentPages(JsonElement pages, byte[]? pdf, byte[] source) =>
        pdf is null || source.Length == 0 ? throw Damaged("documentPages") : ReadPageMap(pages, "documentPages");

    /// <summary>The PDF piece notes were read from (0023): {"project": id, "pages": [ticks, …]}; only a piece with notes.</summary>
    private static ReadFromPdf ReadReadFrom(JsonElement element, byte[] source)
    {
        if (source.Length == 0 || element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("project", out JsonElement id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString())
            || !element.TryGetProperty("pages", out JsonElement pages))
        {
            throw Damaged("readFrom");
        }

        return new ReadFromPdf(id.GetString()!, ReadPageMap(pages, "readFrom"));
    }

    private static PageMap ReadPageMap(JsonElement pages, string field)
    {
        if (pages.ValueKind != JsonValueKind.Array)
        {
            throw Damaged(field);
        }

        try
        {
            return new PageMap([.. pages.EnumerateArray().Select(start => start.ValueKind == JsonValueKind.Number && start.TryGetInt64(out long value) && value >= 0
                ? new Tick(value)
                : throw Damaged(field))]);
        }
        catch (ArgumentException)
        {
            throw Damaged(field);
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
            return ResumePoint.Start(id.StartsWith("s_", StringComparison.Ordinal) ? "r_" + id[2..] : id, JsonRead.Time(session, "startedAt"), task)
                .Touch(passage, JsonRead.Time(session, "lastActive"));
        }
        catch (ArgumentException)
        {
            throw Damaged("session");
        }
    }

    /// <summary>A performance of a file before version 16: its notes, comparison and provenance in one object.</summary>
    private static SavedPerformance ReadPerformance(JsonElement performance)
    {
        Expect(performance, JsonValueKind.Object, "performances");
        try
        {
            (string? from, DateTimeOffset? at) = ReadMadeFrom(performance);
            SavedPerformance read = ReadComparison(
                performance,
                Text(performance, "id"),
                Text(performance, "name"),
                Property(performance, "midi", JsonValueKind.String).GetBytesFromBase64(),
                performance.TryGetProperty("engine", out _) ? Text(performance, "engine") : null);
            return read with { From = from, At = at };
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw Damaged("performance");
        }
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

        (string? from, DateTimeOffset? at) = ReadMadeFrom(scan);
        return ReadScanBlock(scan, Text(scan, "engine"), from, at);
    }

    /// <summary>A scan's checked bars and page reports, with the engine, original and time its provenance names.</summary>
    private static ScanOrigin ReadScanBlock(JsonElement scan, string engine, string? from, DateTimeOffset? at)
    {
        int[] bars = [.. Property(scan, "confirmedBars", JsonValueKind.Array)
            .EnumerateArray()
            .Select(bar => bar.ValueKind == JsonValueKind.Number && bar.TryGetInt32(out int value) && value >= 0 ? value : throw Damaged("checked bars"))];
        return new ScanOrigin(engine, bars) { From = from, At = at, Pages = ReadScanPages(scan) };
    }

    private static Import.Transcription.TranscriptionOrigin? ReadTranscription(JsonElement root, int version)
    {
        if (!root.TryGetProperty("transcription", out JsonElement transcription) || transcription.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object))
        {
            throw Damaged("transcription");
        }

        if (transcription.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        (string? from, DateTimeOffset? at) = ReadMadeFrom(transcription);
        return ReadHeard(transcription, version, Text(transcription, "engine"), from, at);
    }

    /// <summary>A transcription's bars, seams and stretch sent, with the engine, original and time its provenance names.</summary>
    private static Import.Transcription.TranscriptionOrigin ReadHeard(JsonElement transcription, int version, string engine, string? from, DateTimeOffset? at)
    {
        if (string.IsNullOrWhiteSpace(engine))
        {
            throw Damaged("engine");
        }

        // Seams came within version 12 (0018, preparation item 12): a file without them was heard in one part, or before they were kept.
        long[] seams = [];
        if (transcription.TryGetProperty("seams", out JsonElement saved))
        {
            seams = saved.ValueKind == JsonValueKind.Array
                ? [.. saved.EnumerateArray().Select(seam => seam.ValueKind == JsonValueKind.Number && seam.TryGetInt64(out long value) && value > 0 ? value : throw Damaged("seams"))]
                : throw Damaged("seams");
        }

        try
        {
            return new Import.Transcription.TranscriptionOrigin(engine, version >= 10 ? ReadBars(transcription, version) : null) { Seams = seams, From = from, At = at, SentFromMilliseconds = SentFromIn(transcription) };
        }
        catch (ArgumentException)
        {
            throw Damaged("seams");
        }
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
            return WithMetersIn(new BarGrid(new Tick(origin), Integer(bars, "sixteenthMicroseconds"), chosen, starts, beats, perBeat), bars);
        }
        catch (ArgumentException)
        {
            throw Damaged("bars");
        }
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
}