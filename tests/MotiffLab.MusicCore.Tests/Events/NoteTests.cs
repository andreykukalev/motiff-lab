using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Events;

public class NoteTests
{
    private static Note ImportedMidiNote() => new()
    {
        Id = "n_0057",
        Pitch = new Pitch(67),
        Span = new TickSpan(new Tick(7680), 240),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
        Source = new MidiSourceLocation(Track: 1, Channel: 0, NoteOnIndex: 42),
    };

    [Fact]
    public void Imported_note_holds_source_facts()
    {
        Note note = ImportedMidiNote();

        Assert.Equal(new Pitch(67), note.Pitch);
        Assert.Equal(new Tick(7680), note.Span.Onset);
        Assert.Equal(Provenance.Imported, note.Attribution.Provenance);
        Assert.Null(note.Voice);
        Assert.Null(note.Hand);
        Assert.IsType<MidiSourceLocation>(note.Source);
    }

    [Fact]
    public void Inferred_hand_keeps_its_own_attribution()
    {
        Note note = ImportedMidiNote() with { Hand = new(Hand.Right, Attribution.Inferred(0.85)) };

        Assert.Equal(1, note.Attribution.Confidence);
        Assert.Equal(Provenance.Inferred, note.Hand!.Attribution.Provenance);
        Assert.Equal(0.85, note.Hand.Attribution.Confidence);
    }

    [Fact]
    public void Player_reassigning_a_hand_replaces_only_the_hand()
    {
        Note inferred = ImportedMidiNote() with { Hand = new(Hand.Right, Attribution.Inferred(0.6)) };

        Note corrected = inferred with { Hand = new(Hand.Left, Attribution.UserEdited) };

        Assert.Equal(Hand.Left, corrected.Hand!.Value);
        Assert.Equal(Provenance.UserEdited, corrected.Hand.Attribution.Provenance);
        Assert.Equal(inferred with { Hand = null }, corrected with { Hand = null });
    }

    [Fact]
    public void Notes_with_the_same_fields_are_equal()
    {
        Assert.Equal(ImportedMidiNote(), ImportedMidiNote());
        Assert.NotEqual(ImportedMidiNote(), ImportedMidiNote() with { Velocity = 81 });
    }

    [Fact]
    public void MusicXml_note_has_a_notated_voice_and_imported_hand()
    {
        Note note = ImportedMidiNote() with
        {
            Voice = 2,
            Hand = new(Hand.Left, Attribution.Imported),
            Source = new MusicXmlSourceLocation("P1", MeasureIndex: 4, NoteIndex: 2, Staff: 2, Voice: "2"),
        };

        Assert.Equal(2, note.Voice);
        Assert.Equal(Provenance.Imported, note.Hand!.Attribution.Provenance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    public void Velocity_must_be_a_note_on_velocity(int velocity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImportedMidiNote() with { Velocity = velocity });
    }

    [Fact]
    public void Invalid_fields_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ImportedMidiNote() with { Id = " " });
        Assert.Throws<ArgumentOutOfRangeException>(() => ImportedMidiNote() with { Track = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => ImportedMidiNote() with { Voice = 0 });
        Assert.Throws<ArgumentNullException>(() => ImportedMidiNote() with { Attribution = null! });
    }

    [Fact]
    public void Player_added_note_has_no_source()
    {
        Note added = new()
        {
            Id = "n_user_1",
            Pitch = new Pitch(72),
            Span = new TickSpan(Tick.Zero, 480),
            Velocity = 64,
            Track = 0,
            Attribution = Attribution.UserEdited,
        };

        Assert.Null(added.Source);
    }

    [Fact]
    public void Source_locations_validate_their_fields()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MidiSourceLocation(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MidiSourceLocation(0, 16, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MidiSourceLocation(0, 0, -1));
        Assert.Throws<ArgumentException>(() => new MusicXmlSourceLocation("", 0, 0, 1, "1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MusicXmlSourceLocation("P1", -1, 0, 1, "1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MusicXmlSourceLocation("P1", 0, 0, 0, "1"));
    }
}