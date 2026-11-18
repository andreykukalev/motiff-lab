using System.Xml.Linq;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Notation.NotationParts;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>How the planned notation is written as MEI (<see cref="MeiWriter"/>).</summary>
public class MeiWriterTests
{
    private static readonly XNamespace s_mei = "http://www.music-encoding.org/ns/mei";

    [Theory]
    [InlineData(4, 4, 0, 0, "1")]
    [InlineData(4, 4, 0, 720, "2.5")]
    [InlineData(6, 8, 0, 240, "2")]
    [InlineData(3, 8, 0, 80, "1.333")]
    [InlineData(4, 4, 480, 0, "4")]
    public void A_time_stamp_counts_the_bar_s_beats_from_1_a_pickup_from_where_a_full_bar_starts(int numerator, int denominator, long pickup, long at, string tstamp)
    {
        Bar bar = Bars(1, numerator, denominator, pickup)[0];

        Assert.Equal(tstamp, MeiWriter.Beat(bar, at));
    }

    [Fact]
    public void A_bar_is_written_with_its_meter_key_staves_and_a_whole_bar_rest()
    {
        Bar bar = Bars(1, 3, 4)[0];
        var staff = new StaffPlan(1, 0, Clef.Bass, Hand.Left, false, []);
        var part = new PartPlan([staff], [new ControlEvents()], null, new HeardCounts(), [], 0, 0, 0);
        MeasurePlan measure = new(bar, -2, [part.Measure(bar, 0, -2, null)]);

        XDocument mei = XDocument.Parse(MeiWriter.Write("Title", [part], [], [measure]));

        XElement scoreDef = mei.Descendants(s_mei + "scoreDef").First();
        Assert.Equal(("3", "4", "2f"), ((string)scoreDef.Attribute("meter.count")!, (string)scoreDef.Attribute("meter.unit")!, (string)scoreDef.Attribute("keysig")!));
        XElement staffDef = Assert.Single(mei.Descendants(s_mei + "staffDef"));
        Assert.Equal(("F", "4"), ((string)staffDef.Attribute("clef.shape")!, (string)staffDef.Attribute("clef.line")!));
        Assert.Single(mei.Descendants(s_mei + "mRest"));
        Assert.Equal("Title", mei.Descendants(s_mei + "title").Single().Value);
    }
}