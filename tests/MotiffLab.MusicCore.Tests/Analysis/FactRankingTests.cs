using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>Which fact Understand shows first (#132): a deterministic, explained ranking of the facts found.</summary>
public class FactRankingTests
{
    private static readonly FactPlace s_place = new("bar 1", new TickSpan(Tick.Zero, 480), []);

    private static MotifFact Fact(string id, FactLens lens, FactCertainty certainty, bool places = true) =>
        new(id, lens, certainty, $"{id}.", "basis") { Places = places ? [s_place] : [] };

    [Fact]
    public void A_fact_with_places_about_structure_comes_first_and_an_interpretation_never_does()
    {
        MotifFact[] facts =
        [
            Fact("key", FactLens.Key, FactCertainty.Detected, places: false),
            Fact("answer", FactLens.Structure, FactCertainty.Interpretation),
            Fact("intervals", FactLens.Melody, FactCertainty.Detected),
            Fact("recurs", FactLens.Structure, FactCertainty.Inferred),
        ];

        MotifFact? chosen = FactRanking.KeyObservation(facts);

        Assert.Equal("recurs", chosen!.Id);
        Assert.Equal("It names places to show in the score, is about structure, and is inferred.", FactRanking.Reason(chosen));
    }

    [Fact]
    public void Within_a_lens_the_surer_fact_wins_and_order_breaks_ties()
    {
        MotifFact[] facts =
        [
            Fact("later", FactLens.Melody, FactCertainty.Detected),
            Fact("guess", FactLens.Melody, FactCertainty.Uncertain),
            Fact("first", FactLens.Melody, FactCertainty.Detected),
        ];

        Assert.Equal("later", FactRanking.KeyObservation(facts)!.Id);
        Assert.Equal("first", FactRanking.KeyObservation([.. facts.Reverse()])!.Id);
    }

    [Fact]
    public void Without_places_anywhere_the_first_by_lens_is_chosen_and_the_reason_says_so()
    {
        MotifFact[] facts =
        [
            Fact("key", FactLens.Key, FactCertainty.Detected, places: false),
            Fact("tempo", FactLens.Character, FactCertainty.Detected, places: false),
        ];

        MotifFact? chosen = FactRanking.KeyObservation(facts);

        Assert.Equal("tempo", chosen!.Id);
        Assert.Equal("It names no places, like every fact here, is about the character, and is detected.", FactRanking.Reason(chosen));
        Assert.Null(FactRanking.KeyObservation([]));
        Assert.Null(FactRanking.KeyObservation([Fact("answer", FactLens.Structure, FactCertainty.Interpretation)]));
    }
}