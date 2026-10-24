using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

public class RhythmReshapeTests
{
    private const long Quarter = 480;
    private const long Eighth = 240;

    private static readonly HashSet<KeepProperty> s_keepPitchOrder = [KeepProperty.Pitches, KeepProperty.PitchOrder];

    private static Note Note(string id, int pitch, long onset, long duration) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    /// <summary>Bar 1 of 4/4: C4 D4 E4 as quarters, then G4 as a quarter; bar 2 holds one more note.</summary>
    private static Note[] Piece() =>
    [
        Note("n1", 60, 0, Quarter),
        Note("n2", 62, Quarter, Quarter),
        Note("n3", 64, 2 * Quarter, Quarter),
        Note("n4", 67, 3 * Quarter, Quarter),
        Note("n5", 72, 4 * Quarter, Quarter),
    ];

    private static MotifSelection FirstBar(Note[] notes) =>
        MotifSelector.Select(notes, MeterMap.Default, new TickSpan(Tick.Zero, 4 * Quarter), SelectionFilter.All);

    private static Variation Apply(RhythmReshapeKind kind, int eighths = 1, IReadOnlySet<KeepProperty>? keep = null)
    {
        Note[] notes = Piece();
        return RhythmReshape.Instance.Apply(notes, FirstBar(notes), keep ?? s_keepPitchOrder, new RhythmReshapeParameters(kind, eighths));
    }

    [Fact]
    public void Half_time_doubles_onsets_from_the_motif_start_and_durations()
    {
        Variation variation = Apply(RhythmReshapeKind.HalfTime);

        Assert.Equal("V1", variation.Operation);
        Assert.Equal(["n1", "n2", "n3", "n4"], variation.Notes.Select(note => note.Id));
        Assert.Equal([0L, 960, 1920, 2880], variation.Notes.Select(note => note.Span.Onset.Value));
        Assert.All(variation.Notes, note => Assert.Equal(2 * Quarter, note.Span.Duration));
        Assert.Equal([60, 62, 64, 67], variation.Notes.Select(note => note.Pitch.MidiNumber));
        Assert.All(variation.Notes, note => Assert.Equal(Provenance.UserEdited, note.Attribution.Provenance));
    }

    [Fact]
    public void Double_time_halves_onsets_and_durations()
    {
        Variation variation = Apply(RhythmReshapeKind.DoubleTime);

        Assert.Equal([0L, 240, 480, 720], variation.Notes.Select(note => note.Span.Onset.Value));
        Assert.All(variation.Notes, note => Assert.Equal(Eighth, note.Span.Duration));
    }

    [Fact]
    public void Displace_moves_every_note_by_the_eighths_and_keeps_lengths()
    {
        Variation variation = Apply(RhythmReshapeKind.Displace, eighths: 3);

        Assert.Equal([720L, 1200, 1680, 2160], variation.Notes.Select(note => note.Span.Onset.Value));
        Assert.All(variation.Notes, note => Assert.Equal(Quarter, note.Span.Duration));
    }

    [Fact]
    public void Notes_outside_the_selection_are_left_out()
    {
        Variation variation = Apply(RhythmReshapeKind.HalfTime);

        Assert.DoesNotContain(variation.Notes, note => note.Id == "n5");
        Assert.Equal(4, variation.Changes.Count);
    }

    [Fact]
    public void The_same_input_gives_the_same_variation()
    {
        Variation first = Apply(RhythmReshapeKind.Displace, eighths: 2);
        Variation second = Apply(RhythmReshapeKind.Displace, eighths: 2);

        Assert.Equal(first.Notes, second.Notes);
        Assert.Equal(first.Changes, second.Changes);
        Assert.Equal(first.Explanation, second.Explanation);
    }

    public static TheoryData<RhythmReshapeKind, int> EveryParameter => new()
    {
        { RhythmReshapeKind.Syncopate, 1 },
        { RhythmReshapeKind.Dotted, 1 },
        { RhythmReshapeKind.HalfTime, 1 },
        { RhythmReshapeKind.DoubleTime, 1 },
        { RhythmReshapeKind.Displace, 1 },
        { RhythmReshapeKind.Displace, 2 },
        { RhythmReshapeKind.Displace, 3 },
    };

    [Theory]
    [MemberData(nameof(EveryParameter))]
    public void Every_parameter_is_deterministic_and_leaves_the_input_as_it_was(RhythmReshapeKind kind, int eighths)
    {
        Note[] notes = Piece();
        var context = new VariationContext(FirstBar(notes).Span, s_keepPitchOrder, Seed: 7);
        IReadOnlyList<Note> motif = VariationFacts.MotifNotes(notes, FirstBar(notes));
        var parameters = new RhythmReshapeParameters(kind, eighths);

        Variation first = RhythmReshape.Instance.Apply(motif, context, parameters);
        Variation second = RhythmReshape.Instance.Apply(motif, context with { Seed = 7 }, parameters);

        Assert.Equal(first.Notes, second.Notes);
        Assert.Equal(first.Changes, second.Changes);
        Assert.Equal(first.Checks, second.Checks);
        Assert.Equal(first.Explanation, second.Explanation);
        Assert.Empty(first.Added);
        Assert.Empty(first.Removed);
        Assert.Equal(Piece(), notes);
    }

