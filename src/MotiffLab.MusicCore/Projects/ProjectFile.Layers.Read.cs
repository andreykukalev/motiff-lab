using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Transcription;

namespace MotiffLab.MusicCore.Projects;

/// <summary>Reading a version 16 file (decision record 0030): the originals, the Notes and the workspace into the shape both apps use.</summary>
public static partial class ProjectFile
{
    private static readonly HashSet<string> s_rootFields = new(StringComparer.Ordinal) { "format", "version", "id", "fileName", "createdAt", "lastOpened", OriginalsList, NotesList, WorkspacePath };

    private static readonly HashSet<string> s_notesFields = new(StringComparer.Ordinal) { "id", "name", "provenance", "music", "edits", "check", "scan", "transcription", "pageMap", "performance" };

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
            // An idea built on the working notes says nothing more in memory, as an idea of an earlier file.
            Observations = [.. project.Observations.Select(observation => observation.Notes == working ? observation with { Notes = null } : observation)],
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
        foreach ((PieceOriginal original, JsonElement element) in read)
        {
            bool live = !element.TryGetProperty("removed", out JsonElement tombstone);
            bool pdf = live && original.Id == PieceOriginal.DocumentId;
            KeepFields(fields, OriginalsPath + original.Id, element, pdf ? s_pdfFields : live ? s_originalFields : s_removedFields);
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
                described.Add(original);
                continue;
            }

            Expect(tombstone, JsonValueKind.Object, OriginalsList);
            removed.Add(new RemovedOriginal(original, Time(tombstone, "at")) { Marks = element.TryGetProperty("marks", out JsonElement kept) ? ReadMarksApart(kept) : DocumentMarks.Empty });
        }

        return (project with { RemovedOriginals = removed }, described);
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
            else
            {
                unknowns.Keep(NotesList, index, entry, null, null);
            }

            index++;
        }

        return found || working is null ? project with { Performances = performances } : throw Damaged("working notes");
    }

    private static Project ReadWorkingNotes(Project project, JsonElement entry, Unknowns unknowns, Dictionary<string, IReadOnlyList<UnknownField>> fields)
    {
        if (entry.TryGetProperty("performance", out _))
        {
            throw Damaged("working notes");
        }

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

        project = project with
        {
            NotesId = Text(entry, "id"),
            KeptProvenance = provenance,
            Source = music,
            Edits = new NoteEditLog(Property(entry, "edits", JsonValueKind.Array).EnumerateArray().Select(ReadEdit)),
            Check = ReadCheck(entry, unknowns),
            Scan = scan,
            Transcription = heard,
        };
        if (!entry.TryGetProperty("pageMap", out JsonElement pageMap))
        {
            return project;
        }

        if (project.Document is null && !pageMap.TryGetProperty("project", out _) && unknowns.Kept.Any(static entity => entity.List == OriginalsList && entity.Id == PieceOriginal.DocumentId))
        {
            // The PDF's description holds a value this build does not know, so it is kept whole and the piece opens without
            // it; the page map of it is kept as the file held it, to be written back with it.
            string path = NotesPath + project.NotesId;
            fields[path] = [new UnknownField("pageMap", pageMap.GetRawText()), .. fields.GetValueOrDefault(path) ?? []];
            return project;
        }

        return WithPageMap(project, Expected(pageMap, "pageMap"));
    }

    /// <summary>The working notes' page map: of another piece's PDF they were read from (0023), else of this piece's PDF, which must be here.</summary>
    private static Project WithPageMap(Project project, JsonElement pageMap)
    {
        PageMap starts = ReadPageMap(Property(pageMap, "starts", JsonValueKind.Array), "pageMap");
        if (pageMap.TryGetProperty("project", out _))
        {
            return project with { ReadFrom = new ReadFromPdf(Text(pageMap, "project"), starts) };
        }

        return Text(pageMap, "original") == PieceOriginal.DocumentId && project.Document is not null ? project with { DocumentPages = starts } : throw Damaged("pageMap");
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

    private static JsonElement Expected(JsonElement element, string what)
    {
        Expect(element, JsonValueKind.Object, what);
        return element;
    }

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
                At = provenance.TryGetProperty("at", out _) ? Time(provenance, "at") : null,
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

    private static List<UnknownField> UnknownFieldsOf(JsonElement element, IReadOnlySet<string> known) =>
        [.. element.EnumerateObject().Where(field => !known.Contains(field.Name)).Select(static field => new UnknownField(field.Name, field.Value.GetRawText()))];

    private static void KeepFields(Dictionary<string, IReadOnlyList<UnknownField>> fields, string path, JsonElement element, IReadOnlySet<string> known)
    {
        List<UnknownField> unknown = UnknownFieldsOf(element, known);
        if (unknown.Count > 0)
        {
            fields[path] = unknown;
        }
    }
}