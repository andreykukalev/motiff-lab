using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>V2 Accompaniment pattern: each bar's accompaniment replays its own pitches in a new pattern (Explore experiment C).</summary>
public class AccompanimentPatternTests
{
    private const long Sixteenth = 120;
    private const long Eighth = 240;
    private const long Bar = 6 * Sixteenth;

    private static readonly HashSet<KeepProperty> s_keepMelody = [KeepProperty.Melody];

    private static readonly MeterMap s_threeEight = new([new MeterChange(Tick.Zero, new TimeSignature(3, 8))]);

    private static Note Note(string id, int pitch, long sixteenths, long length, Hand hand, int velocity = 80) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(sixteenths * Sixteenth), length * Sixteenth),
        Velocity = velocity,
        Track = 0,
        Hand = new Attributed<Hand>(hand, Attribution.Imported),
        Attribution = Attribution.Imported,
    };

    /// <summary>
    /// Für Elise bars 2–3 as in the pilot file, from tick 0 in 3/8: A4 and B4 as eighths in the melody,
    /// the right hand rising back to them, and the left hand's A2 E3 A3 and E2 E3 G♯3 rising in
    /// sixteenths and held to the end of each bar.
    /// </summary>
    private static Note[] FurElise() =>
    [
        Note("m1", 69, 0, 2, Hand.Right), Note("r1", 60, 3, 1, Hand.Right), Note("r2", 64, 4, 1, Hand.Right), Note("r3", 69, 5, 1, Hand.Right),
        Note("l1", 45, 0, 6, Hand.Left, 67), Note("l2", 52, 1, 5, Hand.Left), Note("l3", 57, 2, 4, Hand.Left),
        Note("m2", 71, 6, 2, Hand.Right), Note("r4", 64, 9, 1, Hand.Right), Note("r5", 68, 10, 1, Hand.Right), Note("r6", 71, 11, 1, Hand.Right),
        Note("l4", 40, 6, 6, Hand.Left), Note("l5", 52, 7, 5, Hand.Left), Note("l6", 56, 8, 4, Hand.Left),
    ];

    private static VariationContext Context(long end = 2 * Bar) => new(new TickSpan(Tick.Zero, end), s_keepMelody, Meter: s_threeEight);

    private static Variation Apply(AccompanimentPatternKind kind, PatternUnit unit = PatternUnit.Eighths) =>
        AccompanimentPattern.Instance.Apply(FurElise(), Context(), new AccompanimentPatternParameters(kind, unit));

    private static (long Sixteenth, long Length, int Pitch)[] LeftHand(Variation variation) =>
        [.. variation.Notes.Where(note => note.Hand!.Value == Hand.Left)
            .Select(note => (note.Span.Onset.Value / Sixteenth, note.Span.Duration / Sixteenth, note.Pitch.MidiNumber))
            .Order()];

    [Fact]
    public void Repeated_chord_in_eighths_is_pilot_e3()
    {
        Variation variation = Apply(AccompanimentPatternKind.RepeatedChord);

        Assert.Equal("V2", variation.Operation);
        Assert.Equal(
            [(0, 2, 45), (0, 2, 52), (0, 2, 57), (2, 2, 45), (2, 2, 52), (2, 2, 57), (4, 2, 45), (4, 2, 52), (4, 2, 57),
             (6, 2, 40), (6, 2, 52), (6, 2, 56), (8, 2, 40), (8, 2, 52), (8, 2, 56), (10, 2, 40), (10, 2, 52), (10, 2, 56)],
            LeftHand(variation));
        Assert.Equal(
            FurElise().Where(note => note.Hand!.Value == Hand.Right).Select(note => (note.Id, note.Span, note.Pitch)),
            variation.Notes.Where(note => note.Hand!.Value == Hand.Right).Select(note => (note.Id, note.Span, note.Pitch)));
        Assert.Equal(KeepStatus.Kept, variation.Checks.Single(check => check.Property == KeepProperty.Melody).Status);
        Assert.StartsWith("In bars 1–2 the accompaniment keeps its pitches and plays them in a new rhythm or order; the melody is unchanged.", variation.Explanation);
    }

    [Fact]
    public void Held_chord_strikes_each_bar_once_and_holds_it()
    {
        Assert.Equal([(0, 6, 45), (0, 6, 52), (0, 6, 57), (6, 6, 40), (6, 6, 52), (6, 6, 56)], LeftHand(Apply(AccompanimentPatternKind.HeldChord)));
    }

    [Fact]
    public void Arpeggios_walk_the_bar_pitches_one_unit_at_a_time()
    {
        Assert.Equal(
            [(0, 1, 57), (1, 1, 52), (2, 1, 45), (3, 1, 57), (4, 1, 52), (5, 1, 45)],
            LeftHand(Apply(AccompanimentPatternKind.ArpeggioDown, PatternUnit.Sixteenths)).Take(6));
        Assert.Equal(
            [(0, 1, 45), (1, 1, 52), (2, 1, 57), (3, 1, 52), (4, 1, 45), (5, 1, 52)],
            LeftHand(Apply(AccompanimentPatternKind.UpDown, PatternUnit.Sixteenths)).Take(6));
        Assert.Equal(
            [(0, 1, 45), (1, 1, 57), (2, 1, 52), (3, 1, 57), (4, 1, 45), (5, 1, 57)],
            LeftHand(Apply(AccompanimentPatternKind.Alberti, PatternUnit.Sixteenths)).Take(6));
    }

    [Fact]
    public void Triplets_fill_the_bar_and_the_last_unit_is_cut_at_the_bar_line()
    {
        Note[] left = [.. Apply(AccompanimentPatternKind.ArpeggioUp, PatternUnit.Triplets).Notes.Where(note => note.Hand!.Value == Hand.Left && note.Span.Onset.Value < Bar)];

        Assert.Equal([0L, 160, 320, 480, 640], left.Select(note => note.Span.Onset.Value));
        Assert.Equal(80, left[^1].Span.Duration);
    }

    [Fact]
    public void New_notes_copy_their_source_and_get_derived_ids()
    {
        Variation variation = Apply(AccompanimentPatternKind.RepeatedChord);

        Note first = variation.Notes.Single(note => note.Id == "l1~p1");
        Assert.Equal((67, Hand.Left, Provenance.UserEdited), (first.Velocity, first.Hand!.Value, first.Attribution.Provenance));
        Assert.Contains(variation.Notes, note => note.Id == "l6~p3");
        Assert.Equal(["l1", "l2", "l3", "l4", "l5", "l6"], variation.Removed.Select(note => note.Id));
    }

    [Fact]
    public void A_single_line_has_no_accompaniment()
    {
        Note[] line = [.. FurElise().Where(note => note.Hand!.Value == Hand.Right).Take(1)];

        Assert.Equal(
            "Accompaniment pattern needs an accompaniment under the melody, and this selection has none, so it is not available.",
            AccompanimentPattern.Instance.UnavailableReason(line, Context(), new AccompanimentPatternParameters(AccompanimentPatternKind.HeldChord)));
    }

    [Fact]
    public void Alberti_needs_three_pitches_in_every_bar()
    {
        Note[] notes = [.. FurElise().Where(note => note.Id is not "l3")];

        Assert.Equal(
            "Alberti needs three different accompaniment notes in each bar, so it is not available for this selection.",
            AccompanimentPattern.Instance.UnavailableReason(notes, Context(), new AccompanimentPatternParameters(AccompanimentPatternKind.Alberti)));
    }

    [Fact]
    public void An_accompaniment_already_in_the_pattern_has_nothing_to_change()
    {
        Note[] held = [.. Apply(AccompanimentPatternKind.HeldChord).Notes];

        Assert.Equal(
            "The accompaniment already plays this pattern, so Accompaniment pattern has nothing to change.",
            AccompanimentPattern.Instance.UnavailableReason(held, Context(), new AccompanimentPatternParameters(AccompanimentPatternKind.HeldChord)));
    }

    [Fact]
    public void A_pattern_that_would_reach_the_melody_is_not_available()
    {
        Note[] notes = [Note("m", 60, 0, 6, Hand.Right), Note("a", 55, 0, 3, Hand.Left), Note("b", 64, 3, 3, Hand.Left)];

        Assert.Equal(
            "Accompaniment pattern would put an accompaniment note at or above the melody, so it is not available for this selection.",
            AccompanimentPattern.Instance.UnavailableReason(notes, Context(Bar), new AccompanimentPatternParameters(AccompanimentPatternKind.HeldChord)));
    }

    [Fact]
    public void The_same_notes_and_pattern_give_the_same_variation()
    {
        Variation first = Apply(AccompanimentPatternKind.Alberti, PatternUnit.Sixteenths);
        Variation second = Apply(AccompanimentPatternKind.Alberti, PatternUnit.Sixteenths);

        Assert.Equal(first.Notes, second.Notes);
        Assert.Equal(first.Explanation, second.Explanation);
    }
}