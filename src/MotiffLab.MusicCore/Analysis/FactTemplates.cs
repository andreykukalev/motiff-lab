namespace MotiffLab.MusicCore.Analysis;

/// <summary>One point of a template explanation: the sentences of the facts it cites, and the weakest certainty among them.</summary>
/// <param name="FactIds">The digest facts the point rests on, in the order their sentences appear.</param>
public sealed record TemplatePoint(string Text, IReadOnlyList<string> FactIds, FactCertainty Certainty);

/// <summary>A template explanation: two to four points, each citing digest facts, as AI text would (S1-AI1).</summary>
public sealed record TemplateExplanation(IReadOnlyList<TemplatePoint> Points)
{
    /// <summary>Every cited fact id, in order, without repeats.</summary>
    public IReadOnlyList<string> CitedIds => [.. Points.SelectMany(point => point.FactIds).Distinct(StringComparer.Ordinal)];

    public string Text => string.Join(" ", Points.Select(point => point.Text));
}

/// <summary>
/// The explanations shown when AI is off, offline or falls back (S1-AI0, S1-AI1): built from the
/// same digest AI would see, citing fact ids the same way.
/// </summary>
/// <remarks>
/// A template states only what the cited facts state, in their own sentences, so it can never say
/// more than the engine measured. Which facts it picks is fixed per subject; a fact the digest does
/// not hold is skipped. Building one is pure: the same digest gives the same explanation.
/// </remarks>
public static class FactTemplates
{
    /// <summary>An explanation has at most this many points, like the AI's 2–4 sentences.</summary>
    public const int MaxPoints = 4;

    // For a motif: its melody, its rhythm, where it recurs, and its character.
    private static readonly string[][] s_motifPoints =
    [
        ["melody.contour", "melody.range"],
        ["rhythm.grouping", "rhythm.syncopation"],
        ["structure.recurrences"],
        ["character.tempo", "character.density"],
    ];

    // For a variation: what the step changed and kept first, then the motif it started from.
    private static readonly string[][] s_variationPoints =
    [
        [FactDigest.ChangesId],
        [FactDigest.KeepId],
        ["melody.contour", "melody.range"],
        ["rhythm.grouping", "rhythm.syncopation"],
    ];

    /// <summary>Explain structure and changes (S1-AI1): the motif, or the variation and its motif.</summary>
    public static TemplateExplanation Explain(FactDigest digest)
    {
        ArgumentNullException.ThrowIfNull(digest);

        string[][] plan = digest.Subject == FactDigest.VariationSubject ? s_variationPoints : s_motifPoints;
        var points = new List<TemplatePoint>();
        foreach (string[] ids in plan)
        {
            DigestFact[] facts = [.. ids.Select(digest.Find).OfType<DigestFact>()];
            if (facts.Length > 0 && points.Count < MaxPoints)
            {
                points.Add(new TemplatePoint(
                    string.Join(" ", facts.Select(fact => fact.Text)),
                    [.. facts.Select(fact => fact.Id)],
                    facts.Max(fact => fact.Certainty)));
            }
        }

        return new TemplateExplanation(points);
    }
}