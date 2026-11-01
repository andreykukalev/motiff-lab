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

    /// <summary>
    /// The strongest facts for the Insights strip (decision record 0018, section 4; preparation item 9): up to
    /// <paramref name="count"/> facts in the order of <see cref="KeyObservation"/>, taking a fact from a lens not yet
    /// shown before a second fact of the same lens, so the strip reads across structure, line and rhythm rather
    /// than three facts about one thing. An interpretation is never among them. Deterministic.
    /// </summary>
    public static IReadOnlyList<MotifFact> Strongest(IReadOnlyList<MotifFact> facts, int count = 3)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        List<MotifFact> ranked = [.. facts
            .Select((fact, index) => (fact, index))
            .Where(entry => entry.fact.Certainty != FactCertainty.Interpretation)
            .OrderBy(entry => entry.fact.Places.Count == 0 ? 1 : 0)
            .ThenBy(entry => LensRank(entry.fact.Lens))
            .ThenBy(entry => entry.fact.Certainty)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.fact)];
        var chosen = new List<MotifFact>();
        var lenses = new HashSet<FactLens>();
        while (chosen.Count < count && ranked.Count > 0)
        {
            MotifFact next = ranked.FirstOrDefault(fact => !lenses.Contains(fact.Lens)) ?? ranked[0];
            chosen.Add(next);
            lenses.Add(next.Lens);
            ranked.Remove(next);
        }

        return chosen;
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