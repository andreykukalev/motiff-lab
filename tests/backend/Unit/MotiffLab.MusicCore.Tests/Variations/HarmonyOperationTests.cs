using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Tests.Analysis;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>
/// The operations built on chord detection (S1-A3) and the line's notes and rests: V7 Density and rests, V12 Bass
/// pattern, V14 Ornament and V15 Reharmonise, on a plain cadence in C (C, F/C, G7/B, C in whole bars).
/// </summary>
public class HarmonyOperationTests
{
    private const long Bar = 1920;

    private static readonly MusicalKey s_cMajor = new(0, KeyMode.Major);

    private static readonly HashSet<KeepProperty> s_keepMelody = [KeepProperty.Melody];

    private static VariationContext Context(Words words = default, IReadOnlySet<KeepProperty>? keep = null) =>
        new(new TickSpan(Tick.Zero, 4 * Bar), keep ?? s_keepMelody, Key: s_cMajor, Meter: MeterMap.Default, Words: words);

    /// <summary>Applies a step with Keep melody where the operation's Keep row allows it, and with no Keep choice otherwise.</summary>
    private static Variation Apply(IVariationOperation operation, OperationParameters parameters, Words words = default) =>
        operation.Apply(HarmonyAnalyzerTests.Cadence(), Context(words, operation.DisabledReason(s_keepMelody) is null ? s_keepMelody : new HashSet<KeepProperty>()), parameters);

    private static (string Id, long Onset, long Length, int Pitch)[] Shapes(IEnumerable<Note> notes) =>
        [.. notes.Select(note => (note.Id, note.Span.Onset.Value, note.Span.Duration, note.Pitch.MidiNumber)).OrderBy(shape => shape.Value).ThenBy(shape => shape.MidiNumber)];

    private static string[] Labels(Variation variation) =>
        [.. HarmonyAnalyzer.FindChords(variation.Notes, MeterMap.Default, new TickSpan(Tick.Zero, 4 * Bar), s_cMajor).Select(chord => chord.Symbol(s_cMajor))];

    [Fact]
    public void Roots_in_the_bass_put_each_chord_in_root_position()
    {
        Variation variation = Apply(BassPattern.Instance, new BassPatternParameters(BassPatternKind.RootsOnly));

        Assert.Equal("V12", variation.Operation);
        Assert.Equal([("c1~b1", 0, Bar, 48), ("f1~b1", Bar, Bar, 41), ("g1~b1", 2 * Bar, Bar, 43), ("e1~b1", 3 * Bar, Bar, 48)], Shapes(variation.Notes.Where(note => note.Id.Contains("~b", StringComparison.Ordinal))));
        Assert.DoesNotContain(variation.Notes, note => note.Id is "c1" or "f1" or "g1" or "e1");
        Assert.Equal(["C", "F", "G7", "C"], Labels(variation));
        Assert.Equal(KeepStatus.Kept, variation.Checks.Single(check => check.Property == KeepProperty.Melody).Status);
    }

    [Fact]
    public void A_pedal_holds_the_tonic_at_one_pitch_and_strikes_it_in_each_bar()
    {
        Variation variation = Apply(BassPattern.Instance, new BassPatternParameters(BassPatternKind.Pedal));

        Assert.Equal([0, Bar, 2 * Bar, 3 * Bar], variation.Notes.Where(note => note.Id.Contains("~b", StringComparison.Ordinal)).Select(note => note.Span.Onset.Value).Order());
        Assert.All(variation.Notes.Where(note => note.Id.Contains("~b", StringComparison.Ordinal)), note => Assert.Equal(48, note.Pitch.MidiNumber));
    }

