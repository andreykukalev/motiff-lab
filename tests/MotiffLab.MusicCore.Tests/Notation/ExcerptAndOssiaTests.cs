using System.Xml.Linq;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>An excerpt engraved on its own, and ossias above the original bar for bar (decision record 0018, section 4; preparation item 10).</summary>
public class ExcerptAndOssiaTests
{
    private const long Quarter = 480;
    private const long Bar = 4 * Quarter;

    private static readonly XNamespace s_mei = "http://www.music-encoding.org/ns/mei";
    private static readonly XNamespace s_xml = XNamespace.Xml;

    private static Note Note(string id, int pitch, long onset, long duration, Hand? hand = null) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Hand = hand is { } value ? new Attributed<Hand>(value, Attribution.Imported) : null,
        Attribution = Attribution.Imported,
    };

    /// <summary>Eight bars for two hands: quarters in the right hand, a whole note in the left; bar 4's last note is tied into bar 5.</summary>
    private static Note[] EightBars()
    {
        var notes = new List<Note>();
        for (int bar = 0; bar < 8; bar++)
        {
            for (int beat = 0; beat < 4; beat++)
            {
                long length = bar == 3 && beat == 3 ? 2 * Quarter : Quarter;
                if (bar == 4 && beat == 0)
                {
                    continue;
                }

                notes.Add(Note($"r{bar}{beat}", 72 + beat, (bar * Bar) + (beat * Quarter), length, Hand.Right));
            }

            notes.Add(Note($"l{bar}", 48, bar * Bar, Bar, Hand.Left));
        }

        return [.. notes];
    }

    private static XElement[] Measures(XDocument mei) => [.. mei.Descendants(s_mei + "measure")];

    private static string[] Ids(XElement element) => [.. element.Descendants(s_mei + "note").Select(note => (string)note.Attribute(s_xml + "id")!)];

    [Fact]
    public void An_excerpt_writes_only_its_bars_with_their_numbers_and_the_key_in_force()
    {
        KeyChange[] keys = [new(Tick.Zero, 0), new(new Tick(2 * Bar), 2)];

        GeneratedNotation excerpt = NotationGenerator.Generate(EightBars(), MeterMap.Default, keys, new Tick(8 * Bar), bars: new BarRange(4, 6));
        XDocument mei = XDocument.Parse(excerpt.Mei);

        Assert.Equal(["4", "5", "6"], Measures(mei).Select(measure => (string)measure.Attribute("n")!));
        Assert.Equal("2s", (string)mei.Descendants(s_mei + "scoreDef").First().Attribute("keysig")!);
        Assert.Equal(new BarRange(4, 6), excerpt.Excerpt);
        Assert.Empty(excerpt.Ossias);

        // The note tied over from bar 4 into bar 5 keeps its tie: its second piece starts bar 5.
        XElement tiedIn = Assert.Single(mei.Descendants(s_mei + "note"), note => (string)note.Attribute(s_xml + "id")! == "r33-2");
        Assert.Equal("t", (string)tiedIn.Attribute("tie")!);
        Assert.Equal("i", (string)mei.Descendants(s_mei + "note").Single(note => (string)note.Attribute(s_xml + "id")! == "r33").Attribute("tie")!);
        Assert.DoesNotContain(Ids(mei.Root!), id => id.StartsWith("r0", StringComparison.Ordinal) || id.StartsWith("r7", StringComparison.Ordinal));

        // The whole piece written the same way has the excerpt's bars, note for note.
        XDocument whole = XDocument.Parse(NotationGenerator.Generate(EightBars(), MeterMap.Default, keys, new Tick(8 * Bar)).Mei);
        for (int index = 0; index < 3; index++)
        {
            Assert.Equal(Measures(whole)[index + 3].ToString(), Measures(mei)[index].ToString());
        }
    }

    [Fact]
    public void A_bar_range_outside_the_piece_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NotationGenerator.Generate(EightBars(), MeterMap.Default, [], new Tick(8 * Bar), bars: new BarRange(9, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BarRange(3, 2));
        Assert.Equal(new BarRange(2, 3), BarRange.Of(MeterMap.Default, TickSpan.FromBounds(new Tick(Bar), new Tick(3 * Bar))));
        Assert.Equal(new BarRange(1, 1), BarRange.Of(MeterMap.Default, new TickSpan(Tick.Zero, 1)));
    }

    [Fact]
    public void An_ossia_stands_above_the_original_bar_for_bar_smaller_and_labelled()
    {
        // The variation changes bar 5's right hand into eighths.
        Note[] variation = [.. Enumerable.Range(0, 8).Select(i => Note($"v{i}", 72 + i, (4 * Bar) + (i * Quarter / 2), Quarter / 2, Hand.Right))];

        GeneratedNotation notation = NotationGenerator.Generate(EightBars(), MeterMap.Default, [], new Tick(8 * Bar), bars: new BarRange(4, 6), ossias: [new NotationOssia("Variation", variation)]);
        XDocument mei = XDocument.Parse(notation.Mei);

        // One staff group for the ossia, with its label, at three quarters of the size, above the original's brace.
        XElement outer = mei.Descendants(s_mei + "scoreDef").First().Element(s_mei + "staffGrp")!;
        XElement[] groups = [.. outer.Elements(s_mei + "staffGrp")];
        Assert.Equal(2, groups.Length);
        Assert.Equal("Variation", groups[0].Element(s_mei + "label")!.Value);
        XElement ossiaStaff = Assert.Single(groups[0].Descendants(s_mei + "staffDef"));
        Assert.Equal(("1", NotationGenerator.OssiaScale), ((string)ossiaStaff.Attribute("n")!, (string)ossiaStaff.Attribute("scale")!));
        Assert.Equal("brace", (string)groups[1].Attribute("symbol")!);
        Assert.Equal(["2", "3"], groups[1].Elements(s_mei + "staffDef").Select(staff => (string)staff.Attribute("n")!));
        Assert.Null(groups[1].Element(s_mei + "staffDef")!.Attribute("scale"));

        // Every bar has the ossia's staff first; outside the variation it rests.
        Assert.All(Measures(mei), measure => Assert.Equal(["1", "2", "3"], measure.Elements(s_mei + "staff").Select(staff => (string)staff.Attribute("n")!)));
        Assert.Single(Measures(mei)[0].Elements(s_mei + "staff").First().Descendants(s_mei + "mRest"));
        Assert.Equal([.. variation.Select(note => "ossia-" + note.Id)], Ids(Measures(mei)[1].Elements(s_mei + "staff").First()));
        Assert.Single(Measures(mei)[2].Elements(s_mei + "staff").First().Descendants(s_mei + "mRest"));

        // The original keeps its own ids and counts, and the answer names both.
        Assert.Contains("r41", Ids(Measures(mei)[1]));
        Assert.Equal([2, 3], notation.Staves.Select(staff => staff.Number));
        GeneratedOssia ossia = Assert.Single(notation.Ossias);
        Assert.Equal(("Variation", 8, "ossia-"), (ossia.Label, ossia.NoteCount, ossia.IdPrefix));
        Assert.Equal([1], ossia.Staves.Select(staff => staff.Number));
        Assert.Equal(EightBars().Length, notation.NoteCount);
    }

    [Fact]
    public void Two_ossias_compare_two_variations_and_a_two_hand_ossia_has_its_brace()
    {
        Note[] first = [Note("a", 76, 4 * Bar, Bar, Hand.Right)];
        Note[] second = [Note("a", 79, 4 * Bar, Bar, Hand.Right), Note("b", 43, 4 * Bar, Bar, Hand.Left)];

        GeneratedNotation notation = NotationGenerator.Generate(EightBars(), MeterMap.Default, [], new Tick(8 * Bar), bars: new BarRange(5, 5),
            ossias: [new NotationOssia("More air", first), new NotationOssia("Pedal bass", second)]);
        XDocument mei = XDocument.Parse(notation.Mei);

        Assert.Equal(["More air", "Pedal bass"], notation.Ossias.Select(ossia => ossia.Label));
        Assert.Equal(["ossia-", "ossia2-"], notation.Ossias.Select(ossia => ossia.IdPrefix));
        Assert.Equal([2, 3], notation.Ossias[1].Staves.Select(staff => staff.Number));
        Assert.Equal([4, 5], notation.Staves.Select(staff => staff.Number));
        Assert.Contains("ossia-a", Ids(mei.Root!));
        Assert.Contains("ossia2-a", Ids(mei.Root!));
        Assert.Contains("ossia2-b", Ids(mei.Root!));
        Assert.Equal(5, Measures(mei)[0].Elements(s_mei + "staff").Count());
        XElement braced = mei.Descendants(s_mei + "staffGrp").Single(group => group.Element(s_mei + "label")?.Value == "Pedal bass");
        Assert.Equal("brace", (string)braced.Element(s_mei + "staffGrp")!.Attribute("symbol")!);
    }

    [Fact]
    public void As_played_above_for_reading_marks_the_playing_on_the_ossias_own_staves()
    {
        Note[] notes = [.. Enumerable.Range(0, 8).Select(i => Note($"n{i}", 72 + (i % 3), i * Quarter, i < 3 ? Quarter : Quarter / 2) with { Velocity = 40 + (8 * i), Attribution = Attribution.Detected(0.9) })];
        PlaybackTempo[] tempos = [new(Tick.Zero, 500_000)];
        TickSpan[] pedal = [TickSpan.FromBounds(Tick.Zero, new Tick(2 * Bar))];

        GeneratedNotation notation = NotationGenerator.Generate(notes, MeterMap.Default, [], new Tick(2 * Bar), asHeard: true,
            ossias: [new NotationOssia("As played", notes, PlayedTempos: tempos, Pedal: pedal)]);
        XDocument mei = XDocument.Parse(notation.Mei);

        // The marks of the playing are on the ossia's staff, 1; the original, written for reading, has none.
        Assert.All(mei.Descendants(s_mei + "pedal"), mark => Assert.Equal("1", (string)mark.Attribute("staff")!));
        Assert.NotEmpty(mei.Descendants(s_mei + "pedal"));
        Assert.All(mei.Descendants(s_mei + "dir").Concat(mei.Descendants(s_mei + "dynam")), mark => Assert.Equal("1", (string)mark.Attribute("staff")!));
        Assert.True(notation.WrittenAsHeard);
        Assert.Equal(0, notation.PedalCount);
        Assert.Equal([2], notation.Staves.Select(staff => staff.Number));
    }

    [Fact]
    public void An_excerpt_ends_a_pedal_or_a_hairpin_that_runs_past_it_at_its_last_bar()
    {
        Note[] notes = [.. Enumerable.Range(0, 16).Select(i => Note($"n{i}", 72 + (i % 3), i * Quarter, Quarter) with { Velocity = 30 + (5 * i) })];
        PlaybackTempo[] tempos = [new(Tick.Zero, 500_000)];
        TickSpan[] pedal = [TickSpan.FromBounds(Tick.Zero, new Tick(4 * Bar))];

        GeneratedNotation notation = NotationGenerator.Generate(notes, MeterMap.Default, [], new Tick(4 * Bar), playedTempos: tempos, pedal: pedal, bars: new BarRange(1, 2));
        XDocument mei = XDocument.Parse(notation.Mei);

        XElement[] marks = [.. mei.Descendants(s_mei + "pedal")];
        Assert.Equal(["down", "up"], marks.Select(mark => (string)mark.Attribute("dir")!));
        Assert.Equal("2", (string)marks[1].Parent!.Attribute("n")!);
        Assert.All(mei.Descendants(s_mei + "hairpin"), hairpin => Assert.StartsWith("0m+", (string)hairpin.Attribute("tstamp2")!));
        Assert.All(mei.Descendants(s_mei + "slur"), slur => Assert.Contains(((string)slur.Attribute("endid")!)[1..], Ids(mei.Root!)));
    }

    [Fact]
    public void Excerpts_and_ossias_are_deterministic()
    {
        Note[] variation = [Note("v", 74, 4 * Bar, Bar, Hand.Right)];
        string Write(IEnumerable<Note> notes) => NotationGenerator.Generate(notes, MeterMap.Default, [], new Tick(8 * Bar), bars: new BarRange(4, 6), ossias: [new NotationOssia("Variation", variation)]).Mei;

        Assert.Equal(Write(EightBars()), Write(EightBars().Reverse()));
    }
}