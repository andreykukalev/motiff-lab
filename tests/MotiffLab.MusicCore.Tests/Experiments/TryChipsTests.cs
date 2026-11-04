using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Invitations;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Experiments;

/// <summary>The fact-to-chips table (decision record 0018, section 7; preparation item 8): only operations the engine has, only steps that can run.</summary>
public class TryChipsTests
{
    private const string FurElise = "fur-elise-original.musicxml";

    private static InvitationContext Context(int firstBar, int lastBar)
    {
        ImportedPiece piece = PieceImporter.Import(FurElise, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", FurElise))).Piece!;
        TickSpan span = TickSpan.FromBounds(piece.Meter.GetBar(firstBar).Span.Onset, piece.Meter.GetBar(lastBar).Span.End);
        MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, span, SelectionFilter.All);
        KeySignature? key = KeySignature.InForce(piece.Keys, selection.Span.Onset);
        return new InvitationContext(
            piece.Notes,
            piece.Meter,
            selection,
            MotifAnalyzer.Analyze(piece.Notes, piece.Meter, selection, key, phrases: []),
            StructureAnalyzer.Analyze(piece.Notes, piece.Meter, selection, []),
            MotifAnalyzer.AnalyzeCharacter(piece.Notes, piece.Meter, selection, key, CharacterContext.FromPiece(piece)));
    }

    [Fact]
    public void Every_chip_names_a_catalogue_operation_and_a_preset_and_the_table_names_only_chips()
    {
        Assert.Equal(TryChips.Chips.Count, TryChips.Chips.Select(chip => chip.Id).Distinct(StringComparer.Ordinal).Count());
        // Ornament (V14) and Reharmonise (V15) joined the Keep table with their operations (0018; preparation item 19).
        Assert.All(TryChips.Chips, chip => Assert.NotNull(KeepTable.Find(chip.OperationId)));
        Assert.All(TryChips.Chips, chip => Assert.NotNull(KeepTable.FindPreset(chip.KeepPresetId)));
        Assert.All(TryChips.Chips, chip => Assert.NotEqual(chip.Label, chip.RussianLabel));
        Assert.All(TryChips.Table, row => Assert.All(row.ChipIds, id => Assert.NotNull(TryChips.Find(id))));
        Assert.All(TryChips.Chips.Where(chip => chip.Parameters is not null), chip => Assert.True(chip.IsBuilt, chip.Id));
        Assert.All(TryChips.Chips.Where(chip => chip.Parameters is null), chip => Assert.Null(VariationOperations.Find(chip.OperationId)));
    }

    [Fact]
    public void A_fact_offers_the_chips_of_its_kind_whose_operation_is_built()
    {
        Assert.Equal(["v1.dotted", "v1.syncopate", "v1.halfTime", "v7.air", "v6.simplify"], TryChips.For("rhythm.durations").Select(chip => chip.Id));
        Assert.Equal(["v4.up", "v4.down", "v11.passing", "v14.ornament", "v6.simplify"], TryChips.For("melody.line").Select(chip => chip.Id));
        Assert.Equal(["v12.pedal", "v15.reharmonise"], TryChips.Table.Single(row => row.FactKind == "sentence.close").ChipIds);
        Assert.Equal(["v12.pedal", "v15.reharmonise"], TryChips.For("sentence.close.3").Select(chip => chip.Id));
        Assert.Empty(TryChips.For("key.unknown"));
        Assert.Equal("sentence.close", TryChips.KindOf("sentence.close.3"));
        Assert.Equal("melody.intervals", TryChips.KindOf("melody.intervals"));
        Assert.Equal("Долгая–короткая", TryChips.Find("v1.dotted")!.LabelIn(Words.Russian));
    }

