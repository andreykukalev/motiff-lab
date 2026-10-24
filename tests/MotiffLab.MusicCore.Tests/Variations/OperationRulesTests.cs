using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>The PRD operation rules every step is checked against: grid, range, hand span, voice crossing.</summary>
public class OperationRulesTests
{
    private static Note Note(string id, int pitch, long onset, long duration, int? voice = null, Hand? hand = null) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Voice = voice,
        Hand = hand is { } value ? new Attributed<Hand>(value, Attribution.Imported) : null,
        Attribution = Attribution.Imported,
    };

    [Fact]
    public void A_result_on_the_grid_and_in_range_passes()
    {
        Note[] before = [Note("a", 60, 0, 480), Note("b", 64, 480, 480)];
        Note[] after = [Note("a", 60, 80, 400), Note("b", 64, 540, 60)];

        RuleReport report = OperationRules.Check(before, after);

        Assert.Null(report.Violation);
        Assert.Empty(report.Flags);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(109)]
    public void A_note_outside_the_piano_is_a_violation(int pitch)
    {
        RuleReport report = OperationRules.Check([Note("a", 60, 0, 480)], [Note("a", pitch, 0, 480)]);

        Assert.Contains("range", report.Violation);
    }

    [Fact]
    public void A_note_that_leaves_the_grid_is_a_violation()
    {
        RuleReport report = OperationRules.Check([Note("a", 60, 0, 480)], [Note("a", 60, 30, 480)]);

        Assert.Contains("grid", report.Violation);
    }

    [Fact]
    public void A_note_already_off_the_grid_may_stay_off_it()
    {
        Assert.Null(OperationRules.Check([Note("a", 60, 7, 480)], [Note("a", 60, 13, 480)]).Violation);
    }

    [Fact]
    public void An_added_note_must_be_on_the_grid_when_the_source_was()
    {
        Note[] before = [Note("a", 60, 0, 480)];

        Assert.Contains("grid", OperationRules.Check(before, [.. before, Note("a+", 62, 250, 230)]).Violation);
        Assert.Null(OperationRules.Check(before, [.. before, Note("a+", 62, 240, 240)]).Violation);
    }

    [Fact]
    public void Voices_that_newly_cross_are_a_violation()
    {
        Note[] before = [Note("s", 72, 0, 960, voice: 1), Note("b", 60, 0, 960, voice: 2)];
        Note[] after = [Note("s", 55, 0, 960, voice: 1), Note("b", 60, 0, 960, voice: 2)];

        Assert.Contains("cross", OperationRules.Check(before, after).Violation);
    }

    [Fact]
    public void Voices_that_already_cross_in_the_source_may_cross()
    {
        Note[] before =
        [
            Note("s1", 72, 0, 480, voice: 1), Note("b1", 60, 0, 480, voice: 2),
            Note("s2", 55, 480, 480, voice: 1), Note("b2", 60, 480, 480, voice: 2),
        ];
        Note[] after =
        [
            Note("s1", 50, 0, 480, voice: 1), Note("b1", 60, 0, 480, voice: 2),
            Note("s2", 55, 480, 480, voice: 1), Note("b2", 60, 480, 480, voice: 2),
        ];

        Assert.Null(OperationRules.Check(before, after).Violation);
    }

    [Fact]
    public void Hands_are_voices_when_the_source_has_no_notated_voices()
    {
        Note[] before = [Note("r", 72, 0, 960, hand: Hand.Right), Note("l", 48, 0, 960, hand: Hand.Left)];
        Note[] after = [Note("r", 40, 0, 960, hand: Hand.Right), Note("l", 48, 0, 960, hand: Hand.Left)];

        Assert.Contains("cross", OperationRules.Check(before, after).Violation);
    }

    [Fact]
    public void A_hand_span_over_a_tenth_is_flagged_not_blocked()
    {
        Note[] before = [Note("a", 60, 0, 480, hand: Hand.Right), Note("b", 72, 0, 480, hand: Hand.Right)];
        Note[] after = [Note("a", 60, 0, 480, hand: Hand.Right), Note("b", 77, 0, 480, hand: Hand.Right)];

        RuleReport report = OperationRules.Check(before, after);

        Assert.Null(report.Violation);
        Assert.Equal(["The right hand spans 17 semitones, more than a 10th."], report.Flags);
    }

    [Fact]
    public void A_tenth_itself_and_a_span_the_source_already_had_are_not_flagged()
    {
        Note[] tenth = [Note("a", 60, 0, 480, hand: Hand.Left), Note("b", 76, 0, 480, hand: Hand.Left)];
        Note[] wide = [Note("a", 60, 0, 480, hand: Hand.Left), Note("b", 79, 0, 480, hand: Hand.Left)];

        Assert.Empty(OperationRules.Check(tenth, tenth).Flags);
        Assert.Empty(OperationRules.Check(wide, wide).Flags);
    }

    [Fact]
    public void An_operation_whose_result_breaks_a_rule_is_unavailable_with_the_reason()
    {
        Note[] thirtySeconds = [Note("a", 60, 0, 60), Note("b", 62, 60, 60)];
        var context = new VariationContext(new TickSpan(Tick.Zero, 480), new HashSet<KeepProperty>());
        var doubleTime = new RhythmReshapeParameters(RhythmReshapeKind.DoubleTime);

        Assert.Equal(
            "Rhythm reshape would put a note off the grid of 32nds and triplet 16ths, so it is not available for this selection.",
            RhythmReshape.Instance.UnavailableReason(thirtySeconds, context, doubleTime));
        Assert.Throws<InvalidOperationException>(() => RhythmReshape.Instance.Apply(thirtySeconds, context, doubleTime));
    }
}