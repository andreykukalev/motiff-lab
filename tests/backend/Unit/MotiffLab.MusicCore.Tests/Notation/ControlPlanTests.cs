using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Notation.NotationParts;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>The marks of the playing a part writes bar by bar (<see cref="ControlPlan"/>).</summary>
public class ControlPlanTests
{
    /// <summary>A staff of one voice with a chord for each note given.</summary>
    private static StaffPlan Staff(params ShownNote[] notes) =>
        new(1, 0, Clef.Treble, null, false, notes) { Voices = [[.. notes.Select(note => new Chord(note.Onset, note.End, [note]))]] };

    [Fact]
    public void The_bar_index_of_a_tick_is_the_bar_that_holds_it_and_the_last_after_the_end()
    {
        Bar[] bars = Bars(3);

        Assert.Equal([0, 0, 1, 2, 2], new long[] { 0, 1919, 1920, 3840, 99_999 }.Select(tick => ControlPlan.BarIndex(bars, tick)));
    }

    [Fact]
    public void Three_melody_notes_held_into_the_next_are_slurred_two_are_not()
    {
        StaffPlan three = Staff(Shown("n_0001", 72, 0, 480), Shown("n_0002", 74, 480, 470), Shown("n_0003", 76, 960, 480));
        StaffPlan two = Staff(Shown("n_0001", 72, 0, 480), Shown("n_0002", 74, 480, 480), Shown("n_0003", 76, 1200, 240));

        (Chord from, Chord to, int staff) = Assert.Single(ControlPlan.Slurs([three], Bars(2)));
        Assert.Equal(("n_0001", "n_0003", 1), (from.Notes[0].Note.Id, to.Notes[0].Note.Id, staff));
        Assert.Empty(ControlPlan.Slurs([two], Bars(2)));
    }

    [Fact]
    public void A_slur_covers_at_most_two_bars()
    {
        StaffPlan staff = Staff([.. Enumerable.Range(0, 12).Select(i => Shown($"n_{i + 1:D4}", 72, i * 480L, 480))]);

        Assert.Equal([(0L, 3360L), (3840L, 5280L)], ControlPlan.Slurs([staff], Bars(3)).Select(slur => (slur.From.Onset, slur.To.Onset)));
    }

    [Fact]
    public void Without_the_marks_of_the_playing_no_bar_has_a_control_event()
    {
        var written = new WrittenBars(Bars(2), 0, 1);

        (ControlEvents[] controls, int slurs, int pedals) = ControlPlan.Of([Staff(Shown("n_0001", 72, 0, 480))], written, null, [new TickSpan(Tick.Zero, 960)], significant: false);

        Assert.Equal(2, controls.Length);
        Assert.All(controls, bar => Assert.Empty(bar.Pedals));
        Assert.Equal((0, 0), (slurs, pedals));
    }

    [Fact]
    public void The_pedal_is_written_under_the_bottom_staff_and_a_press_shorter_than_a_32nd_is_not()
    {
        var written = new WrittenBars(Bars(2), 0, 1);
        Note[] notes = [Note("n_0001", 72, 0, 480)];
        PlayingMarks marks = PlayingMarks.Of(notes, written.Bars, [new PlaybackTempo(Tick.Zero, 500_000)]);
        StaffPlan[] staves = [Staff(Shown("n_0001", 72, 0, 480)), Staff() with { Number = 2 }];

        (ControlEvents[] controls, _, int pedals) = ControlPlan.Of([.. staves], written, marks, [TickSpan.FromBounds(Tick.Zero, new Tick(2400)), new TickSpan(new Tick(3000), 20)], significant: false);

        Assert.Equal(1, pedals);
        Assert.Equal([(0L, true, 2)], controls[0].Pedals);
        Assert.Equal([(2400L, false, 2)], controls[1].Pedals);
    }
}