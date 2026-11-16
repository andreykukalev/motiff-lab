using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>V13 Articulation: the same notes played short, detached or connected, onsets kept (Explore experiment A).</summary>
public class ArticulationTests
{
    private const long Quarter = 480;
    private const long Eighth = 240;
    private const long Sixteenth = 120;
    private const long ThirtySecond = 60;

    private static readonly HashSet<KeepProperty> s_keepPitches = [KeepProperty.Pitches, KeepProperty.PitchOrder];

    private static Note Note(string id, int pitch, long onset, long duration, Hand? hand = null) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Hand = hand is { } value ? new Attributed<Hand>(value, Attribution.Imported) : null,
        Attribution = Attribution.Imported,
    };

    /// <summary>Ode to Joy, bar 1: E4 E4 F4 G4 as quarters.</summary>
    private static Note[] Ode() =>
    [
        Note("n1", 64, 0, Quarter),
        Note("n2", 64, Quarter, Quarter),
        Note("n3", 65, 2 * Quarter, Quarter),
        Note("n4", 67, 3 * Quarter, Quarter),
    ];

    /// <summary>
    /// Für Elise, bar 2 in 3/8 from tick 0: the melody's A4 as an eighth over the left hand's A2 E3 A3
    /// rising in sixteenths and held to the end of the bar.
    /// </summary>
    private static Note[] FurEliseBar2() =>
    [
        Note("m1", 69, 0, Eighth, Hand.Right),
        Note("l1", 45, 0, 6 * Sixteenth, Hand.Left),
        Note("l2", 52, Sixteenth, 4 * Sixteenth, Hand.Left),
        Note("l3", 57, 2 * Sixteenth, 4 * Sixteenth, Hand.Left),
    ];

    /// <summary>A selection from tick 0 to <paramref name="end"/>, or to the end of the last note.</summary>
    private static VariationContext Context(Note[] notes, long? end = null) =>
        new(TickSpan.FromBounds(Tick.Zero, new Tick(end ?? notes.Max(note => note.Span.End.Value))), s_keepPitches);

    private static Variation Apply(Note[] notes, ArticulationKind kind, OperationScope scope = OperationScope.All, long? end = null) =>
        Articulation.Instance.Apply(notes, Context(notes, end), new ArticulationParameters(kind) { Scope = scope });

    [Fact]
    public void Detached_halves_each_quarter_and_keeps_pitches_and_onsets()
    {
        Variation variation = Apply(Ode(), ArticulationKind.Detached, end: 4 * Quarter);

        Assert.Equal("V13", variation.Operation);
        Assert.Equal([0L, 480, 960, 1440], variation.Notes.Select(note => note.Span.Onset.Value));
        Assert.All(variation.Notes, note => Assert.Equal(Eighth, note.Span.Duration));
        Assert.Equal([64, 64, 65, 67], variation.Notes.Select(note => note.Pitch.MidiNumber));
        Assert.All(variation.Notes, note => Assert.Equal(Provenance.UserEdited, note.Attribution.Provenance));
        Assert.StartsWith("Only note lengths change: 4 notes sound shorter.", variation.Explanation);
        Assert.Contains("Every note starts where it did.", variation.Explanation);
    }

    [Fact]
    public void Short_keeps_a_quarter_of_the_time_to_the_next_onset()
    {
        Variation variation = Apply(Ode(), ArticulationKind.Short, end: 4 * Quarter);

        Assert.All(variation.Notes, note => Assert.Equal(Sixteenth, note.Span.Duration));
    }

    [Fact]
    public void Sixteenths_end_on_the_grid_so_short_and_detached_both_give_a_32nd()
    {
        Note[] line = [Note("a", 76, 0, Sixteenth), Note("b", 75, Sixteenth, Sixteenth), Note("c", 76, 2 * Sixteenth, Sixteenth)];

        Assert.All(Apply(line, ArticulationKind.Detached).Notes, note => Assert.Equal(ThirtySecond, note.Span.Duration));
        Assert.All(Apply(line, ArticulationKind.Short).Notes, note => Assert.Equal(ThirtySecond, note.Span.Duration));
    }

    [Fact]
    public void Connected_holds_each_note_until_the_next_onset_and_the_last_to_the_end_of_the_selection()
    {
        Note[] line = [Note("a", 60, 0, Eighth), Note("b", 62, Quarter, Sixteenth), Note("c", 64, 2 * Quarter, Eighth)];

        Variation variation = Apply(line, ArticulationKind.Connected, end: 3 * Quarter);

        Assert.Equal([Quarter, Quarter, Quarter], variation.Notes.Select(note => note.Span.Duration));
        Assert.StartsWith("Only note lengths change: 3 notes sound longer.", variation.Explanation);
    }

    [Fact]
    public void The_last_onset_is_measured_to_the_end_of_the_selection_even_when_its_note_runs_past()
    {
        Note[] line = [Note("a", 60, 0, Quarter), Note("b", 62, Quarter, 2 * Quarter)];

        Variation variation = Apply(line, ArticulationKind.Detached, end: 2 * Quarter);

        Assert.Equal([Eighth, Eighth], variation.Notes.Select(note => note.Span.Duration));
    }

    [Fact]
    public void Notes_struck_together_share_the_time_to_the_next_onset()
    {
        Note[] chords = [Note("a", 60, 0, Quarter), Note("b", 64, 0, Quarter), Note("c", 67, Quarter, Quarter)];

        Variation variation = Apply(chords, ArticulationKind.Detached);

        Assert.Equal([Eighth, Eighth, Eighth], variation.Notes.Select(note => note.Span.Duration));
    }

    [Fact]
    public void Limited_to_the_melody_it_leaves_the_held_accompaniment_ringing()
    {
        Note[] notes = [Note("m0", 76, 0, Eighth, Hand.Right), .. FurEliseBar2().Select(note => note with { Span = new TickSpan(note.Span.Onset.Offset(Eighth), note.Span.Duration) })];

        Variation variation = Apply(notes, ArticulationKind.Detached, OperationScope.Melody, end: Eighth + (6 * Sixteenth));

        Assert.Equal(Sixteenth, variation.Notes.Single(note => note.Id == "m0").Span.Duration);
        Assert.Equal(Eighth, variation.Notes.Single(note => note.Id == "m1").Span.Duration);
        Assert.Equal(
            notes.Where(note => note.Hand?.Value == Hand.Left).Select(note => note.Span),
            variation.Notes.Where(note => note.Hand?.Value == Hand.Left).Select(note => note.Span));
    }

    [Fact]
    public void Across_the_whole_selection_the_held_bass_is_shortened_too()
    {
        Variation variation = Apply(FurEliseBar2(), ArticulationKind.Detached);

        Assert.Equal(ThirtySecond, variation.Notes.Single(note => note.Id == "l1").Span.Duration);
    }

    [Fact]
    public void Notes_off_the_grid_get_exactly_their_share()
    {
        Note[] played = [Note("a", 60, 7, 470), Note("b", 62, 481, 455)];

        Variation variation = Apply(played, ArticulationKind.Detached);

        Assert.Equal([237L, 227], variation.Notes.Select(note => note.Span.Duration));
    }

    [Fact]
    public void Nothing_to_change_is_unavailable_with_a_reason()
    {
        Note[] notes = Ode();

        Assert.Equal(
            "Every note already holds until the next one starts, so Connected has nothing to change.",
            Articulation.Instance.UnavailableReason(notes, Context(notes, 4 * Quarter), new ArticulationParameters(ArticulationKind.Connected)));

        Note[] detached = [.. Apply(notes, ArticulationKind.Detached, end: 4 * Quarter).Notes];
        Assert.Equal(
            "Every note is already this short, so Detached has nothing to change.",
            Articulation.Instance.UnavailableReason(detached, Context(detached, 4 * Quarter), new ArticulationParameters(ArticulationKind.Detached)));
    }

    [Fact]
    public void Keep_rhythm_rules_it_out_because_lengths_change()
    {
        Assert.Equal(
            "Articulation changes how long notes sound, so it is off while Keep rhythm is on.",
            Articulation.Instance.DisabledReason(new HashSet<KeepProperty> { KeepProperty.Rhythm }));
    }

    [Fact]
    public void The_same_notes_and_kind_give_the_same_variation()
    {
        Variation first = Apply(FurEliseBar2(), ArticulationKind.Short, OperationScope.Accompaniment);
        Variation second = Apply(FurEliseBar2(), ArticulationKind.Short, OperationScope.Accompaniment);

        Assert.Equal(first.Notes, second.Notes);
        Assert.Equal(first.Explanation, second.Explanation);
    }
}