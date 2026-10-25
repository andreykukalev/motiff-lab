using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>A step limited to the melody, the accompaniment or the bass changes only those notes (Explore experiments A–C).</summary>
public class OperationScopeTests
{
    private const long Sixteenth = 120;

    private static readonly HashSet<KeepProperty> s_keepNothing = [];

    private static Note Note(string id, int pitch, long sixteenths, long length, Hand hand) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(sixteenths * Sixteenth), length * Sixteenth),
        Velocity = 80,
        Track = 0,
        Hand = new Attributed<Hand>(hand, Attribution.Imported),
        Attribution = Attribution.Imported,
    };

    /// <summary>
    /// Für Elise bar 1 to the downbeat of bar 3 as in the pilot file, from bar 1 at sixteenth 0: the
    /// motif in sixteenths; then A4 as an eighth, the left hand's A2 E3 A3 held under it, and the right
    /// hand's C4 E4 A4 rising back to B4.
    /// </summary>
    private static Note[] FurElise() =>
    [
        Note("m1", 76, 0, 1, Hand.Right), Note("m2", 75, 1, 1, Hand.Right), Note("m3", 76, 2, 1, Hand.Right),
        Note("m4", 71, 3, 1, Hand.Right), Note("m5", 74, 4, 1, Hand.Right), Note("m6", 72, 5, 1, Hand.Right),
        Note("m7", 69, 6, 2, Hand.Right),
        Note("l1", 45, 6, 6, Hand.Left), Note("l2", 52, 7, 4, Hand.Left), Note("l3", 57, 8, 4, Hand.Left),
        Note("r1", 60, 9, 1, Hand.Right), Note("r2", 64, 10, 1, Hand.Right), Note("r3", 69, 11, 1, Hand.Right),
        Note("m8", 71, 12, 2, Hand.Right),
    ];

    private static VariationContext Context(Note[] notes) =>
        new(TickSpan.FromBounds(Tick.Zero, new Tick(notes.Max(note => note.Span.End.Value))), s_keepNothing, Meter: new MeterMap([new MeterChange(Tick.Zero, new TimeSignature(3, 8))]));

    [Fact]
    public void All_keeps_every_note_inside()
    {
        Note[] notes = FurElise();

        (Note[] inside, Note[] outside) = OperationScopes.Split(notes, OperationScope.All);

        Assert.Equal(notes, inside);
        Assert.Empty(outside);
    }

    [Fact]
    public void In_fur_elise_the_accompaniment_is_the_left_hand_and_the_rising_arpeggio_reads_as_melody()
    {
        // The texture's melody is the top line, so once A4 has ended the right hand's C4 E4 A4 is the
        // top line. The accompaniment a step can change is therefore the left hand here, and its bass
        // is the held A2: E3 and A3 sound above it while it is still held.
        Note[] notes = FurElise();

        Assert.Equal(["l1", "l2", "l3"], OperationScopes.Split(notes, OperationScope.Accompaniment).Inside.Select(note => note.Id));
        Assert.Equal(["m1", "m2", "m3", "m4", "m5", "m6", "m7", "r1", "r2", "r3", "m8"], OperationScopes.Split(notes, OperationScope.Melody).Inside.Select(note => note.Id));
        Assert.Equal(["l1"], OperationScopes.Split(notes, OperationScope.Bass).Inside.Select(note => note.Id));
    }

    [Fact]
    public void Rhythm_reshape_limited_to_the_accompaniment_leaves_the_melody_alone()
    {
        Note[] notes = FurElise();

        Variation variation = RhythmReshape.Instance.Apply(notes, Context(notes), new RhythmReshapeParameters(RhythmReshapeKind.Displace, 1) { Scope = OperationScope.Accompaniment });

        Assert.Equal(["l1", "l2", "l3"], variation.Changes.Where(change => change.OnsetChanged).Select(change => change.Before.Id));
        Assert.All(variation.Changes.Where(change => change.Before.Hand!.Value == Hand.Right), change => Assert.Equal(change.Before, change.After));
    }

    [Fact]
    public void A_single_line_has_no_accompaniment_to_vary()
    {
        Note[] line = [.. FurElise().Take(6)];

        Assert.Equal(
            "The selection has no notes in the accompaniment to vary.",
            Articulation.Instance.UnavailableReason(line, Context(line), new ArticulationParameters(ArticulationKind.Detached) { Scope = OperationScope.Accompaniment }));
    }

    [Theory]
    [InlineData(OperationScope.Melody)]
    [InlineData(OperationScope.Accompaniment)]
    public void Operations_that_pick_their_own_notes_take_only_the_whole_selection(OperationScope scope)
    {
        Note[] notes = FurElise();
        VariationContext context = Context(notes) with { Key = new MusicalKey(0, KeyMode.Minor) };

        Assert.False(ChangeDirection.Instance.Supports(scope));
        Assert.False(MelodicSequence.Instance.Supports(scope));
        Assert.False(HandEdit.Instance.Supports(scope));
        Assert.Equal(
            $"Change direction works on the whole selection, so it can't be limited to {OperationScopes.Describe(scope)}.",
            ChangeDirection.Instance.UnavailableReason(notes, context, new ChangeDirectionParameters() { Scope = scope }));
    }

    [Fact]
    public void Steps_with_different_scopes_are_different_steps()
    {
        Assert.NotEqual(
            new ArticulationParameters(ArticulationKind.Short),
            new ArticulationParameters(ArticulationKind.Short) { Scope = OperationScope.Melody });
        Assert.Equal(
            new ArticulationParameters(ArticulationKind.Short) { Scope = OperationScope.Melody },
            new ArticulationParameters(ArticulationKind.Short) { Scope = OperationScope.Melody });
    }

    [Fact]
    public void A_chain_can_change_the_melody_in_one_step_and_the_accompaniment_in_the_next()
    {
        Note[] notes = FurElise();

        VariationChain chain = VariationChain.Start(notes, Context(notes))
            .Propose(Articulation.Instance, new ArticulationParameters(ArticulationKind.Detached) { Scope = OperationScope.Melody }).Accept()
            .Propose(Articulation.Instance, new ArticulationParameters(ArticulationKind.Short) { Scope = OperationScope.Accompaniment }).Accept();

        Assert.Equal("V13 › V13", chain.Result!.Operation);
        Assert.All(chain.Result.Notes, note => Assert.Equal(notes.Single(other => other.Id == note.Id).Span.Onset, note.Span.Onset));
        Assert.All(chain.Result.Notes, note => Assert.True(note.Span.Duration < notes.Single(other => other.Id == note.Id).Span.Duration, note.Id));
        Assert.Equal(chain.Result.Notes, chain.Replay(notes, chain.Context)!.Result!.Notes);
    }
}