using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// Writes the facade's answers (0020): every answer is one JSON object, written by hand so the
/// layout is fixed and the native build needs no reflection. Ticks are integers at 480 per quarter;
/// enums are lower camel case names (<see cref="JsonWrite"/>). This file holds the shapes every answer shares; each
/// feature's answers are in their own <c>Answer.&lt;Feature&gt;.cs</c>.
/// </summary>
internal static partial class Answer
{
    public static string Write(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            body(json);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static string Error(string code, string message) => Write(json =>
    {
        json.WriteString("error", code);
        json.WriteString("message", message);
    });

    public static void Span(Utf8JsonWriter json, string name, TickSpan span)
    {
        json.WriteStartObject(name);
        json.WriteNumber("onset", span.Onset.Value);
        json.WriteNumber("end", span.End.Value);
        json.WriteEndObject();
    }

    public static void Attribution(Utf8JsonWriter json, Attribution attribution)
    {
        json.WriteString("provenance", JsonWrite.Name(attribution.Provenance));
        json.WriteNumber("confidence", Math.Round(attribution.Confidence, 3));
    }

    public static void Note(Utf8JsonWriter json, Note note)
    {
        json.WriteStartObject();
        json.WriteString("id", note.Id);
        json.WriteNumber("pitch", note.Pitch.MidiNumber);
        json.WriteNumber("onset", note.Span.Onset.Value);
        json.WriteNumber("duration", note.Span.Duration);
        json.WriteNumber("velocity", note.Velocity);
        json.WriteNumber("track", note.Track);
        if (note.Voice is int voice)
        {
            json.WriteNumber("voice", voice);
        }

        if (note.Hand is { } hand)
        {
            json.WriteString("hand", JsonWrite.Name(hand.Value));
            json.WriteBoolean("handInferred", hand.Attribution.Provenance == Provenance.Inferred);
        }

        json.WriteString("provenance", JsonWrite.Name(note.Attribution.Provenance));
        json.WriteEndObject();
    }

    public static void Notes(Utf8JsonWriter json, string name, IEnumerable<Note> notes)
    {
        json.WriteStartArray(name);
        foreach (Note note in notes)
        {
            Note(json, note);
        }

        json.WriteEndArray();
    }

    /// <summary>A selection's filter, with only the fields it narrows by, the hand in lower camel case: the project file's shape.</summary>
    public static void Filter(Utf8JsonWriter json, string name, SelectionFilter filter) =>
        ProjectFile.WriteFilter(json, name, filter, static hand => JsonWrite.Name(hand));

    public static void Keep(Utf8JsonWriter json, string name, IEnumerable<KeepProperty> keep)
    {
        JsonWrite.Texts(json, name, keep.Order().Select(JsonWrite.Name));
    }

    public static void KeySignature(Utf8JsonWriter json, string name, KeySignature? key)
    {
        if (key is null)
        {
            json.WriteNull(name);
            return;
        }

        json.WritePropertyName(name);
        ProjectFile.WriteKeySignature(json, key);
    }

    public static void MusicalKey(Utf8JsonWriter json, string name, MusicalKey key)
    {
        json.WriteStartObject(name);
        json.WriteString("name", key.Name);
        json.WriteNumber("fifths", key.Fifths);
        json.WriteString("mode", JsonWrite.Name(key.Mode));
        json.WriteEndObject();
    }

    public static void Passage(Utf8JsonWriter json, string name, TickSpan span, int firstBar, int lastBar)
    {
        json.WriteStartObject(name);
        json.WriteNumber("onset", span.Onset.Value);
        json.WriteNumber("end", span.End.Value);
        json.WriteNumber("firstBar", firstBar);
        json.WriteNumber("lastBar", lastBar);
        json.WriteEndObject();
    }

    /// <summary>
    /// "doubt": why a chord, key or fact rests on recognised notes still to check (#352), written only when it does, so an
    /// answer about imported notes reads as before (contract 11).
    /// </summary>
    private static void Doubt(Utf8JsonWriter json, string? doubt)
    {
        if (doubt is not null)
        {
            json.WriteString("doubt", doubt);
        }
    }
}