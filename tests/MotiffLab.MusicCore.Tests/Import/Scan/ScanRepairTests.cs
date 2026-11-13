using System.Text;
using System.Xml.Linq;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;

namespace MotiffLab.MusicCore.Tests.Import.Scan;

/// <summary>
/// The engine's split moments put back and the time signature taken from the bars (v0.5, issue #479, item 20). Every
/// fixture is written here, in the shape the engine writes: one piano part on two staves, divisions 2 (an eighth is 1).
/// </summary>
public class ScanRepairTests
{
    private const long Eighth = 240;

    private static string Note(string step, int octave, int duration, int staff, bool chord = false) =>
        $"<note>{(chord ? "<chord/>" : "")}<pitch><step>{step}</step><octave>{octave}</octave></pitch><duration>{duration}</duration><voice>{(staff == 1 ? 1 : 5)}</voice><staff>{staff}</staff></note>";

    private static string Grace(string step, int octave) =>
        $"<note><grace slash=\"yes\"/><pitch><step>{step}</step><octave>{octave}</octave></pitch><voice>1</voice><staff>1</staff></note>";

    private static string Rest(int duration, int staff) => $"<note><rest/><duration>{duration}</duration><voice>{(staff == 1 ? 1 : 5)}</voice><staff>{staff}</staff></note>";

    private static string Backup(int duration) => $"<backup><duration>{duration}</duration></backup>";

    /// <summary>
    /// A 4/4 bar as the engine splits it: a grace note and the right hand's first eighth, then the left hand's half note
    /// written after it instead of with it, so the bar runs six quarters.
    /// </summary>
    private static string Split() =>
        Grace("D", 4) + Note("E", 4, 1, 1) + Note("E", 2, 4, 2) + Note("E", 1, 4, 2, chord: true)
        + Note("B", 3, 1, 1) + Note("B", 3, 1, 1) + Note("B", 3, 1, 1)
        + Note("D", 4, 1, 1) + Backup(1) + Note("G", 3, 4, 2) + Note("E", 3, 4, 2, chord: true) + Backup(3)
        + Note("D", 4, 1, 1) + Note("D", 4, 1, 1) + Note("D", 4, 1, 1);

    /// <summary>The same bar read right: the eighth rest and the left hand's half note start together.</summary>
    private static string Whole() =>
        Rest(1, 1) + Backup(1) + Note("G", 2, 4, 2) + Note("G", 1, 4, 2, chord: true) + Backup(3)
        + Note("B", 3, 1, 1) + Note("B", 3, 1, 1) + Note("B", 3, 1, 1)
        + Note("D", 4, 1, 1) + Backup(1) + Note("G", 3, 4, 2) + Note("B", 3, 4, 2, chord: true) + Backup(3)
        + Note("D", 4, 1, 1) + Note("D", 4, 1, 1) + Note("D", 4, 1, 1);

    /// <summary>The hands take turns in eighths with no rests: four quarters as read, two if each pair were put together.</summary>
    private static string Turns() =>
        string.Concat(Enumerable.Repeat(Note("C", 5, 1, 1) + Note("C", 3, 1, 2), 4));

