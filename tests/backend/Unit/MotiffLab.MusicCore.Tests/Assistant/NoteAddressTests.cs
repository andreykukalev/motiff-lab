using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Assistant.AssistantScores;

namespace MotiffLab.MusicCore.Tests.Assistant;

/// <summary>Note addresses (bar.hand.index), resolved both ways (the AI companion study, section 4.3).</summary>
public class NoteAddressTests
{
    private static string AddressAt(NoteAddressBook book, ImportedPiece piece, int midi, long onset) =>
        book.TextOf(piece.Notes.Single(note => note.Pitch.MidiNumber == midi && note.Span.Onset.Value == onset).Id)!;

    [Fact]
    public void Each_hand_counts_its_own_notes_in_each_bar_from_1()
    {
        ImportedPiece piece = Score(
            Note("E", 5, 2) + Note("D", 5, 2) + Note("C", 5, 2) + Backup(6) + Note("C", 3, 6, staff: 2),
            Note("G", 4, 6) + Backup(6) + Note("G", 2, 6, staff: 2));
        NoteAddressBook book = NoteAddressBook.Of(piece.Notes, piece.Meter);

        Assert.Equal("1.R.1", AddressAt(book, piece, 76, 0));
        Assert.Equal("1.R.3", AddressAt(book, piece, 72, 960));
        Assert.Equal("1.L.1", AddressAt(book, piece, 48, 0));
        Assert.Equal("2.R.1", AddressAt(book, piece, 67, 1440));
        Assert.Equal("2.L.1", AddressAt(book, piece, 43, 1440));
        Assert.Equal(piece.Notes.Count, book.Count);
    }

    [Fact]
    public void A_chord_is_counted_from_its_lowest_note_up()
    {
        ImportedPiece piece = Score(Note("C", 4, 2) + Note("E", 4, 2, extra: "<chord/>") + Note("G", 4, 2, extra: "<chord/>") + Note("A", 4, 4));
        NoteAddressBook book = NoteAddressBook.Of(piece.Notes, piece.Meter);

        Assert.Equal("1.R.1", AddressAt(book, piece, 60, 0));
        Assert.Equal("1.R.2", AddressAt(book, piece, 64, 0));
        Assert.Equal("1.R.3", AddressAt(book, piece, 67, 0));
        Assert.Equal("1.R.4", AddressAt(book, piece, 69, 480));
    }

    [Fact]
    public void A_note_tied_over_the_bar_line_belongs_to_the_bar_it_starts_in()
    {
        ImportedPiece piece = Score(
            Note("C", 5, 4) + Note("D", 5, 2, after: "<tie type=\"start\"/>"),
            Note("D", 5, 2, after: "<tie type=\"stop\"/>") + Note("E", 5, 4));
        NoteAddressBook book = NoteAddressBook.Of(piece.Notes, piece.Meter);

        Note tied = piece.Notes.Single(note => note.Pitch.MidiNumber == 74);
        Assert.Equal(960, tied.Span.Duration);
        Assert.Equal("1.R.2", book.TextOf(tied.Id));
        // The tie's second half is not a note of bar 2: E is its first.
        Assert.Equal("2.R.1", AddressAt(book, piece, 76, 1920));
        Assert.Null(book.NoteAt(new NoteAddress(2, Hand.Right, 0, 2)));
    }

    [Fact]
    public void Grace_notes_take_no_place()
    {
        ImportedPiece piece = Score(Note("F", 5, 0, extra: "<grace/>") + Note("E", 5, 2) + Note("D", 5, 2) + Note("C", 5, 2));
        NoteAddressBook book = NoteAddressBook.Of(piece.Notes, piece.Meter);

        Assert.Single(piece.MusicXml!.GraceNotes);
        Assert.Equal("1.R.1", AddressAt(book, piece, 76, 0));
        Assert.Equal(3, book.Count);
    }

    [Fact]
    public void Notes_without_hands_are_addressed_by_track()
    {
        Note[] notes =
        [
            new() { Id = "a", Pitch = new Pitch(60), Span = new TickSpan(new Tick(0), 480), Velocity = 80, Track = 0, Attribution = Attribution.Imported },
            new() { Id = "b", Pitch = new Pitch(64), Span = new TickSpan(new Tick(480), 480), Velocity = 80, Track = 0, Attribution = Attribution.Imported },
            new() { Id = "c", Pitch = new Pitch(48), Span = new TickSpan(new Tick(0), 960), Velocity = 80, Track = 1, Attribution = Attribution.Imported },
            new() { Id = "d", Pitch = new Pitch(67), Span = new TickSpan(new Tick(1920), 480), Velocity = 80, Track = 0, Hand = new Attributed<Hand>(Hand.Right, Attribution.Inferred(0.8)), Attribution = Attribution.Imported },
        ];
        NoteAddressBook book = NoteAddressBook.Of(notes, MeterMap.Default);

        Assert.Equal("1.T1.2", book.TextOf("b"));
        Assert.Equal("1.T2.1", book.TextOf("c"));
        Assert.Equal("2.R.1", book.TextOf("d"));
        Assert.Equal("c", book.Resolve("1.t2.1", Words.English, out _)!.Id);
    }

    [Fact]
    public void An_address_resolves_back_to_its_note_and_a_wrong_one_says_why()
    {
        ImportedPiece piece = FurElise();
        NoteAddressBook book = NoteAddressBook.Of(piece.Notes, piece.Meter);

        Assert.All(piece.Notes, note => Assert.Equal(note.Id, book.Resolve(book.TextOf(note.Id), Words.English, out _)!.Id));
        Assert.Null(book.Resolve("seven", Words.English, out string? notAnAddress));
        Assert.Contains("not a note address", notAnAddress, StringComparison.Ordinal);
        Assert.Null(book.Resolve("1.R.99", Words.Russian, out string? tooFar));
        Assert.Contains("ноты 1.R.99 нет", tooFar, StringComparison.Ordinal);
        Assert.Null(book.Resolve("999.L.1", Words.English, out string? noBar));
        Assert.Contains("no notes in L", noBar, StringComparison.Ordinal);
    }

    [Fact]
    public void Parsing_reads_only_the_three_part_form()
    {
        Assert.True(NoteAddress.TryParse("6.L.1", out NoteAddress left));
        Assert.Equal(new NoteAddress(6, Hand.Left, 0, 1), left);
        Assert.True(NoteAddress.TryParse("4.T2.3", out NoteAddress track));
        Assert.Equal("4.T2.3", track.ToString());
        Assert.False(NoteAddress.TryParse("6.L.0", out _));
        Assert.False(NoteAddress.TryParse("6.X.1", out _));
        Assert.False(NoteAddress.TryParse("6.L", out _));
        Assert.False(NoteAddress.TryParse("-1.L.1", out _));
        Assert.Matches(NoteAddress.Pattern, "12.R.3");
    }

    [Fact]
    public void A_note_reads_as_its_address_pitch_length_and_place_in_the_bar()
    {
        ImportedPiece piece = Score(Note("E", 5, 1) + Note("D", 5, 1) + Note("C", 5, 4));
        NoteAddressBook book = NoteAddressBook.Of(piece.Notes, piece.Meter);

        Assert.Equal("1.R.2 D5 1/8 at 1/8", book.Describe(piece.Notes.Single(note => note.Pitch.MidiNumber == 74)));
        Assert.Equal("3/16", NoteAddressBook.Fraction(360));
        Assert.Equal("1", NoteAddressBook.Fraction(1920));
    }
}