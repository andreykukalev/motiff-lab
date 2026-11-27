using System.Text;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Performance;

/// <summary>
/// The player's corrections of an earlier reading carried over to a new reading of the same pages («Как напечатано», step
/// 2; concept 3.3): carried where the new reading makes the same mistake, left where it reads the note as corrected, kept on
/// the earlier reading otherwise; removed and added notes, the bars confirmed, the group, and the line of the toast.
/// </summary>
public class ReadingCarryTests
{
    /// <summary>A reading in 4/4 of quarter notes, bars split by "|", a note "C4" or "-" for a quarter rest.</summary>
    internal static ImportedPiece Reading(string bars) => PieceImporter.Import("read.musicxml", ReadingXml(bars)).Piece!;

    /// <summary>The MusicXML of a <see cref="Reading"/>.</summary>
    internal static byte[] ReadingXml(string bars)
    {
        StringBuilder xml = new("""<?xml version="1.0"?><score-partwise version="3.1"><part-list><score-part id="P1"><part-name>P</part-name></score-part></part-list><part id="P1">""");
        int number = 1;
        foreach (string bar in bars.Split('|', StringSplitOptions.TrimEntries))
        {
            xml.Append(System.Globalization.CultureInfo.InvariantCulture, $"<measure number=\"{number}\">");
            if (number++ == 1)
            {
                xml.Append("<attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>");
            }

            foreach (string note in bar.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                xml.Append(note == "-"
                    ? "<note><rest/><duration>1</duration><type>quarter</type></note>"
                    : $"<note><pitch><step>{note[0]}</step>{(note.Length == 3 ? "<alter>1</alter>" : "")}<octave>{note[^1]}</octave></pitch><duration>1</duration><type>quarter</type></note>");
            }

            xml.Append("</measure>");
        }

        xml.Append("</part></score-partwise>");
        return Encoding.UTF8.GetBytes(xml.ToString());
    }

    private static Note At(ImportedPiece piece, int quarter) => piece.Notes.Single(note => note.Span.Onset.Value == quarter * CanonicalTime.TicksPerQuarter);

    private static NoteEditLog Raised(ImportedPiece piece, int quarter)
    {
        Note note = At(piece, quarter);
        return NoteEditLog.Empty.Change(piece.Notes, note.Id, NoteValues.Of(note) with { Pitch = new Pitch(note.Pitch.MidiNumber + 1) });
    }

    private static ReadingCarry Carry(ImportedPiece before, NoteEditLog edits, ImportedPiece after, params int[] confirmed) =>
        ReadingCarry.Of(before, edits, confirmed, after, "g_1");

    private static int[] Pitches(ImportedPiece piece, ReadingCarry carry) => [.. carry.Edits.Apply(piece.Notes).Select(static note => note.Pitch.MidiNumber)];

    [Fact]
    public void A_correction_the_new_reading_needs_too_is_carried_in_one_group_and_applies()
    {
        ImportedPiece before = Reading("C4 D4 E4 F4 | G4 A4 B4 C5"), after = Reading("C4 D4 E4 F4 | G4 A4 B4 C5");
        NoteEditLog edits = Raised(before, 1);

        ReadingCarry carry = Carry(before, edits, after);

        CarriedCorrection only = Assert.Single(carry.Corrections);
        Assert.Equal((CarryOutcome.Carried, 1, 63), (only.Outcome, only.Bar, only.Corrected!.Value.Pitch.MidiNumber));
        Assert.Equal("g_1", Assert.Single(carry.Edits.Edits).Group);
        Assert.Equal([60, 63, 64, 65, 67, 69, 71, 72], Pitches(after, carry));
    }

    [Fact]
    public void A_correction_the_new_reading_already_makes_is_not_carried()
    {
        ImportedPiece before = Reading("C4 D4 E4 F4"), after = Reading("C4 D#4 E4 F4");

        ReadingCarry carry = Carry(before, Raised(before, 1), after);

        Assert.Equal(CarryOutcome.ByReading, Assert.Single(carry.Corrections).Outcome);
        Assert.True(carry.Edits.IsEmpty);
    }

