using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.MusicXml;

/// <summary>
/// The self-made files in Fixtures (see make_fixtures.py and README.md). Until the MusicXML importer exists,
/// these tests pin the structure each file was written to have, so importer tests can rely on it.
/// </summary>
public class MusicXmlFixtureTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", name);

    /// <summary>Reads like an importer must: the DOCTYPE names a DTD on the web, which is never fetched.</summary>
    private static XDocument Load(Stream stream)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader);
    }

    private static XDocument LoadFixture(string name)
    {
        using FileStream stream = File.OpenRead(FixturePath(name));
        return Load(stream);
    }

    [Theory]
    [InlineData("ties-triplets-pickup.musicxml", 5, 20, 0, 2, 3)]
    [InlineData("septuplets.musicxml", 3, 25, 0, 0, 3)]
    [InlineData("grand-staff.musicxml", 4, 26, 1, 0, 0)]
    [InlineData("phrases-repeats.musicxml", 5, 15, 1, 0, 0)]
    public void Fixture_has_the_written_content(string fixture, int measures, int notes, int rests, int ties, int tuplets)
    {
        XDocument score = LoadFixture(fixture);
        XElement part = Assert.Single(score.Root!.Elements("part"));
        XElement[] written = [.. part.Descendants("note")];

        Assert.Equal("score-partwise", score.Root.Name.LocalName);
        Assert.Equal("4.0", score.Root.Attribute("version")?.Value);
        Assert.Equal(measures, part.Elements("measure").Count());
        Assert.Equal(notes, written.Count(note => note.Element("rest") is null));
        Assert.Equal(rests, written.Count(note => note.Element("rest") is not null));
        Assert.Equal(ties, written.SelectMany(note => note.Elements("tie")).Count(tie => tie.Attribute("type")?.Value == "start"));
        Assert.Equal(tuplets, part.Descendants("tuplet").Count(tuplet => tuplet.Attribute("type")?.Value == "start"));
    }

    [Theory]
    [InlineData("ties-triplets-pickup.musicxml", "6 24 24 24 18")]
    [InlineData("septuplets.musicxml", "28 28 28")]
    [InlineData("grand-staff.musicxml", "6 6 6 6")]
    [InlineData("phrases-repeats.musicxml", "16 16 16 16 16")]
    public void Measures_have_the_written_lengths_in_divisions(string fixture, string lengths)
    {
        XElement part = LoadFixture(fixture).Root!.Element("part")!;

        Assert.Equal(lengths, string.Join(' ', part.Elements("measure").Select(MeasureLength)));
    }

    [Fact]
    public void Pickup_is_an_implicit_bar_zero()
    {
        XElement pickup = LoadFixture("ties-triplets-pickup.musicxml").Root!.Element("part")!.Element("measure")!;

        Assert.Equal("0", pickup.Attribute("number")?.Value);
        Assert.Equal("yes", pickup.Attribute("implicit")?.Value);
    }

    [Fact]
    public void Septuplets_round_at_twenty_one_notes()
    {
        XElement part = LoadFixture("septuplets.musicxml").Root!.Element("part")!;
        var divisions = new MusicXmlDivisions(7);
        long position = 0;
        int rounded = 0;

        foreach (XElement note in part.Descendants("note"))
        {
            long duration = long.Parse(note.Element("duration")!.Value, CultureInfo.InvariantCulture);
            ConvertedSpan span = CanonicalTime.ToSpan(divisions.ToQuarters(position), divisions.ToQuarters(position + duration));
            rounded += span.IsExact ? 0 : 1;
            position += duration;
        }

        Assert.Equal(21, rounded);
    }

    [Fact]
    public void Grand_staff_splits_notes_between_two_staves()
    {
        XElement part = LoadFixture("grand-staff.musicxml").Root!.Element("part")!;
        XElement[] notes = [.. part.Descendants("note").Where(note => note.Element("rest") is null)];

        Assert.Equal("2", part.Descendants("staves").Single().Value);
        Assert.Equal(11, notes.Count(note => note.Element("staff")?.Value == "1"));
        Assert.Equal(15, notes.Count(note => note.Element("staff")?.Value == "2"));
        Assert.Equal(["1", "2", "5"], notes.Select(note => note.Element("voice")!.Value).Distinct().Order());
    }

    [Fact]
    public void Phrases_and_repeats_are_marked_in_the_source()
    {
        XElement part = LoadFixture("phrases-repeats.musicxml").Root!.Element("part")!;

        Assert.Equal(
            ["forward", "backward"],
            part.Descendants("repeat").Select(repeat => repeat.Attribute("direction")!.Value));
        Assert.Equal(["1", "2"], part.Descendants("ending").Select(ending => ending.Attribute("number")!.Value).Distinct());
        Assert.Equal(3, part.Descendants("slur").Count(slur => slur.Attribute("number")?.Value == "1" && slur.Attribute("type")?.Value == "start"));
        Assert.Equal(1, part.Descendants("slur").Count(slur => slur.Attribute("number")?.Value == "2" && slur.Attribute("type")?.Value == "start"));
    }

    [Fact]
    public void Compressed_file_holds_the_grand_staff_score()
    {
        using ZipArchive archive = ZipFile.OpenRead(FixturePath("grand-staff.mxl"));

        ZipArchiveEntry mimetype = archive.Entries[0];
        Assert.Equal("mimetype", mimetype.FullName);
        Assert.Equal(mimetype.Length, mimetype.CompressedLength);
        using (var reader = new StreamReader(mimetype.Open()))
        {
            Assert.Equal("application/vnd.recordare.musicxml", reader.ReadToEnd());
        }

        XDocument container;
        using (Stream stream = archive.GetEntry("META-INF/container.xml")!.Open())
        {
            container = Load(stream);
        }

        string rootPath = container.Descendants("rootfile").First().Attribute("full-path")!.Value;
        using Stream score = archive.GetEntry(rootPath)!.Open();
        using var copy = new MemoryStream();
        score.CopyTo(copy);
        Assert.Equal(File.ReadAllBytes(FixturePath("grand-staff.musicxml")), copy.ToArray());
    }

    /// <summary>The furthest position reached in a measure, following backup and forward and skipping chord notes.</summary>
    private static long MeasureLength(XElement measure)
    {
        long position = 0;
        long furthest = 0;
        foreach (XElement element in measure.Elements())
        {
            long duration = element.Element("duration") is { } d ? long.Parse(d.Value, CultureInfo.InvariantCulture) : 0;
            switch (element.Name.LocalName)
            {
                case "note" when element.Element("chord") is null:
                case "forward":
                    position += duration;
                    break;
                case "backup":
                    position -= duration;
                    break;
            }

            furthest = Math.Max(furthest, position);
        }

        return furthest;
    }
}