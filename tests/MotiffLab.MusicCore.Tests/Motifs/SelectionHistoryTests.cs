using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Motifs;

/// <summary>The selection's Undo and Redo (#511 section 11); ipad/App/MotiffLabTests/SelectionHistoryTests.swift has the same cases.</summary>
public class SelectionHistoryTests
{
    private static readonly TickSpan s_one = TickSpan.FromBounds(new Tick(0), new Tick(1920));
    private static readonly TickSpan s_two = TickSpan.FromBounds(new Tick(0), new Tick(3840));
    private static readonly TickSpan s_far = TickSpan.FromBounds(new Tick(7680), new Tick(9600));

    [Fact]
    public void A_made_selection_is_a_step_that_undo_takes_away_and_redo_brings_back()
    {
        SelectionHistory<TickSpan> history = new();

        SelectionStep<TickSpan>? step = history.Record(null, s_one, "g1");

        Assert.Equal(SelectionStepKind.Create, step!.Kind);
        SelectionStep<TickSpan>? undone = history.Undo();
        Assert.Null(undone!.Before);
        Assert.True(history.CanRedo);
        Assert.False(history.CanUndo);
        Assert.Equal(s_one, history.Redo()!.After);
    }

    [Fact]
    public void A_hold_that_clears_and_a_drag_after_it_are_one_replace_step()
    {
        SelectionHistory<TickSpan> history = new();
        history.Record(null, s_one, "g1");

        history.Record(s_one, null, "g2");
        SelectionStep<TickSpan>? step = history.Record(null, s_far, "g2");

        Assert.Equal(SelectionStepKind.Replace, step!.Kind);
        Assert.Equal(2, history.Done.Count);
        Assert.Equal(s_one, history.Undo()!.Before);
    }

    [Fact]
    public void A_clear_on_its_own_is_a_clear_step()
    {
        SelectionHistory<TickSpan> history = new();
        history.Record(null, s_one);

        Assert.Equal(SelectionStepKind.Clear, history.Record(s_one, null)!.Kind);
    }

    [Fact]
    public void Edge_moves_of_one_gesture_join_and_a_change_that_ends_where_it_began_is_no_step()
    {
        SelectionHistory<TickSpan> history = new();
        history.Record(null, s_one, "g1");

        Assert.Equal(SelectionStepKind.Change, history.Record(s_one, s_two, "g2")!.Kind);
        Assert.Null(history.Record(s_two, s_one, "g2"));
        Assert.Single(history.Done);
        Assert.Null(history.Record(s_one, s_one, "g3"));
        Assert.Single(history.Done);
    }

    [Fact]
    public void A_new_step_drops_redo()
    {
        SelectionHistory<TickSpan> history = new();
        history.Record(null, s_one, "g1");
        history.Record(s_one, s_two, "g2");
        history.Undo();

        history.Record(s_one, s_far, "g3");

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void A_cancelled_gesture_leaves_no_step()
    {
        SelectionHistory<TickSpan> history = new();
        history.Record(null, s_one, "g1");
        history.Record(s_one, null, "g2");

        Assert.Equal(s_one, history.Revert("g2")!.Before);
        Assert.Null(history.Revert("g9"));
        Assert.Single(history.Done);
    }

    [Fact]
    public void Only_the_last_fifty_steps_are_kept()
    {
        SelectionHistory<TickSpan> history = new();
        for (int index = 0; index < 60; index++)
        {
            history.Record(TickSpan.FromBounds(new Tick(index), new Tick(index + 1)), TickSpan.FromBounds(new Tick(index + 1), new Tick(index + 2)));
        }

        Assert.Equal(SelectionHistory<TickSpan>.Depth, history.Done.Count);
        Assert.Equal(new Tick(10), history.Done[0].Before!.Value.Onset);
    }

    [Fact]
    public void Nothing_to_undo_or_redo_gives_nothing()
    {
        SelectionHistory<TickSpan> history = new();

        Assert.Null(history.Undo());
        Assert.Null(history.Redo());
    }
}