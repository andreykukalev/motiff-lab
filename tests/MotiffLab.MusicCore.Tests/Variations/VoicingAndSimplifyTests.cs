using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Tests.Analysis;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>V3 Voicing and register and V6 Simplify, on the cadence C, F/C, G7/B, C in whole bars under a melody.</summary>
public class VoicingAndSimplifyTests
{
    private const long Bar = 1920;

    private static readonly MusicalKey s_cMajor = new(0, KeyMode.Major);

    private static VariationContext Context(IReadOnlySet<KeepProperty>? keep = null, Words words = default, long end = 4 * Bar) =>
        new(new TickSpan(Tick.Zero, end), keep ?? new HashSet<KeepProperty>(), Key: s_cMajor, Meter: MeterMap.Default, Words: words);

    private static Variation Voice(VoicingKind kind, IReadOnlySet<KeepProperty>? keep = null, Words words = default) =>
        VoicingAndRegister.Instance.Apply(HarmonyAnalyzerTests.Cadence(), Context(keep, words), new VoicingParameters(kind));

    private static string[] Symbols(Variation variation) =>
        [.. HarmonyAnalyzer.FindChords(variation.Notes, MeterMap.Default, new TickSpan(Tick.Zero, 4 * Bar), s_cMajor).Select(chord => chord.Symbol(s_cMajor))];

    private static int[] Pitches(Variation variation, params string[] ids) =>
        [.. ids.Select(id => variation.Notes.Single(note => note.Id == id).Pitch.MidiNumber)];

