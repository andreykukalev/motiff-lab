using System.Xml.Linq;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>What the tests of the notation writer's parts build their cases from: notes, notes as shown, bars.</summary>
internal static class NotationParts
{
    public const long BarTicks = 1920;

    private static readonly XNamespace s_mei = "http://www.music-encoding.org/ns/mei";

    /// <summary>An MEI tuplet's length in ticks: its values' written lengths together, times its numbase over its num, a whole number of ticks.</summary>
    public static long TupletTicks(XElement tuplet)
    {
        long scaled = tuplet.Descendants().Where(element => element.Attribute("dur") is not null && element.Parent!.Name != s_mei + "chord")
            .Sum(element => 1920 / int.Parse((string)element.Attribute("dur")!) * (((int?)element.Attribute("dots") ?? 0) == 1 ? 3 : 2) / 2)
            * (int)tuplet.Attribute("numbase")!;
        int num = (int)tuplet.Attribute("num")!;
        Assert.Equal(0, scaled % num);
        return scaled / num;
    }

    public static Note Note(string id, int pitch, long onset, long duration, int track = 0, Hand? hand = null) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = track,
        Attribution = Attribution.Imported,
        Hand = hand is null ? null : new Attributed<Hand>(hand.Value, Attribution.Imported),
    };

    /// <summary>A note shown where it sounds.</summary>
    public static ShownNote Shown(string id, int pitch, long onset, long duration, int track = 0, Hand? hand = null) =>
        new(Note(id, pitch, onset, duration, track, hand), onset, onset + duration);

    /// <summary>The bars of <paramref name="count"/> bars in one meter.</summary>
    public static Bar[] Bars(int count, int numerator = 4, int denominator = 4, long pickup = 0)
    {
        var meter = new MeterMap([new MeterChange(Tick.Zero, new TimeSignature(numerator, denominator))], pickup);
        return [.. meter.BarsBefore(new Tick(pickup + (count * new TimeSignature(numerator, denominator).BarTicks)))];
    }
}