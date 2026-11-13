using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Notation;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The «Справочник» v0.5 calls on piece 0 for the iPad (within contract 12, calls added): a chord or a scale as it sounds
/// (<see cref="ReferenceSounding"/>, «Аккорды и гаммы») and the harmony explorer (<see cref="HarmonyExplorer"/>), as the web
/// app calls them directly. Nothing is read from or written into a piece.
/// </summary>
internal static partial class ReferenceRequest
{
    /// <summary>The tempo the explorer starts at, quarters a minute (the web's ExplorerChoice).</summary>
    private const int ExplorerTempo = 72;

    /// <summary>
    /// {"step", "alter", "type" or "form", "inversion"?, "spacing"? ("close", "open"), "pattern"? (a chord: "together", "up",
    /// "down", "upAndDown", "broken"; a scale: "up", "down", "upAndDown"), "octaves"? (1 or 2, a scale), "language", "naming"} →
    /// the `playback` answer of the chord or scale as it sounds, with "mei" (its notation), "keys" ({"pitch", "name"}: the
    /// keys a keyboard marks), "keyboardLow", "keyboardHigh", "pattern", "patterns" (the ways offered); for a chord also
    /// "inversion" (at most its last position), "title", "figure", "bass", "spacing" and "spacings"; for a scale "octaves".
    /// Without them a chord sounds close, up and down, in root position, and a scale up and down over one octave.
    /// </summary>
    public static string Sounding(Request fields)
    {
        Words words = Words.For(fields.Text("language"));
        NoteNaming naming = NoteNames.For(fields.Text("naming"), words);
        if (fields.Has("form"))
        {
            int octaves = fields.Int("octaves") ?? 1;
            if (octaves is < 1 or > 2)
            {
                throw FacadeException.BadRequest("\"octaves\" is 1 or 2.");
            }

            ScalePattern way = fields.Enum<ScalePattern>("pattern") ?? ScalePattern.UpAndDown;
            ScaleSounding scale = ReferenceSounding.Scale(ScaleReference.For(RootOf(fields), FormOf(fields), naming, words), way, octaves);
            return Answer.Write(json =>
            {
                Music(json, scale.Music, naming);
                Choices(json, "patterns", way, "pattern");
                json.WriteNumber("octaves", octaves);
            });
        }

        ChordCard card = ChordReference.For(RootOf(fields), TypeOf(fields), naming, words);
        int inversion = fields.Int("inversion") ?? 0;
        if (inversion < 0)
        {
            throw FacadeException.BadRequest("\"inversion\" is 0 for root position or more.");
        }

        ChordSpacing spacing = fields.Enum<ChordSpacing>("spacing") ?? ChordSpacing.Close;
        ChordPattern pattern = fields.Enum<ChordPattern>("pattern") ?? ChordPattern.UpAndDown;
        ChordSounding chord = ReferenceSounding.Chord(card, Math.Min(inversion, card.Positions.Count - 1), spacing, pattern);
        return Answer.Write(json =>
        {
            Music(json, chord.Music, naming);
            json.WriteNumber("inversion", chord.Position.Inversion);
            json.WriteString("title", chord.Position.Title);
            json.WriteString("figure", chord.Position.Figure);
            json.WriteString("bass", chord.Position.Notes[0]);
            Choices(json, "spacings", spacing, "spacing");
            Choices(json, "patterns", pattern, "pattern");
        });
    }