    [Fact]
    public void Root_position_puts_each_root_under_its_chord()
    {
        Variation variation = Voice(VoicingKind.RootPosition, new HashSet<KeepProperty> { KeepProperty.Melody });

        Assert.Equal("V3", variation.Operation);
        Assert.Equal(["C", "F", "G7", "C"], Symbols(variation));
        Assert.Equal([48, 57, 41], Pitches(variation, "f1", "f2", "f3"));
        Assert.Equal(KeepStatus.Kept, variation.Checks.Single(check => check.Property == KeepProperty.Melody).Status);
        Assert.StartsWith("The chords keep their notes and change position: C F/C G7/B C become C F G7 C; 2 chord notes move by an octave down", variation.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void First_inversion_puts_the_third_in_the_bass()
    {
        Assert.Equal(["C/E", "F/A", "G7/B", "C/E"], Symbols(Voice(VoicingKind.FirstInversion)));
    }

    [Fact]
    public void Close_gathers_the_notes_into_the_octave_over_the_bass_and_open_spreads_them()
    {
        Variation close = Voice(VoicingKind.Close);
        Variation open = Voice(VoicingKind.Open, words: Words.Russian);

        Assert.Equal([48, 55, 52], Pitches(close, "c1", "c2", "c3"));
        Assert.Equal(["C", "F/C", "G7/B", "C"], Symbols(close));
        Assert.StartsWith("3 chord notes move by an octave down; the chords keep their notes, and the melody and the rhythm are unchanged.", close.Explanation, StringComparison.Ordinal);
        Assert.Equal([48, 67, 64], Pitches(open, "c1", "c2", "c3"));
        Assert.StartsWith("4 ноты переходят на октаву вверх: аккорды сохраняют звуки, а мелодия и ритм не изменились.", open.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void An_octave_shift_moves_every_note_and_keeps_the_melody_s_note_names()
    {
        Variation variation = Voice(VoicingKind.OctaveDown, new HashSet<KeepProperty> { KeepProperty.Melody });

        Assert.All(variation.Notes, note => Assert.Equal(HarmonyAnalyzerTests.Cadence().Single(other => other.Id == note.Id).Pitch.MidiNumber - 12, note.Pitch.MidiNumber));
        Assert.Equal(KeepStatus.Kept, variation.Checks.Single(check => check.Property == KeepProperty.Melody).Status);
        Assert.StartsWith("16 notes move an octave lower; their note names and the rhythm are unchanged.", variation.Explanation, StringComparison.Ordinal);

        Variation melody = VoicingAndRegister.Instance.Apply(HarmonyAnalyzerTests.Cadence(), Context(), new VoicingParameters(VoicingKind.OctaveUp) { Scope = OperationScope.Melody });
        Assert.Equal([84, 84, 86, 84], Pitches(melody, "m1", "m2", "m3", "m4"));
        Assert.Equal([48, 55, 64], Pitches(melody, "c1", "c2", "c3"));
    }

    [Fact]
    public void Keep_bass_rules_out_the_inversions_and_any_step_that_moves_the_bass()
    {
        var keepBass = new HashSet<KeepProperty> { KeepProperty.Bass };

        Assert.Equal(
            "Voicing and register changes the chord in the bass, and Keep bass is on, so it is not available for this selection.",
            VoicingAndRegister.Instance.UnavailableReason(HarmonyAnalyzerTests.Cadence(), Context(keepBass), new VoicingParameters(VoicingKind.FirstInversion)));
        Assert.Equal(
            "Voicing and register would move the bass, and Keep bass is on, so it is not available for this selection.",
            VoicingAndRegister.Instance.UnavailableReason(HarmonyAnalyzerTests.Cadence(), Context(keepBass), new VoicingParameters(VoicingKind.OctaveDown)));
        Assert.Null(VoicingAndRegister.Instance.UnavailableReason(HarmonyAnalyzerTests.Cadence(), Context(keepBass), new VoicingParameters(VoicingKind.Close)));
    }

    [Fact]
    public void Other_hand_moves_the_highest_left_hand_note_of_each_chord()
    {
        Note[] notes = [.. HarmonyAnalyzerTests.Cadence().Select(note => note with
        {
            Hand = new Attributed<Hand>(note.Id.StartsWith('m') ? Hand.Right : Hand.Left, Attribution.Imported),
        })];

        Variation variation = VoicingAndRegister.Instance.Apply(notes, Context(), new VoicingParameters(VoicingKind.OtherHand));

        Assert.Equal(["c3", "e3", "f3", "g3"], variation.Notes.Where(note => note.Hand!.Value == Hand.Right && !note.Id.StartsWith('m')).Select(note => note.Id).Order());
        Assert.StartsWith("4 notes move to the right hand; every pitch, onset and length is unchanged.", variation.Explanation, StringComparison.Ordinal);
        Assert.Equal(
            "Voicing and register needs notes marked for the left hand, so it is not available for this selection.",
            VoicingAndRegister.Instance.UnavailableReason(HarmonyAnalyzerTests.Cadence(), Context(), new VoicingParameters(VoicingKind.OtherHand)));
    }

    [Fact]
    public void Revoicing_works_on_the_whole_selection_and_an_octave_shift_on_any_layer()
    {
        Assert.Contains(
            "can't be limited",
            VoicingAndRegister.Instance.UnavailableReason(HarmonyAnalyzerTests.Cadence(), Context(), new VoicingParameters(VoicingKind.Close) { Scope = OperationScope.Accompaniment }),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Level_1_leaves_out_the_inner_voices()
    {
        Variation variation = Simplify.Instance.Apply(HarmonyAnalyzerTests.Cadence(), Context(), new SimplifyParameters(1));

        Assert.Equal("V6", variation.Operation);
        Assert.Equal(["c1", "e1", "f1", "g1", "m1", "m2", "m3", "m4"], variation.Notes.Select(note => note.Id).Order());
        Assert.StartsWith("8 notes are left out.", variation.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Level_2_also_plays_one_root_per_chord()
    {
        Variation variation = Simplify.Instance.Apply(HarmonyAnalyzerTests.Cadence(), Context(words: Words.Russian), new SimplifyParameters(2));

        Assert.Equal([48, 41, 43, 48], variation.Notes.Where(note => note.Id.EndsWith("~b1", StringComparison.Ordinal)).OrderBy(note => note.Span.Onset).Select(note => note.Pitch.MidiNumber));
        Assert.Equal(8, variation.Notes.Count);
        Assert.StartsWith("12 нот убраны; в басу 4 основных тона, по одному на аккорд.", variation.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Level_3_also_rounds_to_eighths_and_folds_wide_leaps()
    {
        Note[] line =
        [
            N("a", 60, 0, 120), N("b", 62, 120, 120), N("c", 79, 240, 240),
        ];

        Variation variation = Simplify.Instance.Apply(line, Context(end: 480), new SimplifyParameters(3));

        Assert.Equal([("a", 0L, 240L, 60), ("c", 240L, 240L, 67)], variation.Notes.Select(note => (note.Id, note.Span.Onset.Value, note.Span.Duration, note.Pitch.MidiNumber)));
        Assert.Equal(
            "1 note is left out; 1 note moves to the nearest eighth; 1 melody note moves by an octave, so no leap is wider than an octave.",
            variation.Explanation.Split(" The pitch")[0].Split(" 1 note is removed.")[0]);
        Assert.Equal(
            "Simplify has no inner voice or bass to leave out on a single line, so it is not available for this selection.",
            Simplify.Instance.UnavailableReason(line, Context(end: 480), new SimplifyParameters(1)));
    }

    private static Note N(string id, int pitch, long onset, long duration) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    public static TheoryData<string, OperationParameters> Steps() => new()
    {
        { "V3", new VoicingParameters(VoicingKind.SecondInversion) },
        { "V3", new VoicingParameters(VoicingKind.OctaveUp) { Scope = OperationScope.Accompaniment } },
        { "V6", new SimplifyParameters(2) },
        { "V6", new SimplifyParameters(3) },
    };

    [Theory]
    [MemberData(nameof(Steps))]
    public void Every_step_is_pure_and_saves_with_its_parameters(string id, OperationParameters parameters)
    {
        IVariationOperation operation = VariationOperations.Find(id)!;
        Variation first = operation.Apply(HarmonyAnalyzerTests.Cadence(), Context(), parameters);
        Variation again = operation.Apply(HarmonyAnalyzerTests.Cadence(), Context(), parameters);

        Assert.Equal(first.Notes, again.Notes);
        Assert.Equal(first.Explanation, again.Explanation);
        Assert.Matches("[А-Яа-я]", operation.Apply(HarmonyAnalyzerTests.Cadence(), Context(words: Words.Russian), parameters).Explanation);

        var step = new MotiffLab.MusicCore.Projects.SavedStep(id, parameters);
        MotiffLab.MusicCore.Projects.Project project = MotiffLab.MusicCore.Tests.Projects.ProjectFileTests.Worked() with
        {
            Variations = [new MotiffLab.MusicCore.Projects.SavedVariation("v_0001", "m_0001", [step], null, [])],
        };
        Assert.Equal(step, MotiffLab.MusicCore.Projects.ProjectFile.Read(MotiffLab.MusicCore.Projects.ProjectFile.Write(project)).Variations[0].Steps.Single());
    }
}