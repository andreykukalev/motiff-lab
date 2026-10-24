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