    [Fact]
    public void A_correction_the_new_reading_reads_otherwise_stays_on_the_earlier_one_with_both_values()
    {
        ImportedPiece before = Reading("C4 D4 E4 F4 | G4 A4 B4 C5"), after = Reading("C4 D4 E4 F4 | G4 F4 B4 C5");

        ReadingCarry carry = Carry(before, Raised(before, 5), after);

        CarriedCorrection only = Assert.Single(carry.Corrections);
        Assert.Equal((CarryOutcome.NotCarried, 2, 70, 65), (only.Outcome, only.Bar, only.Corrected!.Value.Pitch.MidiNumber, only.Read!.Value.Pitch.MidiNumber));
        Assert.True(carry.Edits.IsEmpty);
        Assert.Equal([2], carry.NotCarriedBars);
    }

    [Fact]
    public void A_correction_on_a_bar_the_new_reading_lost_is_not_carried_and_the_rest_is()
    {
        ImportedPiece before = Reading("C4 D4 E4 F4 | G4 A4 B4 C5"), after = Reading("C4 D4 E4 F4");
        NoteEditLog edits = Raised(before, 1);
        edits = edits.Change(before.Notes, At(before, 6).Id, NoteValues.Of(At(before, 6)) with { Pitch = new Pitch(72) });

        ReadingCarry carry = Carry(before, edits, after, 2);

        Assert.Equal([CarryOutcome.Carried, CarryOutcome.NotCarried], carry.Corrections.Select(static correction => correction.Outcome));
        Assert.Null(carry.Corrections[1].Read);
        Assert.Equal([2], carry.NotCarriedBars);
        Assert.Empty(carry.ConfirmedBars);
        Assert.Equal([60, 63, 64, 65], Pitches(after, carry));
    }

    [Fact]
    public void A_bar_the_new_reading_has_more_moves_the_correction_with_its_music()
    {
        ImportedPiece before = Reading("E4 F4 G4 A4 | B4 C5 D5 E5"), after = Reading("C4 C4 C4 C4 | E4 F4 G4 A4 | B4 C5 D5 E5");

        ReadingCarry carry = Carry(before, Raised(before, 5), after);

        Assert.Equal(CarryOutcome.Carried, Assert.Single(carry.Corrections).Outcome);
        Note carried = carry.Edits.Apply(after.Notes).Single(note => note.Attribution == Attribution.UserEdited);
        Assert.Equal((73, 9L * CanonicalTime.TicksPerQuarter), (carried.Pitch.MidiNumber, carried.Span.Onset.Value));
    }

    [Fact]
    public void A_correction_not_carried_is_told_by_its_bar_in_the_new_reading()
    {
        ImportedPiece before = Reading("E4 F4 G4 A4 | B4 C5 D5 E5"), after = Reading("C4 C4 C4 C4 | E4 F4 G4 A4 | B4 D4 D5 E5");

        ReadingCarry carry = Carry(before, Raised(before, 5), after);

        Assert.Equal((CarryOutcome.NotCarried, 3), (Assert.Single(carry.Corrections).Outcome, carry.Corrections[0].Bar));
        Assert.Equal([3], carry.NotCarriedBars);
        Assert.Equal("not carried, in Check: 1 (bar 3)", carry.Text(Words.English));
    }

    [Fact]
    public void A_confirmed_bar_follows_its_music_past_a_bar_the_new_reading_has_more()
    {
        ImportedPiece before = Reading("E4 F4 G4 A4 | B4 C5 D5 E5"), after = Reading("C4 C4 C4 C4 | E4 F4 G4 A4 | B4 C5 D5 E5");

        Assert.Equal([2, 3], Carry(before, NoteEditLog.Empty, after, 1, 2).ConfirmedBars);
        Assert.Equal([2], Carry(before, NoteEditLog.Empty, Reading("C4 C4 C4 C4 | E4 F4 G4 A4 | B4 C5 D5 F5"), 1, 2).ConfirmedBars);
    }

    [Fact]
    public void A_removed_note_is_removed_again_where_it_is_read_again_and_left_where_it_is_not()
    {
        ImportedPiece before = Reading("C4 D4 E4 F4");
        NoteEditLog removed = NoteEditLog.Empty.Remove(before.Notes, At(before, 2).Id);

        ReadingCarry again = Carry(before, removed, Reading("C4 D4 E4 F4"));
        ReadingCarry gone = Carry(before, removed, Reading("C4 D4 - F4"));

        Assert.Equal(CarryOutcome.Carried, Assert.Single(again.Corrections).Outcome);
        Assert.Equal([60, 62, 65], Pitches(Reading("C4 D4 E4 F4"), again));
        Assert.Equal(CarryOutcome.ByReading, Assert.Single(gone.Corrections).Outcome);
        Assert.True(gone.Edits.IsEmpty);
    }

