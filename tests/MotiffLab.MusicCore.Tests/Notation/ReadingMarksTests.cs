using System.Text.Json;
using System.Xml.Linq;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Tests.Import.Transcription;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>«Для чтения» with the marks of the playing that matter (version 0.5, experimental.readingMarks): <see cref="PlayedMarks.Significant"/>.</summary>
public class ReadingMarksTests
{
    private static readonly XNamespace s_mei = "http://www.music-encoding.org/ns/mei";

    private const int Bar = 1920;

    /// <summary>Quarters in 4/4 over <paramref name="bars"/> bars, each at the velocity <paramref name="velocity"/> gives by its bar.</summary>
    private static Note[] Quarters(int bars, Func<int, int> velocity) => [.. Enumerable.Range(0, bars * 4).Select(i => new Note
    {
        Id = $"n_{i + 1:D4}",
        Pitch = new Pitch(72 + (i % 5)),
        Span = new TickSpan(new Tick(i * 480L), 480),
        Velocity = velocity(i / 4),
        Track = 0,
        Attribution = Attribution.Imported,
    })];

    private static GeneratedNotation Reading(Note[] notes, IReadOnlyList<PlaybackTempo> tempos, int bars) =>
        NotationGenerator.Generate(notes, MeterMap.Default, [], new Tick(bars * Bar), asHeard: true, playedTempos: tempos, marks: PlayedMarks.Significant);

    private static GeneratedNotation Played(Note[] notes, IReadOnlyList<PlaybackTempo> tempos, int bars) =>
        NotationGenerator.Generate(notes, MeterMap.Default, [], new Tick(bars * Bar), playedTempos: tempos);

    private static XElement[] Measures(GeneratedNotation notation) => [.. XDocument.Parse(notation.Mei).Descendants(s_mei + "measure")];

    private static string[] Dynamics(GeneratedNotation notation) =>
        [.. Measures(notation).Select((measure, i) => measure.Elements(s_mei + "dynam").Select(dynam => $"{i + 1}:{dynam.Value}")).SelectMany(found => found)];

    private static string[] TempoWords(GeneratedNotation notation) =>
        [.. Measures(notation).Select((measure, i) => measure.Elements(s_mei + "dir").Where(dir => (string?)dir.Attribute("place") == "above").Select(dir => $"{i + 1}:{dir.Value}")).SelectMany(found => found)];

    [Fact]
    public void The_first_dynamic_is_written_and_then_only_a_change_of_two_steps_or_more_that_holds()
    {
        // mp for three bars, mf for three, f for four, at one tempo.
        Note[] notes = Quarters(10, bar => bar < 3 ? 60 : bar < 6 ? 76 : 95);
        PlaybackTempo[] steady = [new(Tick.Zero, 500_000), new(new Tick(Bar), 500_000)];

        Assert.Equal(["1:mp", "4:mf", "7:f"], Dynamics(Played(notes, steady, 10)));
        GeneratedNotation reading = Reading(notes, steady, 10);
        Assert.Equal(["1:mp", "7:f"], Dynamics(reading));
        Assert.Equal(2, reading.DynamicCount);
        Assert.True(reading.WrittenAsHeard);
    }

    [Fact]
    public void Rit_is_written_only_where_the_bars_grow_a_fifth_longer_and_no_accent_hairpin_slur_or_pedal_is()
    {
        // Six bars at a quarter of 0.5 s, then four a sixth slower (0.58 s) or a third slower (0.66 s); one loud note in bar 1.
        Note[] quarters = Quarters(10, _ => 70);
        Note[] notes = [quarters[0] with { Velocity = 120 }, .. quarters[1..]];
        static PlaybackTempo[] Slower(int microseconds) => [new(Tick.Zero, 500_000), new(new Tick(6 * Bar), microseconds)];

        Assert.Equal(["8:rit."], TempoWords(Played(notes, Slower(580_000), 10)));
        Assert.Empty(TempoWords(Reading(notes, Slower(580_000), 10)));
        GeneratedNotation reading = Reading(notes, Slower(660_000), 10);
        Assert.Equal(["7:rit."], TempoWords(reading));
        Assert.Equal(1, reading.TempoWordCount);

        XDocument mei = XDocument.Parse(reading.Mei);
        Assert.Equal((0, 0, 0), (reading.AccentCount, reading.HairpinCount, reading.SlurCount));
        Assert.Empty(mei.Descendants(s_mei + "artic"));
        Assert.Empty(mei.Descendants(s_mei + "hairpin"));
        Assert.Empty(mei.Descendants(s_mei + "slur"));
        string withPedal = NotationGenerator.Generate(notes, MeterMap.Default, [], new Tick(10 * Bar), asHeard: true, playedTempos: Slower(660_000), pedal: [new TickSpan(Tick.Zero, Bar)], marks: PlayedMarks.Significant).Mei;
        Assert.Empty(XDocument.Parse(withPedal).Descendants(s_mei + "pedal"));
        Assert.Equal(1, Played(notes, Slower(660_000), 10).AccentCount);
    }

