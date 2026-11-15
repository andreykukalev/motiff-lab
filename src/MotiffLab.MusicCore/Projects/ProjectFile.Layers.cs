using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The originals and the Notes of version 16 (decision record 0030; docs/project-file.md, "Originals" and "Notes"): every
/// original with the PDF's bytes and page marks and the tombstones of removed ones, then every Notes with its provenance,
/// music, corrections, Check flags and comparison, each with the fields this build did not know kept (0026 S6).
/// </summary>
public static partial class ProjectFile
{
    private const string NotesList = "notes";
    private const string NotesPath = "notes/";
    private const string OriginalsPath = "originals/";

    private static void WriteOriginalsLayer(Utf8JsonWriter json, Project project)
    {
        List<(PieceOriginal Original, RemovedOriginal? Removed)> all =
            [.. project.Originals.Select(static original => (original, (RemovedOriginal?)null)), .. project.RemovedOriginals.Select(static removed => (removed.Original, (RemovedOriginal?)removed))];
        List<UnknownEntity> unknown = KeptUnknown(project.Unknown, OriginalsList, all.Select(static entry => entry.Original.Id), null, out _);
        WriteList(json, OriginalsList, all, unknown, entry =>
        {
            json.WriteStartObject();
            WriteOriginalFields(json, entry.Original);
            bool pdf = entry.Removed is null && entry.Original.Id == PieceOriginal.DocumentId;
            if (pdf && project.Document is { } document)
            {
                json.WriteBase64String("data", document);
            }

            DocumentMarks marks = entry.Removed?.Marks ?? (pdf ? project.DocumentMarks : DocumentMarks.Empty);
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

    /// <summary>The working notes first, when the piece has notes, then the compared Notes in the order they were added.</summary>
    private static void WriteNotesLayer(Utf8JsonWriter json, Project project)
    {
        List<SavedPerformance?> entries = project.HasNotes ? [null, .. project.Performances] : [.. project.Performances];
        IEnumerable<string> ids = entries.Select(entry => entry?.Id ?? project.NotesId);
        WriteList(json, NotesList, entries, KeptUnknown(project.Unknown, NotesList, ids, null, out _), entry =>
        {
            if (entry is null)
            {
                WriteWorkingNotes(json, project);
            }
            else
            {
                WritePerformanceNotes(json, project, entry);
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
        WriteCheck(json, project);
        if (project.Scan is { } scan)
        {
            json.WriteStartObject("scan");
            json.WriteStartArray("confirmedBars");
            foreach (int bar in scan.ConfirmedBars)
            {
                json.WriteNumberValue(bar);
            }

            json.WriteEndArray();
            WriteScanPages(json, scan.Pages);
            json.WriteEndObject();
        }

        if (project.Transcription is { } heard)
        {
            WriteHeard(json, heard);
        }

        WritePageMap(json, project);
        WriteUnknownFields(json, project.UnknownFieldsAt(NotesPath + project.NotesId));
        json.WriteEndObject();
    }

    private static void WriteCheck(Utf8JsonWriter json, Project project)
    {
        List<UnknownEntity> unknownFlags = KeptUnknown(project.Unknown, "check", [], null, out _);
        if (project.Check is null && unknownFlags.Count == 0)
        {
            json.WriteNull("check");
            return;
        }

        WriteList(json, "check", project.Check ?? [], unknownFlags, flag =>
        {
            json.WriteStartObject();
            json.WriteNumber("bar", flag.Bar);
            json.WriteString("source", flag.Source.ToString());
            json.WriteString("reason", flag.Reason.ToString());
            json.WriteBoolean("resolved", flag.Resolved);
            json.WriteEndObject();
        });
    }

    /// <summary>"transcription": the bars and tempo worked out, the seams and where the stretch sent started (0016; version 0.5).</summary>
    private static void WriteHeard(Utf8JsonWriter json, TranscriptionOrigin heard)
    {
        json.WriteStartObject("transcription");
        if (heard.Bars is { } bars)
        {
            json.WriteStartObject("bars");
            json.WriteNumber("origin", bars.Origin.Value);
            json.WriteNumber("sixteenthMicroseconds", bars.SixteenthMicroseconds);
            json.WriteString("signature", bars.Signature.ToString());
            WriteNumbers(json, "barStarts", bars.BarStarts.Select(static start => (long)start));
            json.WriteNumber("stepsPerBeat", bars.StepsPerBeat);
            WriteNumbers(json, "beats", bars.Beats);
            WriteMeters(json, bars);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("bars");
        }

        if (heard.Seams.Count > 0)
        {
            WriteNumbers(json, "seams", heard.Seams);
        }

        WriteSentFrom(json, heard);
        json.WriteEndObject();
    }

    private static void WriteNumbers(Utf8JsonWriter json, string name, IEnumerable<long> numbers)
    {
        json.WriteStartArray(name);
        foreach (long number in numbers)
        {
            json.WriteNumberValue(number);
        }

        json.WriteEndArray();
    }

    /// <summary>"pageMap": where each page of the piece's PDF starts in the working notes, or of another piece's PDF they were read from (0023).</summary>
    private static void WritePageMap(Utf8JsonWriter json, Project project)
    {
        (PageMap? pages, string? other) = project.ReadFrom is { } read ? (read.Pages, read.ProjectId) : (project.DocumentPages, null);
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

        WriteNumbers(json, "starts", pages.Starts.Select(static start => start.Value));
        json.WriteEndObject();
    }

    private static void WriteProvenance(Utf8JsonWriter json, NotesProvenance provenance)
    {
        json.WriteStartObject("provenance");
        json.WriteString("op", provenance.Op);
        json.WriteStartArray("from");
        foreach (ProvenanceSource source in provenance.From)
        {
            WriteProvenanceSource(json, source);
        }

        json.WriteEndArray();
        if (provenance.Inputs.Count > 0)
        {
            json.WriteStartArray("inputs");
            foreach (string input in provenance.Inputs)
            {
                json.WriteStringValue(input);
            }

            json.WriteEndArray();
        }

        if (provenance.Engine is { } engine)
        {
            json.WriteString("engine", engine);
        }

        if (provenance.ParamsJson is { } parameters)
        {
            WriteUnknownFields(json, [new UnknownField("params", parameters)]);
        }

        if (provenance.At is { } at)
        {
            json.WriteString("at", at.ToString("O", CultureInfo.InvariantCulture));
        }

        WriteUnknownFields(json, provenance.Unknown);
        json.WriteEndObject();
    }

    private static void WriteProvenanceSource(Utf8JsonWriter json, ProvenanceSource source)
    {
        json.WriteStartObject();
        json.WriteString("original", source.Original);
        if (source.Sha256 is { } sha256)
        {
            json.WriteString("sha256", sha256);
        }

        if (source.Pages is var (first, last))
        {
            WriteNumbers(json, "pages", [first, last]);
        }

        if (source.Milliseconds is var (from, to))
        {
            WriteNumbers(json, "ms", [from, to]);
        }

        if (source.Project is { } project)
        {
            json.WriteString("project", project);
        }

        json.WriteEndObject();
    }

    /// <summary>The fields kept unknown, each written back as the file held it, after the fields this build writes.</summary>
    private static void WriteUnknownFields(Utf8JsonWriter json, IReadOnlyList<UnknownField> fields)
    {
        foreach (UnknownField field in fields)
        {
            json.WritePropertyName(field.Name);
            using JsonDocument kept = JsonDocument.Parse(field.Json);
            kept.RootElement.WriteTo(json);
        }
    }
}