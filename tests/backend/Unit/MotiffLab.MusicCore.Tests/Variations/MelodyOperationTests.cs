using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>V9 Interval reshape and V10 Change direction: melody pitches move, rhythm and accompaniment stay.</summary>
public class MelodyOperationTests
{
    private const long Sixteenth = 120;
    private const long Quarter = 480;

    private static readonly MusicalKey s_cMajor = new(0, KeyMode.Major);
    private static readonly MusicalKey s_aMinor = new(0, KeyMode.Minor);
    private static readonly HashSet<KeepProperty> s_keepNothing = [];

    private static Note Note(string id, int pitch, long onset, long duration) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    private static Note[] Line(long length, params int[] pitches) =>
        [.. pitches.Select((pitch, i) => Note($"n{i + 1}", pitch, i * length, length))];

    /// <summary>Für Elise: E5 D♯5 E5 B4 D5 C5 A4, in sixteenths.</summary>
    private static Note[] FurElise() => Line(Sixteenth, 76, 75, 76, 71, 74, 72, 69);

    private static VariationContext Context(Note[] notes, MusicalKey? key, IReadOnlySet<KeepProperty>? keep = null) =>
        new(TickSpan.FromBounds(Tick.Zero, notes.Max(note => note.Span.End)), keep ?? s_keepNothing, Key: key);

    private static int[] Pitches(Variation variation) => [.. variation.Notes.Select(note => note.Pitch.MidiNumber)];

    [Fact]
    public void Change_direction_mirrors_the_melody_in_scale_steps_around_its_first_note()
    {
        Note[] notes = FurElise();

        Variation variation = ChangeDirection.Instance.Apply(notes, Context(notes, s_aMinor), new ChangeDirectionParameters());

        // D♯ (D raised) one step below E becomes F raised one step above; B, three steps below, becomes A5.
        Assert.Equal([76, 78, 76, 81, 77, 79, 83], Pitches(variation));
        Assert.Equal(notes.Select(note => note.Span), variation.Notes.Select(note => note.Span));
        Assert.Equal("V10", variation.Operation);
    }

    [Fact]
    public void Exact_change_direction_mirrors_in_semitones_and_needs_no_key()
    {
        Note[] notes = FurElise();

        Variation variation = ChangeDirection.Instance.Apply(notes, Context(notes, key: null), new ChangeDirectionParameters(Diatonic: false));

        Assert.Equal([76, 77, 76, 81, 78, 80, 83], Pitches(variation));
    }

    [Fact]
    public void Mirroring_twice_gives_the_original_back()
    {
        Note[] notes = FurElise();
        VariationChain chain = VariationChain.Start(notes, Context(notes, s_aMinor))
            .Propose(ChangeDirection.Instance, new ChangeDirectionParameters()).Accept()
            .Propose(ChangeDirection.Instance, new ChangeDirectionParameters()).Accept();

        Assert.Equal(notes.Select(note => note.Pitch), chain.Current.Select(note => note.Pitch));
    }

