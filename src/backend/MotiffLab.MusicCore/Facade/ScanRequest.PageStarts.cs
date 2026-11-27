using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>Where the pages of a scan's notes start, for check's answer (within contract 16, a field added; v1.5 i-7).</summary>
internal static partial class ScanRequest
{
    /// <summary>
    /// "pageStarts": where each page of a scan's notes starts, as scan.join joined the pages read (v1.5 i-7: the app marks
    /// there a page of the PDF no engine read, which the notes lack), each {"page" (from 1, in the notes' order), "bar" and
    /// "tick" (its first bar and where it starts), "lastBar" and "lastTick" (its last bar and where that starts)}; empty for
    /// notes no scan read.
    /// </summary>
    public static void PageStarts(Utf8JsonWriter json, Project project, ImportedPiece piece)
    {
        json.WriteStartArray("pageStarts");
        if (project.Scan is not null && piece.MusicXml is { Measures.Count: > 0 })
        {
            PageMap map = PageMap.Of(piece, ScanJoin.Split(project.Source).Count);
            for (int page = 1; page <= map.Pages; page++)
            {
                Tick start = map.StartOf(page);
                Bar last = piece.Meter.BarAt(new Tick(Math.Max(start.Value, (page < map.Pages ? map.StartOf(page + 1) : piece.End).Value - 1)));
                json.WriteStartObject();
                json.WriteNumber("page", page);
                json.WriteNumber("bar", piece.Meter.BarAt(start).Number);
                json.WriteNumber("tick", start.Value);
                json.WriteNumber("lastBar", last.Number);
                json.WriteNumber("lastTick", last.Span.Onset.Value);
                json.WriteEndObject();
            }
        }

        json.WriteEndArray();
    }
}