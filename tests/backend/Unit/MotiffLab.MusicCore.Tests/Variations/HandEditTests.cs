using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>Notes edited by hand in a variation, and hand-made variations (S1-E7, S1-E9).</summary>
public class HandEditTests
{
    private const long Quarter = 480;

    private static readonly HashSet<KeepProperty> s_keep = [KeepProperty.Pitches, KeepProperty.PitchOrder, KeepProperty.Rhythm];

    private static Note Note(string id, int pitch, long onset, long duration, int track = 0) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = track,
        Attribution = Attribution.Imported,
    };

    private static Note[] Motif() =>
    [
        Note("n1", 60, 0, Quarter),
        Note("n2", 62, Quarter, Quarter),
        Note("n3", 64, 2 * Quarter, Quarter),
        Note("n4", 67, 3 * Quarter, Quarter),
    ];

    private static VariationChain Start() =>
        VariationChain.Start(Motif(), new VariationContext(new TickSpan(Tick.Zero, 4 * Quarter), s_keep, Meter: MeterMap.Default));

    private static NoteValues Values(int pitch, long onset, long duration, int velocity = 80) =>
        new(new Pitch(pitch), new TickSpan(new Tick(onset), duration), velocity);

    private static KeepStatus Status(VariationChain chain, KeepProperty property) =>
        chain.Result!.Checks.Single(check => check.Property == property).Status;

    [Fact]
    public void A_variation_can_be_made_entirely_by_hand_one_step_per_edit()
    {
        VariationChain chain = Start()
            .Edit(HandEditParameters.Change("n2", Values(63, Quarter, Quarter)))
            .Edit(HandEditParameters.Remove("n4"))
            .Edit(HandEditParameters.Add("h1", Values(65, 3 * Quarter, 2 * Quarter, 90)));

        Assert.Equal(3, chain.Steps.Count);
        Assert.All(chain.Steps, step => Assert.Same(HandEdit.Instance, step.Operation));
        Assert.Null(chain.Pending);
        Assert.Equal("E7 › E7 › E7", chain.Result!.Operation);
        Assert.Equal(["n1", "n2", "n3", "h1"], chain.Result.Notes.Select(note => note.Id));
        Assert.Equal([60, 63, 64, 65], chain.Result.Notes.Select(note => note.Pitch.MidiNumber));
        Assert.Equal(["n4"], chain.Result.Removed.Select(note => note.Id));
        Assert.Equal(["h1"], chain.Result.Added.Select(note => note.Id));
        Assert.All(chain.Result.Notes.Where(note => note.Id is "n2" or "h1"), note => Assert.Equal(Provenance.UserEdited, note.Attribution.Provenance));
        Assert.Equal(Provenance.Imported, chain.Result.Notes[0].Attribution.Provenance);
        Assert.Equal(Motif(), chain.Original);
    }

    [Fact]
    public void Keep_chips_are_checked_after_each_edit_without_blocking_it()
    {
        VariationChain chain = Start();
        VariationChain velocity = chain.Edit(HandEditParameters.Change("n1", Values(60, 0, Quarter, 100)));
        VariationChain pitch = velocity.Edit(HandEditParameters.Change("n3", Values(65, 2 * Quarter, Quarter)));
        VariationChain rhythm = pitch.Edit(HandEditParameters.Change("n4", Values(67, 3 * Quarter, Quarter / 2)));

        Assert.Equal(KeepStatus.Kept, Status(velocity, KeepProperty.Pitches));
        Assert.Equal(KeepStatus.Kept, Status(velocity, KeepProperty.Rhythm));
        Assert.Equal(KeepStatus.NoLongerKept, Status(pitch, KeepProperty.Pitches));
        Assert.Equal(KeepStatus.Kept, Status(pitch, KeepProperty.Rhythm));
        Assert.Equal(KeepStatus.NoLongerKept, Status(rhythm, KeepProperty.Rhythm));
        Assert.Null(HandEdit.Instance.DisabledReason(s_keep));
    }

    [Fact]
    public void Undo_takes_back_an_edit_and_redo_brings_it_back()
    {
        VariationChain edited = Start().Edit(HandEditParameters.Remove("n2"));
        VariationChain undone = edited.Undo();

        Assert.Null(undone.Result);
        Assert.Equal(Motif(), undone.Current);
        Assert.Equal(edited.Result!.Notes, undone.Redo().Result!.Notes);
    }

    [Fact]
    public void An_edit_builds_on_the_step_being_tried()
    {
        VariationChain trying = VariationChain.Start(Motif(), new VariationContext(new TickSpan(Tick.Zero, 4 * Quarter), new HashSet<KeepProperty> { KeepProperty.Pitches })).Propose(RhythmReshape.Instance, new RhythmReshapeParameters(RhythmReshapeKind.HalfTime));
        Note heard = trying.Heard.Single(note => note.Id == "n2");

        VariationChain edited = trying.Edit(HandEditParameters.Change("n2", NoteValues.Of(heard) with { Pitch = new Pitch(61) }));

        Assert.Equal([RhythmReshape.OperationId, HandEdit.OperationId], edited.Steps.Select(step => step.Operation.Id));
        Assert.Equal(heard.Span, edited.Result!.Notes.Single(note => note.Id == "n2").Span);
        Assert.Equal(61, edited.Result.Notes.Single(note => note.Id == "n2").Pitch.MidiNumber);
    }

    [Fact]
    public void Edits_that_cannot_apply_say_why()
    {
        VariationChain chain = Start();

        Assert.Equal("Note x is not in the variation.", chain.EditUnavailableReason(HandEditParameters.Remove("x")));
        Assert.Equal("Note x is not in the variation.", chain.EditUnavailableReason(HandEditParameters.Change("x", Values(60, 0, Quarter))));
        Assert.Equal("The note already has these values.", chain.EditUnavailableReason(HandEditParameters.Change("n1", Values(60, 0, Quarter))));
        Assert.Equal("The variation already has a note n1.", chain.EditUnavailableReason(HandEditParameters.Add("n1", Values(60, 0, Quarter))));
        Assert.Equal("The note would be outside the piano's range (A0–C8).", chain.EditUnavailableReason(HandEditParameters.Change("n1", Values(109, 0, Quarter))));
        Assert.Equal("Velocity runs from 1 to 127.", chain.EditUnavailableReason(HandEditParameters.Change("n1", Values(60, 0, Quarter, 0))));
        Assert.Throws<InvalidOperationException>(() => chain.Edit(HandEditParameters.Remove("x")));

        VariationChain one = chain.Edit(HandEditParameters.Remove("n1")).Edit(HandEditParameters.Remove("n2")).Edit(HandEditParameters.Remove("n3"));
        Assert.Equal("A variation keeps at least one note.", one.EditUnavailableReason(HandEditParameters.Remove("n4")));
    }

    [Fact]
    public void A_note_cannot_move_before_the_selection_but_one_already_there_can_change()
    {
        VariationChain late = VariationChain.Start(Motif(), new VariationContext(new TickSpan(new Tick(Quarter), 3 * Quarter), s_keep));

        Assert.Equal("A note cannot start before the selection.", late.EditUnavailableReason(HandEditParameters.Change("n2", Values(62, 0, Quarter))));
        Assert.Null(late.EditUnavailableReason(HandEditParameters.Change("n1", Values(61, 0, Quarter))));
    }

    [Fact]
    public void An_added_note_takes_the_track_of_the_note_before_it()
    {
        Note[] notes = [Note("a", 48, 0, Quarter, track: 1), Note("b", 72, Quarter, Quarter, track: 0)];
        VariationChain chain = VariationChain.Start(notes, new VariationContext(new TickSpan(Tick.Zero, 4 * Quarter), s_keep));

        Note added = chain.Edit(HandEditParameters.Add("h1", Values(50, Quarter / 2, Quarter / 2))).Result!.Notes.Single(note => note.Id == "h1");

        Assert.Equal(1, added.Track);
        Assert.Equal("h2", HandEdit.NewId([.. notes, added]));
    }

    [Fact]
    public void Each_edit_explains_what_it_did_from_the_diff()
    {
        VariationChain chain = Start();

        Assert.StartsWith(
            "By hand, E4 at beat 3 of bar 1 changes: pitch to F4, start to 1 eighth after beat 3 of bar 1, velocity from 80 to 70.",
            chain.Edit(HandEditParameters.Change("n3", Values(65, (2 * Quarter) + (Quarter / 2), Quarter, 70))).Steps[^1].Result.Explanation);
        Assert.StartsWith("By hand, D4 at beat 2 of bar 1 is removed.", chain.Edit(HandEditParameters.Remove("n2")).Steps[^1].Result.Explanation);
        Assert.StartsWith(
            "By hand, C5 is added at beat 1 of bar 2, lasting 1 quarter note.",
            chain.Edit(HandEditParameters.Add("h1", Values(72, 4 * Quarter, Quarter))).Steps[^1].Result.Explanation);
        Assert.Contains(
            "1 of 4 notes change velocity (0 louder, 1 softer).",
            chain.Edit(HandEditParameters.Change("n1", Values(60, 0, Quarter, 50))).Result!.Explanation,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Replaying_on_other_notes_drops_a_chain_whose_edit_no_longer_fits()
    {
        VariationChain chain = Start().Edit(HandEditParameters.Remove("n4"));

        Assert.NotNull(chain.Replay(Motif(), chain.Context));
        Assert.Null(chain.Replay(Motif()[..3], chain.Context));
    }
}