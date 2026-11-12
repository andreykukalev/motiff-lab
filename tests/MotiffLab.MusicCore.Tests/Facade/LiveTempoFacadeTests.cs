using System.Text;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Tests.Import.Transcription;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>A recording's bars found afresh in each section when the transcription asks for it (version 0.5, «Темп живого исполнения»).</summary>
public class LiveTempoFacadeTests
{
    /// <summary>Two waltzes, at a quarter of 100 and of 68 a minute, three seconds apart, heard in one part.</summary>
    private static (byte[] Midi, long SecondStart) Recital()
    {
        long second = 1_000_000 + (LivePlaying.Length(BarGridTests.Waltz()) * 150_000L) + 3_000_000;
        byte[] midi = LivePlaying.Heard([.. LivePlaying.At(1_000_000, 150_000, BarGridTests.Waltz()), .. LivePlaying.At(second, 220_000, BarGridTests.Waltz())]);
        return (midi, second);
    }

    private static string Transcribed(byte[] midi, string tempo) =>
        $$$"""{"id": "p_live", "now": "2026-11-12T12:00:00Z", "transcription": {"engine": "transkun", "milliseconds": 120000, {{{tempo}}} "parts": [{"index": 0, "midi": "{{{Convert.ToBase64String(midi)}}}"}]}}""";

    private static BarGrid? BarsOf(MusicCoreFacade facade) =>
        ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save"))).Transcription!.Bars;

    [Fact]
    public void A_transcription_asked_for_the_live_tempo_finds_the_beat_in_each_section()
    {
        (byte[] midi, _) = Recital();
        var live = new MusicCoreFacade();
        var once = new MusicCoreFacade();

        Assert.Equal(1, Json(live.Open("Recital.m4a", [], Transcribed(midi, "\"tempo\": \"live\","))).GetProperty("piece").GetInt32());
        Assert.Equal(1, Json(once.Open("Recital.m4a", [], Transcribed(midi, ""))).GetProperty("piece").GetInt32());

        BarGrid heard = BarsOf(live)!;
        Assert.Equal(new TimeSignature(3, 4), heard.Signature);
        Assert.NotEqual(BarsOf(once), heard);
        // The same grid the project finds for the same notes.
        Assert.Equal(heard, ProjectFile.Read(Encoding.UTF8.GetBytes(live.Call(1, "project.save"))).FindBars(search: GridSearch.Live));
    }

    [Fact]
    public void Starts_and_a_hint_are_taken_and_bad_ones_refused()
    {
        (byte[] midi, long second) = Recital();
        var facade = new MusicCoreFacade();

        string asked = $$$"""{"tempo": "live", "starts": [{{{second / 1000}}}], "hint": {"signature": "3/4", "quarterNotesPerMinute": 100}}""";
        GridSearch search = LiveTempoRequest.Of(Request.Parse(asked))!;
        Assert.Equal([second / 1000 * 1000], search.Starts);
        Assert.Equal(new GridHint(new TimeSignature(3, 4), 100), search.Hint);
        Assert.Null(LiveTempoRequest.Of(Request.Parse("""{"tempo": "once"}""")));
        Assert.Null(LiveTempoRequest.Of(Request.Empty));

        Assert.Equal("badRequest", Json(facade.Open("Recital.m4a", [], Transcribed(midi, "\"tempo\": \"steady\","))).GetProperty("error").GetString());
        Assert.Equal("badRequest", Json(facade.Open("Recital.m4a", [], Transcribed(midi, "\"tempo\": \"live\", \"starts\": [-5],"))).GetProperty("error").GetString());
        Assert.Equal("badRequest", Json(facade.Open("Recital.m4a", [], Transcribed(midi, "\"tempo\": \"live\", \"hint\": {\"signature\": \"5/4\"},"))).GetProperty("error").GetString());
    }
}