    [Fact]
    public void A_walk_climbs_the_chord_and_leads_into_the_next_root_by_a_step_below()
    {
        Variation variation = Apply(BassPattern.Instance, new BassPatternParameters(BassPatternKind.Walk));

        // C3 E3 G2 E3: the root, the third, the fifth an octave down under the held G3, then E3 leading to F; F2 A2 C3 F2
        // (the step below G is F itself); G2 B2 D3 B2; C3 E3 G2 C3 after the last chord.
        Assert.Equal(
            [48, 52, 43, 52, 41, 45, 48, 41, 43, 47, 50, 47, 48, 52, 43, 48],
            variation.Notes.Where(note => note.Id.Contains("~b", StringComparison.Ordinal)).OrderBy(note => note.Span.Onset).Select(note => note.Pitch.MidiNumber));
        Assert.Equal(KeepStatus.Kept, variation.Checks.Single(check => check.Property == KeepProperty.Melody).Status);
    }

    [Fact]
    public void A_bass_pattern_is_not_available_on_a_single_line()
    {
        Note[] line = [.. HarmonyAnalyzerTests.Cadence().Where(note => note.Id.StartsWith('m'))];

        Assert.Equal(
            "Bass pattern needs a bass under the melody, and this selection is a single line, so it is not available for this selection.",
            BassPattern.Instance.UnavailableReason(line, Context(), new BassPatternParameters(BassPatternKind.Pedal)));
        Assert.Equal(
            "Для этого выделения «Рисунок баса» не подходит: это одна линия, а нужен бас под мелодией.",
            BassPattern.Instance.UnavailableReason(line, Context(Words.Russian), new BassPatternParameters(BassPatternKind.Pedal)));
    }

    [Fact]
    public void More_air_ends_each_melody_note_an_eighth_before_the_next()
    {
        Variation variation = Apply(DensityAndRests.Instance, new DensityParameters(DensityKind.MoreAir) { Scope = OperationScope.Melody });

        Assert.Equal("V7", variation.Operation);
        Assert.Equal([Bar - 240, Bar - 240, Bar - 240, Bar], variation.Notes.Where(note => note.Id.StartsWith('m')).OrderBy(note => note.Span.Onset).Select(note => note.Span.Duration));
        Assert.Equal(
            Shapes(HarmonyAnalyzerTests.Cadence().Where(note => !note.Id.StartsWith('m'))),
            Shapes(variation.Notes.Where(note => !note.Id.StartsWith('m'))));
    }

    [Fact]
    public void Repeating_splits_a_note_into_repeated_eighths_with_derived_ids()
    {
        Note[] line = [.. HarmonyAnalyzerTests.Cadence().Where(note => note.Id == "m1")];
        Variation variation = DensityAndRests.Instance.Apply(line, Context(keep: new HashSet<KeepProperty>()), new DensityParameters(DensityKind.Repeat));

        Assert.Equal(["m1", "m1~r2", "m1~r3", "m1~r4", "m1~r5", "m1~r6", "m1~r7", "m1~r8"], variation.Notes.OrderBy(note => note.Span.Onset).Select(note => note.Id));
        Assert.All(variation.Notes, note => Assert.Equal(240, note.Span.Duration));

        Variation merged = DensityAndRests.Instance.Apply(variation.Notes, Context(keep: new HashSet<KeepProperty>()), new DensityParameters(DensityKind.MergeRepeats));
        Assert.Equal(Shapes(line), Shapes(merged.Notes));
    }

    [Fact]
    public void A_mordent_adds_the_upper_scale_step_in_32nds_to_each_long_melody_note()
    {
        Variation variation = Apply(Ornament.Instance, new OrnamentParameters(OrnamentKind.Mordent) { Scope = OperationScope.Melody });

        Assert.Equal("V14", variation.Operation);
        Assert.Equal([("m1", 0, 60, 72), ("m1~o1", 60, 60, 74), ("m1~o2", 120, Bar - 120, 72)], Shapes(variation.Notes.Where(note => note.Id.StartsWith("m1", StringComparison.Ordinal))));
        Assert.Equal(Shapes(HarmonyAnalyzerTests.Cadence().Where(note => !note.Id.StartsWith('m'))), Shapes(variation.Notes.Where(note => !note.Id.StartsWith('m'))));
    }

