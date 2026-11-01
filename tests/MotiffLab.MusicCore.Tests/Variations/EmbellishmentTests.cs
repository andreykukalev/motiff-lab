using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>V8 Accent shift, V11 Passing and neighbour notes and the grace notes of V14 Ornament, on a line in C major.</summary>
public class EmbellishmentTests
{
    private const long Quarter = 480;
    private const long Eighth = 240;

    private static readonly MusicalKey s_cMajor = new(0, KeyMode.Major);

    private static readonly HashSet<KeepProperty> s_none = [];

    private static Note N(string id, int pitch, long onset, long duration, int velocity = 80) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = velocity,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    /// <summary>C5 E5 G5 C6 in quarters: one bar of 4/4.</summary>
    private static Note[] Arpeggio() => [N("a", 72, 0, Quarter), N("b", 76, Quarter, Quarter), N("c", 79, 2 * Quarter, Quarter), N("d", 84, 3 * Quarter, Quarter)];

    /// <summary>A scale up from C5 in eighths: one bar of 4/4.</summary>
    private static Note[] Eighths() => [.. new[] { 72, 74, 76, 77, 79, 81, 83, 84 }.Select((pitch, i) => N($"e{i + 1}", pitch, i * Eighth, Eighth))];

    private static VariationContext Context(MusicalKey? key = null, Words words = default, IReadOnlySet<KeepProperty>? keep = null) =>
        new(new TickSpan(Tick.Zero, 4 * Quarter), keep ?? s_none, Key: key ?? s_cMajor, Meter: MeterMap.Default, Words: words);

    private static (string Id, long Onset, long Length, int Pitch)[] Shapes(IEnumerable<Note> notes) =>
        [.. notes.Select(note => (note.Id, note.Span.Onset.Value, note.Span.Duration, note.Pitch.MidiNumber)).OrderBy(shape => shape.Value).ThenBy(shape => shape.MidiNumber)];

    [Fact]
    public void Passing_notes_walk_the_scale_in_the_second_half_of_each_note()
    {
        Variation variation = PassingNotes.Instance.Apply(Arpeggio(), Context(), new PassingNotesParameters(PassingNoteKind.Passing));

        Assert.Equal("V11", variation.Operation);
        Assert.Equal(
            [("a", 0, 240, 72), ("a~p1", 240, 240, 74), ("b", 480, 240, 76), ("b~p1", 720, 240, 77), ("c", 960, 240, 79), ("c~p1", 1200, 120, 81), ("c~p2", 1320, 120, 83), ("d", 1440, 480, 84)],
            Shapes(variation.Notes));
        Assert.Equal(
            "Passing notes are added after 3 melody notes (D5 after C5, F5 after E5, A5 and B5 after G5), in the second half of each; every main note keeps its pitch and onset.",
            variation.Explanation.Split(" 4 notes are added.")[0]);
    }

