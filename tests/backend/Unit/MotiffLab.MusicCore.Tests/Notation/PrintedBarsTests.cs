using System.Text;
using System.Xml.Linq;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Import.Vector;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>
/// «Как напечатано», step 1: notes with a source record written as the score prints them, bar by bar and staff by staff,
/// and the bars where the record does not fit written as before. Synthetic scores at 12 divisions a quarter.
/// </summary>
public class PrintedBarsTests
{
    private static readonly XNamespace s_mei = "http://www.music-encoding.org/ns/mei";
    private static readonly XNamespace s_xml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>A note: step and octave, length in 12ths of a quarter, its type, and what else it writes (dots, ties, stem, beam…).</summary>
    private static string N(string pitch, int duration, string type, string voice = "1", int staff = 1, string more = "", bool chord = false) =>
        $"<note>{(chord ? "<chord/>" : "")}<pitch><step>{pitch[0]}</step><octave>{pitch[1]}</octave></pitch><duration>{duration}</duration><voice>{voice}</voice><type>{type}</type>{more}<staff>{staff}</staff></note>";

    private static string R(int duration, string type, string voice = "1", int staff = 1, string more = "") =>
        $"<note><rest/><duration>{duration}</duration><voice>{voice}</voice><type>{type}</type>{more}<staff>{staff}</staff></note>";

    private static string Back(int duration) => $"<backup><duration>{duration}</duration></backup>";

    private static string Forward(int duration, string voice) => $"<forward><duration>{duration}</duration><voice>{voice}</voice><staff>1</staff></forward>";

    private const string Triplet = "<time-modification><actual-notes>3</actual-notes><normal-notes>2</normal-notes></time-modification>";

    private static string Beam(string where) => $"<beam number=\"1\">{where}</beam>";

