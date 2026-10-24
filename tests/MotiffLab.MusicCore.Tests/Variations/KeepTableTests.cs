using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>The Keep table, its footnotes and presets as data, and the kept-property checks after a step (S1-E2, S1-E9).</summary>
public class KeepTableTests
{
    private static readonly TickSpan s_span = new(Tick.Zero, 960);

    /// <summary>The spec's table, copied cell by cell: ✓ or –, in the order pitches, pitch order, contour, intervals, rhythm, melody, bass, harmony, register.</summary>
    private static readonly (string Id, string Cells)[] s_spec =
    [
        ("V1", "✓✓✓✓–✓✓✓✓"),
        ("V7", "–✓✓–––✓✓✓"),
        ("V8", "✓✓✓✓✓✓✓✓✓"),
        ("V4", "––✓✓✓––––"),
        ("V9", "––✓–✓–✓––"),
        ("V10", "––––✓–✓––"),
        ("V11", "––––––✓✓✓"),
        ("V2", "–––––✓–✓–"),
        ("V12", "–––––✓–✓–"),
        ("V3", "––––✓✓✓✓–"),
        ("V6", "––✓––––✓–"),
    ];

    private static HashSet<KeepProperty> Keep(params KeepProperty[] properties) => [.. properties];

    private static Note Note(string id, int pitch, long onset, long duration = 240) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    [Fact]
    public void The_table_holds_every_stage_1_operation_cell_by_cell()
    {
        Assert.Equal(s_spec.Select(row => row.Id), KeepTable.Rows.Select(row => row.OperationId));
        foreach ((string id, string cells) in s_spec)
        {
            KeepRow row = KeepTable.Find(id)!;
            KeepProperty[] expected = [.. Enum.GetValues<KeepProperty>().Where((_, i) => cells[i] == '✓')];
            Assert.Equal(expected, Enum.GetValues<KeepProperty>().Where(row.Compatible.Contains));
        }
    }

    [Fact]
    public void Footnotes_one_to_four_sit_on_compatible_cells_once_each()
    {
        (string Id, KeepProperty Property, int Number)[] cells = [.. KeepTable.Rows
            .SelectMany(row => row.Footnotes.Select(cell => (row.OperationId, cell.Key, cell.Value.Number)))
            .OrderBy(cell => cell.Number)];

        Assert.Equal(
            [("V1", KeepProperty.Melody, 1), ("V4", KeepProperty.Intervals, 2), ("V3", KeepProperty.Melody, 3), ("V3", KeepProperty.Bass, 4)],
            cells);
        Assert.All(cells, cell => Assert.Contains(cell.Property, KeepTable.Find(cell.Id)!.Compatible));
    }

    [Fact]
    public void Every_offered_operation_reads_its_row()
    {
        foreach (IVariationOperation operation in VariationOperations.All)
        {
            KeepRow row = Assert.IsType<KeepRow>(KeepTable.Find(operation.Id));
            Assert.Equal(row.Name, operation.Name);
            foreach (KeepProperty property in Enum.GetValues<KeepProperty>())
            {
                Assert.Equal(KeepTable.DisabledReason(operation.Id, Keep(property)), operation.DisabledReason(Keep(property)));
            }
        }
    }

    [Fact]
    public void Each_blocked_cell_disables_the_operation_with_a_reason_naming_the_property()
    {
        foreach (KeepRow row in KeepTable.Rows)
        {
            foreach (KeepProperty property in Enum.GetValues<KeepProperty>())
            {
                string? reason = KeepTable.DisabledReason(row.OperationId, Keep(property));
                if (row.Compatible.Contains(property))
                {
                    Assert.Null(reason);
                }
                else
                {
                    string name = property == KeepProperty.PitchOrder ? "pitch order" : property.ToString().ToLowerInvariant();
                    Assert.Equal($"{row.Name} {row.WhatItChanges}, so it is off while Keep {name} is on.", reason);
                }
            }
        }
    }

    [Fact]
    public void Keep_rhythm_disables_V1_and_V7_and_leaves_V9_V10_and_V4_enabled()
    {
        // AC-S1-10.
        HashSet<KeepProperty> keep = Keep(KeepProperty.Rhythm);

        Assert.Equal("Rhythm reshape changes when notes start and how long they last, so it is off while Keep rhythm is on.", KeepTable.DisabledReason("V1", keep));
        Assert.Equal("Density and rests changes how many notes there are and where the rests fall, so it is off while Keep rhythm is on.", KeepTable.DisabledReason("V7", keep));
        Assert.All(new[] { "V9", "V10", "V4" }, id => Assert.Null(KeepTable.DisabledReason(id, keep)));
    }

