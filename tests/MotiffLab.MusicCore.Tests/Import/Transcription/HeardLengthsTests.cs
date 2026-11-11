using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.Transcription;

/// <summary>The notes heard in a recording whose length is uncertain, apart from pitch and onset doubt (#352).</summary>
public class HeardLengthsTests
{
    private static byte[] Melody() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Transcription", "Fixtures", "basic-pitch-melody.mid"));

    /// <summary>The heard melody, its notes in time order: the first held two bars, the third ending under the pedal.</summary>
    private static (ImportedPiece Piece, Note[] Notes) Heard()
    {
        ImportedPiece piece = Transcribed.Mark(PieceImporter.Import("take.mid", Melody()).Piece!);
        Note[] notes = [.. piece.Notes.OrderBy(note => note.Span.Onset)];
        Note third = notes[2];
        notes[0] = notes[0] with { Span = new TickSpan(notes[0].Span.Onset, 8 * CanonicalTime.TicksPerQuarter) };
        var pedal = new MidiPedal(0, 0, third.Span.Onset.Value, third.Span.End.Value + 10, TickSpan.FromBounds(third.Span.Onset, new Tick(third.Span.End.Value + 10)));
        piece = piece with { Notes = notes, Midi = piece.Midi! with { SustainPedal = [pedal] } };
        return (piece, notes);
    }

    private static int BarOf(ImportedPiece piece, Note note) => piece.Meter.BarAt(note.Span.Onset).Number;

    [Fact]
    public void A_long_note_and_a_note_ending_under_the_pedal_have_uncertain_lengths()
    {
        (ImportedPiece piece, Note[] notes) = Heard();

        IReadOnlyList<UncertainLengths> lengths = HeardLengths.Of(piece, notes);

        Assert.Contains(lengths, bar => bar.Bar == BarOf(piece, notes[0]) && bar.NoteIds.Contains(notes[0].Id) && bar.Reasons.Contains(LengthDoubt.Long));
        Assert.Contains(lengths, bar => bar.Bar == BarOf(piece, notes[2]) && bar.NoteIds.Contains(notes[2].Id) && bar.Reasons.Contains(LengthDoubt.Pedal));
        Assert.DoesNotContain(lengths, bar => bar.NoteIds.Contains(notes[1].Id) || bar.NoteIds.Contains(notes[6].Id));
        Assert.Equal(lengths.OrderBy(bar => bar.Bar).Select(bar => bar.Bar), lengths.Select(bar => bar.Bar));
    }

    [Fact]
    public void A_fixed_note_is_sure_and_so_is_a_piece_not_heard_in_a_recording()
    {
        (ImportedPiece piece, Note[] notes) = Heard();
        Note[] fixedNotes = [.. notes.Select(note => note.Id == notes[0].Id || note.Id == notes[2].Id ? note with { Attribution = Attribution.UserEdited } : note)];

        Assert.Empty(HeardLengths.Of(piece, fixedNotes));

        ImportedPiece imported = PieceImporter.Import("take.mid", Melody()).Piece!;
        Note[] longImported = [.. imported.Notes.Select(note => note with { Span = new TickSpan(note.Span.Onset, 8 * CanonicalTime.TicksPerQuarter) })];
        Assert.Empty(HeardLengths.Of(imported with { Notes = longImported }, longImported));
    }

    [Fact]
    public void The_lengths_are_deterministic_and_said_in_both_languages()
    {
        (ImportedPiece piece, Note[] notes) = Heard();

        Assert.Equal(HeardLengths.Of(piece, notes), HeardLengths.Of(piece, [.. notes.Reverse()]));
        foreach (LengthDoubt reason in Enum.GetValues<LengthDoubt>())
        {
            Assert.NotEqual(HeardLengths.NameOf(reason, Words.English), HeardLengths.NameOf(reason, Words.Russian));
            Assert.NotEqual(HeardLengths.Explain(reason, Words.English), HeardLengths.Explain(reason, Words.Russian));
        }
    }
}