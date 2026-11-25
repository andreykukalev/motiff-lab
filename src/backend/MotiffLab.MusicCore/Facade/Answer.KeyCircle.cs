using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>The circle of fifths (0018): the selection's key among the twelve places, its neighbours and chords.</summary>
internal static partial class Answer
{
    /// <summary>
    /// The circle of fifths for the selection (<see cref="KeyCircle"/>): the twelve places, the key with its certainty and
    /// the key that fits almost as well, its neighbours, the chords on the sectors of their roots, the notes outside the
    /// scale, the scale to hear and the lines that say what it shows; null when no key was read.
    /// </summary>
    public static void Circle(Utf8JsonWriter json, KeyCircleView? circle, Tick at = default, Tick? end = null, string name = "circle")
    {
        if (circle is null)
        {
            json.WriteNull(name);
            return;
        }

        json.WriteStartObject(name);
        MusicalKey(json, "key", circle.Key);
        json.WriteString("name", circle.Name);
        json.WriteString("certainty", JsonWrite.Name(circle.Certainty));
        json.WriteNumber("position", circle.Position);
        if (circle.RunnerUp is { } runnerUp)
        {
            MusicalKey(json, "runnerUp", runnerUp);
            json.WriteNumber("runnerUpPosition", circle.RunnerUpPosition ?? 0);
            json.WriteString("runnerUpNote", circle.RunnerUpNote);
        }

        Sectors(json, circle.Sectors);
        json.WriteStartArray("neighbours");
        foreach (CircleNeighbour neighbour in circle.Neighbours)
        {
            json.WriteStartObject();
            json.WriteString("relation", JsonWrite.Name(neighbour.Relation));
            MusicalKey(json, "key", neighbour.Key);
            json.WriteNumber("position", neighbour.Position);
            json.WriteString("name", neighbour.Name);
            json.WriteString("label", neighbour.Label);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("chords");
        foreach (CircleChord chord in circle.Chords)
        {
            json.WriteStartObject();
            json.WriteNumber("number", chord.Number);
            json.WriteString("symbol", chord.Symbol);
            json.WriteString("degree", chord.Degree);
            json.WriteNumber("position", chord.Position);
            json.WriteString("ring", JsonWrite.Name(chord.Ring));
            json.WriteString("function", JsonWrite.Name(chord.Function));
            json.WriteString("role", chord.Role);
            CirclePlace(json, "place", chord.Place);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        if (circle.Outside is { } outside)
        {
            CirclePlace(json, "outside", outside);
        }

        JsonWrite.Numbers(json, "scale", circle.ScalePitches);
        json.WriteStartArray("scaleNotes");
        foreach (Note note in circle.ScaleNotes(at, end))
        {
            json.WriteStartObject();
            json.WriteNumber("pitch", note.Pitch.MidiNumber);
            json.WriteNumber("onset", note.Span.Onset.Value);
            json.WriteNumber("end", note.Span.End.Value);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        JsonWrite.Texts(json, "lines", circle.Lines);
        JsonWrite.Texts(json, "zones", circle.Zones.Select(static zone => JsonWrite.Name(zone)));
        json.WriteEndObject();
    }

    /// <summary>The circle's twelve places: each with its signature's fifths, its major and minor keys and the signature in short.</summary>
    public static void Sectors(Utf8JsonWriter json, IEnumerable<CircleSector> sectors)
    {
        json.WriteStartArray("sectors");
        foreach (CircleSector sector in sectors)
        {
            json.WriteStartObject();
            json.WriteNumber("position", sector.Position);
            json.WriteNumber("fifths", sector.Fifths);
            json.WriteString("major", sector.Major);
            json.WriteString("minor", sector.Minor);
            json.WriteString("signature", sector.Signature);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void CirclePlace(Utf8JsonWriter json, string name, FactPlace place)
    {
        json.WritePropertyName(name);
        PlaceOfFact(json, place);
    }
}