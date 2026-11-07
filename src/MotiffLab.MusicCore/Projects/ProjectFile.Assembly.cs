using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>The assembly in the project file (version 15, decision record 0024, section 7; the format is in docs/project-file.md).</summary>
public static partial class ProjectFile
{
    private static void WriteAssembly(Utf8JsonWriter json, PieceAssembly assembly)
    {
        json.WriteStartObject("assembly");
        json.WriteNumber("next", assembly.Next);
        json.WriteStartArray("steps");
        foreach (AssemblyStep step in assembly.Steps)
        {
            WriteAssemblyStep(json, step);
        }

        json.WriteEndArray();
        if (assembly.Applied is { } applied)
        {
            json.WriteStartObject("applied");
            WriteMusic(json, applied);
            json.WriteEndObject();
        }

        if (assembly.Before is { } before)
        {
            json.WriteStartObject("before");
            WriteMusic(json, before);
            json.WriteEndObject();
        }

        if (assembly.AppliedAt is { } at)
        {
            json.WriteString("appliedAt", at.ToString("O", CultureInfo.InvariantCulture));
        }

        json.WriteEndObject();
    }

    private static PieceAssembly ReadAssembly(JsonElement root)
    {
        if (!root.TryGetProperty("assembly", out JsonElement assembly))
        {
            return PieceAssembly.Empty;
        }

        Expect(assembly, JsonValueKind.Object, "assembly");
        try
        {
            List<AssemblyStep> steps = [.. Property(assembly, "steps", JsonValueKind.Array).EnumerateArray().Select(ReadAssemblyStep)];
            if (steps.Select(step => step.Number).Distinct().Count() != steps.Count)
            {
                throw Damaged("assembly");
            }

            return new PieceAssembly(steps, Integer(assembly, "next"))
            {
                Applied = assembly.TryGetProperty("applied", out JsonElement applied) ? ReadMusic(applied) : null,
                Before = assembly.TryGetProperty("before", out JsonElement before) ? ReadMusic(before) : null,
                AppliedAt = assembly.TryGetProperty("appliedAt", out _) ? Time(assembly, "appliedAt") : null,
            };
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            throw Damaged("assembly");
        }
    }

    private static void WriteAssemblyStep(Utf8JsonWriter json, AssemblyStep step)
    {
        json.WriteStartObject();
        json.WriteNumber("number", step.Number);
        json.WriteString("at", step.At.ToString("O", CultureInfo.InvariantCulture));
        switch (step)
        {
            case CarryStep carry:
                json.WriteString("kind", "carry");
                json.WriteString("source", carry.SourceId);
                json.WriteStartObject("from");
                WriteSpan(json, carry.From);
                json.WriteEndObject();
                WritePlace(json, "to", carry.To);
                json.WriteNumber("transpose", carry.Transpose);
                if (carry.Keep)
                {
                    json.WriteBoolean("keep", true);
                }

                break;
            case NoteStep note:
                json.WriteString("kind", "note");
                json.WriteString("note", note.NoteId);
                if (note.Semitones is { } semitones)
                {
                    json.WriteNumber("semitones", semitones);
                }
                else
                {
                    json.WriteNull("semitones");
                }

                break;
            case TransposeStep transpose:
                json.WriteString("kind", "transpose");
                WritePlace(json, "place", transpose.Place);
                json.WriteNumber("semitones", transpose.Semitones);
                break;
            default:
                throw new ArgumentException($"Unknown step {step.GetType().Name}.", nameof(step));
        }

        json.WriteEndObject();
    }

