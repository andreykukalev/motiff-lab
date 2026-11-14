using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Playback;

/// <summary>
/// Where a tempo comes from (version 0.5.4, #511 section 17): the +30 % of a scanned piece was the default of 120 taken for
/// a score homr reads without a tempo, never shown as taken; the notes' own, a recording's and the player's are told apart,
/// played and marked each as what it is.
/// </summary>
public class TempoSourceTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static byte[] Homr() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"));

    /// <summary>Two bars of 4/4; with <paramref name="metronome"/> the first starts with "♩ = 72", and the second says "♩ = 60" when <paramref name="change"/>.</summary>
    private static byte[] Score(bool metronome, bool change = false)
    {
        static string Mark(int perMinute) =>
            $"""<direction placement="above"><direction-type><metronome><beat-unit>quarter</beat-unit><per-minute>{perMinute}</per-minute></metronome></direction-type><sound tempo="{perMinute}"/></direction>""";
        const string Note = "<note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration><type>whole</type></note>";
        return Encoding.UTF8.GetBytes(
            $"""<?xml version="1.0" encoding="UTF-8"?><score-partwise version="4.0"><part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list><part id="P1"><measure number="1"><attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>{(metronome ? Mark(72) : "")}{Note}</measure><measure number="2">{(change ? Mark(60) : "")}{Note}</measure></part></score-partwise>""");
    }

    /// <summary>A bar of 6/8 marked "♩. = 60" (90 quarter notes a minute), or unmarked.</summary>
    private static byte[] Compound(bool metronome) =>
        Encoding.UTF8.GetBytes(
            $"""<?xml version="1.0" encoding="UTF-8"?><score-partwise version="4.0"><part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list><part id="P1"><measure number="1"><attributes><divisions>2</divisions><time><beats>6</beats><beat-type>8</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>{(metronome ? """<direction placement="above"><direction-type><metronome><beat-unit>quarter</beat-unit><beat-unit-dot/><per-minute>60</per-minute></metronome></direction-type><sound tempo="90"/></direction>""" : "")}<note><pitch><step>C</step><octave>4</octave></pitch><duration>6</duration><type>half</type><dot/></note></measure></part></score-partwise>""");

    private static ImportedPiece Piece(byte[] musicXml) => PieceImporter.Import("score.musicxml", musicXml).Piece!;

    [Fact]
    public void A_score_homr_reads_without_a_tempo_plays_at_an_assumed_120_and_says_so()
    {
        ImportedPiece piece = Piece(Homr());

        Assert.Equal(1, piece.Report.CountOf(MusicXmlIssueCodes.TempoDefault));
        PlaybackTempo start = PlaybackScore.FromPiece(piece).StartTempo;
        Assert.Equal(TempoSource.Assumed, start.Source);
        Assert.Equal(120, start.PerMinute);
        Assert.True(CharacterContext.FromPiece(piece).TempoAssumed);
    }

    [Fact]
    public void A_metronome_mark_in_the_score_is_notated()
    {
        PlaybackScore score = PlaybackScore.FromPiece(Piece(Score(metronome: true, change: true)));

        Assert.Equal([(0L, 72, TempoSource.Notated), (1920L, 60, TempoSource.Notated)], score.Tempos.Select(tempo => (tempo.At.Value, tempo.PerMinute, tempo.Source)));
        Assert.Same(score.Tempos, score.PieceTempos);
    }

    [Fact]
    public void A_midi_tempo_event_is_notated_and_a_file_without_one_assumed()
    {
        byte[] file = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Midi", "Fixtures", "scale-type0.mid"));
        MidiImport import = MidiImporter.Import(file);

        Assert.Equal(TempoSource.Notated, PlaybackScore.FromMidi(import).StartTempo.Source);
        Assert.Equal(TempoSource.Assumed, PlaybackScore.FromMidi(import with { Tempos = [new MidiTempoChange(Tick.Zero, 0, MidiTempoChange.DefaultMicrosecondsPerQuarter, IsAssumed: true)] }).StartTempo.Source);
        Assert.Equal(TempoSource.Assumed, PlaybackScore.Create([], [], MeterMap.Default, Tick.Zero).StartTempo.Source);
    }

    [Fact]
    public void The_players_tempo_replaces_the_assumed_one_and_is_the_players()
    {
        PlaybackScore piece = PlaybackScore.FromPiece(Piece(Homr()));

        PlaybackScore set = piece.WithPlayerTempo(92);

        Assert.Equal((92, TempoSource.Player), (set.StartTempo.PerMinute, set.StartTempo.Source));
        Assert.Equal(652_174, set.StartTempo.MicrosecondsPerQuarter);
        Assert.Equal(TempoSource.Assumed, set.PieceTempos[0].Source);
        Assert.Equal(piece.Notes, set.Notes);
        Assert.Equal(piece.Beats, set.Beats);
        Assert.Equal(piece.Tempos, set.WithPlayerTempo(null).Tempos);
        Assert.Equal(set.Tempos, set.WithNotes(Piece(Homr()).Notes).Tempos);
        Assert.Equal(set.Tempos, set.ExtendedTo(new Tick(piece.End.Value + 1920)).Tempos);
    }

    [Fact]
    public void Over_a_notated_tempo_the_players_is_shown_as_the_players_and_later_notated_changes_stay_as_stated()
    {
        PlaybackScore set = PlaybackScore.FromPiece(Piece(Score(metronome: true, change: true)) with { PlayerTempo = 80 });

        Assert.Equal([(80, TempoSource.Player), (60, TempoSource.Notated)], set.Tempos.Select(tempo => (tempo.PerMinute, tempo.Source)));
        Assert.Equal((72, TempoSource.Notated), (set.PieceTempos[0].PerMinute, set.PieceTempos[0].Source));
    }

    [Fact]
    public void A_recordings_inferred_tempos_follow_the_players_in_proportion()
    {
        IReadOnlyList<PlaybackTempo> played = PlayerTempo.Over(
            [new PlaybackTempo(Tick.Zero, 600_000, TempoSource.Inferred), new PlaybackTempo(new Tick(480), 750_000, TempoSource.Inferred)], 120);

        Assert.Equal([(500_000, TempoSource.Player), (625_000, TempoSource.Player)], played.Select(tempo => (tempo.MicrosecondsPerQuarter, tempo.Source)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerTempo.Over([], PlayerTempo.Max + 1));
    }

    [Fact]
    public void The_score_is_marked_with_the_notes_or_the_players_tempo_an_inferred_one_as_about_and_an_assumed_one_never()
    {
        static PlaybackTempo Start(TempoSource source) => new(Tick.Zero, 652_174, source);

        Assert.Empty(TempoMarks.Of([Start(TempoSource.Assumed)]));
        Assert.Equal("= 92", Assert.Single(TempoMarks.Of([Start(TempoSource.Notated)])).Text);
        Assert.Equal("= 92", Assert.Single(TempoMarks.Of([Start(TempoSource.Player)])).Text);
        Assert.Equal("≈ 92", Assert.Single(TempoMarks.Of([Start(TempoSource.Inferred)])).Text);
        // After the start only what the notes state: a recording's beat-by-beat tempo is not written.
        Assert.Single(TempoMarks.Of([Start(TempoSource.Inferred), new PlaybackTempo(new Tick(480), 700_000, TempoSource.Inferred)]));
        Assert.Equal(2, TempoMarks.Of([Start(TempoSource.Player), new PlaybackTempo(new Tick(1920), 1_000_000, TempoSource.Notated)]).Count);
    }

    [Fact]
    public void The_mei_carries_the_mark_as_verovio_writes_a_metronome()
    {
        ImportedPiece notated = Piece(Score(metronome: true, change: true));
        GeneratedNotation written = NotationGenerator.FromMusicXml(notated.MusicXml!, tempos: PlaybackScore.FromPiece(notated).Tempos);
        ImportedPiece scanned = Piece(Homr());
        GeneratedNotation assumed = NotationGenerator.FromMusicXml(scanned.MusicXml!, tempos: PlaybackScore.FromPiece(scanned).Tempos);

        Assert.Equal(2, written.TempoMarkCount);
        Assert.Contains("<tempo staff=\"1\" place=\"above\" tstamp=\"1\" mm=\"72\" mm.unit=\"4\" midi.bpm=\"72\"><rend glyph.auth=\"smufl\"></rend> = 72</tempo>", written.Mei, StringComparison.Ordinal);
        Assert.Contains("mm=\"60\"", written.Mei, StringComparison.Ordinal);
        Assert.Equal(0, assumed.TempoMarkCount);
        Assert.DoesNotContain("<tempo", assumed.Mei, StringComparison.Ordinal);
        Assert.DoesNotContain("<tempo", NotationGenerator.FromMusicXml(notated.MusicXml!).Mei, StringComparison.Ordinal);
    }

    [Fact]
    public void A_score_shown_as_written_carries_the_players_tempo_in_place_of_its_own()
    {
        string xml = Encoding.UTF8.GetString(Score(metronome: true));
        PlaybackScore notated = PlaybackScore.FromPiece(Piece(Score(metronome: true)));

        Assert.Same(xml, WrittenTempo.With(xml, notated));
        string marked = WrittenTempo.With(xml, notated.WithPlayerTempo(92));

        Assert.DoesNotContain("<per-minute>72</per-minute>", marked, StringComparison.Ordinal);
        Assert.Contains("</attributes><direction placement=\"above\"><direction-type><metronome><beat-unit>quarter</beat-unit><per-minute>92</per-minute></metronome></direction-type><sound tempo=\"92\" /></direction>", marked, StringComparison.Ordinal);
        Assert.Equal(92, PlaybackScore.FromPiece(Piece(Encoding.UTF8.GetBytes(marked))).StartTempo.PerMinute);
        Assert.Contains("<per-minute>92</per-minute>", WrittenTempo.With(Encoding.UTF8.GetString(Homr()), PlaybackScore.FromPiece(Piece(Homr())).WithPlayerTempo(92)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_tempo_fact_names_the_players_tempo_as_theirs()
    {
        ImportedPiece piece = Piece(Homr()) with { PlayerTempo = 92 };

        CharacterContext context = CharacterContext.FromPiece(piece);

        Assert.False(context.TempoAssumed);
        Assert.Equal(TempoSource.Player, context.Tempos[0].Source);
    }

    [Fact]
    public void The_project_keeps_the_players_tempo_and_a_file_without_it_reads_as_none()
    {
        Project project = Project.Create("p_tempo", "scan.musicxml", Homr(), s_now) with { PlayerTempo = 92 };

        JsonObject file = JsonNode.Parse(ProjectFile.Write(project))!.AsObject();

        Assert.Equal(92, file["tempo"]!["quarterNotesPerMinute"]!.GetValue<int>());
        Assert.Equal(92, ProjectFile.Read(ProjectFile.Write(project)).PlayerTempo);
        Assert.Equal(92, OpenedProject.Open(project).Piece.PlayerTempo);
        Assert.False(JsonNode.Parse(ProjectFile.Write(project with { PlayerTempo = null }))!.AsObject().ContainsKey("tempo"));
        Assert.Equal(FieldClass.Player, ProjectSync.Fields["tempo"]);
        foreach (string broken in new[] { "92", """{"quarterNotesPerMinute": 19}""", """{"quarterNotesPerMinute": 301}""", """{"quarterNotesPerMinute": 92.5}""", "{}" })
        {
            file["tempo"] = JsonNode.Parse(broken);
            Assert.Contains("tempo", Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString()))).Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Through_the_facade_the_player_sets_and_clears_the_tempo_and_playback_and_notation_follow()
    {
        var facade = new MusicCoreFacade();
        Assert.False(Json(facade.Open("scan.musicxml", Homr(), """{"id": "p_scan", "now": "2026-11-14T12:00:00Z"}""")).TryGetProperty("error", out _));

        JsonElement before = Call(facade, "playback").GetProperty("tempo");
        Assert.Equal((120, "assumed"), (before.GetProperty("perMinute").GetInt32(), before.GetProperty("source").GetString()));
        Assert.Equal(JsonValueKind.Null, before.GetProperty("player").ValueKind);
        Assert.Equal("assumed", Call(facade, "playback").GetProperty("tempos")[0].GetProperty("source").GetString());
        Assert.DoesNotContain("<tempo", Call(facade, "notation").GetProperty("mei").GetString()!, StringComparison.Ordinal);

        JsonElement set = Call(facade, "tempo.set", """{"perMinute": 92}""").GetProperty("tempo");
        Assert.Equal((92, "player", 92), (set.GetProperty("perMinute").GetInt32(), set.GetProperty("source").GetString(), set.GetProperty("player").GetInt32()));
        Assert.Equal("assumed", set.GetProperty("piece").GetProperty("source").GetString());
        Assert.Equal(652_174, Call(facade, "playback").GetProperty("tempos")[0].GetProperty("microsecondsPerQuarter").GetInt32());
        Assert.Contains("mm=\"92\"", Call(facade, "notation").GetProperty("mei").GetString()!, StringComparison.Ordinal);
        Assert.Equal(92, Call(facade, "project.save").GetProperty("tempo").GetProperty("quarterNotesPerMinute").GetInt32());
        Assert.Equal("badRequest", Error(facade, "tempo.set", """{"perMinute": 400}"""));

        Assert.Equal("assumed", Call(facade, "tempo.set", "{}").GetProperty("tempo").GetProperty("source").GetString());
        Assert.False(Call(facade, "project.save").TryGetProperty("tempo", out _));
    }

    [Fact]
    public void In_six_eight_the_tempo_is_counted_and_marked_in_dotted_quarters()
    {
        ImportedPiece piece = Piece(Compound(metronome: true));
        PlaybackScore score = PlaybackScore.FromPiece(piece);

        Assert.Equal((90, TempoSource.Notated), (score.StartTempo.PerMinute, score.StartTempo.Source));
        Assert.Equal(TempoBeat.DottedQuarter, TempoBeats.At(score.Meter, Tick.Zero));
        Assert.Equal(TempoBeat.Quarter, TempoBeats.Of(new TimeSignature(3, 8)));
        Assert.Equal(TempoBeat.Quarter, TempoBeats.Of(new TimeSignature(6, 4)));
        Assert.Equal(TempoBeat.DottedQuarter, TempoBeats.Of(new TimeSignature(12, 8)));
        TempoMark mark = Assert.Single(TempoMarks.Of(score.Tempos, score.Meter));
        Assert.Equal((60, TempoBeat.DottedQuarter, "= 60"), (mark.PerMinute, mark.Beat, mark.Text));
        Assert.Contains("mm=\"60\" mm.unit=\"4\" mm.dots=\"1\" midi.bpm=\"90\"><rend glyph.auth=\"smufl\">\uECA5 \uECB7</rend> = 60</tempo>", NotationGenerator.FromMusicXml(piece.MusicXml!, tempos: score.Tempos).Mei, StringComparison.Ordinal);

        // The player's "♩. = 61" is kept as 92 quarter notes a minute and reads back as 61; every whole number does.
        Assert.Equal(92, TempoBeat.DottedQuarter.QuarterNotes(61));
        Assert.All(Enumerable.Range(14, 187), perMinute =>
            Assert.Equal(perMinute, TempoBeat.DottedQuarter.PerMinute(TempoSources.MicrosecondsPerQuarter(TempoBeat.DottedQuarter.QuarterNotes(perMinute)))));
        string written = WrittenTempo.With(Encoding.UTF8.GetString(Compound(metronome: false)), PlaybackScore.FromPiece(Piece(Compound(metronome: false))).WithPlayerTempo(92));
        Assert.Contains("<metronome><beat-unit>quarter</beat-unit><beat-unit-dot /><per-minute>61</per-minute></metronome></direction-type><sound tempo=\"92\" />", written, StringComparison.Ordinal);
        Assert.Equal(92, PlaybackScore.FromPiece(Piece(Encoding.UTF8.GetBytes(written))).StartTempo.PerMinute);
    }

    [Fact]
    public void The_facade_tells_the_beat_the_tempo_is_counted_in()
    {
        var compound = new MusicCoreFacade();
        Assert.False(Json(compound.Open("six-eight.musicxml", Compound(metronome: true), """{"id": "p_68", "now": "2026-11-14T12:00:00Z"}""")).TryGetProperty("error", out _));
        var common = new MusicCoreFacade();
        Assert.False(Json(common.Open("scan.musicxml", Homr(), """{"id": "p_44", "now": "2026-11-14T12:00:00Z"}""")).TryGetProperty("error", out _));

        JsonElement tempo = Call(compound, "playback").GetProperty("tempo");
        Assert.Equal(("dottedQuarter", 90, 60, 60), (tempo.GetProperty("beat").GetString(), tempo.GetProperty("perMinute").GetInt32(), tempo.GetProperty("beatsPerMinute").GetInt32(), tempo.GetProperty("piece").GetProperty("beatsPerMinute").GetInt32()));
        Assert.Equal("quarter", Call(common, "playback").GetProperty("tempo").GetProperty("beat").GetString());
        Assert.Equal("dottedQuarter", Call(compound, "tempo.set", """{"perMinute": 92}""").GetProperty("tempo").GetProperty("beat").GetString());
    }
}