    [Fact]
    public void Several_blocking_choices_are_all_named()
    {
        Assert.Equal(
            "Simplify plays fewer, easier notes, so it is off while Keep pitches and Keep rhythm and Keep register are on.",
            KeepTable.DisabledReason("V6", Keep(KeepProperty.Register, KeepProperty.Pitches, KeepProperty.Rhythm, KeepProperty.Harmony)));
    }

    [Fact]
    public void No_keep_choices_leave_every_operation_enabled()
    {
        Assert.All(KeepTable.Rows, row => Assert.Null(KeepTable.DisabledReason(row.OperationId, Keep())));
    }

    [Fact]
    public void Footnote_1_keep_melody_and_keep_rhythm_cannot_both_be_on_with_V1()
    {
        Assert.Null(KeepTable.DisabledReason("V1", Keep(KeepProperty.Melody)));
        Assert.Equal(
            "Rhythm reshape changes when notes start and how long they last, so Keep melody and Keep rhythm cannot both be on.",
            KeepTable.DisabledReason("V1", Keep(KeepProperty.Melody, KeepProperty.Rhythm)));
        Assert.Equal([KeepTable.MelodyPitchesOnly], KeepTable.Footnotes("V1", Keep(KeepProperty.Melody, KeepProperty.Pitches)));
        Assert.Empty(KeepTable.Footnotes("V1", Keep(KeepProperty.Pitches)));
    }

    [Fact]
    public void Footnote_1_the_melody_check_reads_pitches_not_rhythm()
    {
        Note[] before = [Note("a", 72, 0), Note("b", 74, 240), Note("c", 76, 480)];
        Note[] after = [Note("a", 72, 0, 480), Note("b", 74, 480, 480), Note("c", 76, 960, 480)];

        Variation variation = VariationFacts.Build("V1", s_span, before, after, Keep(KeepProperty.Melody), []);

        Assert.Equal([new KeepCheck(KeepProperty.Melody, KeepStatus.Kept)], variation.Checks);
    }

    /// <summary>C5 D5 E5 over a held C major chord, C3 E4 G4; the melody moved an octave up after.</summary>
    private static (Note[] Before, Note[] After) MelodyUpAnOctave()
    {
        Note[] chord = [Note("c3", 48, 0, 720), Note("e4", 64, 0, 720), Note("g4", 67, 0, 720)];
        Note[] before = [.. chord, Note("c5", 72, 0), Note("d5", 74, 240), Note("e5", 76, 480)];
        Note[] after = [.. chord, Note("c5", 84, 0), Note("d5", 86, 240), Note("e5", 88, 480)];
        return (before, after);
    }

    [Fact]
    public void Footnote_3_after_V3_melody_pitch_classes_count_as_kept()
    {
        (Note[] before, Note[] after) = MelodyUpAnOctave();

        Variation voiced = VariationFacts.Build("V3", s_span, before, after, Keep(KeepProperty.Melody), []);
        Variation chained = VariationFacts.Build($"V1{VariationChain.StepSeparator}V3", s_span, before, after, Keep(KeepProperty.Melody), []);
        Variation other = VariationFacts.Build("V9", s_span, before, after, Keep(KeepProperty.Melody), []);

        Assert.Equal([new KeepCheck(KeepProperty.Melody, KeepStatus.Kept)], voiced.Checks);
        Assert.Contains("melody pitch classes", voiced.Explanation);
        Assert.DoesNotContain("melody pitches", voiced.Explanation);
        Assert.Equal([new KeepCheck(KeepProperty.Melody, KeepStatus.Kept)], chained.Checks);
        Assert.Equal([new KeepCheck(KeepProperty.Melody, KeepStatus.NoLongerKept)], other.Checks);
    }

    [Fact]
    public void Footnote_3_does_not_accept_a_changed_pitch_class()
    {
        (Note[] before, Note[] after) = MelodyUpAnOctave();
        after[^1] = after[^1] with { Pitch = new Pitch(89) };

        Variation variation = VariationFacts.Build("V3", s_span, before, after, Keep(KeepProperty.Melody), []);

        Assert.Equal([new KeepCheck(KeepProperty.Melody, KeepStatus.NoLongerKept)], variation.Checks);
    }

