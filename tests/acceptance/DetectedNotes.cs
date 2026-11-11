using System.Text.Json;

namespace MotiffLab.Acceptance.Tests;

/// <summary>Scenario k: notes read from a scan or heard in a recording are detected, never imported, and raise Check flags (README.md).</summary>
public class DetectedNotes
{
    [Fact(DisplayName = "k1. The player has the notes read from a PDF: they are marked detected, and the bar too long for its time is flagged for Check")]
    public void Notes_read_from_a_scan_are_detected_and_flagged()
    {
        (Player pdf, _) = Player.OpensFixture("blank-page.pdf", $$"""{"id": "p_scan", "now": "{{Player.Now}}"}""");
        string read = JsonSerializer.Serialize(new { musicXml = Convert.ToBase64String(Player.Fixture("scan-result.musicxml")), engine = "scan server", pages = 1, now = Player.Now });

        JsonElement answer = pdf.Does("document.read", read);

        Assert.Equal("scanned", answer.GetProperty("project").GetProperty("origin").GetString());
        (Player player, JsonElement opened) = Player.Reopens(answer.GetProperty("file").GetString()!);
        Assert.Equal("scanned", opened.GetProperty("project").GetProperty("origin").GetString());
        // The fixture: 4 + 5 + 2 notes, all of them a reading of the page.
        JsonElement[] notes = player.Notes();
        Assert.Equal(11, notes.Length);
        Assert.All(notes, note => Assert.Equal("detected", note.GetProperty("provenance").GetString()));

        // Bar 2 holds five quarters in 4/4: the one doubt the fixture was written with, open until the player checks it.
        JsonElement check = player.Does("check");
        Assert.Equal("scanned", check.GetProperty("origin").GetString());
        JsonElement flag = Assert.Single(check.GetProperty("flags").EnumerateArray());
        Assert.Equal((2, "scan", "barTooLong", false), (flag.GetProperty("bar").GetInt32(), flag.GetProperty("source").GetString(), flag.GetProperty("reason").GetString(), flag.GetProperty("resolved").GetBoolean()));
        Assert.Equal(1, check.GetProperty("open").GetInt32());
        Assert.Equal(1, opened.GetProperty("project").GetProperty("openChecks").GetInt32());
    }

    [Fact(DisplayName = "k2. The player opens a recording heard in two parts: its notes are marked detected, and the seam between the parts is flagged for Check")]
    public void Notes_heard_in_a_recording_are_detected_and_flagged()
    {
        // Two minutes and ten seconds: two parts that overlap (0014); each part's sound was heard as the C major scale.
        string midi = Convert.ToBase64String(Player.Fixture("scale-type0.mid"));
        string request = $$$"""{"id": "p_heard", "now": "{{{Player.Now}}}", "transcription": {"engine": "transcription engine", "milliseconds": 130000, "parts": [{"index": 0, "midi": "{{{midi}}}"}, {"index": 1, "midi": "{{{midi}}}"}]}}""";

        (Player player, JsonElement opened) = Player.Opens("recital.m4a", [], request);

        Assert.Equal("transcribed", opened.GetProperty("project").GetProperty("origin").GetString());
        Assert.True(opened.GetProperty("summary").GetProperty("heard").GetBoolean());
        JsonElement[] notes = player.Notes();
        Assert.NotEmpty(notes);
        Assert.All(notes, note => Assert.Equal("detected", note.GetProperty("provenance").GetString()));

        // Where two parts were joined the notes may be doubled or lost, so the seam is always flagged, and open.
        JsonElement check = player.Does("check");
        Assert.Equal("transcribed", check.GetProperty("origin").GetString());
        JsonElement[] seams = [.. check.GetProperty("flags").EnumerateArray().Where(flag => flag.GetProperty("reason").GetString() == "partSeam")];
        Assert.NotEmpty(seams);
        Assert.All(seams, seam => Assert.Equal(("transcription", false), (seam.GetProperty("source").GetString(), seam.GetProperty("resolved").GetBoolean())));
        Assert.True(check.GetProperty("open").GetInt32() >= seams.Length);

        // Saved and opened again, the notes are still detected and the flags still open.
        (Player reopened, _) = Player.Reopens(player.Saves());
        Assert.All(reopened.Notes(), note => Assert.Equal("detected", note.GetProperty("provenance").GetString()));
        Assert.Equal(check.GetRawText(), reopened.Does("check").GetRawText());
    }
}