    [Fact]
    public void A_fermata_goes_over_a_chord_held_through_a_beat_twice_as_long_as_those_around_it()
    {
        // Four bars of quarters at 0.5 s a beat, the third beat of bar 3 held 1.2 s; in bar 2 a beat as long holds two eighths.
        Note[] quarters = Quarters(4, _ => 70);
        Note[] notes = [.. quarters.Where(note => note.Span.Onset.Value != 6 * 480), quarters[6] with { Span = new TickSpan(new Tick(6 * 480), 240) }, quarters[6] with { Id = "n_0099", Span = new TickSpan(new Tick((6 * 480) + 240), 240) }];
        PlaybackTempo[] tempos =
        [
            new(Tick.Zero, 500_000),
            new(new Tick(6 * 480), 1_200_000), new(new Tick(7 * 480), 500_000),
            new(new Tick(10 * 480), 1_200_000), new(new Tick(11 * 480), 500_000),
        ];

        GeneratedNotation reading = Reading(notes, tempos, 4);

        Assert.Equal(1, reading.FermataCount);
        XElement fermata = Assert.Single(XDocument.Parse(reading.Mei).Descendants(s_mei + "fermata"));
        Assert.Equal("3", (string?)fermata.Parent!.Attribute("n"));
        Assert.Equal(("3", "above", "1"), ((string?)fermata.Attribute("tstamp"), (string?)fermata.Attribute("place"), (string?)fermata.Attribute("staff")));
        Assert.Equal(PlayingMarks.Significant(notes, [.. MeterMap.Default.BarsBefore(new Tick(4 * Bar))], tempos).Fermatas, [10 * 480L]);

        // A steady tempo, as a score's, holds none, and «Как сыграно» writes none.
        Assert.Equal(0, Reading(notes, [new(Tick.Zero, 500_000)], 4).FermataCount);
        Assert.Empty(XDocument.Parse(Played(notes, tempos, 4).Mei).Descendants(s_mei + "fermata"));
    }

    [Fact]
    public void A_held_chord_spanning_two_long_beats_has_one_fermata_and_the_marks_are_the_same_every_time()
    {
        Note[] notes = [.. Quarters(4, _ => 70).Where(note => note.Span.Onset.Value != 11 * 480).Select(note => note.Span.Onset.Value == 10 * 480 ? note with { Span = new TickSpan(note.Span.Onset, 960) } : note)];
        PlaybackTempo[] tempos = [new(Tick.Zero, 500_000), new(new Tick(10 * 480), 1_200_000), new(new Tick(12 * 480), 500_000)];

        Assert.Equal(1, Reading(notes, tempos, 4).FermataCount);
        Assert.Equal(Reading(notes, tempos, 4).Mei, Reading(notes, tempos, 4).Mei);
    }

    [Fact]
    public void Through_the_facade_reading_marks_asks_for_the_score_for_reading_with_its_marks()
    {
        long second = 1_000_000 + (LivePlaying.Length(BarGridTests.Waltz()) * 150_000L) + 3_000_000;
        byte[] midi = LivePlaying.Heard([.. LivePlaying.At(1_000_000, 150_000, BarGridTests.Waltz()), .. LivePlaying.At(second, 220_000, BarGridTests.Waltz())]);
        var facade = new MusicCoreFacade();
        string open = $$$"""{"id": "p_read", "now": "2026-11-12T12:00:00Z", "transcription": {"engine": "transkun", "milliseconds": 120000, "parts": [{"index": 0, "midi": "{{{Convert.ToBase64String(midi)}}}"}]}}""";
        Assert.Equal(1, Json(facade.Open("Recital.m4a", [], open)).GetProperty("piece").GetInt32());

        static int Count(JsonElement notation, string name) => XDocument.Parse(notation.GetProperty("mei").GetString()!).Descendants(s_mei + name).Count();
        JsonElement plain = Call(facade, "notation", "{}");
        JsonElement marked = Call(facade, "notation", """{"reading": "marks"}""");
        Assert.Equal(0, Count(plain, "dynam"));
        Assert.True(Count(marked, "dynam") >= 1);
        Assert.Equal(0, Count(marked, "artic") + Count(marked, "hairpin") + Count(marked, "slur"));
        Assert.Equal("badRequest", Error(facade, "notation", """{"reading": "all"}"""));
    }
}