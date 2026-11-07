using System.Text.Json;

namespace MotiffLab.Acceptance.Tests;

/// <summary>
/// Opening a score. The expected values are read from the fixtures themselves (README.md, «Откуда ожидания»),
/// not from what the code answered.
/// </summary>
public class OpeningAPiece
{
    [Fact(DisplayName = "A1. Für Elise (MusicXML): затакт и 8 тактов на 3/8, ля минор, 53 ноты")]
    public void Fur_Elise_opens_with_its_bars_key_and_notes()
    {
        (Player player, JsonElement opened) = Player.OpensFixture("fur-elise-original.musicxml");

        JsonElement summary = opened.GetProperty("summary");
        Assert.Equal("musicXml", summary.GetProperty("format").GetString());
        Assert.Equal(53, summary.GetProperty("noteCount").GetInt32());
        Assert.Equal(8, summary.GetProperty("barCount").GetInt32());

        JsonElement piece = player.Does("piece");
        JsonElement[] bars = [.. piece.GetProperty("bars").EnumerateArray()];
        Assert.Equal(9, bars.Length);
        Assert.True(bars[0].GetProperty("pickup").GetBoolean());
        Assert.Equal((0L, 240L), (bars[0].GetProperty("onset").GetInt64(), bars[0].GetProperty("end").GetInt64()));
        Assert.All(bars[1..], bar =>
        {
            Assert.False(bar.GetProperty("pickup").GetBoolean());
            Assert.Equal((3, 8), (bar.GetProperty("numerator").GetInt32(), bar.GetProperty("denominator").GetInt32()));
            Assert.Equal(720, bar.GetProperty("end").GetInt64() - bar.GetProperty("onset").GetInt64());
        });
        Assert.Equal(6000, bars[^1].GetProperty("end").GetInt64());

        JsonElement key = Assert.Single(piece.GetProperty("keys").EnumerateArray());
        Assert.Equal(0, key.GetProperty("fifths").GetInt32());
        Assert.Equal("minor", key.GetProperty("mode").GetString());

        Assert.All(piece.GetProperty("notes").EnumerateArray(), note => Assert.Equal("imported", note.GetProperty("provenance").GetString()));
    }

    [Fact(DisplayName = "A2. Für Elise звучит как написано: ми–ре♯–ми–ре♯–ми–си–ре–до шестнадцатыми")]
    public void Fur_Elise_sounds_its_opening_notes_in_sixteenths()
    {
        (Player player, _) = Player.OpensFixture("fur-elise-original.musicxml");

        (long Onset, long Duration, int Pitch)[] opening = [.. player.Hears().Where(note => note.Onset < 960)];

        Assert.Equal([76, 75, 76, 75, 76, 71, 74, 72], opening.Select(note => note.Pitch));
        Assert.Equal([0L, 120, 240, 360, 480, 600, 720, 840], opening.Select(note => note.Onset));
    }

    [Fact(DisplayName = "A3. «Ода к радости» (MusicXML): 8 тактов на 4/4, до мажор, 30 нот, начало ми–ми–фа–соль четвертями")]
    public void Ode_to_Joy_opens_with_its_bars_key_and_melody()
    {
        (Player player, JsonElement opened) = Player.OpensFixture("ode-to-joy.musicxml");

        Assert.Equal(30, opened.GetProperty("summary").GetProperty("noteCount").GetInt32());
        Assert.Equal(8, opened.GetProperty("summary").GetProperty("barCount").GetInt32());
        JsonElement piece = player.Does("piece");
        Assert.All(piece.GetProperty("bars").EnumerateArray(), bar =>
        {
            Assert.False(bar.GetProperty("pickup").GetBoolean());
            Assert.Equal((4, 4), (bar.GetProperty("numerator").GetInt32(), bar.GetProperty("denominator").GetInt32()));
        });
        JsonElement key = Assert.Single(piece.GetProperty("keys").EnumerateArray());
        Assert.Equal((0, "major"), (key.GetProperty("fifths").GetInt32(), key.GetProperty("mode").GetString()));

        (long Onset, long Duration, int Pitch)[] opening = [.. player.Hears().Take(4)];
        Assert.Equal([64, 64, 65, 67], opening.Select(note => note.Pitch));
        Assert.Equal([0L, 480, 960, 1440], opening.Select(note => note.Onset));
    }

    [Fact(DisplayName = "A4. MIDI-файл: гамма до мажор вверх и вниз, 15 нот четвертями на 4/4")]
    public void A_MIDI_scale_opens_with_its_notes_on_the_beats()
    {
        (Player player, JsonElement opened) = Player.OpensFixture("scale-type0.mid");

        Assert.Equal("midi", opened.GetProperty("summary").GetProperty("format").GetString());
        (long Onset, long Duration, int Pitch)[] heard = player.Hears();
        Assert.Equal([60, 62, 64, 65, 67, 69, 71, 72, 71, 69, 67, 65, 64, 62, 60], heard.Select(note => note.Pitch));
        Assert.Equal(Enumerable.Range(0, 15).Select(beat => beat * 480L), heard.Select(note => note.Onset));
        JsonElement bar = player.Does("piece").GetProperty("bars")[0];
        Assert.Equal((4, 4), (bar.GetProperty("numerator").GetInt32(), bar.GetProperty("denominator").GetInt32()));
    }

    [Fact(DisplayName = "A5. Испорченный файл не открывается: понятная причина, ничего не открыто")]
    public void A_damaged_file_is_refused_with_a_reason()
    {
        (Player player, JsonElement answer) = Player.TriesToOpen("notes.mid", [1, 2, 3, 4]);

        Assert.Equal("import", answer.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(answer.GetProperty("message").GetString()));
        Assert.Equal(0, player.OpenPieces);
    }
}