    [Fact]
    public void Only_chips_whose_step_runs_on_the_selection_are_offered_and_one_sounds_as_a_variation()
    {
        InvitationContext context = Context(1, 2);

        IReadOnlyList<TryChip> offered = TryChips.Offer("melody.line", context);
        Assert.NotEmpty(offered);
        Assert.All(offered, chip => Assert.True(chip.IsBuilt));
        Assert.All(TryChips.Offer("rhythm.durations", context), chip => Assert.Null(TryChips.Start(chip, context, out _)?.Pending));

        VariationChain? chain = TryChips.Start(offered[0], context, out string? reason);
        Assert.Null(reason);
        Assert.Single(chain!.Steps);
        Assert.Equal(offered[0].OperationId, chain.Steps[0].Operation.Id);
        Assert.Equal(KeepTable.FindPreset(offered[0].KeepPresetId)!.Keep, chain.Context.Keep);

        Assert.All(TryChips.Chips, chip => Assert.True(chip.IsBuilt));
        Assert.Null(TryChips.Start(new TryChip("v5.colour", "V5", null, "keep-melody", "Colour", "Краска"), context, out string? missing));
        Assert.Contains("V5", missing);
        Assert.Equal(TryChips.Offer("melody.line", context), TryChips.Offer("melody.line", context));
    }

    [Fact]
    public void The_interface_offers_only_the_chips_a_playing_musician_uses_at_most_three_and_the_engine_keeps_the_rest()
    {
        InvitationContext context = Context(1, 2);

        Assert.All(TryChips.Offered, id => Assert.NotNull(TryChips.Find(id)));
        Assert.All(TryChips.Table.Select(row => row.FactKind), kind => Assert.InRange(TryChips.Offer(kind, context).Count, 0, TryChips.MaxOffered));
        Assert.All(TryChips.Table.Select(row => row.FactKind), kind => Assert.All(TryChips.Offer(kind, context), chip => Assert.Contains(chip.Id, TryChips.Offered)));
        // The rhythm and contour reshapes leave the interface and stay in the engine: a chip still runs by its id.
        Assert.Empty(TryChips.Offer("character.articulation", context));
        Assert.DoesNotContain(TryChips.Offer("rhythm.durations", context), chip => chip.Id == "v1.dotted");
        Assert.NotNull(TryChips.Start(TryChips.Find("v1.dotted")!, context, out _));
    }

    [Fact]
    public void A_fact_kind_with_chips_gets_one_Try_button_whose_ways_are_its_chips_each_with_its_preset()
    {
        InvitationContext context = Context(1, 2);
        MotifFact[] facts =
        [
            new("melody.line.1", FactLens.Melody, FactCertainty.Detected, "The line.", "Basis."),
            new("melody.line.2", FactLens.Melody, FactCertainty.Detected, "The line again.", "Basis."),
            new("character.articulation", FactLens.Character, FactCertainty.Detected, "Articulation.", "Basis."),
            // Its only chip is one melody.line already offers: one way is offered once.
            new("melody.contour", FactLens.Melody, FactCertainty.Detected, "The contour.", "Basis."),
        ];

        Experiment button = Assert.Single(TryChips.Buttons(facts, context));
        Assert.Equal("try.melody.line", button.Id);
        Assert.Contains(button.Id, TryChips.ExperimentIds);
        Assert.Equal(["melody.line.1", "melody.line.2"], button.FactIds);
        Assert.Equal(context.Selection.Span, button.Passage.Span);
        IReadOnlyList<TryChip> chips = TryChips.Offer("melody.line", context);
        Assert.Equal(chips.Select(chip => chip.Label), button.Alternatives.Select(way => way.Label));
        Assert.Equal(chips.Select(chip => chip.KeepPresetId), button.Alternatives.Select(way => way.KeepPresetId));

        // A way runs with its own preset, as the chip does.
        for (int way = 0; way < button.Alternatives.Count; way++)
        {
            VariationChain chain = ExperimentCatalogue.Start(button, way, context);
            Assert.Equal(KeepTable.FindPreset(chips[way].KeepPresetId)!.Keep, chain.Context.Keep);
            Assert.Equal(chips[way].OperationId, Assert.Single(chain.Steps).Operation.Id);
        }

        Assert.Equal("Попробовать вариацию", TryChips.Buttons(facts, context with { Words = Words.Russian })[0].Title);
    }
}