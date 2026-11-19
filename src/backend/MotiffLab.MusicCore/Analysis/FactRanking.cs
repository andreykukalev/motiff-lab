namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// Which fact to show first about a selection (#132, decision record 0013): a ranking of the facts
/// music-core already found, not new analysis, so the Understand tab can open on one observation
/// and say why that one. Deterministic: the same facts give the same choice.
/// </summary>
public static class FactRanking
{
    /// <summary>The lenses in the order they are worth hearing first: structure, then the line, its rhythm, its character, its harmony, the key.</summary>
    private static readonly FactLens[] s_lensOrder = [FactLens.Structure, FactLens.Melody, FactLens.Rhythm, FactLens.Character, FactLens.Harmony, FactLens.Key];

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

    /// <summary>The facts the Insights stream leads with, in this order, when the selection has them (testing item 14).</summary>
    private static readonly string[] s_mainFirst = ["harmony.motion", "character.texture"];

    /// <summary>
    /// What the Insights stream shows first, under "Main" (testing item 14): the harmonic motion and what it gives, then
    /// the texture, then the strongest of the rest (<see cref="Strongest"/>) from lenses not yet shown, up to
    /// <paramref name="count"/> facts. An interpretation is never among them. Deterministic.
    /// </summary>
    public static IReadOnlyList<MotifFact> Main(IReadOnlyList<MotifFact> facts, int count = 3)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        List<MotifFact> chosen = [.. s_mainFirst
            .Select(id => facts.FirstOrDefault(fact => fact.Id == id && fact.Certainty != FactCertainty.Interpretation))
            .OfType<MotifFact>()
            .Take(count)];
        var lenses = chosen.Select(static fact => fact.Lens).ToHashSet();
        MotifFact[] rest = [.. facts.Where(fact => !chosen.Contains(fact) && !lenses.Contains(fact.Lens) && fact.Places.Count > 0)];
        chosen.AddRange(Strongest(rest, count - chosen.Count));
        return chosen;
    }

    /// <summary>
    /// Facts in the order the Insights stream lists them under a lens (assessment items 16–20), so its first two or three
    /// are the strongest: those that name places in the score before those that name none, such as "No syncopation";
    /// an interpretation last, since it rests on the others; otherwise music-core's own order. Deterministic.
    /// </summary>
    public static IReadOnlyList<MotifFact> InStream(IReadOnlyList<MotifFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return [.. facts
            .Select((fact, index) => (fact, index))
            .OrderBy(entry => entry.fact.Certainty == FactCertainty.Interpretation ? 1 : 0)
            .ThenBy(entry => entry.fact.Places.Count == 0 ? 1 : 0)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.fact)];
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
            FactLens.Harmony => words.T("is about the harmony", "о гармонии"),
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