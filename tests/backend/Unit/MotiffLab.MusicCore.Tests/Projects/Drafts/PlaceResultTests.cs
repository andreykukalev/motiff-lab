using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Projects.Drafts;

/// <summary>A place's result: its base with the journal's steps that name it, deterministic, a lost step not applied (decision record 0034, section 4).</summary>
public class PlaceResultTests
{
    /// <summary>A place of bars 1–4 of the notes, with a variant of the recording that plays bar 2 an octave higher.</summary>
    private static DraftPlace Place()
    {
        DraftVariant notes = Variant("v_1", Score(), 1, 4);
        DraftVariant heard = Variant("v_2", Score(melody: bar => bar == 1 ? [84, 86, 88, 89] : [72, 74, 76, 77], prefix: "h"), 1, 4);
        return new DraftPlace("pl_1", "v_1", [notes, heard]);
    }

    private static DraftTake Octave(int number = 1) => new(number, At, "pl_1", "v_2")
    {
        Technique = new DraftTechnique(TechniqueKind.Octave, [2]),
        Removed = ["v_1.n1r0", "v_1.n1r1", "v_1.n1r2", "v_1.n1r3"],
        Added = ["v_2.h1r0", "v_2.h1r1", "v_2.h1r2", "v_2.h1r3"],
    };

    [Fact]
    public void The_result_with_no_steps_is_the_base_its_notes_named_by_the_variant()
    {
        PlaceResult result = PlaceResult.Build(Place(), []);

        Assert.Equal(20, result.Music.Notes.Count);
        Assert.All(result.Music.Notes, note => Assert.StartsWith("v_1.", note.Id, StringComparison.Ordinal));
        Assert.Equal(4, result.Music.Bars.Count);
        Assert.Empty(result.Origins);
    }

    [Fact]
    public void A_take_removes_the_base_notes_it_names_and_puts_in_the_variants_where_the_alignment_puts_them()
    {
        PlaceResult result = PlaceResult.Build(Place(), [Octave()]);

        Assert.True(result.OutcomeOf(1)!.Applied);
        Assert.DoesNotContain(result.Music.Notes, note => note.Id == "v_1.n1r0");
        Note taken = Assert.Single(result.Music.Notes, note => note.Id == "v_2.h1r0");
        Assert.Equal((84, new Tick(BarLength)), (taken.Pitch.MidiNumber, taken.Span.Onset));
        Assert.Equal("v_2", result.Origins["v_2.h1r0"]);
        Assert.Equal(20, result.Music.Notes.Count);
    }

    [Fact]
    public void A_step_whose_notes_are_gone_is_not_applied_and_names_what_it_lost()
    {
        DraftTake again = Octave(2);

        PlaceResult result = PlaceResult.Build(Place(), [Octave(), again]);

        DraftStepOutcome outcome = result.OutcomeOf(2)!;
        Assert.False(outcome.Applied);
        Assert.Equal("v_1.n1r0", outcome.Lost);
        Assert.Equal(PlaceResult.Build(Place(), [Octave()]).Music.Notes, result.Music.Notes);
    }

    [Fact]
    public void Inserted_bars_go_in_whole_in_their_own_meter_and_the_music_after_them_moves_on()
    {
        DraftTake insert = new(1, At, "pl_1", "v_2") { Technique = new DraftTechnique(TechniqueKind.InsertBars, [3]), Insert = new DraftInsert(new Tick(2 * BarLength), new TickSpan(new Tick(BarLength), BarLength)) };

        PlaceResult result = PlaceResult.Build(Place(), [insert]);

        Assert.Equal(5, result.Music.Bars.Count);
        Assert.Equal(new Tick(2 * BarLength), result.Music.Notes.Single(note => note.Id == "v_2.h1r0").Span.Onset);
        Assert.Equal(new Tick(3 * BarLength), result.Music.Notes.Single(note => note.Id == "v_1.n2r0").Span.Onset);
        Assert.Equal(new Tick(BarLength), result.Music.Notes.Single(note => note.Id == "v_1.n1r0").Span.Onset);
    }

    [Fact]
    public void A_take_after_inserted_bars_lands_where_the_base_notes_it_replaces_moved_to()
    {
        DraftTake insert = new(1, At, "pl_1", "v_2") { Technique = new DraftTechnique(TechniqueKind.InsertBars, [2]), Insert = new DraftInsert(new Tick(BarLength), new TickSpan(Tick.Zero, BarLength)) };

        PlaceResult result = PlaceResult.Build(Place(), [insert, Octave(2)]);

        Assert.True(result.OutcomeOf(2)!.Applied);
        Assert.Equal(new Tick(2 * BarLength), result.Music.Notes.Single(note => note.Id == "v_2.h1r0").Span.Onset);
        Assert.Equal(new Tick(BarLength), result.Music.Notes.Single(note => note.Id == "v_2.h0r0").Span.Onset);
        Assert.Equal(new Tick(2 * BarLength), result.Music.Notes.Single(note => note.Id == "v_1.n1l").Span.Onset);
    }

    [Fact]
    public void A_transposition_and_a_tempo_change_the_whole_place_and_a_leave_changes_nothing()
    {
        PlaceResult result = PlaceResult.Build(Place(), [new DraftTranspose(2, At, "pl_1", -2), new DraftTempo(3, At, "pl_1", 60), new DraftLeave(1, At, "pl_1") { Seam = true }]);

        Assert.All(result.Steps, step => Assert.True(step.Applied));
        Assert.Equal([1, 2, 3], result.Steps.Select(step => step.Number));
        Assert.Equal(70, result.Music.Notes.Single(note => note.Id == "v_1.n0r0").Pitch.MidiNumber);
        Assert.Equal(60, result.Music.TempoAt(Tick.Zero));
    }

    [Fact]
    public void The_same_place_and_steps_always_give_the_same_result()
    {
        PlaceResult one = PlaceResult.Build(Place(), [Octave(), new DraftTranspose(2, At, "pl_1", 1)]);
        PlaceResult two = PlaceResult.Build(Place(), [new DraftTranspose(2, At, "pl_1", 1), Octave()]);

        Assert.Equal(one.Music.Notes, two.Music.Notes);
        Assert.Equal(one.Music.Bars, two.Music.Bars);
        Assert.Equal(one.Steps, two.Steps);
    }
}