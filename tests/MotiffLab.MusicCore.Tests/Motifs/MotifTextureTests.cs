using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Motifs;

/// <summary>The one reading of melody, bass and sonorities that analysis, Keep checks and operations share.</summary>
public class MotifTextureTests
{
    private const long Quarter = 480;
    private const long Eighth = 240;

    private static Note Note(string id, int pitch, long onset, long duration, Hand? hand = null) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Hand = hand is { } value ? new Attributed<Hand>(value, Attribution.Imported) : null,
        Attribution = Attribution.Imported,
    };

    private static string[] Ids(IEnumerable<Note> notes) => [.. notes.Select(note => note.Id)];

    [Fact]
    public void A_single_line_is_all_melody_with_no_separate_bass()
    {
        Note[] notes = [Note("a", 64, 0, Quarter), Note("b", 62, Quarter, Quarter), Note("c", 60, 2 * Quarter, Quarter)];

        MotifTexture texture = MotifTexture.Read(notes);

        Assert.True(texture.IsSingleLine);
        Assert.Equal(Attribution.Imported, texture.Attribution);
        Assert.Equal(["a", "b", "c"], Ids(texture.Melody));
        Assert.Empty(texture.Bass);
        Assert.All(texture.Sonorities, sonority => Assert.Single(sonority.Sounding));
    }

    [Fact]
    public void Chords_give_a_top_line_and_a_bottom_line_and_both_are_inferred()
    {
        // C major then G major, each with the melody on top: E5 over C3 E4 G4, D5 over G2 B3 D4.
        Note[] notes =
        [
            Note("c3", 48, 0, Quarter), Note("e4", 64, 0, Quarter), Note("g4", 67, 0, Quarter), Note("e5", 76, 0, Quarter),
            Note("g2", 43, Quarter, Quarter), Note("b3", 59, Quarter, Quarter), Note("d4", 62, Quarter, Quarter), Note("d5", 74, Quarter, Quarter),
        ];

        MotifTexture texture = MotifTexture.Read(notes);

        Assert.False(texture.IsSingleLine);
        Assert.Equal(Attribution.Inferred(MotifTexture.Confidence), texture.Attribution);
        Assert.Equal(["e5", "d5"], Ids(texture.Melody));
        Assert.Equal(["c3", "g2"], Ids(texture.Bass));
        Assert.Equal([VoiceRole.Inner], texture.RolesOf(notes[1]));
        Assert.Equal([0, 4, 7], texture.Sonorities[0].PitchClasses);
    }

    [Fact]
    public void Accompaniment_under_a_held_melody_note_is_not_melody()
    {
        // Für Elise, bar 2: the melody holds A4 while the left hand plays A2 E3 A3; then the melody goes on with C4.
        Note[] notes =
        [
            Note("a4", 69, 0, 3 * Eighth), Note("a2", 45, 0, Eighth), Note("e3", 52, Eighth, Eighth), Note("a3", 57, 2 * Eighth, Eighth),
            Note("c4", 60, 3 * Eighth, Eighth),
        ];

        MotifTexture texture = MotifTexture.Read(notes);

        Assert.Equal(["a4", "c4"], Ids(texture.Melody));

        // C4 is struck alone once everything else has stopped, so it is the bottom line too.
        Assert.Equal(["a2", "e3", "a3", "c4"], Ids(texture.Bass));
        Assert.Equal(["a4"], Ids(texture.Sonorities[1].Held));
        Assert.Equal(["e3"], Ids(texture.Sonorities[1].Struck));
    }

    [Fact]
    public void Hands_decide_the_lines_when_they_are_known()
    {
        // The left hand crosses above the right: the melody stays in the right hand.
        Note[] notes =
        [
            Note("rh1", 67, 0, Quarter, Hand.Right), Note("lh1", 72, 0, Quarter, Hand.Left),
            Note("rh2", 69, Quarter, Quarter, Hand.Right), Note("lh2", 48, Quarter, Quarter, Hand.Left),
        ];

        MotifTexture texture = MotifTexture.Read(notes);

        Assert.Equal(["rh1", "rh2"], Ids(texture.Melody));
        Assert.Equal(["lh1", "lh2"], Ids(texture.Bass));
    }

    [Fact]
    public void A_note_struck_alone_with_nothing_held_is_both_melody_and_bass()
    {
        Note[] notes = [Note("chord-low", 48, 0, Quarter), Note("chord-top", 64, 0, Quarter), Note("alone", 60, Quarter, Quarter)];

        MotifTexture texture = MotifTexture.Read(notes);

        Assert.Equal([VoiceRole.Melody, VoiceRole.Bass], texture.RolesOf(notes[2]).Order());
        Assert.Empty(texture.SoundingWith(notes[2]));
        Assert.Equal(["chord-low"], Ids(texture.SoundingWith(notes[1])));
    }

    [Fact]
    public void A_note_outside_the_motif_has_no_role()
    {
        MotifTexture texture = MotifTexture.Read([Note("a", 60, 0, Quarter)]);

        Assert.Empty(texture.RolesOf(Note("elsewhere", 62, 0, Quarter)));
    }

    [Fact]
    public void No_notes_give_an_empty_texture()
    {
        MotifTexture texture = MotifTexture.Read([]);

        Assert.Empty(texture.Melody);
        Assert.Empty(texture.Bass);
        Assert.Empty(texture.Sonorities);
    }

    [Fact]
    public void The_same_notes_in_any_order_give_the_same_texture()
    {
        Note[] notes =
        [
            Note("a4", 69, 0, 3 * Eighth), Note("a2", 45, 0, Eighth), Note("e3", 52, Eighth, Eighth), Note("a3", 57, 2 * Eighth, Eighth),
            Note("c4", 60, 3 * Eighth, Eighth),
        ];

        MotifTexture first = MotifTexture.Read(notes);
        MotifTexture second = MotifTexture.Read(notes.Reverse());

        Assert.Equal(Ids(first.Notes), Ids(second.Notes));
        Assert.Equal(Ids(first.Melody), Ids(second.Melody));
        Assert.Equal(Ids(first.Bass), Ids(second.Bass));
        Assert.Equal(first.Sonorities.Select(sonority => Ids(sonority.Sounding)), second.Sonorities.Select(sonority => Ids(sonority.Sounding)));
    }
}