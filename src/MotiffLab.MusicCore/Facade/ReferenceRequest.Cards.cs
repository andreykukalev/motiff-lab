using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Notation;

namespace MotiffLab.MusicCore.Facade;

/// <summary>The Reference's chord and scale cards on piece 0 (part 2, within contract 12, calls added).</summary>
internal static partial class ReferenceRequest
{
    /// <summary>
    /// {"step"?, "alter"?, "type"?, "language", "naming"} → {"roots": the seventeen roots ({"step", "alter", "letter",
    /// "name"}), "types": the chords to choose from ({"id", "suffix", "name"}), "card": the chord's card, or null without
    /// "type"}. A card: {"root", "type", "symbol", "name", "formula", "notes", "positions": [{"inversion", "title", "figure"
    /// (or null), "notes", "block", "arpeggio"}]}, each sound as a key card's ({"id", "pitches", "keyboardLow",
    /// "keyboardHigh"}) for reference.sound with the same "step", "alter" and "type".
    /// </summary>
    public static string Chord(Request fields)
    {
        Words words = Words.For(fields.Text("language"));
        NoteNaming naming = NoteNames.For(fields.Text("naming"), words);
        ChordCard? card = fields.Has("type") ? ChordReference.For(RootOf(fields), TypeOf(fields), naming, words) : null;
        return Answer.Write(json =>
        {
            Roots(json, naming);
            json.WriteStartArray("types");
            foreach (ChordTypeChoice choice in ChordReference.Types(words))
            {
                json.WriteStartObject();
                json.WriteString("id", choice.Id);
                json.WriteString("suffix", choice.Suffix);
                json.WriteString("name", choice.Name);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            ChordCardOf(json, card, naming);
        });
    }

    /// <summary>
    /// {"step"?, "alter"?, "form"?, "language", "naming"} → {"roots", "forms": the scales to choose from ({"id", "name"}),
    /// "card": the scale's card, or null without "form"}. A card: {"root", "form", "name", "title", "formula", "notes",
    /// "descending", "sound"}, its sound up and down with "keys", the way up the keyboard marks.
    /// </summary>
    public static string Scale(Request fields)
    {
        Words words = Words.For(fields.Text("language"));
        NoteNaming naming = NoteNames.For(fields.Text("naming"), words);
        ScaleCard? card = fields.Has("form") ? ScaleReference.For(RootOf(fields), FormOf(fields), naming, words) : null;
        return Answer.Write(json =>
        {
            Roots(json, naming);
            json.WriteStartArray("forms");
            foreach (ScaleFormChoice choice in ScaleReference.Forms(words))
            {
                json.WriteStartObject();
                json.WriteString("id", choice.Id);
                json.WriteString("name", choice.Name);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            ScaleCardOf(json, card, naming);
        });
    }

    /// <summary>The root a request names by "step" (0 for C to 6 for B) and "alter" (−1 to 1): one of <see cref="KeyReference.Roots"/>.</summary>
    private static SpelledName RootOf(Request fields)
    {
        var root = new SpelledName(fields.Int("step") ?? 0, fields.Int("alter") ?? 0);
        return KeyReference.Roots.Contains(root)
            ? root
            : throw FacadeException.BadRequest("\"step\" and \"alter\" name a root: a letter, or a black key as a sharp or a flat.");
    }

    private static ChordType TypeOf(Request fields) =>
        ChordReference.Parse(fields.Text("type")) ?? throw FacadeException.BadRequest("\"type\" is a chord the Reference offers, as reference.chord lists them.");

    private static ScaleForm FormOf(Request fields) =>
        ScaleReference.Parse(fields.Text("form")) ?? throw FacadeException.BadRequest("\"form\" is a scale the Reference offers, as reference.scale lists them.");

    private static void Roots(Utf8JsonWriter json, NoteNaming naming)
    {
        json.WriteStartArray("roots");
        foreach (SpelledName root in KeyReference.Roots)
        {
            json.WriteStartObject();
            RootFields(json, root, naming);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>"root": {"step", "alter", "letter", "name"}, where a chord's or a scale's own card starts.</summary>
    private static void Root(Utf8JsonWriter json, SpelledName root, NoteNaming naming = NoteNaming.Letters)
    {
        json.WriteStartObject("root");
        RootFields(json, root, naming);
        json.WriteEndObject();
    }

    private static void RootFields(Utf8JsonWriter json, SpelledName root, NoteNaming naming)
    {
        json.WriteNumber("step", root.Step);
        json.WriteNumber("alter", root.Alter);
        json.WriteString("letter", root.Letter);
        json.WriteString("name", root.Name(naming));
    }

    private static void ChordCardOf(Utf8JsonWriter json, ChordCard? card, NoteNaming naming)
    {
        if (card is null)
        {
            json.WriteNull("card");
            return;
        }

        json.WriteStartObject("card");
        Root(json, card.Root, naming);
        json.WriteString("type", ChordReference.Id(card.Type));
        json.WriteString("symbol", card.Symbol);
        json.WriteString("name", card.Name);
        Answer.Texts(json, "formula", card.Formula);
        Answer.Texts(json, "notes", card.Notes);
        json.WriteStartArray("positions");
        foreach (ChordPosition position in card.Positions)
        {
            json.WriteStartObject();
            json.WriteNumber("inversion", position.Inversion);
            json.WriteString("title", position.Title);
            json.WriteString("figure", position.Figure);
            Answer.Texts(json, "notes", position.Notes);
            json.WriteStartObject("block");
            SoundOf(json, position.Block);
            json.WriteEndObject();
            json.WriteStartObject("arpeggio");
            SoundOf(json, position.Arpeggio);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void ScaleCardOf(Utf8JsonWriter json, ScaleCard? card, NoteNaming naming)
    {
        if (card is null)
        {
            json.WriteNull("card");
            return;
        }

        json.WriteStartObject("card");
        Root(json, card.Tonic, naming);
        json.WriteString("form", ScaleReference.Id(card.Form));
        json.WriteString("name", card.Name);
        json.WriteString("title", card.Title);
        Answer.Texts(json, "formula", card.Formula);
        Answer.Texts(json, "notes", card.Notes);
        Answer.Texts(json, "descending", card.Descending);
        json.WriteStartObject("sound");
        SoundOf(json, card.Sound);
        json.WriteEndObject();
        json.WriteEndObject();
    }
}