    [Fact]
    public void A_neighbour_note_steps_away_and_the_line_comes_back()
    {
        Variation upper = PassingNotes.Instance.Apply(Arpeggio(), Context(), new PassingNotesParameters(PassingNoteKind.UpperNeighbour, new IntervalSet([0])));
        Variation lower = PassingNotes.Instance.Apply(Arpeggio(), Context(words: Words.Russian), new PassingNotesParameters(PassingNoteKind.LowerNeighbour, new IntervalSet([1])));

        Assert.Equal([("a", 0, 240, 72), ("a~p1", 240, 240, 74)], Shapes(upper.Notes).Take(2));
        Assert.Equal(("b~p1", 720, 240, 74), Shapes(lower.Notes).Single(shape => shape.Id == "b~p1"));
        Assert.StartsWith("Вспомогательные ноты добавлены после 1 ноты мелодии (D5 после E5)", lower.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Passing_notes_need_a_key_and_a_wide_enough_interval()
    {
        Assert.Equal(
            "Passing and neighbour notes counts in steps of the key's scale, and this selection has no key, so it is not available.",
            PassingNotes.Instance.UnavailableReason(Arpeggio(), Context() with { Key = null }, new PassingNotesParameters(PassingNoteKind.Passing)));
        Assert.Equal(
            "Passing and neighbour notes finds no two melody notes a third to a fifth apart with room for passing notes, so it is not available for this selection.",
            PassingNotes.Instance.UnavailableReason(Eighths(), Context(), new PassingNotesParameters(PassingNoteKind.Passing)));
        Assert.Contains("interval 4", PassingNotes.Instance.UnavailableReason(Arpeggio(), Context(), new PassingNotesParameters(PassingNoteKind.Passing, new IntervalSet([3]))), StringComparison.Ordinal);
    }

    [Fact]
    public void Accents_on_the_beats_lift_the_beat_notes_and_soften_the_rest()
    {
        Variation variation = AccentShift.Instance.Apply(Eighths(), Context(), new AccentShiftParameters(AccentPattern.OnBeat));

        Assert.Equal("V8", variation.Operation);
        Assert.Equal([100, 70, 100, 70, 100, 70, 100, 70], variation.Notes.OrderBy(note => note.Span.Onset).Select(note => note.Velocity));
        Assert.Equal(Shapes(Eighths()), Shapes(variation.Notes));
        Assert.StartsWith("The accents move: 4 notes now play louder, at velocity 100, and 4 notes now play softer, at velocity 70; every pitch, onset and length is unchanged.", variation.Explanation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AccentPattern.OffBeat, new[] { 70, 100, 70, 100, 70, 100, 70, 100 })]
    [InlineData(AccentPattern.EveryThird, new[] { 100, 70, 70, 100, 70, 70, 100, 70 })]
    [InlineData(AccentPattern.Downbeat, new[] { 100, 70, 70, 70, 70, 70, 70, 70 })]
    public void Each_pattern_accents_its_onsets(AccentPattern pattern, int[] velocities)
    {
        Variation variation = AccentShift.Instance.Apply(Eighths(), Context(), new AccentShiftParameters(pattern));

        Assert.Equal(velocities, variation.Notes.OrderBy(note => note.Span.Onset).Select(note => note.Velocity));
    }

    [Fact]
    public void Custom_accents_fall_on_the_picked_onsets_and_round_trip_in_the_project_file()
    {
        var parameters = new AccentShiftParameters(AccentPattern.Custom, new IntervalSet([1, 2]));
        Variation variation = AccentShift.Instance.Apply(Eighths(), Context(), parameters);

        Assert.Equal([70, 100, 100, 70, 70, 70, 70, 70], variation.Notes.OrderBy(note => note.Span.Onset).Select(note => note.Velocity));
        Assert.Throws<ArgumentOutOfRangeException>(() => AccentShift.Instance.UnavailableReason(Eighths(), Context(), new AccentShiftParameters(AccentPattern.Custom)));
    }

    [Fact]
    public void Accents_that_would_lift_every_note_or_none_are_not_offered()
    {
        Assert.Equal(
            "Accent shift would accent every note, so none would stand out, so it is not available for this selection.",
            AccentShift.Instance.UnavailableReason(Arpeggio(), Context(), new AccentShiftParameters(AccentPattern.OnBeat)));
        Assert.Equal(
            "Для этого выделения «Сдвиг акцентов» не подходит: ни одна нота не попадает в рисунок акцентов.",
            AccentShift.Instance.UnavailableReason(Arpeggio(), Context(words: Words.Russian), new AccentShiftParameters(AccentPattern.OffBeat)));
    }

    [Fact]
    public void A_grace_note_takes_a_32nd_from_the_start_of_each_long_note()
    {
        Variation above = Ornament.Instance.Apply(Arpeggio(), Context(), new OrnamentParameters(OrnamentKind.GraceAbove));
        Variation below = Ornament.Instance.Apply(Arpeggio(), Context(words: Words.Russian), new OrnamentParameters(OrnamentKind.GraceBelow));

        Assert.Equal([("a~o1", 0, 60, 74), ("a", 60, 420, 72)], Shapes(above.Notes).Take(2));
        Assert.Equal([("a~o1", 0, 60, 71), ("a", 60, 420, 72)], Shapes(below.Notes).Take(2));
        Assert.StartsWith("A grace note from above on 4 long notes (C5, E5, G5 and C6): the note above on the note's beat", above.Explanation, StringComparison.Ordinal);
        Assert.StartsWith("Форшлаг снизу на 4 долгих нотах (C5, E5, G5 и C6): нижняя соседняя на месте ноты", below.Explanation, StringComparison.Ordinal);
    }

    public static TheoryData<string, OperationParameters> Steps() => new()
    {
        { "V8", new AccentShiftParameters(AccentPattern.OffBeat) },
        { "V8", new AccentShiftParameters(AccentPattern.Custom, new IntervalSet([0, 5])) },
        { "V11", new PassingNotesParameters(PassingNoteKind.UpperNeighbour) },
        { "V11", new PassingNotesParameters(PassingNoteKind.LowerNeighbour, new IntervalSet([2, 4])) { Scope = OperationScope.Melody } },
        { "V14", new OrnamentParameters(OrnamentKind.GraceAbove) },
    };

    [Theory]
    [MemberData(nameof(Steps))]
    public void Every_step_is_pure_and_saves_with_its_parameters(string id, OperationParameters parameters)
    {
        IVariationOperation operation = VariationOperations.Find(id)!;
        Note[] notes = id == "V14" ? Arpeggio() : Eighths();
        Variation first = operation.Apply(notes, Context(), parameters);
        Variation again = operation.Apply(notes, Context(), parameters);

        Assert.Equal(first.Notes, again.Notes);
        Assert.Equal(first.Explanation, again.Explanation);
        Assert.Matches("[А-Яа-я]", operation.Apply(notes, Context(words: Words.Russian), parameters).Explanation);

        var step = new MotiffLab.MusicCore.Projects.SavedStep(id, parameters);
        MotiffLab.MusicCore.Projects.Project project = MotiffLab.MusicCore.Tests.Projects.ProjectFileTests.Worked() with
        {
            Variations = [new MotiffLab.MusicCore.Projects.SavedVariation("v_0001", "m_0001", [step], null, [])],
        };
        Assert.Equal(step, MotiffLab.MusicCore.Projects.ProjectFile.Read(MotiffLab.MusicCore.Projects.ProjectFile.Write(project)).Variations[0].Steps.Single());
    }
}