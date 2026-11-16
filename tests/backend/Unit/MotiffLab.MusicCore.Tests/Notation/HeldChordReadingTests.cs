using System.Xml.Linq;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>
/// Issue #456: in «Для чтения», a chord struck on the downbeat and held, its notes a few milliseconds apart and shared
/// between the hands, was written as a 16th rest, split onsets and chains of tied 16ths. Synthetic notes.
/// </summary>
public class HeldChordReadingTests
{
    private static readonly XNamespace s_mei = "http://www.music-encoding.org/ns/mei";
    private static readonly XNamespace s_xml = XNamespace.Xml;

    private const long Bar = 1920;

    private static readonly Note s_heard = new() { Id = "n_0000", Pitch = new Pitch(60), Span = new TickSpan(Tick.Zero, 1), Velocity = 80, Track = 0, Attribution = Attribution.Imported };

    private static Note Note(string id, int pitch, long onset, long duration, Hand hand) =>
        s_heard with { Id = id, Pitch = new Pitch(pitch), Span = new TickSpan(new Tick(onset), duration), Hand = new Attributed<Hand>(hand, Attribution.Imported) };

    /// <summary>A bar of quarters in both hands, then bar 2: <paramref name="chord"/> (pitch, ticks after the bar line, hand) held to its end.</summary>
    private static Note[] Then(params (int Pitch, long At, Hand Hand)[] chord) =>
    [
        .. Enumerable.Range(0, 4).Select(i => Note($"n_{i + 1:D4}", 72 + i, i * 480L, 480, Hand.Right)),
        .. Enumerable.Range(0, 4).Select(i => Note($"n_{i + 11:D4}", 48 + i, i * 480L, 480, Hand.Left)),
        .. chord.Select((note, i) => Note($"n_{i + 21:D4}", note.Pitch, Bar + note.At, Bar - note.At - 40, note.Hand)),
    ];

    private static XDocument Reading(Note[] notes, bool asHeard = true) =>
        XDocument.Parse(NotationGenerator.Generate(notes, MeterMap.Default, [], new Tick(2 * Bar), asHeard: asHeard).Mei);

    /// <summary>What each staff of bar 2 writes first, and every duration it writes there.</summary>
    private static (string First, string[] Durations) Staff(XDocument mei, int staff)
    {
        XElement layer = mei.Descendants(s_mei + "measure").Single(measure => (string?)measure.Attribute("n") == "2")
            .Elements(s_mei + "staff").Single(element => (string?)element.Attribute("n") == staff.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Elements(s_mei + "layer").First();
        XElement[] written = [.. layer.Descendants().Where(element => element.Name == s_mei + "rest" || element.Name == s_mei + "chord" || (element.Name == s_mei + "note" && element.Parent!.Name != s_mei + "chord"))];
        return (written[0].Name.LocalName, [.. written.Select(element => (string?)element.Attribute("dur") ?? "")]);
    }

    private static XElement ById(XDocument mei, string id) =>
        Assert.Single(mei.Descendants(), element => (string?)element.Attribute(s_xml + "id") == id);

    [Fact]
    public void A_chord_struck_a_little_late_after_a_short_note_is_one_chord_on_the_downbeat_held_in_few_values()
    {
        // A short B4 on the downbeat, then C3, G3, D4 in the left hand and C5 in the right 52 to 62 ticks later, held.
        Note[] notes = Then((71, 0, Hand.Left), (48, 52, Hand.Left), (55, 58, Hand.Left), (62, 59, Hand.Left), (72, 62, Hand.Right));
        notes = [.. notes.Select(note => note.Id == "n_0021" ? note with { Span = new TickSpan(note.Span.Onset, 117) } : note)];

        XDocument mei = Reading(notes);

        (string right, string[] rightDurations) = Staff(mei, 1);
        (string left, string[] leftDurations) = Staff(mei, 2);
        Assert.Equal("note", right);
        Assert.Equal("note", left);
        Assert.Equal("acc", (string?)ById(mei, "n_0021").Attribute("grace"));
        Assert.Equal("1", (string?)ById(mei, "n_0025").Attribute("dur"));
        Assert.Equal("1", (string?)ById(mei, "c_n_0022").Attribute("dur"));
        Assert.DoesNotContain("16", rightDurations);
        Assert.DoesNotContain("32", leftDurations.Skip(1));
    }

    [Fact]
    public void A_rolled_chord_across_the_hands_starts_on_the_downbeat_on_both_staves()
    {
        // Played upwards over 58 ticks, the right hand's notes after the left's, every note held.
        XDocument mei = Reading(Then((43, 0, Hand.Left), (50, 22, Hand.Left), (59, 40, Hand.Left), (67, 50, Hand.Right), (74, 58, Hand.Right)));

        Assert.Equal(("chord", "1"), (Staff(mei, 1).First, Staff(mei, 1).Durations[0]));
        Assert.Equal(("chord", "1"), (Staff(mei, 2).First, Staff(mei, 2).Durations[0]));
        Assert.DoesNotContain(mei.Descendants(s_mei + "note"), note => note.Attribute("grace") is not null);
    }

    [Fact]
    public void Two_notes_a_32nd_apart_stay_two_and_as_played_keeps_the_timing()
    {
        // Only two notes: a quick pair, not a spread chord.
        XDocument pair = Reading(Then((72, 0, Hand.Right), (74, 50, Hand.Right)));
        Assert.Equal(("note", "32"), (Staff(pair, 1).First, Staff(pair, 1).Durations[0]));
        Assert.Empty(pair.Descendants(s_mei + "chord"));

        // «Как сыграно» writes the chord of the first test as it was played, a little after the bar line.
        Note[] notes = Then((48, 52, Hand.Left), (55, 58, Hand.Left), (62, 59, Hand.Left), (72, 62, Hand.Right));
        Assert.Equal("rest", Staff(Reading(notes, asHeard: false), 1).First);
        Assert.Equal("note", Staff(Reading(notes), 1).First);
    }
}