    /// <summary>
    /// {"fifths"?, "mode"?, "steps"? ([{"chord": a chord's id, "inversion"}]), "texture"? ("together", "arpeggio", "broken",
    /// "alberti"), "spacing"? ("close", "open"), "tempo"? (quarters a minute, 30 to 240), "language", "naming"} → the `playback`
    /// answer of the progression in the key (C major without "fifths") with "keys" (the thirty to explore: {"fifths", "mode",
    /// "name"}), "key", "chords" (the key's: {"id", "numeral", "symbol", "positions": [{"inversion", "title", "figure"}]}),
    /// "steps" (the bars that sound; without "steps" the key's start, I–IV–V–I), "progression" (each bar's "chord",
    /// "numeral", "inversion", "figure", "symbol" and "pitches", the keys it sounds), "mei", "keyboardLow", "keyboardHigh",
    /// "maxBars", "texture", "textures", "spacing", "spacings", "tempo" and "tempoChoices" (the playback's own "tempos" are
    /// the music's). Without them the texture is Alberti, the spacing close and the tempo 72, as the web's explorer starts.
    /// </summary>
    public static string Explorer(Request fields)
    {
        Words words = Words.For(fields.Text("language"));
        NoteNaming naming = NoteNames.For(fields.Text("naming"), words);
        MusicalKey key = KeyOf(fields.Int("fifths") ?? 0, fields);
        IReadOnlyList<ExplorerStep> steps = fields.Has("steps") ? StepsOf(fields) : HarmonyExplorer.Start(key);
        ExplorerTexture texture = fields.Enum<ExplorerTexture>("texture") ?? ExplorerTexture.Alberti;
        ChordSpacing spacing = fields.Enum<ChordSpacing>("spacing") ?? ChordSpacing.Close;
        int tempo = fields.Int("tempo") ?? ExplorerTempo;
        if (tempo is < 30 or > 240)
        {
            throw FacadeException.BadRequest("\"tempo\" is from 30 to 240 quarters a minute.");
        }

        ExplorerProgression progression = HarmonyExplorer.Play(key, steps, texture, spacing, 60_000_000 / tempo);
        return Answer.Write(json =>
        {
            json.WriteStartArray("keys");
            foreach (MusicalKey offered in HarmonyExplorer.Keys)
            {
                ExplorerKey(json, null, offered, words);
            }

            json.WriteEndArray();
            ExplorerKey(json, "key", key, words);
            json.WriteNumber("maxBars", HarmonyExplorer.MaxBars);
            ExplorerChords(json, key, naming, words);
            json.WriteStartArray("steps");
            foreach (ExplorerBar bar in progression.Bars)
            {
                json.WriteStartObject();
                json.WriteString("chord", bar.Chord.Id);
                json.WriteNumber("inversion", bar.Inversion);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            Progression(json, progression);
            json.WriteString("mei", progression.Music.Mei);
            Answer.Playback(json, progression.Music.Playback());
            Choices(json, "textures", texture, "texture");
            Choices(json, "spacings", spacing, "spacing");
            json.WriteNumber("tempo", tempo);
            Answer.Numbers(json, "tempoChoices", HarmonyExplorer.Tempos);
        });
    }

    private static List<ExplorerStep> StepsOf(Request fields)
    {
        var steps = new List<ExplorerStep>();
        foreach (JsonElement element in fields.Elements("steps"))
        {
            using Request step = Request.Of(element);
            steps.Add(new ExplorerStep(step.Required("chord"), step.Int("inversion") ?? 0));
        }

        return steps;
    }

    /// <summary>The music's notation and sound, and the keys a keyboard marks, named as the naming names notes.</summary>
    private static void Music(Utf8JsonWriter json, ReferenceMusic music, NoteNaming naming)
    {
        json.WriteString("mei", music.Mei);
        json.WriteStartArray("keys");
        foreach (MarkedKey key in music.Keys)
        {
            json.WriteStartObject();
            json.WriteNumber("pitch", key.Midi);
            json.WriteString("name", key.NameIn(naming));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteNumber("keyboardLow", music.KeyboardLow);
        json.WriteNumber("keyboardHigh", music.KeyboardHigh);
        Answer.Playback(json, music.Playback());
    }

    /// <summary>The values of a choice offered, in lower camel case, and the one chosen.</summary>
    private static void Choices<T>(Utf8JsonWriter json, string offered, T chosen, string name)
        where T : struct, Enum
    {
        json.WriteStartArray(offered);
        foreach (T value in Enum.GetValues<T>())
        {
            json.WriteStringValue(Answer.Name(value));
        }

        json.WriteEndArray();
        json.WriteString(name, Answer.Name(chosen));
    }

    private static void ExplorerKey(Utf8JsonWriter json, string? name, MusicalKey key, Words words)
    {
        if (name is null)
        {
            json.WriteStartObject();
        }
        else
        {
            json.WriteStartObject(name);
        }

        json.WriteNumber("fifths", key.Fifths);
        json.WriteString("mode", key.Mode == KeyMode.Minor ? "minor" : "major");
        json.WriteString("name", key.NameIn(words));
        json.WriteEndObject();
    }

    private static void ExplorerChords(Utf8JsonWriter json, MusicalKey key, NoteNaming naming, Words words)
    {
        json.WriteStartArray("chords");
        foreach (ExplorerChord chord in HarmonyExplorer.Chords(key))
        {
            json.WriteStartObject();
            json.WriteString("id", chord.Id);
            json.WriteString("numeral", chord.Numeral);
            json.WriteString("symbol", chord.Symbol);
            json.WriteStartArray("positions");
            foreach (ChordPosition position in ChordReference.For(chord.Root, chord.Type, naming, words).Positions)
            {
                json.WriteStartObject();
                json.WriteNumber("inversion", position.Inversion);
                json.WriteString("title", position.Title);
                json.WriteString("figure", position.Figure);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>Each bar of the progression with the keys it sounds, and the stretch of keyboard from the C below them all to the B above.</summary>
    private static void Progression(Utf8JsonWriter json, ExplorerProgression progression)
    {
        const long BarTicks = 4L * Time.CanonicalTime.TicksPerQuarter;
        ILookup<long, int> sounding = progression.Music.Notes.ToLookup(note => note.Span.Onset.Value / BarTicks, note => note.Pitch.MidiNumber);
        json.WriteStartArray("progression");
        for (int index = 0; index < progression.Bars.Count; index++)
        {
            ExplorerBar bar = progression.Bars[index];
            json.WriteStartObject();
            json.WriteString("chord", bar.Chord.Id);
            json.WriteString("numeral", bar.Chord.Numeral);
            json.WriteNumber("inversion", bar.Inversion);
            json.WriteString("figure", bar.Figure);
            json.WriteString("symbol", bar.Symbol);
            Answer.Numbers(json, "pitches", sounding[index].Distinct().Order());
            json.WriteEndObject();
        }

        json.WriteEndArray();
        int[] pitches = [.. progression.Music.Notes.Select(note => note.Pitch.MidiNumber)];
        int low = pitches.Length == 0 ? 48 : pitches.Min() - (pitches.Min() % 12);
        int high = pitches.Length == 0 ? 83 : pitches.Max() - (pitches.Max() % 12) + 11;
        json.WriteNumber("keyboardLow", low);
        json.WriteNumber("keyboardHigh", high);
    }
}