    [Fact]
    public void Change_direction_keeps_the_rhythm_and_states_the_new_intervals()
    {
        Note[] notes = FurElise();
        var keep = new HashSet<KeepProperty> { KeepProperty.Rhythm, KeepProperty.Bass };

        Variation variation = ChangeDirection.Instance.Apply(notes, Context(notes, s_aMinor, keep), new ChangeDirectionParameters());

        Assert.Equal([new KeepCheck(KeepProperty.Rhythm, KeepStatus.Kept), new KeepCheck(KeepProperty.Bass, KeepStatus.Kept)], variation.Checks);
        Assert.Equal(KeepStatus.NoLongerKept, variation.Properties.Single(check => check.Property == KeepProperty.Contour).Status);
        Assert.Contains("5 of 7 notes change pitch. The melody's intervals in semitones go from −1 +1 −5 +3 −2 −3 to +2 −2 +5 −4 +2 +4.", variation.Explanation, StringComparison.Ordinal);
        Assert.EndsWith("Unchanged: rhythm.", variation.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void The_keep_table_rules_change_direction_out_unless_only_rhythm_and_bass_are_kept()
    {
        Assert.Null(ChangeDirection.Instance.DisabledReason(new HashSet<KeepProperty> { KeepProperty.Rhythm, KeepProperty.Bass }));
        Assert.Equal(
            "Change direction turns the melody's movement upside down, so it is off while Keep contour and Keep register are on.",
            ChangeDirection.Instance.DisabledReason(new HashSet<KeepProperty> { KeepProperty.Contour, KeepProperty.Register, KeepProperty.Rhythm }));
    }

    [Fact]
    public void Diatonic_operations_need_a_key()
    {
        Note[] notes = FurElise();

        Assert.Equal(
            "Change direction counts in steps of the key's scale, and this selection has no key, so it is not available.",
            ChangeDirection.Instance.UnavailableReason(notes, Context(notes, key: null), new ChangeDirectionParameters()));
        Assert.NotNull(IntervalReshape.Instance.UnavailableReason(notes, Context(notes, key: null), new IntervalReshapeParameters(1)));
    }

    [Fact]
    public void Only_the_melody_moves_and_the_chord_under_it_stays()
    {
        // C5 D5 E5 over a held C3 E4 G4; mirrored in C major around C5: C5 B4 A4, still above G4.
        Note[] notes =
        [
            Note("c3", 48, 0, 3 * Quarter), Note("e4", 64, 0, 3 * Quarter), Note("g4", 67, 0, 3 * Quarter),
            Note("c5", 72, 0, Quarter), Note("d5", 74, Quarter, Quarter), Note("e5", 76, 2 * Quarter, Quarter),
        ];

        Variation variation = ChangeDirection.Instance.Apply(notes, Context(notes, s_cMajor), new ChangeDirectionParameters());

        Assert.Equal([48, 64, 67, 72, 71, 69], variation.Changes.Select(change => change.After.Pitch.MidiNumber));
        Assert.All(variation.Changes.Take(3), change => Assert.False(change.PitchChanged));
    }

    [Fact]
    public void A_melody_note_may_not_sink_under_its_accompaniment()
    {
        // C5 E5 over a held G4 B4: mirrored, E5 becomes A4, under B4.
        Note[] notes =
        [
            Note("g4", 67, 0, 2 * Quarter), Note("b4", 71, 0, 2 * Quarter),
            Note("c5", 72, 0, Quarter), Note("e5", 76, Quarter, Quarter),
        ];

        Assert.Equal(
            "Change direction would put a melody note at or under its accompaniment, so it is not available for this selection.",
            ChangeDirection.Instance.UnavailableReason(notes, Context(notes, s_cMajor), new ChangeDirectionParameters()));
    }

    [Fact]
    public void Melody_operations_need_two_notes_and_some_movement()
    {
        Note[] one = Line(Quarter, 60);
        Note[] repeated = Line(Quarter, 60, 60, 60);

        Assert.Equal(
            "Change direction needs at least two melody notes, so it is not available for this selection.",
            ChangeDirection.Instance.UnavailableReason(one, Context(one, s_cMajor), new ChangeDirectionParameters()));
        Assert.NotNull(ChangeDirection.Instance.UnavailableReason(repeated, Context(repeated, s_cMajor), new ChangeDirectionParameters()));
        Assert.NotNull(IntervalReshape.Instance.UnavailableReason(repeated, Context(repeated, s_cMajor), new IntervalReshapeParameters(1)));
    }

    [Theory]
    [InlineData(1, new[] { 60, 65, 62, 69 })] // +2 −1 +3 steps become +3 −2 +4
    [InlineData(2, new[] { 60, 67, 62, 71 })] // … become +4 −3 +5
    [InlineData(-1, new[] { 60, 62, 60, 64 })] // … become +1 −1 +2: a 2nd stays a 2nd
    public void Interval_reshape_widens_or_narrows_every_interval_and_keeps_the_contour(int steps, int[] expected)
    {
        Note[] notes = Line(Quarter, 60, 64, 62, 67);
        var keep = new HashSet<KeepProperty> { KeepProperty.Contour, KeepProperty.Rhythm };

        Variation variation = IntervalReshape.Instance.Apply(notes, Context(notes, s_cMajor, keep), new IntervalReshapeParameters(steps));

        Assert.Equal(expected, Pitches(variation));
        Assert.All(variation.Checks, check => Assert.Equal(KeepStatus.Kept, check.Status));
    }

    [Fact]
    public void Interval_reshape_keeps_repeated_notes_repeated()
    {
        Note[] notes = Line(Quarter, 60, 60, 64);

        Variation variation = IntervalReshape.Instance.Apply(notes, Context(notes, s_cMajor), new IntervalReshapeParameters(1));

        Assert.Equal([60, 60, 65], Pitches(variation));
    }

    [Fact]
    public void Narrowing_a_melody_of_2nds_only_is_not_available()
    {
        Note[] notes = Line(Quarter, 60, 62, 64, 62);

        Assert.Equal(
            "Interval reshape cannot narrow a melody that moves only by 2nds without turning steps into repeats, so it is not available for this selection.",
            IntervalReshape.Instance.UnavailableReason(notes, Context(notes, s_cMajor), new IntervalReshapeParameters(-1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-3)]
    public void Interval_reshape_parameters_out_of_range_are_refused(int steps)
    {
        Note[] notes = Line(Quarter, 60, 64);

        Assert.Throws<ArgumentOutOfRangeException>(() => IntervalReshape.Instance.Apply(notes, Context(notes, s_cMajor), new IntervalReshapeParameters(steps)));
    }

    [Fact]
    public void The_keep_table_rules_interval_reshape_out_while_keep_pitches_is_on()
    {
        Assert.Null(IntervalReshape.Instance.DisabledReason(new HashSet<KeepProperty> { KeepProperty.Contour, KeepProperty.Rhythm, KeepProperty.Bass }));
        Assert.Equal(
            "Interval reshape changes the size of the melody's intervals, so it is off while Keep pitches is on.",
            IntervalReshape.Instance.DisabledReason(new HashSet<KeepProperty> { KeepProperty.Pitches }));
    }

    [Fact]
    public void Melody_operations_are_deterministic_and_registered()
    {
        Note[] notes = FurElise();
        Variation first = IntervalReshape.Instance.Apply(notes, Context(notes, s_aMinor), new IntervalReshapeParameters(1));
        Variation second = IntervalReshape.Instance.Apply(notes, Context(notes, s_aMinor), new IntervalReshapeParameters(1));

        Assert.Equal(Pitches(first), Pitches(second));
        Assert.Equal(first.Explanation, second.Explanation);
        Assert.Same(IntervalReshape.Instance, VariationOperations.Find("V9"));
        Assert.Same(ChangeDirection.Instance, VariationOperations.Find("V10"));
    }

    [Fact]
    public void Interval_reshape_widens_only_the_picked_intervals()
    {
        Note[] notes = FurElise();

        Variation variation = IntervalReshape.Instance.Apply(notes, Context(notes, s_aMinor), new IntervalReshapeParameters(1, new IntervalSet([2])));

        // Interval 3 (E5 down to B4, a 4th) becomes a 5th down to A4; the notes after it move with it.
        Assert.Equal([76, 75, 76, 69, 72, 71, 67], Pitches(variation));
        Assert.StartsWith("1 of 6 melody intervals is 1 scale step wider in the key's scale, keeping its direction.", variation.Explanation);
    }

    [Fact]
    public void Change_direction_flips_only_the_picked_intervals_and_the_rest_keep_their_size_and_direction()
    {
        Note[] notes = FurElise();

        Variation variation = ChangeDirection.Instance.Apply(notes, Context(notes, key: null), new ChangeDirectionParameters(Diatonic: false, new IntervalSet([2])));

        // E5 down a 4th to B4 becomes E5 up a 4th to A5; the intervals after it stay: +3 −2 −3.
        Assert.Equal([76, 75, 76, 81, 84, 82, 79], Pitches(variation));
        Assert.StartsWith("1 of 6 melody intervals turns the other way, keeping its size in semitones.", variation.Explanation);
    }

    [Fact]
    public void Change_direction_of_every_interval_names_the_axis()
    {
        Note[] notes = FurElise();

        Variation variation = ChangeDirection.Instance.Apply(notes, Context(notes, s_aMinor), new ChangeDirectionParameters());

        Assert.StartsWith("The melody is mirrored around its first note, E5: every interval turns the other way, keeping its size in steps of the key's scale.", variation.Explanation);
    }

    [Fact]
    public void Picking_an_interval_the_melody_does_not_have_is_unavailable()
    {
        Note[] notes = FurElise();

        string? reason = ChangeDirection.Instance.UnavailableReason(notes, Context(notes, s_aMinor), new ChangeDirectionParameters(Intervals: new IntervalSet([6])));

        Assert.Equal("Change direction is asked to change interval 7, and the melody has 6, so it is not available for this selection.", reason);
    }

    [Fact]
    public void Interval_sets_compare_by_value_and_reject_an_empty_pick()
    {
        Assert.Equal(new IntervalReshapeParameters(1, new IntervalSet([2, 0, 2])), new IntervalReshapeParameters(1, new IntervalSet([0, 2])));
        Assert.NotEqual(new IntervalSet([0]), new IntervalSet([1]));
        Assert.Equal("1, 3", new IntervalSet([2, 0]).ToString());
        Assert.Throws<ArgumentException>(() => new IntervalSet([]));
        Assert.Throws<ArgumentException>(() => new IntervalSet([-1]));
    }
}