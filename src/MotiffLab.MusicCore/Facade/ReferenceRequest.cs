using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Notation;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The Reference calls on piece 0 (contract 12, calls added; the Reference experiment, <see cref="KeyReference"/>): the circle
/// of fifths with any key's card, for the iPad's Circle sheet, which opens from the library and from a piece alike.
/// </summary>
internal static class ReferenceRequest
{
    /// <summary>
    /// {"fifths"?, "mode"? ("major" or "minor"), "language", "naming"} → {"signatures": the fifteen choices of the lookup,
    /// "sectors": the circle's twelve places, "card": the key's card, or null without "fifths", "circle": that key on the
    /// circle as the `circle` call writes one, or null}.
    /// </summary>
    public static string Key(Request fields)
    {
        Words words = Words.For(fields.Text("language"));
        NoteNaming naming = NoteNames.For(fields.Text("naming"), words);
        KeyCard? card = fields.Int("fifths") is { } fifths ? KeyReference.For(KeyOf(fifths, fields), naming, words) : null;
        return Answer.Write(json =>
        {
            json.WriteStartArray("signatures");
            foreach (SignatureChoice choice in KeyReference.Signatures(words))
            {
                json.WriteStartObject();
                json.WriteNumber("fifths", choice.Fifths);
                json.WriteString("label", choice.Label);
                json.WriteString("spoken", choice.Spoken);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            Answer.Sectors(json, KeyCircle.Sectors);
            Card(json, card);
            Answer.Circle(json, card is null ? null : KeyCircle.ForKey(card.Key, inferred: false, [], Time.MeterMap.Default, words));
        });
    }

    /// <summary>{"fifths", "mode", "id"} → the `playback` answer of one scale or chord of the key's card, at the reference tempo.</summary>
    public static string Sound(Request fields)
    {
        int fifths = fields.Int("fifths") ?? throw FacadeException.BadRequest("\"fifths\" is required.");
        string id = fields.Text("id") ?? throw FacadeException.BadRequest("\"id\" is required.");
        ReferenceSound sound = KeyReference.For(KeyOf(fifths, fields)).Sound(id) ?? throw FacadeException.BadRequest($"The card has no \"{id}\".");
        return Answer.Write(json => Answer.Playback(json, KeyReference.Playback(sound)));
    }

    private static MusicalKey KeyOf(int fifths, Request fields)
    {
        if (fifths is < -7 or > 7)
        {
            throw FacadeException.BadRequest("\"fifths\" must be from -7 to 7.");
        }

        return new MusicalKey(fifths, fields.Text("mode") == "minor" ? KeyMode.Minor : KeyMode.Major);
    }

    private static void Card(Utf8JsonWriter json, KeyCard? card)
    {
        if (card is null)
        {
            json.WriteNull("card");
            return;
        }

        json.WriteStartObject("card");
        Answer.MusicalKey(json, "key", card.Key);
        json.WriteString("name", card.Name);
        json.WriteNumber("position", card.Position);
        json.WriteStartObject("signature");
        json.WriteNumber("fifths", card.Signature.Fifths);
        json.WriteString("label", card.Signature.Label);
        json.WriteString("count", card.Signature.Count);
        Answer.Texts(json, "accidentals", card.Signature.Accidentals);
        Answer.Numbers(json, "trebleSteps", card.Signature.TrebleSteps);
        json.WriteEndObject();
        Related(json, "relative", card.Relative, card.RelativeName);
        Related(json, "parallel", card.Parallel, card.ParallelName);
        if (card.Enharmonic is { } enharmonic)
        {
            Related(json, "enharmonic", enharmonic, card.EnharmonicName ?? "");
        }
        else
        {
            json.WriteNull("enharmonic");
        }

        json.WriteStartArray("scales");
        foreach (ReferenceScale scale in card.Scales)
        {
            json.WriteStartObject();
            json.WriteString("title", scale.Title);
            Answer.Texts(json, "notes", scale.Notes);
            SoundOf(json, scale.Sound);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("chords");
        foreach (ReferenceChord chord in card.Chords)
        {
            json.WriteStartObject();
            json.WriteString("numeral", chord.Numeral);
            json.WriteString("function", chord.Function.ToString().ToLowerInvariant());
            json.WriteString("functionName", chord.FunctionName);
            json.WriteString("symbol", chord.Symbol);
            Answer.Texts(json, "notes", chord.Notes);
            SoundOf(json, chord.Sound);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void Related(Utf8JsonWriter json, string name, MusicalKey key, string spoken)
    {
        json.WriteStartObject(name);
        json.WriteNumber("fifths", key.Fifths);
        json.WriteString("mode", key.Mode == KeyMode.Minor ? "minor" : "major");
        json.WriteString("name", spoken);
        json.WriteEndObject();
    }

    private static void SoundOf(Utf8JsonWriter json, ReferenceSound sound)
    {
        json.WriteString("id", sound.Id);
        Answer.Numbers(json, "pitches", sound.Pitches);
        json.WriteNumber("keyboardLow", sound.KeyboardLow);
        json.WriteNumber("keyboardHigh", sound.KeyboardHigh);
    }
}