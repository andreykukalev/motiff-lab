using System.Text;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
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
    public void A_transcription_keeps_where_its_stretch_started_in_the_recording_file()
    {
        (byte[] midi, _) = Recital();
        var facade = new MusicCoreFacade();
        Assert.Equal(1, Json(facade.Open("Recital.m4a", [], Transcribed(midi, "\"sentFromMs\": 754000,"))).GetProperty("piece").GetInt32());
        Assert.Equal(754_000, ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save"))).Transcription!.SentFromMilliseconds);

        var unsaid = new MusicCoreFacade();
        _ = unsaid.Open("Recital.m4a", [], Transcribed(midi, ""));
        Assert.Null(ProjectFile.Read(Encoding.UTF8.GetBytes(unsaid.Call(1, "project.save"))).Transcription!.SentFromMilliseconds);
        Assert.Equal("badRequest", Json(new MusicCoreFacade().Open("Recital.m4a", [], Transcribed(midi, "\"sentFromMs\": -1,"))).GetProperty("error").GetString());
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

    [Fact]
    public void Tempo_answers_the_profile_of_the_piece_and_of_each_piece_of_its_layer()
    {
        (byte[] midi, _) = Recital();
        var facade = new MusicCoreFacade();
        Json(facade.Open("Recital.m4a", [], Transcribed(midi, "\"tempo\": \"live\",")));

        System.Text.Json.JsonElement tempo = Call(facade, "tempo");

        Assert.True(tempo.GetProperty("inferred").GetBoolean());
        Assert.True(tempo.GetProperty("low").GetInt32() <= tempo.GetProperty("usual").GetInt32());
        Assert.True(tempo.GetProperty("usual").GetInt32() <= tempo.GetProperty("high").GetInt32());
        Assert.NotEmpty(tempo.GetProperty("bars").EnumerateArray());
        Assert.Equal("piece.tempo.profile", tempo.GetProperty("facts")[0].GetProperty("id").GetString());
        Assert.Empty(tempo.GetProperty("pieces").EnumerateArray());
    }

    [Fact]
    public void The_bars_change_with_the_pieces_layer_moving_along_by_the_recordings_time()
    {
        (byte[] midi, long second) = Recital();
        var facade = new MusicCoreFacade();
        Json(facade.Open("Recital.m4a", [], Transcribed(midi, "")));
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));
        ImportedPiece opened = OpenedProject.Open(project).Piece;
        HeardClock clock = HeardClock.Of(project, opened)!;
        int secondBar = opened.Meter.BarAt(clock.TickAt(second)).Number + 1;
        DateTimeOffset now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
        project = PieceLayer.Save(project, [new PieceStart("First", opened.Meter.BarAt(Tick.Zero).Number), new PieceStart("Second", secondBar)], opened.Meter, opened.End, Words.English, now);

        Assert.False(project.CanChangeBars);
        Assert.True(LiveBars.CanChange(project));
        Assert.Equal([clock.MicrosecondsAt(opened.Meter.GetBar(secondBar).Span.Onset)], LiveBars.SearchFor(project, opened).Starts);

        BarGrid live = project.FindBars(search: LiveBars.SearchFor(project, opened))!;
        Project changed = LiveBars.Change(project, opened, live, Words.English, now);

        ImportedPiece reopened = OpenedProject.Open(changed).Piece;
        IReadOnlyList<LayerPiece> layer = PieceLayer.Read(changed, reopened.Meter);
        Assert.Equal(["First", "Second"], layer.Select(piece => piece.Title));
        Assert.Equal(PieceLayer.Read(project, opened.Meter).Select(piece => piece.BookmarkId), layer.Select(piece => piece.BookmarkId));
        Assert.InRange(HeardClock.Of(changed, reopened)!.MicrosecondsAt(layer[1].Span.Onset), second - 1_500_000, second + 1_500_000);
        Assert.Equal(live, changed.Transcription!.Bars);

        // Anything else placed in the bars still keeps them.
        Assert.False(LiveBars.CanChange(changed.AddBookmark(new Bookmark(changed.NextBookmarkId(), "Mine", BookmarkCategory.BuiltIn[0].Id, layer[0].Span, FromPhrase: false, now))));
    }

    [Fact]
    public void A_score_version_gives_the_hint_and_the_bars_are_found_with_it()
    {
        (byte[] midi, _) = Recital();
        var facade = new MusicCoreFacade();
        Json(facade.Open("Recital.m4a", [], Transcribed(midi, "")));
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));
        Assert.Null(LiveBars.HintOf(project));

        ImportedPiece waltz = BarGridTests.Import(LivePlaying.Heard(LivePlaying.At(0, 150_000, BarGridTests.Waltz())));
        byte[] score = MotiffLab.MusicCore.Export.MidiExporter.Export(waltz.Notes, [], new MeterMap([new MeterChange(Tick.Zero, new TimeSignature(3, 4))]), "score");
        Project linked = project with { Performances = [new SavedPerformance("perf_1", "Score", PerformanceSource.Score, score, 0, [])] };

        GridHint hint = LiveBars.HintOf(linked)!;
        Assert.Equal(new TimeSignature(3, 4), hint.Signature);
        GridFound found = LiveBars.Find(linked, GridSearch.Live with { Hint = hint });
        Assert.True(found.HintFits);
        Assert.Equal(new TimeSignature(3, 4), found.Grid!.Signature);
        Assert.Equal(2, found.Sections);
    }
}