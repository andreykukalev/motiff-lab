using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>Variations as chains of steps over the original (S1-E5, S1-E8, AC-S1-12).</summary>
public class VariationChainTests
{
    private const long Quarter = 480;

    private static readonly HashSet<KeepProperty> s_keep = [KeepProperty.Pitches, KeepProperty.PitchOrder];

    private static readonly RhythmReshapeParameters s_halfTime = new(RhythmReshapeKind.HalfTime);

    private static readonly RhythmReshapeParameters s_doubleTime = new(RhythmReshapeKind.DoubleTime);

    private static readonly RhythmReshapeParameters s_later = new(RhythmReshapeKind.Displace, 1);

    private static Note Note(string id, int pitch, long onset, long duration) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    private static Note[] Motif() =>
    [
        Note("n1", 60, 0, Quarter),
        Note("n2", 62, Quarter, Quarter),
        Note("n3", 64, 2 * Quarter, Quarter),
        Note("n4", 67, 3 * Quarter, Quarter),
    ];

    private static VariationChain Start(IReadOnlySet<KeepProperty>? keep = null) =>
        VariationChain.Start(Motif(), new VariationContext(new TickSpan(Tick.Zero, 4 * Quarter), keep ?? s_keep));

    private static VariationChain Accepted(params RhythmReshapeParameters[] steps) =>
        steps.Aggregate(Start(), (chain, step) => chain.Propose(RhythmReshape.Instance, step).Accept());

    [Fact]
    public void A_proposed_step_is_heard_but_not_part_of_the_chain_until_accepted()
    {
        VariationChain proposed = Start().Propose(RhythmReshape.Instance, s_halfTime);

        Assert.NotNull(proposed.Pending);
        Assert.Empty(proposed.Steps);
        Assert.Equal(Motif(), proposed.Current);
        Assert.Equal([0L, 960, 1920, 2880], proposed.Result!.Notes.Select(note => note.Span.Onset.Value));

        VariationChain accepted = proposed.Accept();

        Assert.Null(accepted.Pending);
        Assert.Single(accepted.Steps);
        Assert.Equal(proposed.Result.Notes, accepted.Current);
    }

    [Fact]
    public void Rejecting_drops_the_pending_step_and_keeps_the_accepted_ones()
    {
        VariationChain chain = Accepted(s_halfTime).Propose(RhythmReshape.Instance, s_later).Reject();

        Assert.Null(chain.Pending);
        Assert.Single(chain.Steps);
        Assert.Equal(Accepted(s_halfTime).Current, chain.Current);
    }

    [Fact]
    public void Each_step_reads_the_notes_the_step_before_it_made()
    {
        VariationChain chain = Accepted(s_halfTime, s_later);

        Assert.Equal([240L, 1200, 2160, 3120], chain.Current.Select(note => note.Span.Onset.Value));
        Assert.All(chain.Current, note => Assert.Equal(2 * Quarter, note.Span.Duration));
        Assert.Equal("V1 › V1", chain.Result!.Operation);
        Assert.Equal(chain.Steps[0].Result.Notes, chain.Steps[1].Result.Changes.Select(change => change.Before));
    }

    [Fact]
    public void Undoing_three_accepted_steps_gives_the_original_note_for_note()
    {
        VariationChain chain = Accepted(s_halfTime, s_later, s_doubleTime);

        VariationChain undone = chain.Undo().Undo().Undo();

        Assert.Equal(Motif(), undone.Current);
        Assert.Null(undone.Result);
        Assert.False(undone.CanUndo);
        Assert.Equal(3, undone.Undone.Count);
    }

    [Fact]
    public void Redo_brings_back_the_undone_steps_in_order()
    {
        VariationChain chain = Accepted(s_halfTime, s_later, s_doubleTime);

        VariationChain redone = chain.Undo().Undo().Redo().Redo();

        Assert.Equal(chain.Current, redone.Current);
        Assert.False(redone.CanRedo);
    }

    [Fact]
    public void Accepting_a_new_step_clears_redo()
    {
        VariationChain chain = Accepted(s_halfTime, s_later).Undo().Propose(RhythmReshape.Instance, s_doubleTime).Accept();

        Assert.False(chain.CanRedo);
        Assert.Equal(2, chain.Steps.Count);
    }

