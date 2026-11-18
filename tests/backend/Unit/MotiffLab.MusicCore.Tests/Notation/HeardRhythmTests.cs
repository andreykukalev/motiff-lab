using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;

using static MotiffLab.MusicCore.Tests.Notation.NotationParts;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>How notes heard in a recording are written for reading (<see cref="HeardRhythm"/>).</summary>
public class HeardRhythmTests
{
    private static (ShownNote[] Written, HeardCounts Counts) AsHeard(params ShownNote[] notes)
    {
        var counts = new HeardCounts();
        return (HeardRhythm.AsHeard(notes, counts, [], HeardOnsets.Of(notes.Select(note => note.Note))), counts);
    }

    [Fact]
    public void Onsets_go_on_the_nearest_sixteenth_and_a_short_gap_is_held_to_the_next()
    {
        (ShownNote[] written, HeardCounts counts) = AsHeard(Shown("n_0001", 72, 10, 300), Shown("n_0002", 74, 475, 480));

        Assert.Equal([(0L, 480L), (480L, 960L)], written.Select(note => (note.Onset, note.End)));
        Assert.Equal((1, 0, 0), (counts.Held, counts.Cut, counts.Graces));
    }

    [Fact]
    public void A_gap_of_a_quarter_or_more_stays_a_rest()
    {
        (ShownNote[] written, HeardCounts counts) = AsHeard(Shown("n_0001", 72, 0, 240), Shown("n_0002", 74, 960, 480));

        Assert.Equal(240, written[0].End);
        Assert.Equal(0, counts.Held);
    }

    [Fact]
    public void A_note_that_sounds_into_the_next_onset_is_cut_at_it()
    {
        (ShownNote[] written, HeardCounts counts) = AsHeard(Shown("n_0001", 72, 0, 700), Shown("n_0002", 74, 480, 480));

        Assert.Equal(480, written[0].End);
        Assert.Equal(1, counts.Cut);
    }

    [Fact]
    public void Onsets_within_a_64th_are_one_chord_with_the_length_of_its_longest_note()
    {
        (ShownNote[] written, _) = AsHeard(Shown("n_0001", 60, 0, 400), Shown("n_0002", 64, 20, 300));

        Assert.All(written, note => Assert.Equal((0L, 360L), (note.Onset, note.End)));
    }

    [Fact]
    public void A_note_shorter_than_a_32nd_just_before_the_next_is_a_grace_note_in_front_of_it()
    {
        (ShownNote[] written, HeardCounts counts) = AsHeard(Shown("n_0001", 74, 440, 30), Shown("n_0002", 72, 480, 480));

        ShownNote grace = Assert.Single(written, note => note.IsGrace);
        Assert.Equal(("n_0001", "n_0002", 480L, 480L), (grace.Note.Id, grace.Target, grace.Onset, grace.End));
        Assert.Equal(1, counts.Graces);
    }

    [Fact]
    public void A_grace_note_is_attached_to_the_chord_that_holds_its_note()
    {
        ShownNote main = Shown("n_0002", 72, 480, 480);
        var staff = new StaffPlan(1, 0, Clef.Treble, Hand.Right, false, []) { Voices = [[new Chord(480, 960, [main])]] };

        HeardRhythm.AttachGraces(staff, [Shown("n_0001", 74, 450, 30) with { IsGrace = true, Target = "n_0002" }]);

        Chord grace = Assert.Single(staff.Voices[0][0].Graces);
        Assert.Equal(("n_0001", 480L, 480L), (grace.Notes[0].Note.Id, grace.Onset, grace.End));
    }
}