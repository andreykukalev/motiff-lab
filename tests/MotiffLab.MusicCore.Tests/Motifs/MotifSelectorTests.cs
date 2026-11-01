using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Motifs;

public class MotifSelectorTests
{
    private const long Beat = 480;
    private const long Bar = 4 * Beat;

    // 3/4 with a one-beat pickup: bar 0 is [0, 480), bar 1 starts at 480.
    private static readonly MeterMap s_pickup = new([new MeterChange(Tick.Zero, new TimeSignature(3, 4))], pickupDuration: Beat);

    private static Note Note(string id, int pitch, long onset, long duration = Beat, int track = 0, Hand? hand = null, bool inferred = false) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = track,
        Hand = hand is { } value ? new Attributed<Hand>(value, inferred ? Attribution.Inferred(0.8) : Attribution.Imported) : null,
        Attribution = Attribution.Imported,
    };

    private static TickSpan Span(long from, long to) => TickSpan.FromBounds(new Tick(from), new Tick(to));

    [Fact]
    public void Bars_run_from_the_start_of_the_first_to_the_end_of_the_last_in_either_order()
    {
        Tick end = new(10 * Bar);

        Assert.Equal(Span(4 * Bar, 6 * Bar), MotifSelector.Bars(MeterMap.Default, 5, 6, end));
        Assert.Equal(Span(4 * Bar, 6 * Bar), MotifSelector.Bars(MeterMap.Default, 6, 5, end));
        Assert.Equal(Span(0, Bar), MotifSelector.Bars(MeterMap.Default, 1, 1, end));
    }

    [Fact]
    public void Bars_allow_one_to_eight_bars_inside_the_piece()
    {
        Tick end = new(20 * Bar);

        Assert.Equal(Span(0, 8 * Bar), MotifSelector.Bars(MeterMap.Default, 1, 8, end));
        Assert.Throws<ArgumentOutOfRangeException>(() => MotifSelector.Bars(MeterMap.Default, 1, 9, end));
        Assert.Throws<ArgumentOutOfRangeException>(() => MotifSelector.Bars(MeterMap.Default, 0, 2, end));
        Assert.Throws<ArgumentOutOfRangeException>(() => MotifSelector.Bars(MeterMap.Default, 20, 21, end));
    }

    [Fact]
    public void Bars_include_the_pickup()
    {
        Assert.Equal(Span(0, Beat + (3 * Beat)), MotifSelector.Bars(s_pickup, 0, 1, new Tick(9 * Beat)));
    }

    [Fact]
    public void Drag_edges_snap_out_to_beats()
    {
        Tick end = new(4 * Bar);

        // From the middle of beat 2 of bar 1 to the middle of beat 1 of bar 2.
        Assert.Equal(Span(Beat, Bar + Beat), MotifSelector.Drag(MeterMap.Default, new Tick(Beat + 100), new Tick(Bar + 200), end));

        // Dragged backwards, the same range.
        Assert.Equal(Span(Beat, Bar + Beat), MotifSelector.Drag(MeterMap.Default, new Tick(Bar + 200), new Tick(Beat + 100), end));

        // Ending exactly on a beat line keeps that line.
        Assert.Equal(Span(0, 2 * Beat), MotifSelector.Drag(MeterMap.Default, new Tick(10), new Tick(2 * Beat), end));
    }

    [Fact]
    public void Drag_inside_one_beat_selects_that_beat()
    {
        Tick end = new(4 * Bar);

        Assert.Equal(Span(2 * Beat, 3 * Beat), MotifSelector.Drag(MeterMap.Default, new Tick((2 * Beat) + 10), new Tick((2 * Beat) + 50), end));
        Assert.Equal(Span(2 * Beat, 3 * Beat), MotifSelector.Drag(MeterMap.Default, new Tick(2 * Beat), new Tick(2 * Beat), end));
    }

    [Fact]
    public void Drag_snaps_to_beats_in_a_pickup()
    {
        // A one-beat pickup in 3/4 is beat 3; dragging inside it selects all of it.
        Assert.Equal(Span(0, Beat), MotifSelector.Drag(s_pickup, new Tick(100), new Tick(200), new Tick(9 * Beat)));
    }

    [Fact]
    public void Drag_over_eight_bars_stops_eight_bars_from_where_it_started()
    {
        Tick end = new(20 * Bar);

        Assert.Equal(Span(Beat, 8 * Bar), MotifSelector.Drag(MeterMap.Default, new Tick(Beat), new Tick(12 * Bar), end));
        Assert.Equal(Span(3 * Bar, 11 * Bar), MotifSelector.Drag(MeterMap.Default, new Tick(11 * Bar), new Tick(100), end));
    }

    [Fact]
    public void Drag_stays_inside_the_piece()
    {
        Assert.Equal(Span(3 * Bar, 4 * Bar), MotifSelector.Drag(MeterMap.Default, new Tick(3 * Bar), new Tick(50 * Bar), new Tick(4 * Bar)));
    }

    [Fact]
    public void Select_keeps_notes_that_start_inside_the_range_in_time_order()
    {
        Note[] notes =
        [
            Note("held", 60, 0, 3 * Beat),
            Note("b", 67, Bar),
            Note("a", 64, Bar),
            Note("c", 72, Bar + Beat, 4 * Beat),
            Note("after", 60, 2 * Bar),
        ];

        MotifSelection selection = MotifSelector.Select(notes, MeterMap.Default, Span(Bar, 2 * Bar), SelectionFilter.All);

        Assert.Equal(["a", "b", "c"], selection.NoteIds);
        Assert.Equal(2, selection.FirstBar);
        Assert.Equal(2, selection.LastBar);
        Assert.Equal(1, selection.BarCount);
        Assert.False(selection.HandInferred);
    }

    [Fact]
    public void Select_limits_to_one_hand_or_one_track()
    {
        Note[] notes =
        [
            Note("r", 72, 0, track: 0, hand: Hand.Right),
            Note("l", 48, 0, track: 0, hand: Hand.Left),
            Note("none", 60, 0, track: 1),
        ];
        TickSpan span = Span(0, Bar);

        Assert.Equal(["r"], MotifSelector.Select(notes, MeterMap.Default, span, SelectionFilter.ForHand(Hand.Right)).NoteIds);
        Assert.Equal(["l"], MotifSelector.Select(notes, MeterMap.Default, span, SelectionFilter.ForHand(Hand.Left)).NoteIds);
        Assert.Equal(["none"], MotifSelector.Select(notes, MeterMap.Default, span, SelectionFilter.ForTrack(1)).NoteIds);
    }

    [Fact]
    public void Select_says_when_a_selected_hand_is_inferred()
    {
        Note[] notes = [Note("r", 72, 0, hand: Hand.Right, inferred: true), Note("l", 48, 0, hand: Hand.Left)];
        TickSpan span = Span(0, Bar);

        Assert.True(MotifSelector.Select(notes, MeterMap.Default, span, SelectionFilter.ForHand(Hand.Right)).HandInferred);
        Assert.False(MotifSelector.Select(notes, MeterMap.Default, span, SelectionFilter.ForHand(Hand.Left)).HandInferred);
        Assert.False(MotifSelector.Select(notes, MeterMap.Default, span, SelectionFilter.All).HandInferred);
    }

    [Fact]
    public void Select_rejects_more_than_eight_bars()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MotifSelector.Select([], MeterMap.Default, Span(0, (8 * Bar) + 1), SelectionFilter.All));
    }

    [Fact]
    public void A_structure_is_selected_whole_up_to_the_structure_cap()
    {
        // A sentence of nine bars is selected whole (decision record 0013); 33 bars is still too many, and the cap itself is bounded.
        MotifSelection nine = MotifSelector.Select([Note("a", 60, 0)], MeterMap.Default, Span(0, 9 * Bar), SelectionFilter.All, MotifSelection.MaxStructureBars);
        Assert.Equal((1, 9), (nine.FirstBar, nine.LastBar));
        Assert.Throws<ArgumentOutOfRangeException>(() => MotifSelector.Select([], MeterMap.Default, Span(0, 33 * Bar), SelectionFilter.All, MotifSelection.MaxStructureBars));
        Assert.Throws<ArgumentOutOfRangeException>(() => MotifSelector.Select([], MeterMap.Default, Span(0, Bar), SelectionFilter.All, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => MotifSelector.Select([], MeterMap.Default, Span(0, Bar), SelectionFilter.All, 64));
    }

    [Fact]
    public void Select_is_deterministic_whatever_the_input_order()
    {
        Note[] notes = [Note("x", 64, 0), Note("y", 64, 0), Note("w", 60, 0)];

        MotifSelection forward = MotifSelector.Select(notes, MeterMap.Default, Span(0, Bar), SelectionFilter.All);
        MotifSelection backward = MotifSelector.Select(notes.Reverse(), MeterMap.Default, Span(0, Bar), SelectionFilter.All);

        Assert.Equal(["w", "x", "y"], forward.NoteIds);
        Assert.Equal(forward.NoteIds, backward.NoteIds);
    }
}