    [Fact]
    public void The_operation_is_registered_by_its_catalogue_id()
    {
        Assert.Same(RhythmReshape.Instance, VariationOperations.Find("V1"));
        Assert.Null(VariationOperations.Find("V99"));
    }

    [Fact]
    public void Keep_rhythm_disables_the_operation_with_a_reason()
    {
        Assert.Contains("Keep rhythm", RhythmReshape.Instance.DisabledReason(new HashSet<KeepProperty> { KeepProperty.Rhythm }));
        Assert.Null(RhythmReshape.Instance.DisabledReason(s_keepPitchOrder));
        Assert.Throws<InvalidOperationException>(() => Apply(RhythmReshapeKind.HalfTime, keep: new HashSet<KeepProperty> { KeepProperty.Rhythm }));
    }

    [Fact]
    public void Displace_rejects_more_than_three_eighths()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Apply(RhythmReshapeKind.Displace, eighths: 4));
    }

    [Fact]
    public void Double_time_is_unavailable_when_a_note_would_fall_between_ticks()
    {
        Note[] notes = [Note("n1", 60, 0, 3), Note("n2", 62, 3, 477)];
        MotifSelection selection = FirstBar(notes);

        Assert.NotNull(RhythmReshape.Instance.UnavailableReason(notes, VariationContext.For(selection, s_keepPitchOrder), new RhythmReshapeParameters(RhythmReshapeKind.DoubleTime)));
        Assert.Throws<InvalidOperationException>(() =>
            RhythmReshape.Instance.Apply(notes, selection, s_keepPitchOrder, new RhythmReshapeParameters(RhythmReshapeKind.DoubleTime)));
    }

    [Fact]
    public void Kept_properties_are_checked_against_the_diff()
    {
        var keep = new HashSet<KeepProperty>
        {
            KeepProperty.Pitches, KeepProperty.PitchOrder, KeepProperty.Contour, KeepProperty.Intervals, KeepProperty.Register, KeepProperty.Harmony,
        };

        Variation variation = Apply(RhythmReshapeKind.HalfTime, keep: keep);

        Assert.Equal(
            [
                new KeepCheck(KeepProperty.Pitches, KeepStatus.Kept),
                new KeepCheck(KeepProperty.PitchOrder, KeepStatus.Kept),
                new KeepCheck(KeepProperty.Contour, KeepStatus.Kept),
                new KeepCheck(KeepProperty.Intervals, KeepStatus.Kept),
                new KeepCheck(KeepProperty.Harmony, KeepStatus.NotChecked),
                new KeepCheck(KeepProperty.Register, KeepStatus.Kept),
            ],
            variation.Checks);
    }

    [Fact]
    public void Half_time_explanation_states_the_diff()
    {
        Assert.Equal(
            "The pitch sequence is unchanged. All 4 notes last twice as long. The time between onsets doubles, so 3 of 4 notes move. "
            + "From its first onset to its last note's end, the motif now takes 8 quarter notes instead of 4 quarter notes."
            + " Unchanged: pitches, pitch order, contour, intervals, melody pitches and register.",
            Apply(RhythmReshapeKind.HalfTime).Explanation);
    }

    [Fact]
    public void Double_time_explanation_states_the_diff()
    {
        Assert.Equal(
            "The pitch sequence is unchanged. All 4 notes last half as long. The time between onsets halves, so 3 of 4 notes move. "
            + "From its first onset to its last note's end, the motif now takes 2 quarter notes instead of 4 quarter notes."
            + " Unchanged: pitches, pitch order, contour, intervals, melody pitches and register.",
            Apply(RhythmReshapeKind.DoubleTime).Explanation);
    }

    [Fact]
    public void Displace_explanation_states_the_diff()
    {
        Assert.Equal(
            "The pitch sequence is unchanged. Every note keeps its length. All 4 notes start 1 eighth later."
            + " Unchanged: pitches, pitch order, contour, intervals, melody pitches and register.",
            Apply(RhythmReshapeKind.Displace).Explanation);
    }

    [Fact]
    public void Syncopate_moves_each_beat_after_the_first_half_a_beat_earlier_and_keeps_where_it_ends()
    {
        Variation variation = Apply(RhythmReshapeKind.Syncopate);

        Assert.Equal([0L, 240, 720, 1200], variation.Notes.Select(note => note.Span.Onset.Value));
        Assert.Equal([240L, 720, 1200, 1920], variation.Notes.Select(note => note.Span.End.Value));
        Assert.Equal([60, 62, 64, 67], variation.Notes.Select(note => note.Pitch.MidiNumber));
    }

    /// <summary>AC-S1-11: V1 Syncopate on a melody with Keep pitches on keeps the pitch sequence, and the explanation's counts and beats match the diff.</summary>
    [Fact]
    public void Syncopate_on_a_melody_keeps_the_pitch_sequence_and_its_explanation_matches_the_diff()
    {
        Variation variation = Apply(RhythmReshapeKind.Syncopate, keep: new HashSet<KeepProperty> { KeepProperty.Pitches });

        Assert.Equal([new KeepCheck(KeepProperty.Pitches, KeepStatus.Kept)], variation.Checks);
        Assert.DoesNotContain(variation.Changes, change => change.PitchChanged);
        NoteChange[] moved = [.. variation.Changes.Where(change => change.OnsetChanged)];
        Assert.Equal([480L, 960, 1440], moved.Select(change => change.Before.Span.Onset.Value));
        Assert.All(moved, change => Assert.Equal(Eighth, change.Before.Span.Onset.Value - change.After.Span.Onset.Value));
        Assert.Equal(2, variation.Changes.Count(change => change.DurationChanged));
        Assert.Equal(
            "3 notes that started on a beat now start 1 eighth ahead of it: beats 2, 3 and 4 of bar 1. The pitch sequence is unchanged."
            + " 2 of 4 notes change length. 3 of 4 notes start at a new place."
            + " Unchanged: pitches, pitch order, contour, intervals, melody pitches and register.",
            variation.Explanation);
    }

    [Fact]
    public void Syncopate_counts_beats_in_the_meter_and_skips_a_beat_with_a_note_just_before_it()
    {
        // 3/8: a beat is an eighth, so notes move a sixteenth. Beat 2 has a note a sixteenth before it.
        var meter = new MeterMap([new MeterChange(Tick.Zero, new TimeSignature(3, 8))]);
        Note[] notes = [Note("n1", 72, 0, 120), Note("n2", 74, 120, 120), Note("n3", 76, 240, 240), Note("n4", 77, 480, 240)];
        var context = new VariationContext(new TickSpan(Tick.Zero, 720), s_keepPitchOrder, Meter: meter);

        Variation variation = RhythmReshape.Instance.Apply(notes, context, new RhythmReshapeParameters(RhythmReshapeKind.Syncopate));

        Assert.Equal([0L, 120, 240, 360], variation.Notes.Select(note => note.Span.Onset.Value));
        Assert.Equal([120L, 240, 360, 720], variation.Notes.Select(note => note.Span.End.Value));
        Assert.StartsWith("1 note that started on a beat now starts 1 sixteenth ahead of it: beat 3 of bar 1.", variation.Explanation);
    }

    [Fact]
    public void Syncopate_is_unavailable_when_every_beat_has_a_note_just_before_it()
    {
        Note[] notes = [.. Enumerable.Range(0, 8).Select(i => Note($"n{i}", 60 + i, i * Eighth, Eighth))];

        Assert.Contains("Syncopate needs", RhythmReshape.Instance.UnavailableReason(notes, VariationContext.For(FirstBar(notes), s_keepPitchOrder), new RhythmReshapeParameters(RhythmReshapeKind.Syncopate)));
    }

    [Fact]
    public void Dotted_turns_pairs_of_equal_notes_on_the_beat_into_long_short()
    {
        Variation variation = Apply(RhythmReshapeKind.Dotted);

        Assert.Equal([0L, 720, 960, 1680], variation.Notes.Select(note => note.Span.Onset.Value));
        Assert.Equal([720L, 240, 720, 240], variation.Notes.Select(note => note.Span.Duration));
        Assert.StartsWith("2 pairs of equal notes become long–short, 3 eighths then 1 eighth, starting on beats 1 and 3 of bar 1.", variation.Explanation);
    }

    [Fact]
    public void Dotted_leaves_notes_that_do_not_pair_and_is_unavailable_without_a_pair()
    {
        // A dotted quarter and an eighth already, then two eighths on beat 3, then a quarter.
        Note[] notes = [Note("n1", 60, 0, 720), Note("n2", 62, 720, Eighth), Note("n3", 64, 960, Eighth), Note("n4", 65, 1200, Eighth), Note("n5", 67, 1440, Quarter)];
        Variation variation = RhythmReshape.Instance.Apply(notes, FirstBar(notes), s_keepPitchOrder, new RhythmReshapeParameters(RhythmReshapeKind.Dotted));

        Assert.Equal([0L, 720, 960, 1320, 1440], variation.Notes.Select(note => note.Span.Onset.Value));
        Assert.Equal(["n3", "n4"], variation.Changes.Where(change => change.OnsetChanged || change.DurationChanged).Select(change => change.Before.Id));

        Note[] lone = [Note("n1", 60, 0, 720), Note("n2", 62, 720, Eighth)];
        Assert.Contains("Dotted needs", RhythmReshape.Instance.UnavailableReason(lone, VariationContext.For(FirstBar(lone), s_keepPitchOrder), new RhythmReshapeParameters(RhythmReshapeKind.Dotted)));
    }

    [Theory]
    [InlineData(480, "1 quarter note")]
    [InlineData(1440, "3 quarter notes")]
    [InlineData(240, "1 eighth")]
    [InlineData(720 + 240, "2 quarter notes")]
    [InlineData(360, "3 sixteenths")]
    [InlineData(7, "7 ticks")]
    public void Lengths_are_named_in_the_largest_whole_unit(long ticks, string expected) =>
        Assert.Equal(expected, VariationFacts.Length(ticks));
}