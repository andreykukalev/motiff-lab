using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>The piece as the facade opens it (0020): what was imported, its tracks, keys, bars and notes.</summary>
internal static partial class Answer
{
    /// <summary>
    /// The piece as imported: its file, format, counts, tracks, key signatures and import report; with
    /// <paramref name="withNotes"/> also its bars, the start choices and the notes.
    /// </summary>
    public static void Piece(Utf8JsonWriter json, ImportedPiece piece, IReadOnlyList<Note> notes, bool withNotes, Words words = default)
    {
        json.WriteString("fileName", piece.FileName);
        json.WriteString("format", JsonWrite.Name(piece.Format));
        json.WriteNumber("noteCount", notes.Count);
        json.WriteNumber("barCount", MotifSelector.LastBar(piece.Meter, piece.End));
        json.WriteNumber("end", piece.End.Value);
        json.WriteBoolean("heard", Transcribed.IsHeard(piece));
        Tracks(json, piece.Tracks);
        json.WriteStartArray("keys");
        foreach (KeySignature key in piece.Keys)
        {
            ProjectFile.WriteKeySignature(json, key);
        }

        json.WriteEndArray();
        Report(json, piece.Report, words);
        if (!withNotes)
        {
            return;
        }

        Bars(json, piece.Meter.BarsBefore(piece.End));
        StartChoices(json, piece.StartChoices);
        Notes(json, "notes", notes);
    }

    private static void Tracks(Utf8JsonWriter json, IEnumerable<ImportedTrack> tracks)
    {
        json.WriteStartArray("tracks");
        foreach (ImportedTrack track in tracks)
        {
            json.WriteStartObject();
            json.WriteNumber("index", track.Index);
            json.WriteString("label", track.Label);
            if (track.Name is { } trackName)
            {
                json.WriteString("name", trackName);
            }

            if (track.InstrumentName is { } instrument)
            {
                json.WriteString("instrument", instrument);
            }

            json.WriteNumber("noteCount", track.NoteCount);
            json.WriteString("hands", JsonWrite.Name(track.Hands));
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void Report(Utf8JsonWriter json, ImportReport report, Words words)
    {
        json.WriteStartObject("report");
        json.WriteString("format", report.Format);
        json.WriteNumber("tracks", report.TrackCount);
        json.WriteNumber("notes", report.NoteCount);
        json.WriteNumber("measures", report.MeasureCount);
        json.WriteStartArray("issues");
        foreach (ImportIssue issue in report.Issues)
        {
            json.WriteStartObject();
            json.WriteString("kind", JsonWrite.Name(issue.Kind));
            json.WriteString("code", issue.Code);
            if (issue.Track is int track)
            {
                json.WriteNumber("track", track);
            }

            json.WriteNumber("count", issue.Count);
            json.WriteString("message", issue.Text(words));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void Bars(Utf8JsonWriter json, IEnumerable<Bar> bars)
    {
        json.WriteStartArray("bars");
        foreach (Bar bar in bars)
        {
            json.WriteStartObject();
            json.WriteNumber("number", bar.Number);
            json.WriteNumber("onset", bar.Span.Onset.Value);
            json.WriteNumber("end", bar.Span.End.Value);
            json.WriteNumber("numerator", bar.Signature.Numerator);
            json.WriteNumber("denominator", bar.Signature.Denominator);
            json.WriteBoolean("pickup", bar.IsPickup);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void StartChoices(Utf8JsonWriter json, IEnumerable<StartChoice> choices)
    {
        json.WriteStartArray("startChoices");
        foreach (StartChoice choice in choices)
        {
            json.WriteStartObject();
            json.WriteString("label", choice.Label);
            Filter(json, "filter", choice.Filter);
            json.WriteBoolean("inferred", choice.IsInferred);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }
}