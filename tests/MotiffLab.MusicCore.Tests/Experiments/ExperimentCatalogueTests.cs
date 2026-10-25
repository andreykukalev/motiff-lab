using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Observations;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Experiments;

/// <summary>The Explore experiments A–D on the pilot pieces, read from the pilot's MusicXML files.</summary>
public class ExperimentCatalogueTests
{
    private const string FurElise = "fur-elise-original.musicxml";
    private const string OdeToJoy = "ode-to-joy.musicxml";

    private static ImportedPiece Piece(string name) =>
        PieceImporter.Import(name, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name))).Piece!;

    /// <summary>What the app reads for a selection of bars: the motif, structure and character analyses.</summary>
    private static ObservationContext Context(string name, int firstBar, int lastBar)
    {
        ImportedPiece piece = Piece(name);
        MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, MotifSelector.Bars(piece.Meter, firstBar, lastBar, piece.End), SelectionFilter.All);
        KeySignature? key = KeySignature.InForce(piece.Keys, selection.Span.Onset);
        return new ObservationContext(
            piece.Notes,
            piece.Meter,
            selection,
            MotifAnalyzer.Analyze(piece.Notes, piece.Meter, selection, key, phrases: []),
            StructureAnalyzer.Analyze(piece.Notes, piece.Meter, selection, []),
            MotifAnalyzer.AnalyzeCharacter(piece.Notes, piece.Meter, selection, key, CharacterContext.FromPiece(piece)));
    }

    private static Experiment Find(ObservationContext context, string id) => ExperimentCatalogue.Offer(context).Single(experiment => experiment.Id == id);

    [Fact]
    public void The_fur_elise_motif_offers_space_groove_and_an_answer()
    {
        IReadOnlyList<Experiment> offered = ExperimentCatalogue.Offer(Context(FurElise, 0, 1));

        Assert.Equal(["melody.space", "rhythm.groove", "motif.answer"], offered.Select(experiment => experiment.Id));
        Assert.Equal([ExperimentTopic.Character, ExperimentTopic.Rhythm, ExperimentTopic.Motif], offered.Select(experiment => experiment.Topic));
    }

    [Fact]
    public void Alternatives_that_sound_the_same_are_offered_once()
    {
        // On 16ths, Short and Detached both give 32nds, so only Detached is offered; on quarters they differ.
        Assert.Equal(["Detached"], Find(Context(FurElise, 0, 1), "melody.space").Alternatives.Select(alternative => alternative.Label));
        Assert.Equal(["Detached", "Short"], Find(Context(OdeToJoy, 1, 2), "melody.space").Alternatives.Select(alternative => alternative.Label));
    }

    [Fact]
    public void Experiment_a_spaces_the_melody_and_leaves_the_left_hand_ringing()
    {
        ObservationContext context = Context(FurElise, 0, 2);
        Experiment experiment = Find(context, "melody.space");

        Variation result = ExperimentCatalogue.Start(experiment, 0, context).Result!;

        Assert.Equal("V13", result.Operation);
        Assert.All(result.Changes.Where(change => change.Before.Hand?.Value == Hand.Left), change => Assert.Equal(change.Before, change.After));
        Assert.All(result.Changes, change => Assert.Equal(change.Before.Span.Onset, change.After.Span.Onset));
        Assert.Contains(result.Changes, change => change.After.Span.Duration < change.Before.Span.Duration);
        Assert.Equal(["character.articulation", "melody.line"], experiment.FactIds);
        Assert.Equal("keep-pitch-order", experiment.KeepPresetId);
    }

    [Fact]
    public void Experiment_b_makes_ode_to_joy_long_short_or_pushes_it_ahead_of_the_beat()
    {
        ObservationContext context = Context(OdeToJoy, 1, 2);
        Experiment experiment = Find(context, "rhythm.groove");

        Assert.Equal(["Long–short", "Ahead of the beat"], experiment.Alternatives.Select(alternative => alternative.Label));
        Variation dotted = ExperimentCatalogue.Start(experiment, 0, context).Result!;
        Assert.Equal([720L, 240, 720, 240, 720, 240, 720, 240], dotted.Notes.Select(note => note.Span.Duration));
        Assert.Equal([64, 64, 65, 67, 67, 65, 64, 62], dotted.Notes.Select(note => note.Pitch.MidiNumber));
    }

    [Fact]
    public void A_melody_that_is_not_even_gets_no_groove()
    {
        // Bar 4 of Ode to Joy is a dotted quarter, an 8th and a half.
        Assert.DoesNotContain(ExperimentCatalogue.Offer(Context(OdeToJoy, 1, 4)), experiment => experiment.Id == "rhythm.groove");
    }

    [Fact]
    public void Experiment_c_replays_the_fur_elise_left_hand_as_repeated_chords()
    {
        ObservationContext context = Context(FurElise, 2, 4);
        Experiment experiment = Find(context, "layers.accompaniment");

        Assert.Equal(ExperimentTopic.Layers, experiment.Topic);
        Assert.Equal(["Repeated chord in 8ths", "Held chord", "Falling arpeggio", "Held chord under a detached melody"], experiment.Alternatives.Select(alternative => alternative.Label));
        Variation result = ExperimentCatalogue.Start(experiment, 0, context).Result!;
        Tick bar2 = context.Meter.GetBar(2).Span.Onset;
        Note[] left = [.. result.Notes.Where(note => note.Hand?.Value == Hand.Left && note.Span.Onset < context.Meter.GetBar(3).Span.Onset)];
        Assert.Equal([0L, 0, 0, 240, 240, 240, 480, 480, 480], left.Select(note => note.Span.Onset.Value - bar2.Value));
        Assert.Equal([45, 52, 57], left.Take(3).Select(note => note.Pitch.MidiNumber));
        Assert.Equal(KeepStatus.Kept, result.Checks.Single(check => check.Property == KeepProperty.Melody).Status);
    }

    [Fact]
    public void Experiment_c_can_compose_two_steps()
    {
        ObservationContext context = Context(FurElise, 2, 4);
        Experiment experiment = Find(context, "layers.accompaniment");

        VariationChain chain = ExperimentCatalogue.Start(experiment, 3, context);

        Assert.Equal("V2 › V13", chain.Result!.Operation);
        Assert.Equal(2, chain.Steps.Count);
    }

    [Fact]
    public void Experiment_d_changes_the_repeat_and_keeps_its_rhythm()
    {
        ObservationContext context = Context(FurElise, 0, 1);
        Experiment experiment = Find(context, "motif.answer");

        Assert.Equal((4, 5), (experiment.Passage.FirstBar, experiment.Passage.LastBar));
        Assert.Equal(["Wider steps", "Narrower steps", "Upside down"], experiment.Alternatives.Select(alternative => alternative.Label));
        Assert.Equal("The motif stays as written; when it comes back in bars 4–5, its melody changes and its rhythm stays.", experiment.Change);
        VariationChain chain = ExperimentCatalogue.Start(experiment, 2, context);
        Assert.All(chain.Original, note => Assert.True(note.Span.Onset >= experiment.Passage.Span.Onset));
        Assert.Equal(KeepStatus.Kept, chain.Result!.Checks.Single(check => check.Property == KeepProperty.Rhythm).Status);
        Assert.Contains(chain.Result.Changes, change => change.PitchChanged);
    }

    [Fact]
    public void Ode_to_joy_offers_an_answer_in_bars_5_and_6()
    {
        Experiment experiment = Find(Context(OdeToJoy, 1, 2), "motif.answer");

        Assert.Equal((5, 6), (experiment.Passage.FirstBar, experiment.Passage.LastBar));
    }

    [Fact]
    public void A_question_lists_only_its_experiments()
    {
        ObservationContext context = Context(FurElise, 0, 1);

        Assert.Equal(["rhythm.groove"], ExperimentCatalogue.ForQuestion(context, ExperimentTopic.Rhythm).Select(experiment => experiment.Id));
        Assert.Empty(ExperimentCatalogue.ForQuestion(context, ExperimentTopic.Layers));
        Assert.Equal("What does each layer add?", ExperimentCatalogue.Prompt(ExperimentTopic.Layers));
    }

    [Fact]
    public void Surprise_me_is_deterministic_and_prefers_what_was_not_tried()
    {
        ObservationContext context = Context(FurElise, 0, 1);

        Assert.Equal(ExperimentCatalogue.Surprise(context, 7, new HashSet<string>()), ExperimentCatalogue.Surprise(context, 7, new HashSet<string>()));
        for (int seed = 0; seed < 10; seed++)
        {
            SurprisePick pick = ExperimentCatalogue.Surprise(context, seed, new HashSet<string> { "melody.space", "rhythm.groove" })!;
            Assert.Equal("motif.answer", pick.Experiment.Id);
            Assert.InRange(pick.Alternative, 0, pick.Experiment.Alternatives.Count - 1);
        }

        SurprisePick any = ExperimentCatalogue.Surprise(context, 3, new HashSet<string> { "melody.space", "rhythm.groove", "motif.answer" })!;
        Assert.Contains(any.Experiment.Id, new[] { "melody.space", "rhythm.groove", "motif.answer" });
    }

    [Fact]
    public void Surprise_me_has_nothing_for_a_single_note()
    {
        ImportedPiece piece = Piece(OdeToJoy);
        MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, new TickSpan(Tick.Zero, 480), SelectionFilter.All);
        var context = new ObservationContext(
            piece.Notes,
            piece.Meter,
            selection,
            MotifAnalyzer.Analyze(piece.Notes, piece.Meter, selection, piece.Keys[0], phrases: []),
            StructureAnalyzer.Analyze(piece.Notes, piece.Meter, selection, []),
            MotifAnalyzer.AnalyzeCharacter(piece.Notes, piece.Meter, selection, piece.Keys[0], CharacterContext.FromPiece(piece)));

        Assert.Empty(ExperimentCatalogue.Offer(context));
        Assert.Null(ExperimentCatalogue.Surprise(context, 1, new HashSet<string>()));
    }

    [Fact]
    public void Every_offered_alternative_starts_and_the_offer_is_deterministic()
    {
        foreach ((string name, int first, int last) in new[] { (FurElise, 0, 1), (FurElise, 0, 2), (FurElise, 2, 4), (OdeToJoy, 1, 2), (OdeToJoy, 1, 4) })
        {
            ObservationContext context = Context(name, first, last);
            IReadOnlyList<Experiment> offered = ExperimentCatalogue.Offer(context);
            Assert.Equal(offered, ExperimentCatalogue.Offer(context));
            foreach (Experiment experiment in offered)
            {
                // Analytics may send only the catalogue's ids and alternative numbers.
                Assert.Contains(experiment.Id, ExperimentCatalogue.Ids);
                Assert.InRange(experiment.Alternatives.Count, 1, ExperimentCatalogue.MaxAlternatives);
                for (int i = 0; i < experiment.Alternatives.Count; i++)
                {
                    Assert.NotNull(ExperimentCatalogue.Start(experiment, i, context).Result);
                }
            }
        }
    }
}