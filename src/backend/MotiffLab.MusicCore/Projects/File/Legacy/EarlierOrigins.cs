using System.Text.Json;

using MotiffLab.MusicCore.Performance;

using static MotiffLab.MusicCore.Projects.File.JsonRead;
using static MotiffLab.MusicCore.Projects.File.OriginCodec;
using static MotiffLab.MusicCore.Projects.File.PerformanceCodec;

namespace MotiffLab.MusicCore.Projects.File.Legacy;

/// <summary>
/// Where the notes and the performances of a file before version 16 came from: the scan (version 7) or the
/// transcription (version 9) at the top level, and each performance (version 13) with its notes, comparison and
/// provenance in one object, each with the original and the time it was made from (version 0.3).
/// </summary>
internal static class EarlierOrigins
{
    /// <summary>"from" and "at" on a scan, a transcription or a performance of a file before version 16 (version 0.3): the original the notes were made from, and when.</summary>
    private static (string? From, DateTimeOffset? At) ReadMadeFrom(JsonElement element) => (
        element.TryGetProperty("from", out _) ? Text(element, "from") : null,
        element.TryGetProperty("at", out _) ? JsonRead.Time(element, "at") : null);

    internal static ScanOrigin? ReadScan(JsonElement root)
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

    internal static Import.Transcription.TranscriptionOrigin? ReadTranscription(JsonElement root, int version)
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

    /// <summary>A performance of a file before version 16: its notes, comparison and provenance in one object.</summary>
    internal static SavedPerformance ReadPerformance(JsonElement performance)
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
}