    [Fact]
    public void The_original_is_never_overwritten()
    {
        Note[] motif = Motif();
        VariationChain chain = VariationChain.Start(motif, new VariationContext(new TickSpan(Tick.Zero, 4 * Quarter), s_keep));

        chain = chain.Propose(RhythmReshape.Instance, s_halfTime).Accept().Propose(RhythmReshape.Instance, s_later).Accept();

        Assert.Equal(Motif(), motif);
        Assert.Equal(Motif(), chain.Original);
        Assert.All(chain.Original, note => Assert.Equal(Provenance.Imported, note.Attribution.Provenance));
    }

    [Fact]
    public void Undo_and_redo_reach_back_fifty_steps_and_the_chain_stops_there()
    {
        RhythmReshapeParameters[] steps = Enumerable.Range(0, VariationChain.MaxSteps)
            .Select(i => i % 2 == 0 ? s_later : new RhythmReshapeParameters(RhythmReshapeKind.Displace, 2))
            .ToArray();
        VariationChain chain = Accepted(steps);

        Assert.Equal(VariationChain.MaxSteps, chain.Steps.Count);
        VariationChain proposed = chain.Propose(RhythmReshape.Instance, s_later);
        Assert.NotNull(proposed.AcceptDisabledReason);
        Assert.Throws<InvalidOperationException>(proposed.Accept);

        VariationChain undone = chain;
        for (int i = 0; i < VariationChain.MaxSteps; i++)
        {
            undone = undone.Undo();
        }

        Assert.Equal(Motif(), undone.Current);
        VariationChain redone = undone;
        for (int i = 0; i < VariationChain.MaxSteps; i++)
        {
            redone = redone.Redo();
        }

        Assert.Equal(chain.Current, redone.Current);
    }

    [Fact]
    public void Kept_properties_are_checked_against_the_original()
    {
        var keep = new HashSet<KeepProperty> { KeepProperty.Pitches, KeepProperty.Register };
        VariationChain chain = VariationChain.Start(Motif(), new VariationContext(new TickSpan(Tick.Zero, 4 * Quarter), keep))
            .Propose(RhythmReshape.Instance, s_halfTime).Accept()
            .Propose(RhythmReshape.Instance, s_later);

        Assert.Equal(
            [new KeepCheck(KeepProperty.Pitches, KeepStatus.Kept), new KeepCheck(KeepProperty.Register, KeepStatus.Kept)],
            chain.Result!.Checks);
        Assert.Equal(Motif(), chain.Result.Changes.Select(change => change.Before));
    }

    [Fact]
    public void The_same_choices_give_the_same_chain()
    {
        VariationChain first = Accepted(s_halfTime, s_later).Propose(RhythmReshape.Instance, s_doubleTime);
        VariationChain second = Accepted(s_halfTime, s_later).Propose(RhythmReshape.Instance, s_doubleTime);

        Assert.Equal(first.Result!.Notes, second.Result!.Notes);
        Assert.Equal(first.Result.Changes, second.Result.Changes);
        Assert.Equal(first.Result.Explanation, second.Result.Explanation);
    }

    [Fact]
    public void Replay_runs_the_same_steps_over_other_notes()
    {
        VariationChain chain = Accepted(s_halfTime).Propose(RhythmReshape.Instance, s_later);
        Note[] longer = [.. Motif(), Note("n5", 72, 4 * Quarter, Quarter)];

        VariationChain replayed = chain.Replay(longer, new VariationContext(new TickSpan(Tick.Zero, 8 * Quarter), s_keep))!;

        Assert.Single(replayed.Steps);
        Assert.NotNull(replayed.Pending);
        Assert.Equal(5, replayed.Result!.Notes.Count);
        Assert.Equal(4080L, replayed.Result.Notes[^1].Span.Onset.Value);
    }

    [Fact]
    public void Replay_gives_null_when_a_step_can_no_longer_run()
    {
        VariationChain chain = Accepted(s_halfTime);

        Assert.Null(chain.Replay(Motif(), chain.Context with { Keep = new HashSet<KeepProperty> { KeepProperty.Rhythm } }));
    }

    [Fact]
    public void Proposing_a_step_the_keep_choices_rule_out_throws()
    {
        VariationChain chain = Start(new HashSet<KeepProperty> { KeepProperty.Rhythm });

        Assert.NotNull(chain.UnavailableReason(RhythmReshape.Instance, s_halfTime));
        Assert.Throws<InvalidOperationException>(() => chain.Propose(RhythmReshape.Instance, s_halfTime));
    }

    [Fact]
    public void Undo_and_redo_with_nothing_to_take_back_throw()
    {
        Assert.Throws<InvalidOperationException>(() => Start().Undo());
        Assert.Throws<InvalidOperationException>(() => Start().Redo());
        Assert.Throws<InvalidOperationException>(() => Start().Accept());
    }
}