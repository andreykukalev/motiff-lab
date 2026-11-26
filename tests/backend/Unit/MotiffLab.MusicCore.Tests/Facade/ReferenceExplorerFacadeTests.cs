using System.Text.Json;

using MotiffLab.MusicCore.Facade;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The «Справочник» v0.5 calls the iPad makes on piece 0 (within contract 12, calls added): reference.explorer, the harmony
/// explorer as the web calls HarmonyExplorer, and reference.sounding, a chord or a scale as «Аккорды и гаммы» sounds it.
/// </summary>
public sealed class ReferenceExplorerFacadeTests
{
    [Fact]
    public void The_explorer_starts_a_key_on_I_IV_V_I_in_Alberti_at_72()
    {
        var facade = new MusicCoreFacade();
        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "reference.explorer", """{"fifths": 0, "mode": "major", "language": "en"}"""));
        JsonElement root = answer.RootElement;

        Assert.Equal(30, root.GetProperty("keys").GetArrayLength());
        Assert.Equal("C major", root.GetProperty("key").GetProperty("name").GetString());
        Assert.Equal(["I", "ii", "iii", "IV", "V", "V7", "vi", "vii°"], Texts(root.GetProperty("chords"), "numeral"));
        Assert.Equal(["I", "IV", "V", "I"], Texts(root.GetProperty("progression"), "numeral"));
        Assert.Equal(["I", "IV", "V", "I"], Texts(root.GetProperty("steps"), "chord"));
        Assert.Equal("alberti", root.GetProperty("texture").GetString());
        Assert.Equal(["together", "arpeggio", "broken", "alberti"], root.GetProperty("textures").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("close", root.GetProperty("spacing").GetString());
        Assert.Equal(72, root.GetProperty("tempo").GetInt32());
        Assert.Equal([60, 72, 90, 108, 120], root.GetProperty("tempoChoices").EnumerateArray().Select(value => value.GetInt32()));
        Assert.Equal(8, root.GetProperty("maxBars").GetInt32());
        Assert.Equal(60_000_000 / 72, Tempo(root));
        // Each bar: the bass held and eight quavers over it.
        Assert.Equal(36, root.GetProperty("notes").GetArrayLength());
        Assert.StartsWith("<?xml", root.GetProperty("mei").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_explorer_sounds_the_steps_sent_and_leaves_out_what_the_key_has_not()
    {
        var facade = new MusicCoreFacade();
        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "reference.explorer",
            """{"fifths": 0, "mode": "minor", "steps": [{"chord": "i"}, {"chord": "V7", "inversion": 1}, {"chord": "X"}, {"chord": "i", "inversion": 5}], "texture": "together", "spacing": "open", "tempo": 90, "language": "ru"}"""));
        JsonElement root = answer.RootElement;
        JsonElement progression = root.GetProperty("progression");

        Assert.Equal("ля минор", root.GetProperty("key").GetProperty("name").GetString());
        Assert.Equal(2, progression.GetArrayLength());
        Assert.Equal("6/5", progression[1].GetProperty("figure").GetString());
        Assert.Equal("E7/G♯", progression[1].GetProperty("symbol").GetString());
        Assert.Equal(JsonValueKind.Null, progression[0].GetProperty("figure").ValueKind);
        Assert.Equal(2, root.GetProperty("steps").GetArrayLength());
        Assert.Equal(4, root.GetProperty("chords").EnumerateArray().First(chord => chord.GetProperty("id").GetString() == "V7").GetProperty("positions").GetArrayLength());
        // Together: the bass and the three tones of the right hand, an octave up when open.
        Assert.Equal(4, progression[0].GetProperty("pitches").GetArrayLength());
        Assert.Equal(8, root.GetProperty("notes").GetArrayLength());
        Assert.Equal(60_000_000 / 90, Tempo(root));
        Assert.True(root.GetProperty("keyboardLow").GetInt32() % 12 == 0);
        Assert.True(root.GetProperty("keyboardHigh").GetInt32() % 12 == 11);
    }

    [Fact]
    public void The_explorer_with_no_steps_sounds_nothing_and_refuses_what_it_does_not_offer()
    {
        var facade = new MusicCoreFacade();
        using JsonDocument empty = JsonDocument.Parse(facade.Call(0, "reference.explorer", """{"fifths": 2, "mode": "major", "steps": []}"""));
        using JsonDocument texture = JsonDocument.Parse(facade.Call(0, "reference.explorer", """{"texture": "waltz"}"""));
        using JsonDocument tempo = JsonDocument.Parse(facade.Call(0, "reference.explorer", """{"tempo": 400}"""));

        Assert.Equal(0, empty.RootElement.GetProperty("progression").GetArrayLength());
        Assert.Equal(0, empty.RootElement.GetProperty("notes").GetArrayLength());
        Assert.Equal("badRequest", texture.RootElement.GetProperty("error").GetString());
        Assert.Equal("badRequest", tempo.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void A_chord_sounds_in_its_position_spacing_and_way()
    {
        var facade = new MusicCoreFacade();
        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "reference.sounding",
            """{"step": 4, "alter": 0, "type": "dominant7", "inversion": 1, "spacing": "open", "pattern": "together", "language": "en", "naming": "letters"}"""));
        using JsonDocument turned = JsonDocument.Parse(facade.Call(0, "reference.sounding", """{"step": 0, "alter": 0, "type": "major", "inversion": 7}"""));
        JsonElement root = answer.RootElement;

        // G7 in its first inversion, open: B2 F3 D4 G4 (decision of 2026-11-12, ReferenceSounding.Spread).
        Assert.Equal([47, 53, 62, 67], root.GetProperty("notes").EnumerateArray().Select(note => note.GetProperty("pitch").GetInt32()));
        Assert.Equal(["B", "F", "D", "G"], Texts(root.GetProperty("keys"), "name"));
        Assert.Equal("6/5", root.GetProperty("figure").GetString());
        Assert.Equal("B", root.GetProperty("bass").GetString());
        Assert.Equal(["close", "open"], root.GetProperty("spacings").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(["together", "up", "down", "upAndDown", "broken"], root.GetProperty("patterns").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(2, turned.RootElement.GetProperty("inversion").GetInt32());
        Assert.Equal("upAndDown", turned.RootElement.GetProperty("pattern").GetString());
    }

    [Fact]
    public void A_scale_sounds_up_down_or_both_over_one_or_two_octaves()
    {
        var facade = new MusicCoreFacade();
        using JsonDocument one = JsonDocument.Parse(facade.Call(0, "reference.sounding", """{"step": 1, "alter": 0, "form": "harmonicMinor", "language": "ru", "naming": "syllables"}"""));
        using JsonDocument two = JsonDocument.Parse(facade.Call(0, "reference.sounding", """{"step": 1, "alter": 0, "form": "harmonicMinor", "pattern": "up", "octaves": 2}"""));
        using JsonDocument wrong = JsonDocument.Parse(facade.Call(0, "reference.sounding", """{"step": 1, "alter": 0, "form": "major", "octaves": 3}"""));

        Assert.Equal(15, one.RootElement.GetProperty("notes").GetArrayLength());
        Assert.Equal(8, one.RootElement.GetProperty("keys").GetArrayLength());
        Assert.Equal("ре", one.RootElement.GetProperty("keys")[0].GetProperty("name").GetString());
        Assert.Equal(["up", "down", "upAndDown"], one.RootElement.GetProperty("patterns").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(15, two.RootElement.GetProperty("notes").GetArrayLength());
        Assert.Equal(2, two.RootElement.GetProperty("octaves").GetInt32());
        Assert.Equal("badRequest", wrong.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void The_chords_and_scales_offered_tell_their_group()
    {
        var facade = new MusicCoreFacade();
        using JsonDocument chords = JsonDocument.Parse(facade.Call(0, "reference.chord", """{"language": "ru"}"""));
        using JsonDocument scales = JsonDocument.Parse(facade.Call(0, "reference.scale", """{"language": "ru"}"""));

        Assert.Equal(["Triad", "Seventh", "SixthOrNinth"], chords.RootElement.GetProperty("types").EnumerateArray().Select(type => type.GetProperty("family").GetString()).Distinct());
        Assert.Equal(["MajorAndMinor", "Mode", "Pentatonic"], scales.RootElement.GetProperty("forms").EnumerateArray().Select(form => form.GetProperty("family").GetString()).Distinct());
        Assert.Equal(["мажорная", "минорная"], scales.RootElement.GetProperty("forms").EnumerateArray().Where(form => form.GetProperty("family").GetString() == "Pentatonic").Select(form => form.GetProperty("short").GetString()));
    }

    private static IEnumerable<string?> Texts(JsonElement items, string name) => items.EnumerateArray().Select(item => item.GetProperty(name).GetString());

    /// <summary>How fast the progression sounds: the playback's first tempo, microseconds a quarter.</summary>
    private static int Tempo(JsonElement answer) => answer.GetProperty("tempos")[0].GetProperty("microsecondsPerQuarter").GetInt32();
}