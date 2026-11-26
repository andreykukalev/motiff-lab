using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects.File;

using static MotiffLab.MusicCore.Projects.File.JsonRead;
using static MotiffLab.MusicCore.Projects.File.Legacy.EarlierFile;
using static MotiffLab.MusicCore.Projects.File.NotesCodec;
using static MotiffLab.MusicCore.Projects.File.OriginalsCodec;
using static MotiffLab.MusicCore.Projects.File.UnknownEntities;
using static MotiffLab.MusicCore.Projects.File.WorkspaceCodec;

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
/// <para>
/// This class holds the document's top level and the version switch; each part of the file has its codec in
/// <c>Projects.File</c> (the originals, the Notes, the workspace, the marks, the variations, ...) and
/// <c>Projects.File.Legacy</c> reads the versions before 16. Nothing outside <c>Projects</c> uses them: the facade reads
/// and writes a stroke, the fingering and a variation step in the file's shape through this class.
/// </para>
/// </remarks>
public static class ProjectFile
{
    public const string FormatName = "motiff-lab-project";

    /// <summary>
    /// 17 lets a piece with no notes be only its recording, kept on the device (version 1.1, finding 29; a file of an
    /// earlier version with no notes and no PDF stays damaged), and adds the piece's drafts (0034, the drafts step);
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
    public const int Version = 17;

    /// <summary>The first version laid out in the three layers of decision record 0030; every later one keeps the layout.</summary>
    private const int LayersVersion = 16;

    /// <summary>The extension an exported project gets.</summary>
    public const string Extension = ".motiff.json";

    /// <summary>Where the unknown fields of a Notes stand in <see cref="Project.UnknownFields"/>: this and the Notes' id.</summary>
    internal const string NotesPath = "notes/";

    private static readonly HashSet<string> s_rootFields = new(StringComparer.Ordinal) { "format", "version", "id", "fileName", "createdAt", "lastOpened", OriginalsList, NotesList, WorkspacePath };

    /// <summary>
    /// The project as a file of <see cref="Version"/> in the layers of decision record 0030: its originals, its Notes and
    /// its workspace, each with the fields this build did not know when it read them (0026 S6). The same project always
    /// gives the same bytes.
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
    /// The project a file holds, of any version from 1 to <see cref="Version"/>, converted to <see cref="Version"/> in
    /// memory: the same file always gives the same project. A file of a later version is refused before any field is read.
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

            // Version 16 and 17 share a layout, and builds 0.6 to 1.0 wrote version 16 with the values they knew: an
            // unknown value in either is read as in a file of today's version (0026 S6).
            var unknowns = new Unknowns(version >= LayersVersion);
            Project project = version >= LayersVersion ? ReadLayers(root, version, unknowns) : ReadEarlier(root, version, unknowns);
            return unknowns.KeptIn(project).WithProvenanceKept().Described(version);
        }
    }

    private static Project ReadLayers(JsonElement root, int version, Unknowns unknowns)
    {
        JsonElement workspace = Property(root, WorkspacePath, JsonValueKind.Object);
        string? working = workspace.TryGetProperty("working", out _) ? Text(workspace, "working") : null;
        Dictionary<string, IReadOnlyList<UnknownField>> fields = new(StringComparer.Ordinal);
        KeepFields(fields, "", root, s_rootFields);
        KeepFields(fields, WorkspacePath, workspace, s_workspaceFields);
        Project project = ReadWork(root, workspace, version, unknowns);
        (project, List<PieceOriginal> described) = ReadOriginalsLayer(project, Property(root, OriginalsList, JsonValueKind.Array), unknowns, fields);
        project = ReadNotesLayer(project, Property(root, NotesList, JsonValueKind.Array), working, unknowns, fields);
        // The descriptions the piece's own data shows are worked out again, as for every earlier file, so they follow it.
        IReadOnlyList<PieceOriginal> derived = PieceOriginals.Derived(project);
        return project with
        {
            KeptOriginals = [.. described.Where(original => !derived.Contains(original))],
            // An idea or a motif built on the working notes says nothing more in memory, as one of an earlier file.
            Observations = [.. project.Observations.Select(observation => observation.Notes == working ? observation with { Notes = null } : observation)],
            Motifs = [.. project.Motifs.Select(motif => motif.Notes == working ? motif with { Notes = null } : motif)],
            UnknownFields = fields,
        };
    }

    /// <summary>
    /// The envelope of a project file (0026 S2): the format's name, a version of 1 or more (a later one too), the id and
    /// file name, a time it was last opened, and how many motifs, variations, resume points and ideas it keeps (the
    /// workspace's from version 16, the top level's before; none where a list is not there). Nothing else is read, so a file
    /// with a value this build does not know, or of a later version, passes.
    /// </summary>
    /// <exception cref="ProjectFileException">Not JSON, not a project file, or its envelope is damaged.</exception>
    public static ProjectEnvelope ReadEnvelope(ReadOnlySpan<byte> data)
    {
        using JsonDocument document = Parse(data);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out JsonElement format) || format.ValueKind != JsonValueKind.String || format.GetString() != FormatName)
        {
            throw new ProjectFileException("This is not a Motiff Lab project file.");
        }

        int version = Integer(root, "version");
        if (version < 1)
        {
            throw Damaged("version");
        }

        JsonElement work = version >= LayersVersion && root.TryGetProperty("workspace", out JsonElement workspace) && workspace.ValueKind == JsonValueKind.Object ? workspace : root;
        return new ProjectEnvelope(Text(root, "id"), version, Text(root, "fileName"), JsonRead.Time(root, "lastOpened"))
        {
            MotifCount = Count(work, "motifs"),
            VariationCount = Count(work, "variations"),
            ResumeCount = Count(work, "resumePoints"),
            IdeaCount = Count(work, "observations"),
        };
    }

    private static int Count(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement list) && list.ValueKind == JsonValueKind.Array ? list.GetArrayLength() : 0;

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
                MarksCodec.WriteStroke(json, stroke);
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

    /// <summary>One stroke in the pencil layer's shape (<see cref="MarksCodec.WriteStroke"/>), its start written by <paramref name="place"/>: the facade's marks answers write it so.</summary>
    internal static void WriteStroke(Utf8JsonWriter json, InkStroke stroke, Action<Utf8JsonWriter, InkStroke> place) => MarksCodec.WriteStroke(json, stroke, place);

    /// <summary>A filter as the file writes a range's (<see cref="SpanCodec.WriteFilter"/>), the hand named by <paramref name="handName"/>: the facade's answers use the same shape.</summary>
    internal static void WriteFilter(Utf8JsonWriter json, string name, SelectionFilter filter, Func<Hand, string> handName) => SpanCodec.WriteFilter(json, name, filter, handName);

    /// <summary>A key signature as the file writes an assembly's (<see cref="AssemblyCodec.WriteKeySignature"/>): the facade's answers use the same shape.</summary>
    internal static void WriteKeySignature(Utf8JsonWriter json, KeySignature key) => AssemblyCodec.WriteKeySignature(json, key);

    /// <summary>"fingering" as the file writes it (<see cref="MarksCodec.WriteFingering"/>): the facade's marks answer writes it too.</summary>
    internal static void WriteFingering(Utf8JsonWriter json, IEnumerable<Fingering> fingering) => MarksCodec.WriteFingering(json, fingering);

    /// <summary>One variation step as the file writes it (<see cref="VariationCodec.WriteStep"/>): the facade (0020) uses the same shape.</summary>
    internal static void WriteStep(Utf8JsonWriter json, SavedStep step) => VariationCodec.WriteStep(json, step);
}