    [Fact]
    public void Footnote_4_keep_bass_turns_inversions_off_but_leaves_V3_enabled()
    {
        Assert.Null(KeepTable.DisabledReason("V3", Keep(KeepProperty.Bass)));
        Assert.False(KeepTable.AllowsInversions(Keep(KeepProperty.Bass)));
        Assert.True(KeepTable.AllowsInversions(Keep(KeepProperty.Melody)));
        Assert.Equal([KeepTable.MelodyPitchClasses, KeepTable.UpperVoicesOnly], KeepTable.Footnotes("V3", Keep(KeepProperty.Bass, KeepProperty.Melody)));
    }

    [Fact]
    public void The_five_presets_have_stable_ids_and_enable_the_operations_they_suggest()
    {
        Assert.Equal(["keep-pitch-order", "keep-rhythm", "keep-melody", "keep-bass", "keep-contour"], KeepTable.Presets.Select(preset => preset.Id));
        foreach (KeepPreset preset in KeepTable.Presets)
        {
            Assert.Same(preset, KeepTable.FindPreset(preset.Id));
            Assert.NotEmpty(preset.Operations);
            Assert.All(preset.Operations, id => Assert.Null(KeepTable.DisabledReason(id, preset.Keep)));
        }

        Assert.Equal(Keep(KeepProperty.PitchOrder), KeepTable.FindPreset("keep-pitch-order")!.Keep);
        Assert.Equal(["V1", "V7", "V8"], KeepTable.FindPreset("keep-pitch-order")!.Operations);
        Assert.Equal(Keep(KeepProperty.Rhythm), KeepTable.FindPreset("keep-rhythm")!.Keep);
        Assert.Equal(["V9", "V10", "V4"], KeepTable.FindPreset("keep-rhythm")!.Operations);
        Assert.Null(KeepTable.FindPreset("keep-everything"));
    }

    [Fact]
    public void An_operation_outside_the_table_is_refused()
    {
        Assert.Null(KeepTable.Find("V5"));
        Assert.Throws<ArgumentException>(() => KeepTable.DisabledReason("V5", Keep()));
        Assert.Empty(KeepTable.Footnotes("V5", Keep(KeepProperty.Melody)));
    }

    [Fact]
    public void An_edit_that_breaks_the_contour_shows_no_longer_kept_without_blocking()
    {
        // AC-S1-13: the step goes through; only its check reports the broken property.
        Note[] before = [Note("a", 60, 0), Note("b", 64, 240), Note("c", 67, 480)];
        Note[] after = [Note("a", 60, 0), Note("b", 58, 240), Note("c", 67, 480)];

        Variation variation = VariationFacts.Build("edit", s_span, before, after, Keep(KeepProperty.Contour, KeepProperty.Rhythm), []);

        Assert.Equal(
            [new KeepCheck(KeepProperty.Contour, KeepStatus.NoLongerKept), new KeepCheck(KeepProperty.Rhythm, KeepStatus.Kept)],
            variation.Checks);
        Assert.Equal([60, 58, 67], variation.Notes.Select(note => note.Pitch.MidiNumber));
    }

    [Fact]
    public void Every_step_of_a_chain_is_checked_against_the_original()
    {
        // S1-E9: each proposed step's result carries the kept-property checks.
        Note[] notes = [Note("a", 72, 0), Note("b", 76, 240), Note("c", 74, 480)];
        var context = new VariationContext(s_span, Keep(KeepProperty.PitchOrder, KeepProperty.Contour));
        VariationChain chain = VariationChain.Start(notes, context)
            .Propose(RhythmReshape.Instance, new RhythmReshapeParameters(RhythmReshapeKind.HalfTime))
            .Accept()
            .Propose(RhythmReshape.Instance, new RhythmReshapeParameters(RhythmReshapeKind.Displace, 1));

        Assert.Equal(
            [new KeepCheck(KeepProperty.PitchOrder, KeepStatus.Kept), new KeepCheck(KeepProperty.Contour, KeepStatus.Kept)],
            chain.Result!.Checks);
        Assert.Equal(chain.Result.Checks, chain.Pending!.Result.Checks);
    }
}