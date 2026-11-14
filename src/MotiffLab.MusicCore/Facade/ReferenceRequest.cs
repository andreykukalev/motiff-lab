using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Notation;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The Reference calls on piece 0 (contract 12, calls added; the Reference experiment, <see cref="KeyReference"/>): the circle
/// of fifths with any key's card, for the iPad's Circle sheet, which opens from the library and from a piece alike. Within
/// 12 (the Reference, part 2), reference.chord and reference.scale answer a chord's and a scale's own card, reference.sound
/// sounds them too, and reference.key on an open piece also answers the key the piece starts in, calls and fields added.
/// Within 12 (v0.5, the iPad's «Справочник»), reference.sounding and reference.explorer sound a chord or a scale as chosen and
/// the harmony explorer's progression, calls added.
/// </summary>
internal static partial class ReferenceRequest
{
    /// <summary>The Reference's calls on piece 0, by name.</summary>
    public static string Call(string name, Request fields) => name switch
    {
        "reference.key" => Key(fields),
        "reference.sound" => Sound(fields),
        "reference.chord" => Chord(fields),
        "reference.scale" => Scale(fields),
        "reference.sounding" => Sounding(fields),
        "reference.explorer" => Explorer(fields),
        _ => throw new FacadeException("unknownCall", $"There is no call \"{name}\" without a piece."),
    };

    /// <summary>
    /// {"fifths"?, "mode"? ("major" or "minor"), "language", "naming"} → {"signatures": the fifteen choices of the lookup,
    /// "sectors": the circle's twelve places, "card": the key's card, or null without "fifths", "circle": that key on the
    /// circle as the `circle` call writes one, or null, "pieceKey": on an open piece the key it starts in
    /// ({"fifths", "mode", "name", "inferred"}, <see cref="PieceDna.OpeningKey"/>), else null}.
    /// </summary>
    public static string Key(Request fields, DnaKey? pieceKey = null)
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
            PieceKey(json, pieceKey, words);
        });
    }

    /// <summary>
    /// {"fifths", "mode", "id"} → the `playback` answer of one scale or chord of the key's card, at the reference tempo; with
    /// "type" instead, of a position of that chord's card, and with "form", of that scale's card (both with "step" and "alter").
    /// </summary>
    public static string Sound(Request fields)
    {
        string id = fields.Text("id") ?? throw FacadeException.BadRequest("\"id\" is required.");
        ReferenceSound? sound;
        if (fields.Has("type"))
        {
            sound = ChordReference.For(RootOf(fields), TypeOf(fields)).Sound(id);
        }
        else if (fields.Has("form"))
        {
            ScaleCard scale = ScaleReference.For(RootOf(fields), FormOf(fields));
            sound = scale.Sound.Id == id ? scale.Sound : null;
        }
        else
        {
            int fifths = fields.Int("fifths") ?? throw FacadeException.BadRequest("\"fifths\" is required.");
            sound = KeyReference.For(KeyOf(fifths, fields)).Sound(id);
        }

        ReferenceSound heard = sound ?? throw FacadeException.BadRequest($"The card has no \"{id}\".");
        return Answer.Write(json => Answer.Playback(json, KeyReference.Playback(heard)));
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
            json.WriteString("form", ScaleReference.Id(scale.Form));
            Root(json, scale.Tonic);
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
            json.WriteString("type", ChordReference.Id(chord.Type));
            Root(json, chord.Root);
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

    private static void PieceKey(Utf8JsonWriter json, DnaKey? pieceKey, Words words)
    {
        if (pieceKey is null)
        {
            json.WriteNull("pieceKey");
            return;
        }

        json.WriteStartObject("pieceKey");
        json.WriteNumber("fifths", pieceKey.Key.Fifths);
        json.WriteString("mode", pieceKey.Key.Mode == KeyMode.Minor ? "minor" : "major");
        json.WriteString("name", pieceKey.Key.NameIn(words));
        json.WriteBoolean("inferred", pieceKey.Inferred);
        json.WriteEndObject();
    }

    private static void SoundOf(Utf8JsonWriter json, ReferenceSound sound)
    {
        json.WriteString("id", sound.Id);
        Answer.Numbers(json, "pitches", sound.Pitches);
        if (sound.Keys is { } keys)
        {
            Answer.Numbers(json, "keys", keys);
        }

        json.WriteNumber("keyboardLow", sound.KeyboardLow);
        json.WriteNumber("keyboardHigh", sound.KeyboardHigh);
    }
}