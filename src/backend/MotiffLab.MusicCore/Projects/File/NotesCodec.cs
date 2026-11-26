using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Projects.File.EditsCodec;
using static MotiffLab.MusicCore.Projects.File.JsonRead;
using static MotiffLab.MusicCore.Projects.File.MarksCodec;
using static MotiffLab.MusicCore.Projects.File.OriginalsCodec;
using static MotiffLab.MusicCore.Projects.File.OriginCodec;
using static MotiffLab.MusicCore.Projects.File.PerformanceCodec;
using static MotiffLab.MusicCore.Projects.File.ProvenanceCodec;
using static MotiffLab.MusicCore.Projects.File.UnknownEntities;

namespace MotiffLab.MusicCore.Projects.File;

/// <summary>
/// The Notes layer (decision record 0030; docs/project-file.md, "Notes"): the working notes first, then the other Notes
/// and the compared ones, each with its provenance, music, corrections, Check flags and page map; a Notes of a later
/// build is kept whole as unknown where it stood.
/// </summary>
internal static class NotesCodec
{
    internal const string NotesList = "notes";

    private static readonly HashSet<string> s_notesFields = new(StringComparer.Ordinal) { "id", "name", "fileName", "provenance", "music", "edits", "check", "scan", "transcription", "pageMap", "performance" };

    /// <summary>
    /// The working notes first, when the piece has notes, then the other Notes and the compared ones, each in the order
    /// they were added.
    /// </summary>
    internal static void WriteNotesLayer(Utf8JsonWriter json, Project project)
    {
        List<object> entries = [.. project.HasNotes ? [project] : Array.Empty<object>(), .. project.Sources.Notes, .. project.Performances];
        IEnumerable<string> ids = entries.Select(entry => entry switch { OtherNotes other => other.Id, SavedPerformance performance => performance.Id, _ => project.NotesId });
        WriteList(json, NotesList, entries, KeptUnknown(project.Unknown, NotesList, ids, null, out _), entry =>
        {
            switch (entry)
            {
                case OtherNotes other:
                    WriteOtherNotes(json, project, other);
                    break;
                case SavedPerformance performance:
                    WritePerformanceNotes(json, project, performance);
                    break;
                default:
                    WriteWorkingNotes(json, project);
                    break;
            }
        });
    }

    private static void WriteWorkingNotes(Utf8JsonWriter json, Project project)
    {
        json.WriteStartObject();
        json.WriteString("id", project.NotesId);
        WriteProvenance(json, project.WorkingProvenance);
        json.WriteBase64String("music", project.Source);
        WriteEdits(json, project.Edits);
        WriteCheck(json, project.Check, KeptUnknown(project.Unknown, "check", [], null, out _));
        WriteOrigin(json, project.Scan, project.Transcription);
        WritePageMap(json, project.ReadFrom, project.DocumentPages);
        WriteUnknownFields(json, project.UnknownFieldsAt(ProjectFile.NotesPath + project.NotesId));
        json.WriteEndObject();
    }

    /// <summary>Other Notes: the working notes' fields, with their name and the file name their music is read by.</summary>
    private static void WriteOtherNotes(Utf8JsonWriter json, Project project, OtherNotes notes)
    {
        json.WriteStartObject();
        json.WriteString("id", notes.Id);
        if (notes.Name is { } name)
        {
            json.WriteString("name", name);
        }

        json.WriteString("fileName", notes.FileName);
        WriteProvenance(json, notes.Provenance);
        json.WriteBase64String("music", notes.Music);
        WriteEdits(json, notes.Edits);
        WriteCheck(json, notes.Check, []);
        WriteOrigin(json, notes.Scan, notes.Transcription);
        WritePageMap(json, notes.ReadFrom, notes.Pages);
        if (!notes.Marks.IsEmpty || notes.Marks.Unknown.Count > 0)
        {
            WriteMarks(json, notes.Marks);
        }

        WriteUnknownFields(json, project.UnknownFieldsAt(ProjectFile.NotesPath + notes.Id));
        json.WriteEndObject();
    }

    /// <summary>"pageMap": where each page of the piece's PDF starts in the working notes, or of another piece's PDF they were read from (0023).</summary>
    private static void WritePageMap(Utf8JsonWriter json, ReadFromPdf? readFrom, PageMap? documentPages)
    {
        (PageMap? pages, string? other) = readFrom is { } read ? (read.Pages, read.ProjectId) : (documentPages, null);
        if (pages is null)
        {
            return;
        }

        json.WriteStartObject("pageMap");
        json.WriteString("original", PieceOriginal.DocumentId);
        if (other is not null)
        {
            json.WriteString("project", other);
        }

        JsonWrite.Numbers(json, "starts", pages.Starts.Select(static start => start.Value));
        json.WriteEndObject();
    }