    private static MusicXmlImport Score(int staves, string time, params string[] measures)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\"?><score-partwise version=\"4.0\"><part-list><score-part id=\"P1\"><part-name>Piano</part-name></score-part></part-list><part id=\"P1\">");
        string[] meter = time.Split('/');
        for (int index = 0; index < measures.Length; index++)
        {
            xml.Append($"<measure number=\"{index + 1}\">");
            if (index == 0)
            {
                xml.Append($"<attributes><divisions>12</divisions><time><beats>{meter[0]}</beats><beat-type>{meter[1]}</beat-type></time><staves>{staves}</staves></attributes>");
            }

            xml.Append(measures[index]).Append("</measure>");
        }

        return MusicXmlImporter.Import(Encoding.UTF8.GetBytes(xml.Append("</part></score-partwise>").ToString()));
    }

    private static XDocument Printed(MusicXmlImport import) => XDocument.Parse(NotationGenerator.FromMusicXml(import).Mei);

    private static XElement Bar(XDocument mei, int number, int staff = 1) =>
        mei.Descendants(s_mei + "measure").Single(measure => (string?)measure.Attribute("n") == number.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Elements(s_mei + "staff").Single(element => (int)element.Attribute("n")! == staff);

    private static string[] Ids(XElement element) => [.. element.Descendants(s_mei + "note").Select(note => (string)note.Attribute(s_xml + "id")!)];

    [Fact]
    public void Each_voice_is_written_as_printed_with_its_stems()
    {
        MusicXmlImport score = Score(1, "4/4",
            N("E5", 6, "eighth", more: "<stem>up</stem>" + Beam("begin")) + N("F5", 6, "eighth", more: "<stem>up</stem>" + Beam("end")) + N("G5", 12, "quarter", more: "<stem>up</stem>") + N("A5", 24, "half", more: "<stem>up</stem>")
            + Back(48) + N("C5", 48, "whole", voice: "2", more: "<stem>down</stem>"));

        XElement bar = Bar(Printed(score), 1);

        XElement[] layers = [.. bar.Elements(s_mei + "layer")];
        Assert.Equal(2, layers.Length);
        Assert.Equal(["n_0002", "n_0003", "n_0004", "n_0005"], Ids(layers[0]));
        Assert.Equal(["n_0001"], Ids(layers[1]));
        Assert.All(layers[0].Descendants(s_mei + "note"), note => Assert.Equal("up", (string?)note.Attribute("stem.dir")));
        Assert.Equal("down", (string?)layers[1].Element(s_mei + "note")!.Attribute("stem.dir"));
    }

    [Fact]
    public void A_dotted_note_and_a_tied_note_are_written_as_the_score_writes_them()
    {
        MusicXmlImport score = Score(1, "4/4",
            N("C5", 18, "quarter", more: "<dot/>") + N("D5", 6, "eighth")
            + N("E5", 12, "quarter", more: "<tie type=\"start\"/>") + N("E5", 6, "eighth", more: "<tie type=\"stop\"/>") + N("F5", 6, "eighth"));

        XElement bar = Bar(Printed(score), 1);

        XElement dotted = bar.Descendants(s_mei + "note").First();
        Assert.Equal(("4", "1"), ((string)dotted.Attribute("dur")!, (string)dotted.Attribute("dots")!));
        Assert.Equal(["n_0001", "n_0002", "n_0003", "n_0003-2", "n_0004"], Ids(bar));
    }

    [Fact]
    public void A_triplet_with_a_rest_in_it_is_one_tuplet()
    {
        MusicXmlImport score = Score(1, "2/4",
            N("C5", 4, "eighth", more: Triplet) + R(4, "eighth", more: Triplet) + N("E5", 4, "eighth", more: Triplet) + N("F5", 12, "quarter"));

        XElement tuplet = Bar(Printed(score), 1).Descendants(s_mei + "tuplet").Single();

        Assert.Equal(("3", "2"), ((string)tuplet.Attribute("num")!, (string)tuplet.Attribute("numbase")!));
        Assert.Equal(["note", "rest", "note"], tuplet.Elements().Select(element => element.Name.LocalName));
    }

    [Fact]
    public void The_score_s_beams_are_kept_where_it_beams_across_the_beat()
    {
        MusicXmlImport score = Score(1, "2/4",
            N("C5", 6, "eighth", more: Beam("begin")) + N("D5", 6, "eighth", more: Beam("continue")) + N("E5", 6, "eighth", more: Beam("continue")) + N("F5", 6, "eighth", more: Beam("end")));

        XElement beam = Bar(Printed(score), 1).Descendants(s_mei + "beam").Single();

        Assert.Equal(4, beam.Elements(s_mei + "note").Count());
    }

    [Fact]
    public void A_bar_whose_first_voice_does_not_add_up_is_written_as_before_and_the_others_as_printed()
    {
        string printed = N("C5", 48, "whole", more: "<stem>up</stem>");
        // Three quarters in a 4/4 bar, as an engine misreads one.
        string misread = N("D5", 12, "quarter", more: "<stem>up</stem>") + N("E5", 12, "quarter", more: "<stem>up</stem>") + N("F5", 12, "quarter", more: "<stem>up</stem>");
        MusicXmlImport score = Score(1, "4/4", printed, misread);

        XDocument mei = Printed(score);

        Assert.Equal("up", (string?)Bar(mei, 1).Descendants(s_mei + "note").Single().Attribute("stem.dir"));
        Assert.All(Bar(mei, 2).Descendants(s_mei + "note"), note => Assert.Null(note.Attribute("stem.dir")));
        Assert.NotNull(Bar(mei, 2).Descendants(s_mei + "rest").SingleOrDefault());
    }

    [Fact]
    public void A_voice_whose_notes_overlap_writes_the_bar_as_before()
    {
        MusicXmlImport score = Score(1, "4/4",
            N("C5", 24, "half", more: "<stem>up</stem>") + Back(12) + N("D5", 36, "half", more: "<dot/><stem>up</stem>"));

        Assert.All(Bar(Printed(score), 1).Descendants(s_mei + "note"), note => Assert.Null(note.Attribute("stem.dir")));
    }

    [Fact]
    public void A_corrected_note_s_voice_is_written_by_the_rules_and_the_other_voice_as_printed()
    {
        MusicXmlImport score = Score(1, "4/4",
            N("E5", 24, "half", more: "<stem>up</stem>") + N("F5", 24, "half", more: "<stem>up</stem>")
            + Back(48) + N("C5", 48, "whole", voice: "2", more: "<stem>down</stem>"));
        // The player made the second half note a quarter.
        Note[] notes = [.. score.Notes.Select(note => note.Id == "n_0003" ? note with { Span = new TickSpan(note.Span.Onset, 480), Attribution = Attribution.UserEdited } : note)];

        XDocument mei = XDocument.Parse(NotationGenerator.Generate(notes, score.Meter, [], score.End, printed: PrintedNotation.Of(score)).Mei);

        XElement[] layers = [.. Bar(mei, 1).Elements(s_mei + "layer")];
        Assert.Equal(["n_0002", "n_0003"], Ids(layers[0]));
        Assert.All(layers[0].Descendants(s_mei + "note"), note => Assert.Null(note.Attribute("stem.dir")));
        Assert.Single(layers[0].Elements(s_mei + "rest"));
        Assert.Equal("down", (string?)layers[1].Element(s_mei + "note")!.Attribute("stem.dir"));
    }

    [Fact]
    public void A_note_added_goes_into_the_voice_with_room_for_it()
    {
        MusicXmlImport score = Score(1, "4/4",
            N("E5", 24, "half", more: "<stem>up</stem>") + R(24, "half")
            + Back(48) + N("C5", 48, "whole", voice: "2", more: "<stem>down</stem>"));
        Note added = score.Notes[0] with { Id = "added", Pitch = new Pitch(79), Span = new TickSpan(new Tick(960), 960), Source = null, Attribution = Attribution.UserEdited };

        XDocument mei = XDocument.Parse(NotationGenerator.Generate([.. score.Notes, added], score.Meter, [], score.End, printed: PrintedNotation.Of(score)).Mei);

        XElement[] layers = [.. Bar(mei, 1).Elements(s_mei + "layer")];
        Assert.Equal(2, layers.Length);
        Assert.Equal(["n_0002", "added"], Ids(layers[0]));
    }

    [Fact]
    public void A_tie_from_a_printed_bar_into_one_written_as_before_keeps_every_id_once()
    {
        string tied = N("C5", 24, "half", more: "<stem>up</stem>") + N("D5", 24, "half", more: "<tie type=\"start\"/><stem>up</stem>");
        string misread = N("D5", 12, "quarter", more: "<tie type=\"stop\"/>") + N("E5", 12, "quarter") + N("F5", 12, "quarter");

        XDocument mei = Printed(Score(1, "4/4", tied, misread));

        string[] ids = Ids(mei.Root!);
        Assert.Equal(ids.Distinct(), ids);
        Assert.Contains("n_0002", Ids(Bar(mei, 1)));
        Assert.Contains("n_0002-2", Ids(Bar(mei, 2)));
    }

    [Fact]
    public void A_pickup_and_a_bar_of_seven_eighths_are_written_as_printed()
    {
        string pickup = N("G4", 6, "eighth", more: "<stem>up</stem>");
        string seven = N("C5", 18, "quarter", more: "<dot/><stem>up</stem>") + N("D5", 12, "quarter", more: "<stem>up</stem>") + N("E5", 12, "quarter", more: "<stem>up</stem>");
        var xml = $"<?xml version=\"1.0\"?><score-partwise version=\"4.0\"><part-list><score-part id=\"P1\"><part-name>Piano</part-name></score-part></part-list><part id=\"P1\"><measure number=\"0\" implicit=\"yes\"><attributes><divisions>12</divisions><time><beats>7</beats><beat-type>8</beat-type></time></attributes>{pickup}</measure><measure number=\"1\">{seven}</measure></part></score-partwise>";

        XDocument mei = Printed(MusicXmlImporter.Import(Encoding.UTF8.GetBytes(xml)));

        Assert.All(mei.Descendants(s_mei + "note"), note => Assert.NotNull(note.Attribute("stem.dir")));
        Assert.Equal("1", (string?)Bar(mei, 1).Descendants(s_mei + "note").First().Attribute("dots"));
    }

    [Fact]
    public void A_chord_in_a_voice_is_one_chord_with_its_stem()
    {
        MusicXmlImport score = Score(1, "2/4",
            N("C5", 12, "quarter", more: "<stem>down</stem>") + N("E5", 12, "quarter", chord: true, more: "<stem>down</stem>") + N("G5", 12, "quarter", more: "<stem>down</stem>"));

        XElement chord = Bar(Printed(score), 1).Descendants(s_mei + "chord").Single();

        Assert.Equal("down", (string?)chord.Attribute("stem.dir"));
        Assert.Equal(2, chord.Elements(s_mei + "note").Count());
    }

    [Fact]
    public void A_voice_that_crosses_to_the_other_staff_writes_both_staves_as_before()
    {
        MusicXmlImport score = Score(2, "2/4",
            N("C5", 12, "quarter", more: "<stem>up</stem>") + N("C3", 12, "quarter", staff: 2, more: "<stem>up</stem>")
            + Back(24) + N("C3", 24, "half", voice: "5", staff: 2, more: "<stem>down</stem>"));

        XDocument mei = Printed(score);

        Assert.All(mei.Descendants(s_mei + "note"), note => Assert.Null(note.Attribute("stem.dir")));
    }

    [Fact]
    public void The_time_a_lower_voice_skips_as_written_is_spaces()
    {
        MusicXmlImport score = Score(1, "4/4",
            N("E5", 48, "whole", more: "<stem>up</stem>")
            + Back(48) + Forward(24, "2") + N("C5", 12, "quarter", voice: "2", more: "<stem>down</stem>") + Forward(12, "2"));

        XElement lower = Bar(Printed(score), 1).Elements(s_mei + "layer").Last();

        Assert.Equal(["space", "note", "space"], lower.Elements().Select(element => element.Name.LocalName));
        Assert.Equal("down", (string?)lower.Element(s_mei + "note")!.Attribute("stem.dir"));
    }

    [Fact]
    public void A_lower_voice_that_leaves_a_gap_the_score_does_not_write_writes_the_bar_as_before()
    {
        MusicXmlImport score = Score(1, "4/4",
            N("E5", 48, "whole", more: "<stem>up</stem>")
            + Back(48) + Forward(24, "2") + N("C5", 12, "quarter", voice: "2", more: "<stem>down</stem>"));

        Assert.All(Bar(Printed(score), 1).Descendants(s_mei + "note"), note => Assert.Null(note.Attribute("stem.dir")));
        Assert.Equal(1, NotationGenerator.FromMusicXml(score).AppWrittenBarCount);
    }

    [Fact]
    public void A_hidden_rest_off_the_grid_in_a_lower_voice_writes_the_bar_as_before()
    {
        // A triplet eighth's time without a sign: no note value of the generator fills it.
        MusicXmlImport score = Score(1, "4/4",
            N("E5", 48, "whole", more: "<stem>up</stem>")
            + Back(48) + R(4, "eighth", voice: "2", more: Triplet).Replace("<note>", "<note print-object=\"no\">", StringComparison.Ordinal)
            + N("C5", 4, "eighth", voice: "2", more: Triplet + "<stem>down</stem>") + N("D5", 4, "eighth", voice: "2", more: Triplet + "<stem>down</stem>")
            + N("E4", 12, "quarter", voice: "2", more: "<stem>down</stem>") + N("F4", 24, "half", voice: "2", more: "<stem>down</stem>"));

        Assert.All(Bar(Printed(score), 1).Descendants(s_mei + "note"), note => Assert.Null(note.Attribute("stem.dir")));
    }

    [Fact]
    public void A_note_moved_to_another_pitch_keeps_its_voice_as_printed()
    {
        MusicXmlImport score = Score(1, "4/4",
            N("E5", 18, "quarter", more: "<dot/><stem>up</stem>") + N("F5", 6, "eighth", more: "<stem>up</stem>") + N("G5", 24, "half", more: "<stem>up</stem>"));
        Note[] notes = [.. score.Notes.Select(note => note.Id == "n_0002" ? note with { Pitch = new Pitch(76), Attribution = Attribution.UserEdited } : note)];

        XDocument mei = XDocument.Parse(NotationGenerator.Generate(notes, score.Meter, [], score.End, printed: PrintedNotation.Of(score)).Mei);

        XElement[] written = [.. Bar(mei, 1).Descendants(s_mei + "note")];
        Assert.All(written, note => Assert.Equal("up", (string?)note.Attribute("stem.dir")));
        Assert.Equal("1", (string?)written[0].Attribute("dots"));
    }

    [Fact]
    public void Tuplets_without_brackets_end_on_the_beat()
    {
        string sixTriplets = string.Concat(Enumerable.Range(0, 6).Select(index => N("C5", 4, "eighth", more: Triplet)));
        string quarterTriplets = N("C5", 12, "quarter") + string.Concat(Enumerable.Range(0, 3).Select(index => N("D5", 8, "quarter", more: Triplet))) + N("E5", 12, "quarter");

        XDocument mei = Printed(Score(1, "2/4", sixTriplets));
        XDocument across = Printed(Score(1, "4/4", quarterTriplets));

        Assert.Equal([3, 3], Bar(mei, 1).Descendants(s_mei + "tuplet").Select(tuplet => tuplet.Elements(s_mei + "note").Count() + tuplet.Elements(s_mei + "beam").Elements(s_mei + "note").Count()));
        Assert.Equal(3, Bar(across, 1).Descendants(s_mei + "tuplet").Single().Descendants(s_mei + "note").Count());
    }

    [Fact]
    public void A_note_removed_from_a_lower_voice_leaves_that_voice_to_the_rules()
    {
        MusicXmlImport score = Score(1, "4/4",
            N("E5", 48, "whole", more: "<stem>up</stem>")
            + Back(48) + N("C5", 24, "half", voice: "2", more: "<stem>down</stem>") + N("D5", 24, "half", voice: "2", more: "<stem>down</stem>"));
        Note[] notes = [.. score.Notes.Where(note => note.Pitch.MidiNumber != 74)];

        XDocument mei = XDocument.Parse(NotationGenerator.Generate(notes, score.Meter, [], score.End, printed: PrintedNotation.Of(score)).Mei);

        XElement[] layers = [.. Bar(mei, 1).Elements(s_mei + "layer")];
        Assert.Equal("up", (string?)layers[0].Element(s_mei + "note")!.Attribute("stem.dir"));
        Assert.Null(layers[1].Element(s_mei + "note")!.Attribute("stem.dir"));
    }

    [Fact]
    public void The_bars_the_app_writes_are_counted_and_none_without_a_record()
    {
        string printed = N("C5", 48, "whole");
        // Three quarters in the last 4/4 bar, as an engine misreads one.
        string misread = N("D5", 12, "quarter") + N("E5", 12, "quarter") + N("F5", 12, "quarter");
        MusicXmlImport score = Score(1, "4/4", printed, printed, misread);

        Assert.Equal(1, NotationGenerator.FromMusicXml(score).AppWrittenBarCount);
        Assert.Null(NotationGenerator.Generate(score.Notes, score.Meter, [], score.End).AppWrittenBarCount);
    }

    [Fact]
    public void A_rest_whose_written_value_is_not_its_length_is_written_by_its_length_and_the_bar_as_printed()
    {
        // As the design kit's Für Elise writes the left hand's silent bars: a quarter without a dot filling 3/8.
        string upper = N("E5", 6, "eighth", more: "<stem>up</stem>") + N("D5", 6, "eighth", more: "<stem>up</stem>") + N("C5", 6, "eighth", more: "<stem>up</stem>");
        MusicXmlImport shorter = Score(2, "3/8", upper + Back(18) + R(18, "quarter", voice: "2", staff: 2));
        MusicXmlImport longer = Score(2, "3/8", upper + Back(18) + R(18, "half", voice: "2", staff: 2));

        foreach (MusicXmlImport score in new[] { shorter, longer })
        {
            XDocument mei = Printed(score);

            Assert.All(Bar(mei, 1).Descendants(s_mei + "note"), note => Assert.Equal("up", (string?)note.Attribute("stem.dir")));
            XElement rest = Bar(mei, 1, staff: 2).Descendants(s_mei + "rest").Single();
            Assert.Equal(("4", "1"), ((string)rest.Attribute("dur")!, (string?)rest.Attribute("dots")));
            Assert.Equal(0, NotationGenerator.FromMusicXml(score).AppWrittenBarCount);
        }
    }

    [Fact]
    public void A_rest_written_by_its_length_keeps_its_voice_s_other_notes_as_printed()
    {
        MusicXmlImport score = Score(1, "3/8",
            N("E5", 18, "quarter", more: "<dot/><stem>up</stem>")
            + Back(18) + N("C4", 6, "eighth", voice: "2", more: "<stem>down</stem>") + R(12, "eighth", voice: "2"));

        XElement[] layers = [.. Bar(Printed(score), 1).Elements(s_mei + "layer")];

        Assert.Equal(2, layers.Length);
        Assert.Equal("down", (string?)layers[1].Element(s_mei + "note")!.Attribute("stem.dir"));
        Assert.Equal(["note", "rest"], layers[1].Elements().Select(element => element.Name.LocalName));
        Assert.Equal("4", (string?)layers[1].Element(s_mei + "rest")!.Attribute("dur"));
        Assert.Equal(0, NotationGenerator.FromMusicXml(score).AppWrittenBarCount);
    }

    [Fact]
    public void A_note_whose_written_value_is_not_its_length_still_writes_the_bar_as_before()
    {
        MusicXmlImport score = Score(1, "3/8",
            N("E5", 18, "quarter", more: "<dot/><stem>up</stem>") + Back(18) + N("C4", 18, "quarter", voice: "2", more: "<stem>down</stem>"));

        Assert.All(Bar(Printed(score), 1).Descendants(s_mei + "note"), note => Assert.Null(note.Attribute("stem.dir")));
        Assert.Equal(1, NotationGenerator.FromMusicXml(score).AppWrittenBarCount);
    }

    [Fact]
    public void A_rest_that_runs_past_the_bar_still_writes_the_bar_as_before()
    {
        MusicXmlImport score = Score(1, "3/8",
            N("E5", 18, "quarter", more: "<dot/><stem>up</stem>") + Back(18) + N("C4", 6, "eighth", voice: "2") + R(18, "quarter", voice: "2"),
            N("E5", 18, "quarter", more: "<dot/><stem>up</stem>"));

        Assert.All(Bar(Printed(score), 1).Descendants(s_mei + "note"), note => Assert.Null(note.Attribute("stem.dir")));
        Assert.NotEqual(0, NotationGenerator.FromMusicXml(score).AppWrittenBarCount);
    }

    [Fact]
    public void A_rest_in_a_tuplet_is_written_by_its_length_in_its_tuplet()
    {
        // A triplet eighth's rest that the file calls a quarter.
        MusicXmlImport score = Score(1, "2/4",
            N("C5", 4, "eighth", more: Triplet) + R(4, "quarter", more: Triplet) + N("E5", 4, "eighth", more: Triplet) + N("F5", 12, "quarter"));

        XElement tuplet = Bar(Printed(score), 1).Descendants(s_mei + "tuplet").Single();

        Assert.Equal(["note", "rest", "note"], tuplet.Elements().Select(element => element.Name.LocalName));
        Assert.Equal("8", (string?)tuplet.Element(s_mei + "rest")!.Attribute("dur"));
        Assert.Equal(0, NotationGenerator.FromMusicXml(score).AppWrittenBarCount);
    }

    [Fact]
    public void A_rest_in_a_tuplet_that_no_value_makes_with_its_ratio_writes_the_bar_as_before()
    {
        // Nine twelfths of a quarter under 3:2 would be a note of 9/8 quarters: no value with up to two dots is.
        MusicXmlImport score = Score(1, "2/4",
            R(9, "quarter", more: Triplet) + N("E5", 3, "16th", more: Triplet + "<dot/><stem>up</stem>") + N("F5", 12, "quarter", more: "<stem>up</stem>"));

        Assert.All(Bar(Printed(score), 1).Descendants(s_mei + "note"), note => Assert.Null(note.Attribute("stem.dir")));
        Assert.Equal(1, NotationGenerator.FromMusicXml(score).AppWrittenBarCount);
    }

    [Fact]
    public void The_design_kit_s_Fur_Elise_and_the_app_s_are_written_as_printed()
    {
        static MusicXmlImport Read(params string[] path) => MusicXmlImporter.Import(File.ReadAllBytes(Path.Combine([AppContext.BaseDirectory, .. path])));

        Assert.Equal(0, NotationGenerator.FromMusicXml(Read("Golden", "Analysis", "elise.musicxml")).AppWrittenBarCount);
        Assert.Equal(0, NotationGenerator.FromMusicXml(Read("Import", "MusicXml", "Fixtures", "FurElise.musicxml")).AppWrittenBarCount);
    }

    [Fact]
    public void Notes_written_as_printed_are_not_counted_as_moved_to_the_grid()
    {
        MusicXmlImport septuplets = MusicXmlImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "septuplets.musicxml")));

        Assert.NotEqual(0, NotationGenerator.Generate(septuplets.Notes, septuplets.Meter, [], septuplets.End).ApproximatedNoteCount);
        Assert.Equal(0, NotationGenerator.FromMusicXml(septuplets).ApproximatedNoteCount);
    }

    [Fact]
    public void The_vector_reading_writes_its_beams_and_the_time_a_voice_skips_to_the_bar_s_end()
    {
        static VectorEvent Eighth(double x, int onset, char step) => new()
        {
            X = x,
            Staff = 0,
            Glyph = MusicGlyph.NoteheadBlack,
            Beams = 1,
            Right = x + 1,
            Onset = onset,
            Ticks = 240,
            Voice = 1,
            Beam = 0,
            Pitches = { new VectorPitch(step, 0, 5) },
        };
        var half = new VectorEvent { X = 20, Staff = 0, Glyph = MusicGlyph.NoteheadHalf, Right = 21, Onset = 960, Ticks = 960, Voice = 2, Pitches = { new VectorPitch('A', 0, 4) } };
        var bar = new VectorBar { Staves = [[Eighth(0, 0, 'C'), Eighth(5, 240, 'D'), Eighth(10, 480, 'E'), Eighth(15, 720, 'F'), half]] };

        MusicXmlImport read = MusicXmlImporter.Import(VectorMusicXml.Write([bar], 1, ([VectorClef.Treble], 0, new VectorMeter(4, 4))));

        Assert.Equal([MusicXmlBeam.Begin, MusicXmlBeam.Continue, MusicXmlBeam.Continue, MusicXmlBeam.End], read.WrittenNotes.Where(note => note.Voice == "1").OrderBy(note => note.Span.Onset).Select(note => note.Writing.Beam));
        Assert.Equal(new TickSpan(Tick.Zero, 960), read.Spaces.Single(space => space.Voice == "2").Span);
        // The eighths end halfway: the voice skips the rest of the bar, so it adds up to the bar.
        Assert.Equal(new TickSpan(new Tick(960), 960), read.Spaces.Single(space => space.Voice == "1").Span);
    }
}