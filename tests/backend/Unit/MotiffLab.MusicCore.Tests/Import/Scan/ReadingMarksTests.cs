using System.Text;
using System.Xml.Linq;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.Scan;

/// <summary>
/// The player's marks «что не так» put into a new reading of the pages («Как напечатано», step 3, c-14b1): hands, a clef,
/// an octave sign, a key and a time signature, each on synthetic MusicXML of two bars on two staves, and the marks that
/// cannot go in or wait for a later step.
/// </summary>
public class ReadingMarksTests
{
    /// <summary>
    /// Two bars of 4/4 under a treble and a bass clef in C: staff 1 in bar 1 F4, C#5 (no accidental written, as homr writes),
    /// F4 with a natural written, F4 again; bar 2 four F4; staff 2 a whole G2 in each bar.
    /// </summary>
    private static string Score(string firstAttributes = "<key><fifths>0</fifths></key><time><beats>4</beats><beat-type>4</beat-type></time>")
    {
        static string Note(string step, int octave, int alter = 0, int staff = 1, int duration = 1, string? accidental = null) =>
            $"<note><pitch><step>{step}</step>{(alter == 0 ? "" : $"<alter>{alter}</alter>")}<octave>{octave}</octave></pitch><duration>{duration}</duration><voice>{staff}</voice>"
            + $"{(accidental is null ? "" : $"<accidental>{accidental}</accidental>")}<staff>{staff}</staff></note>";
        string lower = $"<backup><duration>4</duration></backup>{Note("G", 2, staff: 2, duration: 4)}";
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><score-partwise version=\"3.1\"><part-list><score-part id=\"P1\"><part-name>Piano</part-name></score-part></part-list>"
            + $"<part id=\"P1\"><measure number=\"1\"><attributes><divisions>1</divisions>{firstAttributes}<staves>2</staves>"
            + "<clef number=\"1\"><sign>G</sign><line>2</line></clef><clef number=\"2\"><sign>F</sign><line>4</line></clef></attributes>"
            + Note("F", 4) + Note("C", 5, alter: 1) + Note("F", 4, accidental: "natural") + Note("F", 4) + lower + "</measure>"
            + "<measure number=\"2\">" + Note("F", 4) + Note("F", 4) + Note("F", 4) + Note("F", 4) + lower + "</measure></part></score-partwise>";
    }