    /// <summary>
    /// The Notes: the working ones into the piece's notes, the compared ones into its performances, and any other (a later
    /// build's) kept whole as unknown where it stood.
    /// </summary>
    internal static Project ReadNotesLayer(Project project, JsonElement notes, string? working, Unknowns unknowns, Dictionary<string, IReadOnlyList<UnknownField>> fields)
    {
        List<SavedPerformance> performances = [];
        List<OtherNotes> others = [];
        HashSet<string> ids = new(StringComparer.Ordinal);
        bool found = false;
        int index = 0;
        foreach (JsonElement entry in notes.EnumerateArray())
        {
            Expect(entry, JsonValueKind.Object, NotesList);
            string id = Text(entry, "id");
            if (!ids.Add(id))
            {
                throw Damaged(NotesList);
            }

            KeepFields(fields, ProjectFile.NotesPath + id, entry, s_notesFields);
            if (id == working)
            {
                project = ReadWorkingNotes(project, entry, unknowns, fields);
                found = true;
            }
            else if (entry.TryGetProperty("performance", out JsonElement comparison) && !entry.TryGetProperty("edits", out _) && !entry.TryGetProperty("check", out _))
            {
                performances.Add(ReadPerformanceNotes(entry, comparison));
            }
            else if (entry.TryGetProperty("fileName", out _) && ReadOther(project, entry, unknowns, fields) is { } other)
            {
                others.Add(other);
            }
            else
            {
                unknowns.Keep(NotesList, index, entry, null, null);
            }

            index++;
        }

        return found || working is null ? project with { Performances = performances, Sources = project.Sources with { Notes = others } } : throw Damaged("working notes");
    }

    /// <summary>Other Notes, or null when a value in them is one this build does not know and the read keeps such entities whole.</summary>
    private static OtherNotes? ReadOther(Project project, JsonElement entry, Unknowns unknowns, Dictionary<string, IReadOnlyList<UnknownField>> fields)
    {
        try
        {
            return ReadOtherNotes(project, entry, fields);
        }
        catch (ProjectFileException reason) when (unknowns.Keeps(reason, entry))
        {
            return null;
        }
    }

    private static Project ReadWorkingNotes(Project project, JsonElement entry, Unknowns unknowns, Dictionary<string, IReadOnlyList<UnknownField>> fields)
    {
        if (entry.TryGetProperty("performance", out _))
        {
            throw Damaged("working notes");
        }

        NotesBody body = ReadNotesBody(entry, unknowns);
        string id = Text(entry, "id");
        (ReadFromPdf? readFrom, PageMap? pages) = ReadPagesOf(project, entry, id, unknowns, fields);
        return project with
        {
            NotesId = id,
            KeptProvenance = body.Provenance,
            Source = body.Music,
            Edits = body.Edits,
            Check = body.Check,
            Scan = body.Scan,
            Transcription = body.Heard,
            ReadFrom = readFrom,
            DocumentPages = pages,
        };
    }

    /// <summary>
    /// Other Notes (0030): the working notes' fields, with the file name their music is read by. A value this build does
    /// not know anywhere in them keeps them whole as unknown, as an entity of the list.
    /// </summary>
    private static OtherNotes ReadOtherNotes(Project project, JsonElement entry, Dictionary<string, IReadOnlyList<UnknownField>> fields)
    {
        if (entry.TryGetProperty("performance", out _))
        {
            throw Damaged(NotesList);
        }

        NotesBody body = ReadNotesBody(entry, new Unknowns(tolerant: false));
        string id = Text(entry, "id");
        (ReadFromPdf? readFrom, PageMap? pages) = ReadPagesOf(project, entry, id, null, fields);
        return new OtherNotes(id, Text(entry, "fileName"), body.Music, body.Provenance)
        {
            Name = entry.TryGetProperty("name", out _) ? Text(entry, "name") : null,
            Edits = body.Edits,
            Check = body.Check,
            Scan = body.Scan,
            Transcription = body.Heard,
            Pages = pages,
            ReadFrom = readFrom,
            Marks = OwnMarks(entry, ProjectFile.NotesPath + id, fields),
        };
    }

