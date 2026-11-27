using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Tests.Projects;
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

    /// <summary>A hand edit as the chain takes one: proposed, then accepted.</summary>
    private static VariationChain Edited(VariationChain chain, HandEditParameters edit) => chain.Propose(HandEdit.Instance, edit).Accept();

    /// <summary>Why the hand edit cannot apply to the notes as they are heard, or null when it can.</summary>
    private static string? Unavailable(VariationChain chain, HandEditParameters edit) => HandEdit.Instance.UnavailableReason(chain.Heard, chain.Context, edit);

    private static KeepStatus Status(VariationChain chain, KeepProperty property) =>
        chain.Result!.Checks.Single(check => check.Property == property).Status;

    [Fact]
    public void A_variation_can_be_made_entirely_by_hand_one_step_per_edit()
    {
        VariationChain changed = Edited(Start(), new HandEditParameters(HandEditKind.Change, "n2", Values(63, Quarter, Quarter)));
        VariationChain chain = Edited(Edited(changed, new HandEditParameters(HandEditKind.Remove, "n4", null)), HandEditParameters.Add("h1", Values(65, 3 * Quarter, 2 * Quarter, 90)));

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
        VariationChain velocity = Edited(chain, new HandEditParameters(HandEditKind.Change, "n1", Values(60, 0, Quarter, 100)));
        VariationChain pitch = Edited(velocity, new HandEditParameters(HandEditKind.Change, "n3", Values(65, 2 * Quarter, Quarter)));
        VariationChain rhythm = Edited(pitch, new HandEditParameters(HandEditKind.Change, "n4", Values(67, 3 * Quarter, Quarter / 2)));

        Assert.Equal(KeepStatus.Kept, Status(velocity, KeepProperty.Pitches));
        Assert.Equal(KeepStatus.Kept, Status(velocity, KeepProperty.Rhythm));
        Assert.Equal(KeepStatus.NoLongerKept, Status(pitch, KeepProperty.Pitches));
        Assert.Equal(KeepStatus.Kept, Status(pitch, KeepProperty.Rhythm));
        Assert.Equal(KeepStatus.NoLongerKept, Status(rhythm, KeepProperty.Rhythm));
        Assert.Null(HandEdit.Instance.DisabledReason(s_keep));
    }

    [Fact]
    public void Edits_that_cannot_apply_say_why()
    {
        VariationChain chain = Start();

        Assert.Equal("Note x is not in the variation.", Unavailable(chain, new HandEditParameters(HandEditKind.Remove, "x", null)));
        Assert.Equal("Note x is not in the variation.", Unavailable(chain, new HandEditParameters(HandEditKind.Change, "x", Values(60, 0, Quarter))));
        Assert.Equal("The note already has these values.", Unavailable(chain, new HandEditParameters(HandEditKind.Change, "n1", Values(60, 0, Quarter))));
        Assert.Equal("The variation already has a note n1.", Unavailable(chain, HandEditParameters.Add("n1", Values(60, 0, Quarter))));
        Assert.Equal("The note would be outside the piano's range (A0–C8).", Unavailable(chain, new HandEditParameters(HandEditKind.Change, "n1", Values(109, 0, Quarter))));
        Assert.Equal("Velocity runs from 1 to 127.", Unavailable(chain, new HandEditParameters(HandEditKind.Change, "n1", Values(60, 0, Quarter, 0))));
        Assert.Throws<InvalidOperationException>(() => Edited(chain, new HandEditParameters(HandEditKind.Remove, "x", null)));

        VariationChain one = Edited(Edited(Edited(chain, new HandEditParameters(HandEditKind.Remove, "n1", null)), new HandEditParameters(HandEditKind.Remove, "n2", null)), new HandEditParameters(HandEditKind.Remove, "n3", null));
        Assert.Equal("A variation keeps at least one note.", Unavailable(one, new HandEditParameters(HandEditKind.Remove, "n4", null)));
    }

    [Fact]
    public void A_note_cannot_move_before_the_selection_but_one_already_there_can_change()
    {
        VariationChain late = VariationChain.Start(Motif(), new VariationContext(new TickSpan(new Tick(Quarter), 3 * Quarter), s_keep));

        Assert.Equal("A note cannot start before the selection.", Unavailable(late, new HandEditParameters(HandEditKind.Change, "n2", Values(62, 0, Quarter))));
        Assert.Null(Unavailable(late, new HandEditParameters(HandEditKind.Change, "n1", Values(61, 0, Quarter))));
    }

    [Fact]
    public void An_added_note_takes_the_track_of_the_note_before_it()
    {
        Note[] notes = [Note("a", 48, 0, Quarter, track: 1), Note("b", 72, Quarter, Quarter, track: 0)];
        VariationChain chain = VariationChain.Start(notes, new VariationContext(new TickSpan(Tick.Zero, 4 * Quarter), s_keep));

        Note added = Edited(chain, HandEditParameters.Add("h1", Values(50, Quarter / 2, Quarter / 2))).Result!.Notes.Single(note => note.Id == "h1");

        Assert.Equal(1, added.Track);
        Assert.Equal("h2", HandEdit.NewId([.. notes, added]));
    }

    [Fact]
    public void Each_edit_explains_what_it_did_from_the_diff()
    {
        VariationChain chain = Start();

        Assert.StartsWith(
            "By hand, E4 at beat 3 of bar 1 changes: pitch to F4, start to 1 eighth after beat 3 of bar 1, velocity from 80 to 70.",
            Edited(chain, new HandEditParameters(HandEditKind.Change, "n3", Values(65, (2 * Quarter) + (Quarter / 2), Quarter, 70))).Steps[^1].Result.Explanation);
        Assert.StartsWith("By hand, D4 at beat 2 of bar 1 is removed.", Edited(chain, new HandEditParameters(HandEditKind.Remove, "n2", null)).Steps[^1].Result.Explanation);
        Assert.StartsWith(
            "By hand, C5 is added at beat 1 of bar 2, lasting 1 quarter note.",
            Edited(chain, HandEditParameters.Add("h1", Values(72, 4 * Quarter, Quarter))).Steps[^1].Result.Explanation);
        Assert.Contains(
            "1 of 4 notes change velocity (0 louder, 1 softer).",
            Edited(chain, new HandEditParameters(HandEditKind.Change, "n1", Values(60, 0, Quarter, 50))).Result!.Explanation,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_hand_made_variation_round_trips_with_its_edits()
    {
        Project project = ProjectFileTests.Worked();
        Note[] notes = [.. OpenedProject.Open(project).Notes.Take(3)];
        NoteValues first = NoteValues.Of(notes[0]);
        NoteValues last = NoteValues.Of(notes[2]);
        SavedStep[] steps =
        [
            new(HandEdit.OperationId, new HandEditParameters(HandEditKind.Change, notes[0].Id, first with { Pitch = new Pitch(first.Pitch.MidiNumber + 1), Velocity = 100 })),
            new(HandEdit.OperationId, new HandEditParameters(HandEditKind.Remove, notes[1].Id, null)),
        ];
        var pending = new SavedStep(HandEdit.OperationId, HandEditParameters.Add("h1", last with { Span = new TickSpan(last.Span.End, last.Span.Duration) }));
        project = project with { Variations = [project.Variations[0], new SavedVariation("v_0002", "m_0002", steps, pending, [KeepProperty.Pitches])] };

        Project reread = ProjectFile.Read(ProjectFile.Write(project));
        OpenedProject opened = OpenedProject.Open(reread);

        Assert.Equal(steps, reread.Variations[1].Steps);
        Assert.Equal(pending, reread.Variations[1].Pending);
        VariationChain chain = opened.Variations[1].Chain;
        Assert.Equal("E7 › E7 › E7", chain.Result!.Operation);
        Assert.Equal(OpenedProject.Open(project).Variations[1].Chain.Result!.Notes, chain.Result.Notes);
        Assert.Equal(KeepStatus.NoLongerKept, chain.Result.Checks.Single().Status);
    }
}