    [Fact]
    public void Relative_chords_move_the_accompaniment_to_the_nearest_new_chord_tones()
    {
        Variation variation = Apply(Reharmonise.Instance, new ReharmoniseParameters(ReharmoniseKind.Relative));

        Assert.Equal("V15", variation.Operation);
        Assert.Equal(
            [("c1", 0, Bar, 45), ("c2", 0, Bar, 57), ("c3", 0, Bar, 64), ("m1", 0, Bar, 72)],
            Shapes(variation.Notes.Where(note => note.Span.Onset.Value == 0)));
        Assert.Equal(
            Shapes(HarmonyAnalyzerTests.Cadence().Where(note => note.Id.StartsWith('m') || note.Id.StartsWith('g'))),
            Shapes(variation.Notes.Where(note => note.Id.StartsWith('m') || note.Id.StartsWith('g'))));
        Assert.Equal(KeepStatus.Kept, variation.Checks.Single(check => check.Property == KeepProperty.Melody).Status);
    }

    [Fact]
    public void Reharmonise_replaces_only_major_and_minor_chords()
    {
        Assert.Equal(new ChordLabel(9, ChordQuality.Minor), Reharmonise.Replacement(new ChordLabel(0, ChordQuality.Major), ReharmoniseKind.Relative));
        Assert.Equal(new ChordLabel(0, ChordQuality.Major), Reharmonise.Replacement(new ChordLabel(9, ChordQuality.Minor), ReharmoniseKind.Relative));
        Assert.Equal(new ChordLabel(0, ChordQuality.Minor), Reharmonise.Replacement(new ChordLabel(0, ChordQuality.Major), ReharmoniseKind.Parallel));
        Assert.Null(Reharmonise.Replacement(new ChordLabel(7, ChordQuality.Dominant7), ReharmoniseKind.Relative));
    }

    public static TheoryData<string, OperationParameters> Steps() => new()
    {
        { "V7", new DensityParameters(DensityKind.MoreAir) { Scope = OperationScope.Melody } },
        { "V7", new DensityParameters(DensityKind.Repeat, PatternUnit.Sixteenths) { Scope = OperationScope.Melody } },
        { "V12", new BassPatternParameters(BassPatternKind.RootsOnly) },
        { "V12", new BassPatternParameters(BassPatternKind.RootAndFifth) },
        { "V12", new BassPatternParameters(BassPatternKind.Octaves) },
        { "V12", new BassPatternParameters(BassPatternKind.Walk) },
        { "V12", new BassPatternParameters(BassPatternKind.Pedal) },
        { "V14", new OrnamentParameters(OrnamentKind.Mordent) { Scope = OperationScope.Melody } },
        { "V14", new OrnamentParameters(OrnamentKind.LowerMordent) { Scope = OperationScope.Melody } },
        { "V14", new OrnamentParameters(OrnamentKind.Turn) { Scope = OperationScope.Melody } },
        { "V14", new OrnamentParameters(OrnamentKind.Trill) { Scope = OperationScope.Melody } },
        { "V15", new ReharmoniseParameters(ReharmoniseKind.Relative) },
        { "V15", new ReharmoniseParameters(ReharmoniseKind.Parallel) },
    };

    [Theory]
    [MemberData(nameof(Steps))]
    public void Every_step_is_pure_and_explains_itself_in_both_languages(string id, OperationParameters parameters)
    {
        IVariationOperation operation = VariationOperations.Find(id)!;
        Note[] before = HarmonyAnalyzerTests.Cadence();
        Variation first = Apply(operation, parameters);
        Variation again = Apply(operation, parameters);
        Variation russian = Apply(operation, parameters, Words.Russian);

        Assert.Equal(HarmonyAnalyzerTests.Cadence(), before);
        Assert.Equal(first.Notes, again.Notes);
        Assert.Equal(first.Notes, russian.Notes);
        Assert.Equal(first.Explanation, again.Explanation);
        Assert.Equal(Shapes(before.Where(note => note.Id.StartsWith('m') && parameters.Scope == OperationScope.All)), Shapes(first.Notes.Where(note => note.Id.StartsWith('m') && parameters.Scope == OperationScope.All)));
        Assert.Matches("[А-Яа-я]", russian.Explanation);
        Assert.Equal(id, first.Operation);
    }
}