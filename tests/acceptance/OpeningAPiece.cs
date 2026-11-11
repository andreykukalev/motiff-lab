using System.Text.Json;

namespace MotiffLab.Acceptance.Tests;

/// <summary>Scenarios a to d: the player opens a score, a MIDI file, a damaged file and a PDF (README.md).</summary>
public class OpeningAPiece
{
    [Fact(DisplayName = "a. The player opens Für Elise (MusicXML) and gets the bars and notes of the printed score")]
    public void Fur_Elise_opens_with_the_bars_and_notes_of_the_printed_score()
    {
        (Player player, JsonElement opened) = Player.OpensFixture("fur-elise-original.musicxml");

        // The score: a pickup of two sixteenths and eight bars in 3/8, A minor (no sharps or flats), 53 notes.
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
        Assert.Equal(240 + (8 * 720), bars[^1].GetProperty("end").GetInt64());

        JsonElement key = Assert.Single(piece.GetProperty("keys").EnumerateArray());
        Assert.Equal((0, "minor"), (key.GetProperty("fifths").GetInt32(), key.GetProperty("mode").GetString()));

        // The opening as printed: E D♯ E D♯ E B D C in sixteenths from the pickup on, then A on the downbeat of bar 2.
        JsonElement[] melody = [.. player.Notes().Where(note => note.GetProperty("onset").GetInt64() < 1000 && note.GetProperty("pitch").GetInt32() >= 60)];
        Assert.Equal([76, 75, 76, 75, 76, 71, 74, 72, 69], melody.Select(note => note.GetProperty("pitch").GetInt32()));
        Assert.Equal([0L, 120, 240, 360, 480, 600, 720, 840, 960], melody.Select(note => note.GetProperty("onset").GetInt64()));
        Assert.All(player.Notes(), note => Assert.Equal("imported", note.GetProperty("provenance").GetString()));
    }

    [Fact(DisplayName = "b. The player opens a MIDI file and gets it as a piece: the C major scale up and down in quarters")]
    public void A_MIDI_file_opens_as_a_piece()
    {
        (Player player, JsonElement opened) = Player.OpensFixture("scale-type0.mid");

        Assert.Equal(1, opened.GetProperty("piece").GetInt32());
        Assert.Equal("midi", opened.GetProperty("summary").GetProperty("format").GetString());
        (long Onset, long Duration, int Pitch)[] heard = player.Hears();
        Assert.Equal([60, 62, 64, 65, 67, 69, 71, 72, 71, 69, 67, 65, 64, 62, 60], heard.Select(note => note.Pitch));
        Assert.Equal(Enumerable.Range(0, 15).Select(beat => beat * 480L), heard.Select(note => note.Onset));
        JsonElement bar = player.Does("piece").GetProperty("bars")[0];
        Assert.Equal((4, 4), (bar.GetProperty("numerator").GetInt32(), bar.GetProperty("denominator").GetInt32()));
        Assert.All(player.Notes(), note => Assert.Equal("imported", note.GetProperty("provenance").GetString()));
    }

    [Fact(DisplayName = "c. The player opens a damaged file: it is refused with a message, and nothing is open or saved")]
    public void A_damaged_file_is_refused_with_a_message_and_nothing_is_saved()
    {
        byte[] damaged = [0x4D, 0x54, 0x68, 0x64, 0, 0, 0];
        (Player player, JsonElement answer) = Player.TriesToOpen("notes.mid", damaged);

        Assert.Equal("import", answer.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(answer.GetProperty("message").GetString()));
        Assert.Equal(0, player.OpenPieces);
        // There is no piece, so there is no project file to save.
        Assert.Equal("noPiece", Player.Parse(player.Saves()).GetProperty("error").GetString());
        Assert.Equal([0x4D, 0x54, 0x68, 0x64, 0, 0, 0], damaged);
    }

    [Fact(DisplayName = "d. The player opens a PDF and gets a PDF piece with its pages and no notes")]
    public void A_PDF_opens_as_a_PDF_piece_with_no_notes()
    {
        byte[] pdf = Player.Fixture("blank-page.pdf");
        (Player player, JsonElement opened) = Player.Opens("blank-page.pdf", pdf);

        JsonElement project = opened.GetProperty("project");
        Assert.True(project.GetProperty("document").GetBoolean());
        Assert.False(project.GetProperty("hasNotes").GetBoolean());
        Assert.False(opened.TryGetProperty("summary", out _), "a PDF piece has no notes to sum up");
        Assert.Equal("blank-page.pdf", opened.GetProperty("document").GetProperty("fileName").GetString());
        Assert.Equal(pdf, player.Does("document").GetProperty("pdf").GetBytesFromBase64());
        // A call about notes is refused: there are none until they are read from the pages.
        Assert.Equal("noNotes", player.Tries("piece").GetProperty("error").GetString());
        Assert.Equal("noNotes", player.Tries("playback").GetProperty("error").GetString());
    }
}