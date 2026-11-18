using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Notation.NotationParts;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>The plan of the bars and parts written (<see cref="NotationPlan"/>, <see cref="WrittenBars"/>).</summary>
public class NotationPlanTests
{
    [Fact]
    public void The_key_in_force_is_c_major_until_the_first_change_then_the_last_change_at_or_before()
    {
        KeyChange[] keys = [new(new Tick(1920), 2), new(new Tick(3840), -3)];

        Assert.Equal([0, 2, 2, -3], new long[] { 0, 1920, 3839, 5000 }.Select(tick => NotationPlan.KeyAt(keys, new Tick(tick))));
    }

    [Fact]
    public void The_bars_written_are_those_of_the_range_with_their_numbers()
    {
        var written = WrittenBars.Of(MeterMap.Default, 8 * BarTicks, new BarRange(3, 5));

        Assert.Equal((8, 2, 4, 3), (written.Bars.Length, written.FirstIndex, written.LastIndex, written.Count));
        Assert.Equal((2 * BarTicks, 5 * BarTicks), (written.Start, written.End));
        Assert.Equal(new BarRange(3, 5), written.Range);
        Assert.True(written.Holds(4));
        Assert.False(written.Holds(5));
    }

    [Fact]
    public void A_range_outside_the_piece_names_the_bars_it_has()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => WrittenBars.Of(MeterMap.Default, 2 * BarTicks, new BarRange(5, 6)));

        Assert.Equal("bars", error.ParamName);
        Assert.StartsWith("The piece has bars 1 to 2.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_part_counts_the_notes_written_elsewhere_than_they_sound()
    {
        var written = WrittenBars.Of(MeterMap.Default, BarTicks, null);
        Note[] notes = [Note("n_0001", 72, 0, 480), Note("n_0002", 74, 490, 470)];

        PartPlan part = NotationPlan.PlanPart(notes, written, asHeard: false, playedTempos: null, pedal: null, firstStaff: 1, ossia: false);

        Assert.Equal(1, part.Approximated);
        Assert.Equal(1, part.FirstStaff);
        Assert.Null(part.Marks);
    }

    [Fact]
    public void An_ossia_keeps_only_the_staves_that_show_notes_in_the_bars_written()
    {
        var written = WrittenBars.Of(MeterMap.Default, 2 * BarTicks, new BarRange(2, 2));
        Note[] notes = [Note("n_0001", 72, BarTicks, 480, hand: Hand.Right), Note("n_0002", 48, 0, 480, hand: Hand.Left)];

        PartPlan ossia = NotationPlan.PlanPart(notes, written, asHeard: false, playedTempos: null, pedal: null, firstStaff: 1, ossia: true);
        PartPlan original = NotationPlan.PlanPart(notes, written, asHeard: false, playedTempos: null, pedal: null, firstStaff: 1, ossia: false);

        Assert.Equal([(1, Hand.Right)], ossia.Staves.Select(staff => (staff.Number, staff.Hand!.Value)));
        Assert.Equal(2, original.Staves.Count);
    }
}