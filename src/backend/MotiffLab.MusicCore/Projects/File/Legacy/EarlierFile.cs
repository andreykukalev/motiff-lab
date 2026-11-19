using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Projects.File.EditsCodec;
using static MotiffLab.MusicCore.Projects.File.JsonRead;
using static MotiffLab.MusicCore.Projects.File.Legacy.EarlierOrigins;
using static MotiffLab.MusicCore.Projects.File.MarksCodec;
using static MotiffLab.MusicCore.Projects.File.NotesCodec;
using static MotiffLab.MusicCore.Projects.File.OriginalsCodec;
using static MotiffLab.MusicCore.Projects.File.SpanCodec;
using static MotiffLab.MusicCore.Projects.File.UnknownEntities;
using static MotiffLab.MusicCore.Projects.File.WorkspaceCodec;

namespace MotiffLab.MusicCore.Projects.File.Legacy;

/// <summary>
/// A file of a version before 16 (docs/project-file.md, "Opening an older file"): its notes, its PDF and its work at the
/// top level, read into the shape of version 16; and the work sessions of versions 8 to 11, read as resume points.
/// </summary>
internal static class EarlierFile
{
    /// <summary>A file of a version before 16: its notes, its PDF and its work at the top level.</summary>
    internal static Project ReadEarlier(JsonElement root, int version, Unknowns unknowns)
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

    private static List<SavedPerformance> DistinctPerformances(List<SavedPerformance> performances) =>
        performances.Select(static performance => performance.Id).Distinct(StringComparer.Ordinal).Count() == performances.Count ? performances : throw Damaged("performances");

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

    private static List<PieceOriginal> ReadOriginals(JsonElement root, Unknowns unknowns)
    {
        if (!root.TryGetProperty(OriginalsList, out JsonElement originals))
        {
            return [];
        }

        Expect(originals, JsonValueKind.Array, OriginalsList);
        List<PieceOriginal> read = ReadList(originals, OriginalsList, ReadOriginal, unknowns);
        return read.Select(static original => original.Id).Distinct(StringComparer.Ordinal).Count() == read.Count ? read : throw Damaged(OriginalsList);
    }

    /// <summary>A session of versions 8 to 11 as a resume point: its task is the name, its passage the place, "s_0001" becomes "r_0001".</summary>
    internal static ResumePoint ReadSessionAsResumePoint(JsonElement session)
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
}