    private static byte[] Score(string? beats, params string[] bars)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><score-partwise version=\"4.0\">")
            .Append("<identification><encoding><software>homr</software></encoding></identification>")
            .Append("<part-list><score-part id=\"P1\"><part-name>Piano</part-name></score-part></part-list><part id=\"P1\">");
        for (int i = 0; i < bars.Length; i++)
        {
            string time = i == 0 && beats is not null ? $"<time><beats>{beats}</beats><beat-type>4</beat-type></time>" : "";
            string attributes = i == 0
                ? $"<attributes><divisions>2</divisions><key><fifths>1</fifths></key>{time}<staves>2</staves><clef number=\"1\"><sign>G</sign><line>2</line></clef><clef number=\"2\"><sign>F</sign><line>4</line></clef></attributes>"
                : "";
            xml.Append($"<measure number=\"{i + 1}\">{attributes}{bars[i]}</measure>");
        }

        return Encoding.UTF8.GetBytes(xml.Append("</part></score-partwise>").ToString());
    }

    private static ImportedPiece Import(byte[] score) => PieceImporter.Import("scan.musicxml", score).Piece!;

    private static string Text(byte[] score) => Encoding.UTF8.GetString(score);

    [Fact]
    public void A_split_moment_is_put_back_so_the_left_hand_sounds_with_the_right()
    {
        ImportedPiece piece = Import(ScanRepair.Repair(Score("4", Whole(), Split(), Whole(), Split())));

        Assert.All(piece.MusicXml!.Measures, measure => Assert.Equal(4 * 480, measure.Span.Duration));
        Note[] second = [.. piece.Notes.Where(note => note.Span.Onset.Value is >= 1920 and < 3840).OrderBy(note => note.Span.Onset.Value).ThenBy(note => note.Pitch.MidiNumber)];
        Assert.Equal([28, 40, 64], second.Where(note => note.Span.Onset.Value == 1920).Select(note => note.Pitch.MidiNumber));
        Assert.Equal(1920 + Eighth, second.First(note => note.Pitch.MidiNumber == 59).Span.Onset.Value);
        Assert.Equal(1920 + (4 * Eighth), second.First(note => note.Pitch.MidiNumber == 55).Span.Onset.Value);
        Assert.Equal(1920 + (7 * Eighth), second.Max(note => note.Span.Onset.Value));
    }

    [Fact]
    public void Grace_notes_stay_grace_notes_before_the_note_they_ornament()
    {
        byte[] read = Score("4", Whole(), Split(), Whole(), Split());
        ImportedPiece before = Import(read);
        ImportedPiece after = Import(ScanRepair.Repair(read));

        Assert.Equal(before.Notes.Count, after.Notes.Count);
        Assert.Equal([1920, 5760], after.MusicXml!.GraceNotes.Select(grace => grace.At.Value));
        Assert.All(after.MusicXml.GraceNotes, grace => Assert.Equal(62, grace.Pitch));
    }

    [Fact]
    public void The_time_signature_is_taken_from_the_bars_when_the_engine_s_does_not_fit_them()
    {
        byte[] repaired = ScanRepair.Repair(Score("5", Whole(), Split(), Whole(), Split(), Split()));

        ImportedPiece piece = Import(repaired);
        Assert.Equal((4, 4), (piece.MusicXml!.TimeSignatures[0].Numerator, piece.MusicXml.TimeSignatures[0].Denominator));
        Assert.Single(piece.MusicXml.TimeSignatures);
        Assert.Equal(5, piece.Meter.BarsBefore(piece.MusicXml.End).Count());
        Assert.Contains("3 bars re-timed", Text(repaired), StringComparison.Ordinal);
        Assert.Contains("time signature 4/4 where the engine wrote 5/4, inferred from the bar contents", Text(repaired), StringComparison.Ordinal);
    }

    [Fact]
    public void What_was_inferred_is_said_in_the_score_s_encoding_beside_the_engine()
    {
        XElement encoding = XDocument.Parse(Text(ScanRepair.Repair(Score("5", Whole(), Split(), Whole(), Split())))).Root!.Element("identification")!.Element("encoding")!;

        Assert.Equal("homr", (string?)encoding.Element("software"));
        Assert.StartsWith("Motiff Lab scan repair: ", (string?)encoding.Element("encoding-description"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_signature_over_too_few_bars_is_kept_as_written()
    {
        byte[] read = Score("5", Whole(), Whole());

        Assert.Same(read, ScanRepair.Repair(read));
        Assert.Equal(5, Import(read).MusicXml!.TimeSignatures[0].Numerator);
    }

    [Fact]
    public void A_signature_most_bars_do_not_agree_against_is_kept_as_written()
    {
        string sixEighths = Note("C", 5, 6, 1);
        byte[] read = Score("4", Whole(), Whole(), Whole(), sixEighths, sixEighths, sixEighths, sixEighths, Note("C", 5, 3, 1), Note("C", 5, 5, 1));

        Assert.Same(read, ScanRepair.Repair(read));
    }

    [Fact]
    public void Hands_that_take_turns_keep_their_reading()
    {
        byte[] read = Score("4", Turns(), Turns(), Turns(), Turns());

        Assert.Same(read, ScanRepair.Repair(read));
    }

    [Fact]
    public void A_rest_written_on_the_other_staff_keeps_the_moment_after_it_where_it_is()
    {
        // A quarter rest under the first eighth, so the left hand's G2 on the third eighth is where it was read (the right
        // hand's B3 still sounds through it); the half note after the fifth eighth was split from it and is put back.
        string bar = Note("E", 4, 1, 1) + Backup(1) + Rest(2, 2) + Backup(1)
            + Note("C", 4, 1, 1) + Backup(1) + Note("B", 3, 2, 1) + Backup(1)
            + Note("G", 2, 1, 2)
            + Note("D", 4, 1, 1) + Note("D", 4, 1, 1) + Note("G", 3, 4, 2)
            + Note("D", 4, 1, 1) + Note("D", 4, 1, 1) + Note("D", 4, 1, 1);

        ImportedPiece piece = Import(ScanRepair.Repair(Score("4", Whole(), bar, Whole(), Whole())));

        Assert.Equal(4 * 480, piece.MusicXml!.Measures[1].Span.Duration);
        Assert.Equal(1920 + (2 * Eighth), piece.Notes.Single(note => note.Pitch.MidiNumber == 43 && note.Span.Onset.Value >= 1920 && note.Span.Onset.Value < 3840).Span.Onset.Value);
        Assert.Equal(1920 + (4 * Eighth), piece.Notes.Single(note => note.Pitch.MidiNumber == 55 && note.Span.Onset.Value >= 1920 && note.Span.Onset.Value < 3840).Span.Onset.Value);
    }

    [Fact]
    public void A_repaired_score_is_not_repaired_again()
    {
        byte[] once = ScanRepair.Repair(Score("5", Whole(), Split(), Whole(), Split(), Split()));

        Assert.Same(once, ScanRepair.Repair(once));
    }

    [Fact]
    public void Pages_are_repaired_when_they_are_joined_and_a_page_without_a_signature_counts_with_the_one_before()
    {
        byte[] first = Score("5", Whole(), Split(), Split());
        byte[] second = Score(null, Split(), Whole(), Split());

        byte[] joined = ScanJoin.Join([first, second]);

        ImportedPiece piece = Import(joined);
        Assert.Equal(4, piece.MusicXml!.TimeSignatures[0].Numerator);
        Assert.All(piece.MusicXml.Measures, measure => Assert.Equal(4 * 480, measure.Span.Duration));
        Assert.Equal(2, ScanJoin.Split(joined).Count);
        Assert.Equal(joined, ScanJoin.Join(ScanJoin.Split(joined)));
        Assert.Equal(4, Import(ScanJoin.Join([Score("5", Whole(), Split(), Whole(), Split())])).MusicXml!.TimeSignatures[0].Numerator);
    }

    [Fact]
    public void Something_that_is_not_a_partwise_score_comes_back_as_it_is()
    {
        byte[] damaged = "<score"u8.ToArray();
        byte[] timewise = "<score-timewise/>"u8.ToArray();

        Assert.Same(damaged, ScanRepair.Repair(damaged));
        Assert.Same(timewise, ScanRepair.Repair(timewise));
    }

    [Fact]
    public void A_time_signature_that_cannot_be_read_is_left_as_written_and_the_bars_are_still_repaired()
    {
        byte[] repaired = ScanRepair.Repair(Score("x", Whole(), Split(), Whole(), Split(), Split()));

        Assert.Contains("<beats>x</beats>", Text(repaired), StringComparison.Ordinal);
        Assert.All(Import(repaired).MusicXml!.Measures, measure => Assert.Equal(4 * 480, measure.Span.Duration));
    }

    [Fact]
    public void Repairing_is_deterministic()
    {
        Assert.Equal(ScanRepair.Repair(Score("5", Whole(), Split(), Split())), ScanRepair.Repair(Score("5", Whole(), Split(), Split())));
    }
}