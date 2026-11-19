using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects.File;

using static MotiffLab.MusicCore.Projects.File.JsonRead;
using static MotiffLab.MusicCore.Projects.File.MarksCodec;
using static MotiffLab.MusicCore.Projects.File.UnknownEntities;

namespace MotiffLab.MusicCore.Projects;

/// <summary>Reading a version 16 file (decision record 0030): the originals, the Notes and the workspace into the shape both apps use.</summary>
public static partial class ProjectFile
{
    private static readonly HashSet<string> s_rootFields = new(StringComparer.Ordinal) { "format", "version", "id", "fileName", "createdAt", "lastOpened", OriginalsList, NotesList, WorkspacePath };

    private static readonly HashSet<string> s_notesFields = new(StringComparer.Ordinal) { "id", "name", "fileName", "provenance", "music", "edits", "check", "scan", "transcription", "pageMap", "performance" };

    private static readonly HashSet<string> s_originalFields = new(StringComparer.Ordinal)
    {
        "id", "kind", "name", "fileName", "folder", "sha256", "bytes", "pages", "durationMs", "addedAt", "kept", "held", "removed",
    };

    private static readonly HashSet<string> s_pdfFields = new([.. s_originalFields, "data", "marks"], StringComparer.Ordinal);

    private static readonly HashSet<string> s_removedFields = new([.. s_originalFields, "marks"], StringComparer.Ordinal);

    private static readonly HashSet<string> s_provenanceFields = new(StringComparer.Ordinal) { "op", "from", "inputs", "engine", "params", "at" };

    private static Project ReadLayers(JsonElement root, Unknowns unknowns)
    {
        JsonElement workspace = Property(root, WorkspacePath, JsonValueKind.Object);
        string? working = workspace.TryGetProperty("working", out _) ? Text(workspace, "working") : null;
        Dictionary<string, IReadOnlyList<UnknownField>> fields = new(StringComparer.Ordinal);
        KeepFields(fields, "", root, s_rootFields);
        KeepFields(fields, WorkspacePath, workspace, s_workspaceFields);
        Project project = ReadWork(root, workspace, Version, unknowns);
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

    private static (Project Project, List<PieceOriginal> Described) ReadOriginalsLayer(Project project, JsonElement originals, Unknowns unknowns, Dictionary<string, IReadOnlyList<UnknownField>> fields)
    {
        List<(PieceOriginal Original, JsonElement Element)> read = [.. ReadEntries(originals, OriginalsList, ReadOriginal, unknowns).Select(static entry => (entry.Value, entry.Element))];
        if (read.Select(static entry => entry.Original.Id).Distinct(StringComparer.Ordinal).Count() != read.Count)
        {
            throw Damaged(OriginalsList);
        }

        List<PieceOriginal> described = [];
        List<RemovedOriginal> removed = [];
        Dictionary<string, byte[]> kept = new(StringComparer.Ordinal);
        Dictionary<string, DocumentMarks> pageMarks = new(StringComparer.Ordinal);
        foreach ((PieceOriginal original, JsonElement element) in read)
        {
            bool live = !element.TryGetProperty("removed", out JsonElement tombstone);
            bool pdf = live && original.Id == PieceOriginal.DocumentId;
            KeepFields(fields, OriginalsPath + original.Id, element, live ? s_pdfFields : s_removedFields);
            if (pdf)
            {
                project = project with
                {
                    Document = element.TryGetProperty("data", out JsonElement data) ? ReadPdf(data, "original data") : null,
                    DocumentMarks = element.TryGetProperty("marks", out JsonElement marks) ? ReadDocumentMarks(marks, unknowns) : DocumentMarks.Empty,
                };
            }

            if (live)
            {
                if (!pdf && element.TryGetProperty("data", out JsonElement bytes))
                {
                    kept[original.Id] = original.Kind is OriginalKind.Pdf or OriginalKind.Images ? ReadPdf(bytes, "original data") : ReadBytes(bytes, "original data");
                }

                if (!pdf && element.TryGetProperty("marks", out JsonElement own))
                {
                    pageMarks[original.Id] = ReadMarksApart(own);
                }

                described.Add(original);
                continue;
            }

            Expect(tombstone, JsonValueKind.Object, OriginalsList);
            removed.Add(new RemovedOriginal(original, JsonRead.Time(tombstone, "at")) { Marks = element.TryGetProperty("marks", out JsonElement apart) ? ReadMarksApart(apart) : DocumentMarks.Empty });
        }

        return (project with { Sources = project.Sources with { Removed = removed, Data = kept, Marks = pageMarks } }, described);
    }

    private static byte[] ReadBytes(JsonElement element, string what)
    {
        try
        {
            return element.ValueKind == JsonValueKind.String && element.GetBytesFromBase64() is { Length: > 0 } bytes ? bytes : throw Damaged(what);
        }
        catch (FormatException)
        {
            throw Damaged(what);
        }
    }

    /// <summary>A tombstone's page marks, with their own unknown strokes, apart from the marks the piece shows.</summary>
    private static DocumentMarks ReadMarksApart(JsonElement marks)
    {
        var apart = new Unknowns(tolerant: true);
        DocumentMarks read = ReadDocumentMarks(marks, apart);
        return read with { Unknown = [.. apart.Kept.OrderBy(static entity => entity.Index)] };
    }

    /// <summary>
    /// The Notes: the working ones into the piece's notes, the compared ones into its performances, and any other (a later
    /// build's) kept whole as unknown where it stood.
    /// </summary>
    private static Project ReadNotesLayer(Project project, JsonElement notes, string? working, Unknowns unknowns, Dictionary<string, IReadOnlyList<UnknownField>> fields)
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

            KeepFields(fields, NotesPath + id, entry, s_notesFields);
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
            Marks = OwnMarks(entry, NotesPath + id, fields),
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
            ? ReadHeard(Expected(transcription, "transcription"), Version, EngineOf(provenance), from, provenance.At)
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

        string path = NotesPath + id;
        fields[path] = [new UnknownField("pageMap", pageMap.GetRawText()), .. fields.GetValueOrDefault(path) ?? []];
        return (null, null);
    }

