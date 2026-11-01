namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// Which fact to show first about a selection (#132, decision record 0013): a ranking of the facts
/// music-core already found, not new analysis, so the Understand tab can open on one observation
/// and say why that one. Deterministic: the same facts give the same choice.
/// </summary>
public static class FactRanking
{
    /// <summary>The lenses in the order they are worth hearing first: structure, then the line, its rhythm, its character, the key.</summary>
    private static readonly FactLens[] s_lensOrder = [FactLens.Structure, FactLens.Melody, FactLens.Rhythm, FactLens.Character, FactLens.Key];

    /// <summary>
    /// The one fact to hear first: the first that names places in the score, by lens order, the
    /// surer first; an interpretation is never it, since it rests on other facts. Null without facts.
    /// </summary>
    public static MotifFact? KeyObservation(IReadOnlyList<MotifFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts
            .Select((fact, index) => (fact, index))
            .Where(entry => entry.fact.Certainty != FactCertainty.Interpretation)
            .OrderBy(entry => entry.fact.Places.Count == 0 ? 1 : 0)
            .ThenBy(entry => LensRank(entry.fact.Lens))
            .ThenBy(entry => entry.fact.Certainty)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.fact)
            .FirstOrDefault();
    }

    /// <summary>Why a fact is shown first, from the ranking's own criteria: "It names places to show in the score, is about structure, and is detected."</summary>
    public static string Reason(MotifFact fact, Words words = default)
    {
        ArgumentNullException.ThrowIfNull(fact);
        string places = fact.Places.Count > 0
            ? words.T("names places to show in the score", "называет места, которые можно показать в нотах")
            : words.T("names no places, like every fact here", "не называет мест, как и все факты здесь");
        string lens = fact.Lens switch
        {
            FactLens.Structure => words.T("is about structure", "о структуре"),
            FactLens.Melody => words.T("is about the line", "о линии"),
            FactLens.Rhythm => words.T("is about the rhythm", "о ритме"),
            FactLens.Character => words.T("is about the character", "о характере"),
            _ => words.T("is about the key", "о тональности"),
        };
        string certainty = fact.Certainty switch
        {
            FactCertainty.Detected => words.T("is detected", "обнаружен"),
            FactCertainty.Inferred => words.T("is inferred", "предположен"),
            _ => words.T("is uncertain, like the rest", "под вопросом, как и остальные"),
        };
        return words.T($"It {places}, {lens}, and {certainty}.", $"Он {places}, {lens} и {certainty}.");
    }

    private static int LensRank(FactLens lens)
    {
        int rank = Array.IndexOf(s_lensOrder, lens);
        return rank < 0 ? s_lensOrder.Length : rank;
    }
}