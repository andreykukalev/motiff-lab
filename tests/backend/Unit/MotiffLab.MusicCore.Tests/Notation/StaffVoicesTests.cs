using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;

using static MotiffLab.MusicCore.Tests.Notation.NotationParts;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>The staves, clefs and voices of a part (<see cref="StaffVoices"/>).</summary>
public class StaffVoicesTests
{
    [Fact]
    public void A_single_track_over_both_clefs_is_split_at_middle_c_and_says_so()
    {
        List<StaffPlan> staves = StaffVoices.PlanStaves([Shown("n_0001", 72, 0, 480), Shown("n_0002", 60, 0, 480), Shown("n_0003", 48, 0, 480)], 3);

        Assert.Equal([(3, Clef.Treble, Hand.Right, 2), (4, Clef.Bass, Hand.Left, 1)], staves.Select(staff => (staff.Number, staff.Clef, staff.Hand!.Value, staff.Notes.Length)));
        Assert.All(staves, staff => Assert.True(staff.IsSplitInferred));
    }

    [Fact]
    public void Imported_hands_choose_the_staff_and_each_track_without_hands_has_its_own()
    {
        List<StaffPlan> byHand = StaffVoices.PlanStaves([Shown("n_0001", 50, 0, 480, hand: Hand.Right), Shown("n_0002", 70, 0, 480, hand: Hand.Left)], 1);
        List<StaffPlan> byTrack = StaffVoices.PlanStaves([Shown("n_0001", 72, 0, 480, track: 0), Shown("n_0002", 40, 0, 480, track: 1)], 1);

        Assert.Equal(["n_0001", "n_0002"], byHand.Select(staff => staff.Notes.Single().Note.Id));
        Assert.All(byHand, staff => Assert.False(staff.IsSplitInferred));
        Assert.Equal([(0, Clef.Treble), (1, Clef.Bass)], byTrack.Select(staff => (staff.Track, staff.Clef)));
        Assert.All(byTrack, staff => Assert.Null(staff.Hand));
    }

    [Fact]
    public void No_notes_is_one_empty_treble_staff()
    {
        StaffPlan staff = Assert.Single(StaffVoices.PlanStaves([], 2));

        Assert.Equal((2, Clef.Treble), (staff.Number, staff.Clef));
    }

    [Fact]
    public void The_clef_is_where_most_notes_are_and_the_usual_one_on_a_tie()
    {
        Assert.Equal(Clef.Bass, StaffVoices.ClefFor([Shown("n_0001", 55, 0, 480), Shown("n_0002", 59, 0, 480), Shown("n_0003", 64, 0, 480)], Clef.Treble));
        Assert.Equal(Clef.Bass, StaffVoices.ClefFor([Shown("n_0001", 55, 0, 480), Shown("n_0002", 64, 0, 480)], Clef.Bass));
        Assert.Equal(Clef.Treble, StaffVoices.ClefFor([], Clef.Treble));
    }

    [Fact]
    public void Overlapping_chords_get_voices_the_highest_first_and_a_fifth_shortens_the_one_that_ends_first()
    {
        var shortened = new HashSet<string>();
        ShownNote[] notes = [.. Enumerable.Range(0, 5).Select(i => Shown($"n_{i + 1:D4}", 60 + i, i * 60L, 1000 - (i * 60L)))];

        List<List<Chord>> voices = StaffVoices.AssignVoices(notes, shortened);

        Assert.Equal(NotationGenerator.MaxVoicesPerStaff, voices.Count);
        Assert.Equal(["n_0001"], shortened);
        Assert.Equal(63, voices[0][0].Notes[0].Note.Pitch.MidiNumber);
        Assert.Equal(240, voices.Single(voice => voice[0].Notes[0].Note.Id == "n_0001")[0].End);
    }

    [Fact]
    public void Notes_that_start_and_end_together_are_one_chord_lowest_first()
    {
        List<List<Chord>> voices = StaffVoices.AssignVoices([Shown("n_0002", 67, 0, 480), Shown("n_0001", 60, 0, 480)], []);

        Assert.Equal(["n_0001", "n_0002"], Assert.Single(Assert.Single(voices)).Notes.Select(note => note.Note.Id));
    }
}