    private static (MarkedReading Marked, ImportedPiece Piece) Apply(string score, params ReadingMark[] marks)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(score);
        ImportedPiece read = PieceImporter.Import("a.musicxml", bytes).Piece!;
        MarkedReading marked = ReadingMarks.Apply(bytes, read, marks);
        return (marked, PieceImporter.Import("a.musicxml", marked.Score).Piece!);
    }

    private static int[] Pitches(ImportedPiece piece, int bar, Hand hand) =>
        [.. piece.Notes.Where(note => piece.Meter.BarAt(note.Span.Onset).Number == bar && note.Hand?.Value == hand)
            .OrderBy(static note => note.Span.Onset).Select(static note => note.Pitch.MidiNumber)];

    [Fact]
    public void A_key_marked_gives_its_sharps_to_the_notes_from_its_bar_keeping_what_the_page_alters()
    {
        (MarkedReading marked, ImportedPiece piece) = Apply(Score(), new ReadingMark(ReadingMarkKind.Key, 1, 1, null, "1"));

        // F4 → F#4; C#5 kept (the reading's own key does not give it); the F4 with a natural and the one after it in the bar kept.
        Assert.Equal([66, 73, 65, 65], Pitches(piece, 1, Hand.Right));
        Assert.Equal([66, 66, 66, 66], Pitches(piece, 2, Hand.Right));
        Assert.Contains("<fifths>1</fifths>", Encoding.UTF8.GetString(marked.Score), StringComparison.Ordinal);
        Assert.Equal([MarkOutcome.Applied], marked.Marks.Select(static mark => mark.Outcome));
    }

    [Fact]
    public void A_clef_marked_reads_the_staffs_notes_again_under_it_and_only_in_its_bars()
    {
        (MarkedReading marked, ImportedPiece piece) = Apply(Score(), new ReadingMark(ReadingMarkKind.Clef, 1, 1, 2, "treble"));

        // G2 on the bass staff's bottom line is E4 under a treble clef; bar 2 keeps its G2.
        Assert.Equal([64], Pitches(piece, 1, Hand.Left));
        Assert.Equal([43], Pitches(piece, 2, Hand.Left));
        Assert.Equal([MarkOutcome.Applied], marked.Marks.Select(static mark => mark.Outcome));

        // The printed clef is written at the mark's first bar and the reading's own again after its last.
        XElement[] measures = [.. XDocument.Parse(Encoding.UTF8.GetString(marked.Score)).Root!.Element("part")!.Elements("measure")];
        Assert.Equal("G2", Clef(measures[0], 2));
        Assert.Equal("F4", Clef(measures[1], 2));
    }

    private static string? Clef(XElement measure, int staff) =>
        measure.Elements("attributes").Elements("clef").Where(clef => (int?)clef.Attribute("number") == staff)
            .Select(static clef => $"{clef.Element("sign")!.Value}{clef.Element("line")!.Value}").SingleOrDefault();

    [Fact]
    public void An_octave_sign_marked_moves_the_staffs_notes_an_octave_in_its_bars()
    {
        (_, ImportedPiece piece) = Apply(Score(), new ReadingMark(ReadingMarkKind.Octave, 2, 2, 1, "8va"));

        Assert.Equal([77, 77, 77, 77], Pitches(piece, 2, Hand.Right));
        Assert.Equal([65, 73, 65, 65], Pitches(piece, 1, Hand.Right));
    }

    [Fact]
    public void Hands_marked_swap_the_staves_of_the_bars()
    {
        (_, ImportedPiece piece) = Apply(Score(), new ReadingMark(ReadingMarkKind.Hands, 2, 2, null, null));

        Assert.Equal([43], Pitches(piece, 2, Hand.Right));
        Assert.Equal([65, 65, 65, 65], Pitches(piece, 2, Hand.Left));
        Assert.Equal([43], Pitches(piece, 1, Hand.Left));
    }

    [Fact]
    public void A_time_signature_marked_replaces_the_one_read_from_its_bar_on()
    {
        (MarkedReading marked, ImportedPiece piece) = Apply(Score("<time><beats>5</beats><beat-type>4</beat-type></time>"), new ReadingMark(ReadingMarkKind.Meter, 1, 1, null, "4/4"));

        Assert.Equal("4/4", piece.Meter.GetBar(2).Signature.ToString());
        Assert.Contains("Motiff Lab reading marks, the player's: Time signature here: 4/4, from bar 1.", Encoding.UTF8.GetString(marked.Score), StringComparison.Ordinal);
    }

    [Fact]
    public void A_mark_the_reading_cannot_take_is_unfit_and_triplets_and_a_bar_read_badly_wait()
    {
        byte[] score = Encoding.UTF8.GetBytes(Score());
        ImportedPiece read = PieceImporter.Import("a.musicxml", score).Piece!;
        ReadingMark beyond = new(ReadingMarkKind.Hands, 9, 9, null, null);
        ReadingMark staff = new(ReadingMarkKind.Octave, 1, 1, 3, "8vb");
        ReadingMark triplets = new(ReadingMarkKind.Triplets, 1, 2, null, "yes");
        ReadingMark badly = new(ReadingMarkKind.BadlyRead, 1, 1, null, null);

        MarkedReading marked = ReadingMarks.Apply(score, read, [beyond, staff, triplets, badly]);

        Assert.Same(score, marked.Score);
        Assert.Equal([(beyond, MarkOutcome.Unfit), (staff, MarkOutcome.Unfit), (triplets, MarkOutcome.Waiting), (badly, MarkOutcome.Waiting)],
            marked.Marks.Select(static mark => (mark.Mark, mark.Outcome)));
    }

    [Fact]
    public void A_bar_the_marked_meter_fits_once_its_split_moments_are_put_back_is_re_timed()
    {
        byte[] score = ScanRepairTests.Score(2, "4", ScanRepairTests.Whole(), ScanRepairTests.Split());
        ImportedPiece read = PieceImporter.Import("a.musicxml", score).Piece!;

        MarkedReading marked = ReadingMarks.Apply(score, read, [new ReadingMark(ReadingMarkKind.Meter, 1, 1, null, "4/4")]);

        // E1 under the split bar's left hand starts with the bar once re-timed, an eighth late as read.
        Assert.Equal(240, LowestOnset(read));
        Assert.Equal(0, LowestOnset(PieceImporter.Import("a.musicxml", marked.Score).Piece!));
    }

    private static long LowestOnset(ImportedPiece piece)
    {
        TickSpan bar = piece.MusicXml!.Measures[1].Span;
        return piece.Notes.Where(note => bar.Contains(note.Span.Onset)).MinBy(static note => note.Pitch.MidiNumber)!.Span.Onset.Value - bar.Onset.Value;
    }

    [Fact]
    public void Marks_are_numbered_by_the_bars_of_the_notes_made_when_a_meter_mark_makes_a_pickup()
    {
        static string Bar(int quarters) => string.Concat(Enumerable.Repeat(ScanRepairTests.Note("C", 5, 2, 1), quarters)) + ScanRepairTests.Backup(2 * quarters) + ScanRepairTests.Note("C", 3, 2 * quarters, 2);
        byte[] score = ScanRepairTests.Score(2, "3", Bar(3), Bar(4), Bar(4));
        ImportedPiece read = PieceImporter.Import("a.musicxml", score).Piece!;
        Assert.Equal(1, read.Meter.BarAt(Tick.Zero).Number);

        MarkedReading marked = ReadingMarks.Apply(score, read, [new ReadingMark(ReadingMarkKind.Meter, 1, 1, null, "4/4"), new ReadingMark(ReadingMarkKind.Hands, 2, 2, null, null)]);
        ImportedPiece made = PieceImporter.Import("a.musicxml", marked.Score).Piece!;

        // The first bar is a pickup under 4/4: the meter mark is on bar 0, the hands on bar 1, where the hands are swapped.
        Assert.Equal(0, made.Meter.BarAt(Tick.Zero).Number);
        Assert.Equal(["Hands:1-1", "Meter:0-0"], marked.Numbered(made).Select(static each => $"{each.Mark.Kind}:{each.Mark.Bar}-{each.Mark.Last}").Order());
        Assert.Equal([48], Pitches(made, 1, Hand.Right));
    }

    [Fact]
    public void Marks_of_a_kind_overlapping_join_or_give_way_so_each_bar_takes_one()
    {
        ScanOrigin scan = ScanOrigin.Read("homr")
            .WithMark(new ReadingMark(ReadingMarkKind.Hands, 3, 4, null, null))
            .WithMark(new ReadingMark(ReadingMarkKind.Hands, 4, 5, null, null))
            .WithMark(new ReadingMark(ReadingMarkKind.Octave, 2, 4, 1, "8va"))
            .WithMark(new ReadingMark(ReadingMarkKind.Octave, 3, 3, 1, "8vb"))
            .WithMark(new ReadingMark(ReadingMarkKind.Octave, 3, 3, 2, "8va"));

        Assert.Equal(["Octave 2-2 1 8va", "Octave 3-3 1 8vb", "Octave 3-3 2 8va", "Hands 3-5  ", "Octave 4-4 1 8va"],
            scan.Marks.Select(static mark => $"{mark.Kind} {mark.Bar}-{mark.Last} {mark.Staff} {mark.Value}"));
    }

    [Fact]
    public void A_mark_says_only_what_its_kind_can_and_reads_in_both_languages()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadingMark(ReadingMarkKind.Meter, 3, 5, null, "3/4"));
        Assert.Throws<ArgumentException>(() => new ReadingMark(ReadingMarkKind.Clef, 3, 5, null, "bass"));
        Assert.Throws<ArgumentException>(() => new ReadingMark(ReadingMarkKind.Hands, 3, 5, 1, null));
        Assert.Throws<ArgumentException>(() => new ReadingMark(ReadingMarkKind.Meter, 3, 3, null, "3/5"));
        Assert.Throws<ArgumentException>(() => new ReadingMark(ReadingMarkKind.Key, 3, 3, null, "+2"));
        Assert.Throws<ArgumentException>(() => new ReadingMark(ReadingMarkKind.Triplets, 3, 3, null, null));

        Assert.Equal("Размер здесь 5/4, с такта 5", new ReadingMark(ReadingMarkKind.Meter, 5, 5, null, "5/4").Text(Words.Russian));
        Assert.Equal("Тональность здесь: 3 бемоля, с такта 1", new ReadingMark(ReadingMarkKind.Key, 1, 1, null, "-3").Text(Words.Russian));
        Assert.Equal("Здесь басовый ключ, нижний стан, такты 3–6", new ReadingMark(ReadingMarkKind.Clef, 3, 6, 2, "bass").Text(Words.Russian));
        Assert.Equal("Hands swapped, bar 4", new ReadingMark(ReadingMarkKind.Hands, 4, 4, null, null).Text(Words.English));
    }
}