    private static AssemblyStep ReadAssemblyStep(JsonElement step)
    {
        Expect(step, JsonValueKind.Object, "assembly");
        int number = Integer(step, "number");
        DateTimeOffset at = Time(step, "at");
        return Text(step, "kind") switch
        {
            "carry" => new CarryStep(number, at, Text(step, "source"), ReadSpan(Property(step, "from", JsonValueKind.Object), "assembly"), ReadPlace(Property(step, "to", JsonValueKind.Object)), Integer(step, "transpose"),
                step.TryGetProperty("keep", out JsonElement keep) && keep.ValueKind == JsonValueKind.True),
            "note" => new NoteStep(number, at, Text(step, "note"), step.TryGetProperty("semitones", out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null),
            "transpose" => new TransposeStep(number, at, ReadPlace(Property(step, "place", JsonValueKind.Object)), Integer(step, "semitones")),
            _ => throw Damaged("assembly"),
        };
    }

    private static void WritePlace(Utf8JsonWriter json, string name, AssemblyPlace place)
    {
        json.WriteStartObject(name);
        WriteAnchor(json, "start", place.Start);
        if (place.End is { } end)
        {
            WriteAnchor(json, "end", end);
        }
        else
        {
            json.WriteNull("end");
        }

        json.WriteEndObject();
    }

    private static void WriteAnchor(Utf8JsonWriter json, string name, TimeAnchor anchor)
    {
        json.WriteStartObject(name);
        if (anchor.NoteId is { } id)
        {
            json.WriteString("note", id);
        }
        else
        {
            json.WriteNull("note");
        }

        json.WriteNumber("offset", anchor.Offset);
        json.WriteEndObject();
    }

    private static AssemblyPlace ReadPlace(JsonElement place)
    {
        bool ends = place.TryGetProperty("end", out JsonElement end) && end.ValueKind == JsonValueKind.Object;
        return new AssemblyPlace(ReadAnchor(Property(place, "start", JsonValueKind.Object)), ends ? ReadAnchor(end) : null);
    }

    private static TimeAnchor ReadAnchor(JsonElement anchor)
    {
        string? note = anchor.TryGetProperty("note", out JsonElement id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
        return new TimeAnchor(note, Long(anchor, "offset"));
    }

    private static void WriteMusic(Utf8JsonWriter json, SourceMusic music)
    {
        json.WriteStartArray("notes");
        foreach (Note note in music.Notes)
        {
            json.WriteStartObject();
            json.WriteString("id", note.Id);
            json.WriteNumber("pitch", note.Pitch.MidiNumber);
            json.WriteNumber("onset", note.Span.Onset.Value);
            json.WriteNumber("duration", note.Span.Duration);
            json.WriteNumber("velocity", note.Velocity);
            json.WriteNumber("track", note.Track);
            if (note.Voice is { } voice)
            {
                json.WriteNumber("voice", voice);
            }

            if (note.Hand is { } hand)
            {
                json.WriteString("hand", Lower(hand.Value));
                WriteAttribution(json, "handAttribution", hand.Attribution);
            }

            WriteAttribution(json, "attribution", note.Attribution);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("bars");
        foreach (AssemblyBar bar in music.Bars)
        {
            json.WriteStartObject();
            json.WriteNumber("onset", bar.Span.Onset.Value);
            json.WriteNumber("duration", bar.Span.Duration);
            json.WriteNumber("beats", bar.Signature.Numerator);
            json.WriteNumber("beatType", bar.Signature.Denominator);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("tempos");
        foreach (PlaybackTempo tempo in music.Tempos)
        {
            json.WriteStartObject();
            json.WriteNumber("at", tempo.At.Value);
            json.WriteNumber("microsecondsPerQuarter", tempo.MicrosecondsPerQuarter);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("keys");
        foreach (KeySignature key in music.Keys)
        {
            json.WriteStartObject();
            json.WriteNumber("at", key.At.Value);
            json.WriteNumber("fifths", key.Fifths);
            if (key.Mode is { } mode)
            {
                json.WriteString("mode", Lower(mode));
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static SourceMusic ReadMusic(JsonElement music)
    {
        Expect(music, JsonValueKind.Object, "assembly");
        List<Note> notes = [.. Property(music, "notes", JsonValueKind.Array).EnumerateArray().Select(note =>
        {
            Expect(note, JsonValueKind.Object, "assembly");
            long onset = Long(note, "onset");
            long duration = Long(note, "duration");
            return new Note
            {
                Id = Text(note, "id"),
                Pitch = new Pitch(Integer(note, "pitch")),
                Span = onset >= 0 && duration > 0 ? new TickSpan(new Tick(onset), duration) : throw Damaged("assembly"),
                Velocity = Integer(note, "velocity"),
                Track = Integer(note, "track"),
                Voice = note.TryGetProperty("voice", out _) ? Integer(note, "voice") : null,
                Hand = note.TryGetProperty("hand", out _) ? new Attributed<Hand>(LowerName<Hand>(note, "hand"), ReadAttribution(note, "handAttribution")) : null,
                Attribution = ReadAttribution(note, "attribution"),
            };
        })];
        List<AssemblyBar> bars = [.. Property(music, "bars", JsonValueKind.Array).EnumerateArray().Select(bar =>
        {
            Expect(bar, JsonValueKind.Object, "assembly");
            long onset = Long(bar, "onset");
            long duration = Long(bar, "duration");
            return onset >= 0 && duration > 0
                ? new AssemblyBar(new TickSpan(new Tick(onset), duration), new TimeSignature(Integer(bar, "beats"), Integer(bar, "beatType")))
                : throw Damaged("assembly");
        })];
        for (int i = 1; i < bars.Count; i++)
        {
            if (bars[i].Span.Onset != bars[i - 1].Span.End)
            {
                throw Damaged("assembly");
            }
        }

        List<PlaybackTempo> tempos = [.. Property(music, "tempos", JsonValueKind.Array).EnumerateArray().Select(tempo =>
            new PlaybackTempo(new Tick(Long(tempo, "at")), Integer(tempo, "microsecondsPerQuarter")))];
        List<KeySignature> keys = [.. Property(music, "keys", JsonValueKind.Array).EnumerateArray().Select(key =>
            new KeySignature(new Tick(Long(key, "at")), Integer(key, "fifths"), key.TryGetProperty("mode", out _) ? LowerName<KeyMode>(key, "mode") : null))];
        return new SourceMusic(notes, bars, tempos, keys);
    }

    private static void WriteAttribution(Utf8JsonWriter json, string name, Attribution attribution)
    {
        json.WriteStartObject(name);
        json.WriteString("provenance", Lower(attribution.Provenance));
        json.WriteNumber("confidence", attribution.Confidence);
        json.WriteEndObject();
    }

    private static Attribution ReadAttribution(JsonElement parent, string name)
    {
        JsonElement attribution = Property(parent, name, JsonValueKind.Object);
        return new Attribution(LowerName<Provenance>(attribution, "provenance"), Number(attribution, "confidence"));
    }
}