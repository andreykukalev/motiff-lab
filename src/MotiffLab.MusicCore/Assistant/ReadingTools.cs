using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Assistant.ToolArguments;

namespace MotiffLab.MusicCore.Assistant;

/// <summary>The tools that read: the piece's digest, a range's facts, notes, chords and Check flags.</summary>
internal static class ReadingTools
{
    internal static IEnumerable<AssistantTool> All()
    {
        ToolSchema range = ToolSchema.Object(
            ("bars", BarsSchema, "The bars to read, at most 32."),
            ("hand", HandSchema, "One hand, or both."));
        ToolSchema bars = ToolSchema.Object(("bars", BarsSchema, "The bars to read, at most 32."));
        yield return new AssistantTool(
            "piece.describe",
            ToolKind.Read,
            "The digest of the whole piece: its bars and meters, the map's summary, the keys, the form, the closes and the open Check flags, each with an id to cite.",
            ToolSchema.Object(),
            static (piece, _) => ToolResult.Read(json => FactsDigest.OfPiece(piece).Write(json)));
        yield return new AssistantTool(
            "facts.get",
            ToolKind.Read,
            "The facts digest of a range: meter and key, the strongest facts music-core found with their certainty and places (note addresses), the chords, the Check flags and each hand's range. Cite only the ids it lists.",
            range,
            static (piece, parameters) => Ranged(piece, parameters, (from, to) => ToolResult.Read(json => FactsDigest.OfRange(piece, from, to, HandOf(parameters)).Write(json))));
        yield return new AssistantTool(
            "notes.list",
            ToolKind.Read,
            "The notes of a range, each by its address (bar.hand.index, such as 7.R.4), with pitch, where it starts in its bar and its length (fractions of a whole note), velocity and whether it was recognised or imported.",
            range,
            static (piece, parameters) => Ranged(piece, parameters, (from, to) => ToolResult.Read(json => WriteNotes(json, piece, piece.Read(from, to, HandOf(parameters))))));
        yield return new AssistantTool(
            "harmony.chords",
            ToolKind.Read,
            "The chords music-core reads in a range, in time order, with their place, confidence (0-100), alternatives and the notes (addresses) they rest on. Chords are inferred, never imported.",
            bars,
            static (piece, parameters) => Ranged(piece, parameters, (from, to) => ToolResult.Read(json => WriteChords(json, piece, piece.Read(from, to)))));
        yield return new AssistantTool(
            "check.flags",
            ToolKind.Read,
            "The Check flags of a range: the bars worth checking against the page or the recording, why, and whether the player resolved them. Only the player resolves a flag.",
            bars,
            static (piece, parameters) => Ranged(piece, parameters, (from, to) => ToolResult.Read(json => WriteFlags(json, piece, from, to))));
    }

    internal static void WriteNotes(Utf8JsonWriter json, AssistantPiece piece, RangeReading reading)
    {
        json.WriteStartObject();
        json.WriteStartArray("notes");
        foreach (Note note in reading.NotesOf(piece.Notes)
            .OrderBy(note => piece.Addresses.AddressOf(note.Id)!.Value.Bar)
            .ThenBy(note => piece.Addresses.AddressOf(note.Id)!.Value.Line, StringComparer.Ordinal)
            .ThenBy(note => piece.Addresses.AddressOf(note.Id)!.Value.Index))
        {
            json.WriteStartObject();
            json.WriteString("address", piece.Addresses.TextOf(note.Id));
            json.WriteString("pitch", note.Pitch.ToString());
            json.WriteNumber("midi", note.Pitch.MidiNumber);
            json.WriteString("at", NoteAddressBook.Fraction(piece.Addresses.InBar(note.Span.Onset)));
            json.WriteString("length", NoteAddressBook.Fraction(note.Span.Duration));
            json.WriteNumber("velocity", note.Velocity);
            json.WriteString("provenance", ToolSchema.Camel(note.Attribution.Provenance.ToString()));
            if (note.Hand is { Attribution.Provenance: Provenance.Inferred })
            {
                json.WriteBoolean("handInferred", true);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteChords(Utf8JsonWriter json, AssistantPiece piece, RangeReading reading)
    {
        MusicalKey spelling = reading.Motif.Key?.Key ?? new MusicalKey(0, KeyMode.Major);
        json.WriteStartObject();
        json.WriteStartArray("chords");
        foreach (ChordSpan chord in reading.Harmony.Chords)
        {
            MetricPosition at = piece.Meter.ToPosition(chord.Span.Onset);
            json.WriteStartObject();
            json.WriteNumber("bar", at.Bar);
            json.WriteNumber("beat", at.Beat);
            json.WriteString("symbol", chord.Symbol(spelling));
            json.WriteString("bass", chord.Bass.ToString());
            json.WriteNumber("confidence", chord.Confidence);
            json.WriteBoolean("uncertain", chord.IsUncertain);
            json.WriteString("length", NoteAddressBook.Fraction(chord.Span.Duration));
            json.WriteStartArray("alternatives");
            foreach (ChordLabel alternative in chord.Alternatives)
            {
                json.WriteStringValue(alternative.Symbol(spelling));
            }

            json.WriteEndArray();
            json.WriteStartArray("notes");
            foreach (string id in chord.NoteIds)
            {
                json.WriteStringValue(piece.Addresses.TextOf(id));
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteFlags(Utf8JsonWriter json, AssistantPiece piece, int from, int to)
    {
        json.WriteStartObject();
        json.WriteStartArray("flags");
        foreach (CheckFlag flag in piece.Check.Where(flag => flag.Bar >= from && flag.Bar <= to).OrderBy(static flag => flag.Bar).ThenBy(static flag => flag.Reason))
        {
            json.WriteStartObject();
            json.WriteNumber("bar", flag.Bar);
            json.WriteString("reason", ToolSchema.Camel(flag.Reason.ToString()));
            json.WriteString("source", ToolSchema.Camel(flag.Source.ToString()));
            json.WriteString("name", CheckFlag.NameOf(flag.Reason, piece.Words));
            json.WriteString("explanation", CheckFlag.Explain(flag.Reason, piece.Words));
            json.WriteBoolean("resolved", flag.Resolved);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }
}