    /// <summary>
    /// Other Notes' own marks on the score (version 0.6): those drawn while they were the working notes, in the shape of
    /// the workspace's, with their own unknown entities; none when left out. Known here, so not kept as an unknown field.
    /// </summary>
    private static Marks OwnMarks(JsonElement entry, string path, Dictionary<string, IReadOnlyList<UnknownField>> fields)
    {
        if (!entry.TryGetProperty("marks", out JsonElement marks))
        {
            return Marks.Empty;
        }

        Expect(marks, JsonValueKind.Object, "marks");
        var apart = new Unknowns(tolerant: true);
        Marks read = ReadMarks(marks, apart);
        if (fields.TryGetValue(path, out IReadOnlyList<UnknownField>? kept))
        {
            fields[path] = [.. kept.Where(static field => field.Name != "marks")];
        }

        return read with { Unknown = [.. apart.Kept.OrderBy(static entity => entity.Index)] };
    }

    private readonly record struct NotesBody(NotesProvenance Provenance, byte[] Music, NoteEditLog Edits, IReadOnlyList<CheckFlag>? Check, ScanOrigin? Scan, TranscriptionOrigin? Heard);

    private static NotesBody ReadNotesBody(JsonElement entry, Unknowns unknowns)
    {
        NotesProvenance provenance = ReadProvenance(Property(entry, "provenance", JsonValueKind.Object));
        byte[] music;
        try
        {
            music = Property(entry, "music", JsonValueKind.String).GetBytesFromBase64();
        }
        catch (FormatException)
        {
            throw Damaged("music");
        }

        string? from = provenance.Original?.Original;
        ScanOrigin? scan = entry.TryGetProperty("scan", out JsonElement read)
            ? ReadScanBlock(Expected(read, "scan"), EngineOf(provenance), from, provenance.At)
            : null;
        TranscriptionOrigin? heard = entry.TryGetProperty("transcription", out JsonElement transcription)
            ? ReadHeard(Expected(transcription, "transcription"), ProjectFile.Version, EngineOf(provenance), from, provenance.At)
            : null;
        if (music.Length == 0 || (scan is not null && heard is not null))
        {
            throw Damaged(music.Length == 0 ? "music" : "origin");
        }

        NoteEditLog edits = new(Property(entry, "edits", JsonValueKind.Array).EnumerateArray().Select(ReadEdit));
        return new NotesBody(provenance, music, edits, ReadCheck(entry, unknowns), scan, heard);
    }

    /// <summary>
    /// A Notes' page map: of another piece's PDF they were read from (0023), else of this piece's PDF. While that PDF's
    /// description holds a value this build does not know, or the PDF was removed, the map is kept as the file held it, to
    /// be written back with it.
    /// </summary>
    private static (ReadFromPdf? ReadFrom, PageMap? Pages) ReadPagesOf(Project project, JsonElement entry, string id, Unknowns? unknowns, Dictionary<string, IReadOnlyList<UnknownField>> fields)
    {
        if (!entry.TryGetProperty("pageMap", out JsonElement pageMap))
        {
            return (null, null);
        }

        Expect(pageMap, JsonValueKind.Object, "pageMap");
        PageMap starts = ReadPageMap(Property(pageMap, "starts", JsonValueKind.Array), "pageMap");
        if (pageMap.TryGetProperty("project", out _))
        {
            return (new ReadFromPdf(Text(pageMap, "project"), starts), null);
        }

        if (Text(pageMap, "original") != PieceOriginal.DocumentId)
        {
            throw Damaged("pageMap");
        }

        if (project.Document is not null)
        {
            return (null, starts);
        }

        bool unknownPdf = (unknowns?.Kept ?? []).Concat(project.Unknown).Any(static entity => entity.List == OriginalsList && entity.Id == PieceOriginal.DocumentId);
        if (!unknownPdf && project.Sources.RemovedWith(PieceOriginal.DocumentId) is null)
        {
            throw Damaged("pageMap");
        }

        string path = ProjectFile.NotesPath + id;
        fields[path] = [new UnknownField("pageMap", pageMap.GetRawText()), .. fields.GetValueOrDefault(path) ?? []];
        return (null, null);
    }

    private static string EngineOf(NotesProvenance provenance) => provenance.Engine ?? throw Damaged("engine");

    internal static PageMap ReadPageMap(JsonElement pages, string field)
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
}