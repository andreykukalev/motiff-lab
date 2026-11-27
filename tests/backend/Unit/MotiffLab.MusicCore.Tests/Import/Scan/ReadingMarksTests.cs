using System.Text;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;

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
        Assert.Single(marked.Applied);
        Assert.Empty(marked.Unfit);
    }

    [Fact]
    public void A_clef_marked_reads_the_staffs_notes_again_under_it_and_only_in_its_bars()
    {
        (MarkedReading marked, ImportedPiece piece) = Apply(Score(), new ReadingMark(ReadingMarkKind.Clef, 1, 1, 2, "treble"));

        // G2 on the bass staff's bottom line is E4 under a treble clef; bar 2 keeps its G2.
        Assert.Equal([64], Pitches(piece, 1, Hand.Left));
        Assert.Equal([43], Pitches(piece, 2, Hand.Left));
        Assert.Single(marked.Applied);
    }

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
        Assert.Empty(marked.Applied);
        Assert.Equal([beyond, staff], marked.Unfit.OrderBy(static mark => mark.Kind == ReadingMarkKind.Hands ? 0 : 1));
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