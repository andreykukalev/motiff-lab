using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>V4 Melodic sequence: the motif repeats from another scale step, keeping its rhythm and interval shape.</summary>
public class MelodicSequenceTests
{
    private const long Sixteenth = 120;
    private const long Quarter = 480;

    private static readonly MusicalKey s_cMajor = new(0, KeyMode.Major);
    private static readonly MusicalKey s_aMinor = new(0, KeyMode.Minor);
    private static readonly HashSet<KeepProperty> s_keepShape = [KeepProperty.Contour, KeepProperty.Intervals, KeepProperty.Rhythm];

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

    /// <summary>C4 D4 E4 in quarters.</summary>
    private static Note[] Rising() => Line(Quarter, 60, 62, 64);

    private static VariationContext Context(Note[] notes, MusicalKey? key, IReadOnlySet<KeepProperty>? keep = null, long? span = null) =>
        new(new TickSpan(Tick.Zero, span ?? notes.Max(note => note.Span.End.Value)), keep ?? s_keepShape, Key: key);

    private static int[] Pitches(IEnumerable<Note> notes) => [.. notes.Select(note => note.Pitch.MidiNumber)];

    [Fact]
    public void A_diatonic_sequence_repeats_the_motif_a_scale_step_higher_each_time()
    {
        Note[] notes = Rising();

        Variation variation = MelodicSequence.Instance.Apply(notes, Context(notes, s_cMajor), new MelodicSequenceParameters(Steps: 1, Repeats: 2));

        // C D E, then D E F, then E F G: the 2nds stay 2nds in the scale, whole or half tones as the key gives them.
        Assert.Equal([60, 62, 64, 62, 64, 65, 64, 65, 67], Pitches(variation.Notes));
        Assert.Equal(Enumerable.Range(0, 9).Select(i => new TickSpan(new Tick(i * Quarter), Quarter)), variation.Notes.Select(note => note.Span));
        Assert.Equal(6, variation.Added.Count);
        Assert.Empty(variation.Removed);
        Assert.All(variation.Changes, change => Assert.Equal(change.Before, change.After));
        Assert.All(variation.Added, note => Assert.Equal(Attribution.UserEdited, note.Attribution));
        Assert.Equal("V4", variation.Operation);
    }

    [Fact]
    public void A_diatonic_sequence_keeps_rhythm_contour_and_interval_sizes_in_scale_steps()
    {
        Note[] notes = Rising();

        Variation variation = MelodicSequence.Instance.Apply(notes, Context(notes, s_cMajor), new MelodicSequenceParameters(Steps: 1, Repeats: 2));

        Assert.Equal(
            [new KeepCheck(KeepProperty.Contour, KeepStatus.Kept), new KeepCheck(KeepProperty.Intervals, KeepStatus.Kept), new KeepCheck(KeepProperty.Rhythm, KeepStatus.Kept)],
            variation.Checks);
        Assert.Equal(
            "6 notes are added. The motif repeats 2 times, each copy a 2nd higher in the key's scale than the one before, keeping its rhythm. "
            + "The pitch sequence is unchanged. Every note keeps its length. Every note starts where it did. "
            + "From its first onset to its last note's end, the motif now takes 9 quarter notes instead of 3 quarter notes. "
            + "Unchanged: contour, intervals and rhythm.",
            variation.Explanation);
    }

    [Fact]
    public void Interval_sizes_in_semitones_alone_do_not_survive_a_diatonic_sequence()
    {
        // Footnote 2: without the key, D E F is a whole tone then a half tone, so C D E's intervals are not kept.
        Note[] notes = Rising();
        Variation variation = MelodicSequence.Instance.Apply(notes, Context(notes, s_cMajor), new MelodicSequenceParameters());

        Variation withoutKey = VariationFacts.Build("V4", new TickSpan(Tick.Zero, 3 * Quarter), notes, variation.Notes, s_keepShape, []);

        Assert.Equal(KeepStatus.NoLongerKept, withoutKey.Checks.Single(check => check.Property == KeepProperty.Intervals).Status);
        Assert.Equal(KeepStatus.Kept, withoutKey.Checks.Single(check => check.Property == KeepProperty.Contour).Status);
    }

