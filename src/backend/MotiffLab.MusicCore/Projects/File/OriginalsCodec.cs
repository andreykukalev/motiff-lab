using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;

using static MotiffLab.MusicCore.Projects.File.JsonRead;
using static MotiffLab.MusicCore.Projects.File.MarksCodec;
using static MotiffLab.MusicCore.Projects.File.UnknownEntities;

namespace MotiffLab.MusicCore.Projects.File;

/// <summary>
/// The originals layer (decision record 0030; docs/project-file.md, "Originals"): every original of the piece described
/// (version 0.3, local-first: Andrei 2026-11-11 21:20 UTC), with the PDF's bytes and every PDF's page marks, and the
/// tombstones of removed ones, each with the fields this build does not know kept (0026 S6).
/// </summary>
internal static class OriginalsCodec
{
    internal const string OriginalsList = "originals";

    private const string OriginalsPath = "originals/";

    private static readonly HashSet<string> s_originalFields = new(StringComparer.Ordinal)
    {
        "id", "kind", "name", "fileName", "folder", "sha256", "bytes", "pages", "durationMs", "addedAt", "kept", "held", "removed",
    };

    private static readonly HashSet<string> s_pdfFields = new([.. s_originalFields, "data", "marks"], StringComparer.Ordinal);

    private static readonly HashSet<string> s_removedFields = new([.. s_originalFields, "marks"], StringComparer.Ordinal);

    internal static void WriteOriginalsLayer(Utf8JsonWriter json, Project project)
    {
        List<(PieceOriginal Original, RemovedOriginal? Removed)> all =
            [.. project.Originals.Select(static original => (original, (RemovedOriginal?)null)), .. project.Sources.Removed.Select(static removed => (removed.Original, (RemovedOriginal?)removed))];
        List<UnknownEntity> unknown = KeptUnknown(project.Unknown, OriginalsList, all.Select(static entry => entry.Original.Id), null, out _);
        WriteList(json, OriginalsList, all, unknown, entry =>
        {
            json.WriteStartObject();
            WriteOriginalFields(json, entry.Original);
            bool pdf = entry.Removed is null && entry.Original.Id == PieceOriginal.DocumentId;
            if ((pdf ? project.Document : entry.Removed is null ? project.Sources.Data.GetValueOrDefault(entry.Original.Id) : null) is { } data)
            {
                json.WriteBase64String("data", data);
            }

            DocumentMarks marks = entry.Removed?.Marks ?? project.PageMarksOf(entry.Original.Id);
            if (!marks.IsEmpty || marks.Unknown.Count > 0)
            {
                WriteDocumentMarks(json, "marks", marks);
            }

            if (entry.Removed is { } removed)
            {
                json.WriteStartObject("removed");
                json.WriteString("at", removed.At.ToString("O", CultureInfo.InvariantCulture));
                json.WriteEndObject();
            }

            WriteUnknownFields(json, project.UnknownFieldsAt(OriginalsPath + entry.Original.Id));
            json.WriteEndObject();
        });
    }

    /// <summary>The fields of an original's description (version 0.3), as every original of version 16 starts.</summary>
    private static void WriteOriginalFields(Utf8JsonWriter json, PieceOriginal original)
    {
        json.WriteString("id", original.Id);
        json.WriteString("kind", original.Kind.ToString().ToLowerInvariant());
        if (original.Name is { } name)
        {
            json.WriteString("name", name);
        }

        json.WriteString("fileName", original.FileName);
        if (original.Folder is { } folder)
        {
            json.WriteString("folder", folder);
        }

        if (original.Sha256 is { } sha256)
        {
            json.WriteString("sha256", sha256);
        }

        if (original.Bytes is { } bytes)
        {
            json.WriteNumber("bytes", bytes);
        }

        if (original.Pages is { } pages)
        {
            json.WriteNumber("pages", pages);
        }

        if (original.DurationMs is { } duration)
        {
            json.WriteNumber("durationMs", duration);
        }

        json.WriteString("addedAt", original.AddedAt.ToString("O", CultureInfo.InvariantCulture));
        json.WriteString("kept", original.Kept.ToString().ToLowerInvariant());
        if (original.Held.Count > 0)
        {
            json.WriteStartArray("held");
            foreach (OriginalHeld mark in original.Held)
            {
                json.WriteStartObject();
                json.WriteString("device", mark.Device.ToString().ToLowerInvariant());
                json.WriteString("at", mark.At.ToString("O", CultureInfo.InvariantCulture));
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }
    }

    internal static (Project Project, List<PieceOriginal> Described) ReadOriginalsLayer(Project project, JsonElement originals, Unknowns unknowns, Dictionary<string, IReadOnlyList<UnknownField>> fields)
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

    internal static PieceOriginal ReadOriginal(JsonElement original)
    {
        Expect(original, JsonValueKind.Object, OriginalsList);
        try
        {
            return new PieceOriginal(Text(original, "id"), LowerName<OriginalKind>(original, "kind"), Text(original, "fileName"), LowerName<OriginalKept>(original, "kept"), JsonRead.Time(original, "addedAt"))
            {
                Name = original.TryGetProperty("name", out _) ? Text(original, "name") : null,
                Folder = original.TryGetProperty("folder", out JsonElement folder) ? FolderOf(folder) : null,
                Sha256 = original.TryGetProperty("sha256", out _) ? Text(original, "sha256") : null,
                Bytes = original.TryGetProperty("bytes", out _) ? Long(original, "bytes") : null,
                Pages = original.TryGetProperty("pages", out _) ? Integer(original, "pages") : null,
                DurationMs = original.TryGetProperty("durationMs", out _) ? Long(original, "durationMs") : null,
                Held = original.TryGetProperty("held", out JsonElement held)
                    ? [.. Elements(held, "originals held").Select(mark =>
                    {
                        Expect(mark, JsonValueKind.Object, "originals held");
                        return new OriginalHeld(LowerName<HoldingDevice>(mark, "device"), JsonRead.Time(mark, "at"));
                    })]
                    : [],
            };
        }
        catch (ArgumentException)
        {
            throw Damaged(OriginalsList);
        }
    }

    /// <summary>"folder" (version 0.5): where the file was inside the folder the player imported; empty text for the folder itself.</summary>
    private static string FolderOf(JsonElement folder) =>
        folder.ValueKind == JsonValueKind.String && PieceOriginal.IsFolder(folder.GetString()!) ? folder.GetString()! : throw Damaged(OriginalsList);

    /// <summary>A PDF's bytes in base64, as "document" and an original's "data" hold them; anything else is damage.</summary>
    internal static byte[] ReadPdf(JsonElement element, string what)
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
    /// The project read, when it is a piece: with notes, with its PDF, or (version 0.5) describing its PDF that is in the
    /// player's folder or on another device (<see cref="Project.DescribesDocument"/>); a file with none of them is damaged.
    /// </summary>
    internal static Project Described(this Project project) =>
        project.HasNotes || project.Document is not null || project.DescribesDocument ? project : throw Damaged("source");
}