    [Fact]
    public void An_added_note_is_added_unless_the_new_reading_has_it_at_its_place()
    {
        ImportedPiece before = Reading("C4 D4 - F4");
        NoteEditLog added = NoteEditLog.Empty.Add(before.Notes, new NoteValues(new Pitch(64), new TickSpan(new Tick(2 * CanonicalTime.TicksPerQuarter), CanonicalTime.TicksPerQuarter), 80), 0, null, out _);
        ImportedPiece missing = Reading("C4 D4 - F4");

        ReadingCarry carry = Carry(before, added, missing);
        ReadingCarry read = Carry(before, added, Reading("C4 D4 E4 F4"));

        Assert.Equal(CarryOutcome.Carried, Assert.Single(carry.Corrections).Outcome);
        Assert.Equal([60, 62, 64, 65], Pitches(missing, carry));
        Assert.DoesNotContain(missing.Notes, note => note.Id == Assert.Single(carry.Edits.Edits).NoteId);
        Assert.Equal(CarryOutcome.ByReading, Assert.Single(read.Corrections).Outcome);
    }

    [Fact]
    public void A_confirmed_bar_is_confirmed_again_only_where_its_notes_are_the_same()
    {
        ImportedPiece before = Reading("C4 D4 E4 F4 | G4 A4 B4 C5"), after = Reading("C4 D4 E4 F4 | G4 A4 B4 D5");

        ReadingCarry carry = Carry(before, NoteEditLog.Empty, after, 1, 2, 9);

        Assert.Equal([1], carry.ConfirmedBars);
        Assert.Empty(carry.Corrections);
    }

    [Fact]
    public void A_correction_that_fit_no_note_of_the_earlier_reading_is_not_carried_and_said_so()
    {
        ImportedPiece before = Reading("C4 D4 E4 F4");
        NoteEditLog unfit = new([new NoteEdit("n_9", new NoteValues(new Pitch(50), new TickSpan(new Tick(0), 480), 80), null)]);

        ReadingCarry carry = Carry(before, unfit, Reading("C4 D4 E4 F4"));

        Assert.Equal((CarryOutcome.NotCarried, 1), (Assert.Single(carry.Corrections).Outcome, carry.Corrections[0].Bar));
    }

    [Fact]
    public void The_toast_says_what_was_carried_in_both_languages_and_nothing_when_nothing_was()
    {
        ImportedPiece before = Reading("C4 D4 E4 F4 | G4 A4 B4 C5"), after = Reading("C4 D4 E4 F4 | G4 F4 B4 C5");
        NoteEditLog edits = Raised(before, 1);
        edits = edits.Change(before.Notes, At(before, 5).Id, NoteValues.Of(At(before, 5)) with { Pitch = new Pitch(70) });

        ReadingCarry carry = Carry(before, edits, after, 1);

        Assert.Equal("1 correction carried over · not carried, in Check: 1 (bar 2) · «Looks right» carried over: 1 bar", carry.Text(Words.English));
        Assert.Equal("перенесено: 1 исправление · не перенесено, в «Проверить»: 1 (такт 2) · «Верно» перенесено: 1 такт", carry.Text(Words.Russian));
        Assert.Null(Carry(before, NoteEditLog.Empty, after).Text(Words.English));
    }

    [Fact]
    public void The_same_readings_and_corrections_give_the_same_carry()
    {
        ImportedPiece before = Reading("C4 D4 E4 F4 | G4 A4 B4 C5"), after = Reading("C4 C4 C4 C4 | C4 D4 E4 F4 | G4 F4 B4 C5");
        NoteEditLog edits = Raised(before, 1).Remove(before.Notes, At(before, 6).Id);

        ReadingCarry first = Carry(before, edits, after, 1), second = Carry(before, edits, after, 1);

        Assert.Equal(first.Edits, second.Edits);
        Assert.Equal(first.Corrections, second.Corrections);
        Assert.Equal(first.ConfirmedBars, second.ConfirmedBars);
    }
}