    [Fact]
    public void An_exact_sequence_moves_every_copy_by_the_same_semitones()
    {
        Note[] notes = Rising();

        Variation variation = MelodicSequence.Instance.Apply(notes, Context(notes, s_cMajor), new MelodicSequenceParameters(Steps: 1, Repeats: 2, Diatonic: false));

        // A step above C is D, two semitones; every copy moves two more, so F♯ and G♯ leave the key.
        Assert.Equal([60, 62, 64, 62, 64, 66, 64, 66, 68], Pitches(variation.Notes));
        Assert.All(variation.Checks, check => Assert.Equal(KeepStatus.Kept, check.Status));
        Assert.Contains("The motif repeats 2 times, each copy a major 2nd higher than the one before, keeping its rhythm.", variation.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sequence_can_move_down_and_keeps_chromatic_notes_chromatic()
    {
        // E5 D♯5 E5 in A minor, a step lower: D♯ is D raised, so it becomes C raised, C♯.
        Note[] notes = Line(Sixteenth, 76, 75, 76);

        Variation variation = MelodicSequence.Instance.Apply(notes, Context(notes, s_aMinor), new MelodicSequenceParameters(Steps: -1));

        Assert.Equal([76, 75, 76, 74, 73, 74], Pitches(variation.Notes));
        Assert.Contains("The motif repeats once, each copy a major 2nd lower than the one before, keeping its rhythm.", variation.Explanation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(4, new[] { 71, 72, 74, 77, 79, 81 }, "a 5th higher in the key's scale")]
    [InlineData(-4, new[] { 60, 62, 64, 53, 55, 57 }, "a perfect 5th lower")]
    public void A_sequence_moves_up_to_a_5th(int steps, int[] expected, string distance)
    {
        // From B C D, a 5th up in the scale is F G A: a diminished 5th, then two perfect 5ths.
        // From C D E, a 5th down is F G A, every note a perfect 5th lower, so the explanation says so exactly.
        Note[] notes = Line(Quarter, steps > 0 ? [71, 72, 74] : [60, 62, 64]);

        Variation variation = MelodicSequence.Instance.Apply(notes, Context(notes, s_cMajor), new MelodicSequenceParameters(steps));

        Assert.Equal(expected, Pitches(variation.Notes));
        Assert.Contains($"each copy {distance} than the one before", variation.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Copies_follow_each_other_a_whole_span_apart()
    {
        // Three quarters selected as a bar of 4/4: the copy starts on the next downbeat, keeping the rest.
        Note[] notes = Rising();

        Variation variation = MelodicSequence.Instance.Apply(notes, Context(notes, s_cMajor, span: 4 * Quarter), new MelodicSequenceParameters());

        Assert.Equal([4 * Quarter, 5 * Quarter, 6 * Quarter], variation.Added.Select(note => note.Span.Onset.Value));
        Assert.Equal(KeepStatus.Kept, variation.Checks.Single(check => check.Property == KeepProperty.Rhythm).Status);
    }

    [Fact]
    public void The_accompaniment_moves_with_the_melody()
    {
        // C5 D5 E5 over a held C3 E4 G4, a step higher in C major: D3 F4 A4 under D5 E5 F5.
        Note[] notes =
        [
            Note("c3", 48, 0, 3 * Quarter), Note("e4", 64, 0, 3 * Quarter), Note("g4", 67, 0, 3 * Quarter),
            Note("c5", 72, 0, Quarter), Note("d5", 74, Quarter, Quarter), Note("e5", 76, 2 * Quarter, Quarter),
        ];

        Variation variation = MelodicSequence.Instance.Apply(notes, Context(notes, s_cMajor), new MelodicSequenceParameters());

        Assert.Equal([50, 65, 69, 74, 76, 77], Pitches(variation.Added));
        Assert.Equal(["c3~1", "e4~1", "g4~1", "c5~1", "d5~1", "e5~1"], variation.Added.Select(note => note.Id));
        Assert.All(variation.Checks, check => Assert.Equal(KeepStatus.Kept, check.Status));
    }

    [Fact]
    public void A_sequence_that_leaves_the_piano_is_not_available()
    {
        Note[] notes = Line(Quarter, 105, 107);

        Assert.Equal(
            "Melodic sequence would put a note outside the piano's range (A0–C8), so it is not available for this selection.",
            MelodicSequence.Instance.UnavailableReason(notes, Context(notes, s_cMajor), new MelodicSequenceParameters(Steps: 2)));
    }

    [Fact]
    public void A_sequence_needs_a_key_even_when_exact()
    {
        Note[] notes = Rising();

        Assert.Equal(
            "Melodic sequence counts in steps of the key's scale, and this selection has no key, so it is not available.",
            MelodicSequence.Instance.UnavailableReason(notes, Context(notes, key: null), new MelodicSequenceParameters(Diatonic: false)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 4)]
    public void Parameters_out_of_range_are_refused(int steps, int repeats)
    {
        Note[] notes = Rising();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MelodicSequence.Instance.Apply(notes, Context(notes, s_cMajor), new MelodicSequenceParameters(steps, repeats)));
    }

    [Fact]
    public void The_keep_table_allows_contour_intervals_and_rhythm_only()
    {
        Assert.Null(MelodicSequence.Instance.DisabledReason(s_keepShape));
        Assert.Equal(
            "Melodic sequence repeats the motif at another pitch level, so it is off while Keep pitches and Keep register are on.",
            MelodicSequence.Instance.DisabledReason(new HashSet<KeepProperty> { KeepProperty.Pitches, KeepProperty.Register, KeepProperty.Rhythm }));
    }

    [Fact]
    public void Chained_sequences_keep_ids_unique_and_undo_back_to_the_original()
    {
        Note[] notes = Rising();
        VariationChain chain = VariationChain.Start(notes, Context(notes, s_cMajor))
            .Propose(MelodicSequence.Instance, new MelodicSequenceParameters()).Accept()
            .Propose(MelodicSequence.Instance, new MelodicSequenceParameters()).Accept();

        // The second step repeats the two-span result: C D E · D E F, then D E F · E F G.
        Assert.Equal([60, 62, 64, 62, 64, 65, 62, 64, 65, 64, 65, 67], Pitches(chain.Current));
        Assert.Equal(12, chain.Current.Select(note => note.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(chain.Result!.Checks, check => Assert.Equal(KeepStatus.Kept, check.Status));
        Assert.DoesNotContain("The motif repeats", chain.Result.Explanation, StringComparison.Ordinal);

        VariationChain undone = chain.Undo().Undo();
        Assert.Equal(notes, undone.Current);
        Assert.Equal(chain.Current, undone.Redo().Redo().Current);
    }

    [Fact]
    public void Copies_with_another_rhythm_do_not_keep_the_rhythm()
    {
        Note[] notes = Rising();
        Note[] after = [.. notes, Note("x1", 62, 3 * Quarter, Quarter), Note("x2", 64, 4 * Quarter, 2 * Quarter)];

        Variation variation = VariationFacts.Build("edit", new TickSpan(Tick.Zero, 3 * Quarter), notes, after, s_keepShape, [], s_cMajor);

        Assert.Equal(KeepStatus.NoLongerKept, variation.Checks.Single(check => check.Property == KeepProperty.Rhythm).Status);
        Assert.DoesNotContain("The motif repeats", variation.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sequence_is_deterministic_and_registered()
    {
        Note[] notes = Rising();
        var parameters = new MelodicSequenceParameters(Steps: 2, Repeats: 3);
        Variation first = MelodicSequence.Instance.Apply(notes, Context(notes, s_cMajor), parameters);
        Variation second = MelodicSequence.Instance.Apply(notes, Context(notes, s_cMajor), parameters);

        Assert.Equal(first.Notes, second.Notes);
        Assert.Equal(first.Explanation, second.Explanation);
        Assert.Same(MelodicSequence.Instance, VariationOperations.Find("V4"));
    }
}