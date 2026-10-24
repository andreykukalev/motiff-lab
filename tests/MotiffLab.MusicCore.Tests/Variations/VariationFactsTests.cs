using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>The diff shared by every operation: notes paired by id, added and removed notes (S1-E4).</summary>
public class VariationFactsTests
{
    private static readonly TickSpan s_span = new(Tick.Zero, 960);

    private static Note Note(string id, int pitch, long onset) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), 240),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    [Fact]
    public void Notes_are_paired_by_id_and_the_rest_are_added_or_removed()
    {
        Note[] before = [Note("a", 60, 0), Note("b", 62, 240), Note("c", 64, 480)];
        Note[] after = [Note("a", 60, 0), Note("a+", 61, 120), Note("c", 65, 480)];

        Variation variation = VariationFacts.Build("V0", s_span, before, after, new HashSet<KeepProperty>(), []);

        Assert.Equal(["a", "c"], variation.Changes.Select(change => change.Before.Id));
        Assert.Equal(["a+"], variation.Added.Select(note => note.Id));
        Assert.Equal(["b"], variation.Removed.Select(note => note.Id));
        Assert.Equal(["a", "a+", "c"], variation.Notes.Select(note => note.Id));
        Assert.StartsWith("1 note is added. 1 note is removed. 1 of 2 notes change pitch.", variation.Explanation);
    }

    private static Note Long(string id, int pitch, long onset, long duration) => Note(id, pitch, onset) with { Span = new TickSpan(new Tick(onset), duration) };

    /// <summary>C5 D5 E5 over a held C major chord, C3 E4 G4.</summary>
    private static Note[] MelodyOverChord() =>
    [
        Long("c3", 48, 0, 720), Long("e4", 64, 0, 720), Long("g4", 67, 0, 720),
        Note("c5", 72, 0), Note("d5", 74, 240), Note("e5", 76, 480),
    ];

    private static readonly HashSet<KeepProperty> s_all = [.. Enum.GetValues<KeepProperty>()];

    [Fact]
    public void Every_property_is_checked_whatever_the_player_keeps()
    {
        Note[] before = [Note("a", 60, 0), Note("b", 62, 240)];
        Note[] after = [Note("a", 60, 0), Note("b", 64, 240)];

        Variation variation = VariationFacts.Build("V0", s_span, before, after, new HashSet<KeepProperty> { KeepProperty.Rhythm }, []);

        Assert.Equal(Enum.GetValues<KeepProperty>(), variation.Properties.Select(check => check.Property));
        Assert.Equal([new KeepCheck(KeepProperty.Rhythm, KeepStatus.Kept)], variation.Checks);
        Assert.Equal(KeepStatus.NoLongerKept, variation.Properties.Single(check => check.Property == KeepProperty.Intervals).Status);
        Assert.Equal(KeepStatus.Kept, variation.Properties.Single(check => check.Property == KeepProperty.Contour).Status);
    }

    [Fact]
    public void Contour_and_intervals_follow_the_melody_not_the_chord_under_it()
    {
        // Reorder the chord's inner notes' pitches (E4 becomes A3): the melody C5 D5 E5 is untouched.
        Note[] before = MelodyOverChord();
        Note[] after = [.. before.Select(note => note.Id == "e4" ? note with { Pitch = new Pitch(57) } : note)];

        Variation variation = VariationFacts.Build("V0", s_span, before, after, s_all, []);

        KeepStatus Status(KeepProperty property) => variation.Checks.Single(check => check.Property == property).Status;
        Assert.Equal(KeepStatus.Kept, Status(KeepProperty.Contour));
        Assert.Equal(KeepStatus.Kept, Status(KeepProperty.Intervals));
        Assert.Equal(KeepStatus.Kept, Status(KeepProperty.Melody));
        Assert.Equal(KeepStatus.Kept, Status(KeepProperty.Bass));
        Assert.Equal(KeepStatus.NoLongerKept, Status(KeepProperty.Pitches));
        Assert.Equal(KeepStatus.NotChecked, Status(KeepProperty.Harmony));
    }

    [Fact]
    public void A_new_bass_note_breaks_keep_bass_but_not_keep_melody()
    {
        Note[] before = MelodyOverChord();
        Note[] after = [.. before.Select(note => note.Id == "c3" ? note with { Pitch = new Pitch(43) } : note)];

        Variation variation = VariationFacts.Build("V0", s_span, before, after, new HashSet<KeepProperty> { KeepProperty.Melody, KeepProperty.Bass }, []);

        Assert.Equal(
            [new KeepCheck(KeepProperty.Melody, KeepStatus.Kept), new KeepCheck(KeepProperty.Bass, KeepStatus.NoLongerKept)],
            variation.Checks);
    }

    [Fact]
    public void The_explanation_ends_with_what_stayed_the_same()
    {
        Note[] before = MelodyOverChord();
        Note[] after = [.. before.Select(note => note with { Span = new TickSpan(note.Span.Onset.Offset(240), note.Span.Duration) })];

        Variation variation = VariationFacts.Build("V0", s_span, before, after, new HashSet<KeepProperty>(), []);

        Assert.EndsWith("Unchanged: pitches, pitch order, contour, intervals, melody pitches, bass pitches and register.", variation.Explanation);
    }

    [Fact]
    public void A_single_line_names_no_bass_among_what_stayed_the_same()
    {
        Note[] before = [Note("a", 60, 0), Note("b", 62, 240)];
        Note[] after = [Note("a", 60, 240), Note("b", 62, 480)];

        Variation variation = VariationFacts.Build("V0", s_span, before, after, new HashSet<KeepProperty>(), []);

        Assert.EndsWith("Unchanged: pitches, pitch order, contour, intervals, melody pitches and register.", variation.Explanation);
        Assert.Equal(KeepStatus.Kept, variation.Properties.Single(check => check.Property == KeepProperty.Bass).Status);
    }
}