    private static SavedPerformance ReadPerformanceNotes(JsonElement entry, JsonElement comparison)
    {
        Expect(comparison, JsonValueKind.Object, "performance");
        NotesProvenance provenance = ReadProvenance(Property(entry, "provenance", JsonValueKind.Object));
        try
        {
            return ReadComparison(comparison, Text(entry, "id"), Text(entry, "name"), Property(entry, "music", JsonValueKind.String).GetBytesFromBase64(), provenance.Engine) with
            {
                From = provenance.Original?.Original,
                At = provenance.At,
                Provenance = provenance,
            };
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw Damaged("performance");
        }
    }

    private static string EngineOf(NotesProvenance provenance) => provenance.Engine ?? throw Damaged("engine");

    /// <summary>A provenance (0030, section 2); an operation this build does not know is read in the same shape and kept.</summary>
    private static NotesProvenance ReadProvenance(JsonElement provenance)
    {
        Expect(provenance, JsonValueKind.Object, "provenance");
        try
        {
            return new NotesProvenance(Text(provenance, "op"))
            {
                From = provenance.TryGetProperty("from", out JsonElement from) ? [.. Elements(from, "provenance").Select(ReadProvenanceSource)] : [],
                Inputs = provenance.TryGetProperty("inputs", out JsonElement inputs)
                    ? [.. Elements(inputs, "provenance").Select(input => input.ValueKind == JsonValueKind.String ? input.GetString()! : throw Damaged("provenance"))]
                    : [],
                Engine = provenance.TryGetProperty("engine", out _) ? Text(provenance, "engine") : null,
                ParamsJson = provenance.TryGetProperty("params", out JsonElement parameters) ? Expected(parameters, "provenance").GetRawText() : null,
                At = provenance.TryGetProperty("at", out _) ? JsonRead.Time(provenance, "at") : null,
                Unknown = UnknownFieldsOf(provenance, s_provenanceFields),
            };
        }
        catch (ArgumentException)
        {
            throw Damaged("provenance");
        }
    }

    private static ProvenanceSource ReadProvenanceSource(JsonElement source)
    {
        Expect(source, JsonValueKind.Object, "provenance");
        (long First, long Last)? pages = source.TryGetProperty("pages", out JsonElement range) ? Pair(range) : null;
        (long First, long Last)? ms = source.TryGetProperty("ms", out JsonElement stretch) ? Pair(stretch) : null;
        try
        {
            return new ProvenanceSource(Text(source, "original"))
            {
                Sha256 = source.TryGetProperty("sha256", out _) ? Text(source, "sha256") : null,
                Pages = pages is var (first, last) ? (checked((int)first), checked((int)last)) : null,
                Milliseconds = ms,
                Project = source.TryGetProperty("project", out _) ? Text(source, "project") : null,
            };
        }
        catch (OverflowException)
        {
            throw Damaged("provenance");
        }
    }
}